using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace WhiteDragon.Ledger
{
    public sealed partial class InventoryObserver
    {
        // A handle is valid only for the last inspected instance of this owner's window.
        private sealed class WindowRead
        {
            public PixelWindow Window;
            public string Token;
            public JObject View = new JObject();
            public Dictionary<string, PixelElement> Controls = new Dictionary<string, PixelElement>();
        }

        private readonly Dictionary<int, WindowRead> workstations = new Dictionary<int, WindowRead>();

        private void ReadElement(PixelElement element, string path, string role, string label, WindowRead read, JArray rows)
        {
            if (element == null || MachineryContainerUtils.IsHidden(element)) return;
            if (element is MachineryPanel panel)
            {
                ReadElement(panel.content, path + "/body", panel.role ?? role, panel.label?.GetText() ?? label, read, rows);
                return;
            }
            if (element is MachineryIntake intake)
            {
                ReadElement(intake.content, path + "/intake", role, label, read, rows);
                return;
            }
            if (element is MachineryTabs tabs)
            {
                if (tabs.selected >= 0)
                    ReadElement(tabs.GetPage(tabs.selected), path + "/tab" + tabs.selected, role, tabs.GetCaption(tabs.selected), read, rows);
                return;
            }
            if (element is MachineryPanelRow panels)
            {
                for (int i = 0; i < panels.Panels.Count; i++)
                    ReadElement(panels.Panels[i], path + "/" + i, role, label, read, rows);
                return;
            }
            if (element is MachineryStack stack)
            {
                for (int i = 0; i < stack.elements.Count; i++)
                    ReadElement(stack.elements[i], path + "/" + i, role, label, read, rows);
                return;
            }

            var row = new JObject { ["control"]=path, ["role"]=role, ["label"]=label };
            if (element is GameInventory storage)
            {
                row["type"] = "slot";
                row["insert_allowed"] = role != "output" && !storage.IsInsertLocked() && !read.Window.isLocked;
                row["remove_allowed"] = !storage.IsRemoveLocked() && !read.Window.isLocked;
                var items = new JArray();
                foreach (var child in storage.childItems)
                {
                    if (GeneralHelper.IsDisplayOnly(child)) continue;
                    var entry = new JObject { ["id"]=LocalId(child), ["name"]=child.GetDisplayName(true),
                        ["kind"]=child.identifier, ["quantity"]=child.unitCount };
                    if (BottleLabel(child) is string bottleLabel) entry["bottle_label"] = bottleLabel;
                    items.Add(entry);
                }
                row["items"] = items;
            }
            else if (element is MachineryButton button)
            {
                row["type"] = "button";
                row["label"] = button.text;
                row["description"] = button.description;
                row["enabled"] = !button.isDisabled && !read.Window.isLocked && !string.IsNullOrWhiteSpace(button.text);
            }
            else if (element is MachineryText text)
            {
                row["type"] = "text";
                row["text"] = text.GetText();
            }
            else if (element is MachineryProgressBar progress)
            {
                row["type"] = "progress";
                row["value"] = progress.value;
            }
            else return;
            read.Controls.Add(path, element);
            rows.Add(row);
        }

        private WindowRead ReadWindow(GameItem owner)
        {
            var read = new WindowRead { Window=owner.contentWindow };
            var rows = new JArray();
            ReadElement(read.Window.childElement, "content", null, null, read, rows);
            read.View = new JObject { ["owner_id"]=LocalId(owner), ["name"]=owner.GetDisplayName(true),
                ["title"]=read.Window.titleString, ["locked"]=read.Window.isLocked, ["controls"]=rows };
            return read;
        }

        private static JObject WindowChanges(JObject before, JObject after)
        {
            var delta = new JObject();
            foreach (var field in after.Properties())
                if (field.Name != "controls" && !JToken.DeepEquals(before[field.Name], field.Value))
                    delta[field.Name] = field.Value;
            var oldControls = ((JArray)before["controls"]).ToDictionary(c => (string)c["control"]);
            var changed = new JArray();
            foreach (var control in (JArray)after["controls"])
            {
                string key = (string)control["control"];
                if (!oldControls.TryGetValue(key, out var old))
                {
                    changed.Add(control);
                    continue;
                }
                oldControls.Remove(key);
                if (JToken.DeepEquals(old, control)) continue;
                var fields = new JObject { ["control"]=key };
                foreach (var field in ((JObject)control).Properties())
                {
                    if (JToken.DeepEquals(old[field.Name], field.Value)) continue;
                    if (field.Name != "items" || !(old["items"] is JArray priorItems))
                    {
                        fields[field.Name] = field.Value;
                        continue;
                    }
                    var previous = priorItems.ToDictionary(i => (int)i["id"]);
                    var upserts = new JArray();
                    foreach (var item in (JArray)field.Value)
                    {
                        int itemId = (int)item["id"];
                        if (!previous.TryGetValue(itemId, out var prior) || !JToken.DeepEquals(prior, item)) upserts.Add(item);
                        previous.Remove(itemId);
                    }
                    if (upserts.Count > 0) fields["items_updated"] = upserts;
                    if (previous.Count > 0) fields["items_removed"] = new JArray(previous.Keys);
                }
                foreach (var field in ((JObject)old).Properties())
                    if (((JObject)control).Property(field.Name) == null) fields[field.Name] = JValue.CreateNull();
                changed.Add(fields);
            }
            if (changed.Count > 0) delta["controls_updated"] = changed;
            if (oldControls.Count > 0) delta["controls_removed"] = new JArray(oldControls.Keys);
            return delta;
        }

        private static IEnumerable<JToken> DecisionControls(WindowRead read)
        {
            // Progress can tick between observation and input. Slot contents, labels and availability matter to the action.
            return ((JArray)read.View["controls"]).Where(row => (string)row["type"] != "progress");
        }

        private void PublishWindow(int id, WindowRead read, JObject receipt, WindowRead previous = null)
        {
            read.Token = Guid.NewGuid().ToString("N");
            workstations[id] = read;
            receipt["window_token"] = read.Token;
            if (previous == null) receipt["window"] = read.View;
            else
            {
                var delta = WindowChanges(previous.View, read.View);
                if (delta.HasValues) receipt["window_delta"] = delta;
            }
        }

        private string WorkstationAction(JObject request, JObject receipt)
        {
            int id = (int)request["owner_id"];
            string action = (string)request["action"];
            if (!itemsById.TryGetValue(id, out var owner) || owner.parentInventory == null ||
                !areas.TryGetValue(owner.parentInventory, out var area) || (area != "inventory" && area != "showcase") ||
                !GeneralHelper.IsItemOwned(owner)) return "owner_unavailable";
            var window = owner.contentWindow;
            if (window == null) return "no_content_window";
            if (!window.IsVisible())
            {
                workstations.Remove(id);
                if (action != "inspect_window" || (bool?)request["open_if_closed"] != true) return "window_closed";
                if (phase == "OPEN" || GeneralHelper.IsItemLocked(owner) || Interactable.isPaused ||
                    ItemMouseDoubleClickHandler.current == null) return "opening_unavailable";
                ItemMouseDoubleClickHandler.current.OpenContentAction(owner, (Vector2)Input.mousePosition);
                if (!window.IsVisible()) return "window_closed";
            }
            var read = ReadWindow(owner);
            workstations.TryGetValue(id, out var prior);
            if (action == "inspect_window")
            {
                bool continueView = prior != null && prior.Token == (string)request["since_token"];
                PublishWindow(id, read, receipt, continueView ? prior : null);
                return "observed";
            }
            if (phase != "MORNING" && phase != "CLOSED") return "shop_open";
            if (window.isLocked || GeneralHelper.IsItemLocked(owner) || Interactable.isPaused) return "locked";
            string control = (string)request["control"];
            if (prior == null || prior.Token != (string)request["window_token"] ||
                prior.Window != window || !DecisionControls(prior).SequenceEqual(DecisionControls(read), JToken.EqualityComparer) ||
                !read.Controls.TryGetValue(control, out var element) || !prior.Controls.TryGetValue(control, out var old) ||
                !ReferenceEquals(old, element)) return "window_changed";
            var row = (JObject)((JArray)read.View["controls"]).FirstOrDefault(r => (string)r["control"] == control);
            workstations.Remove(id);
            string status;
            if (action == "press_control")
            {
                if (!(element is MachineryButton button) || (bool?)row["enabled"] != true ||
                    button.text != (string)request["label"]) return "button_unavailable";
                receipt["label"] = button.text;
                status = button.Select() ? "pressed" : "button_rejected";
            }
            else
            {
                if (!(element is GameInventory storage)) return "not_storage";
                status = TransferStorage(owner, storage, row, request, receipt);
            }
            FlushChanges();
            receipt["owner_id"] = id;
            receipt["control"] = control;
            if (owner.contentWindow == window && window.IsVisible()) PublishWindow(id, ReadWindow(owner), receipt, prior);
            else receipt["window_closed"] = true;
            return status;
        }

        private string TransferStorage(GameItem owner, GameInventory storage, JObject row, JObject request, JObject receipt)
        {
            string source = (string)request["source"];
            bool retrieve = source == "slot";
            if (!retrieve && source != "inventory" && source != "showcase") return "invalid_source";
            int id = (int)request["item_id"];
            if (!itemsById.TryGetValue(id, out var item) || item == owner || GeneralHelper.IsDisplayOnly(item) ||
                !GeneralHelper.IsItemEffectivelyOwned(item)) return "item_unavailable";
            var shop = EmporiumEntry.Instance;
            GameInventory origin = retrieve ? storage : source == "inventory" ? shop.invElement : shop.showcaseElement;
            GameInventory target = retrieve ? (GameInventory)shop.invElement : storage;
            if (item.parentInventory != origin) return "source_changed";
            if (!retrieve && (bool)row["insert_allowed"] == false) return "insert_blocked";
            if (origin.IsRemoveLocked() || target.IsInsertLocked() || !item.MayRemove() || !GraphUtils.CanAccept(target, item))
                return "transfer_blocked";
            int before = item.unitCount;
            int destinationBefore = KindQuantity(target, item.identifier);
            int sourceBefore = KindQuantity(origin, item.identifier);
            GraphUtils.TryAcceptOnce(target, item);
            changed.Add(item);
            int arrived = KindQuantity(target, item.identifier) - destinationBefore;
            int left = sourceBefore - KindQuantity(origin, item.identifier);
            receipt["item_id"] = id;
            receipt["source"] = source;
            receipt["destination"] = retrieve ? "inventory" : "slot";
            receipt["requested_quantity"] = before;
            receipt["observed_quantity"] = arrived;
            receipt["same_item_at_destination"] = item.parentInventory == target;
            if (left != arrived || arrived < 0 || arrived > before) return "outcome_unknown";
            if (arrived == 0) return "not_moved";
            return arrived == before ? "moved" : "partial";
        }

        private static int KindQuantity(GameInventory storage, string kind)
        {
            int count = 0;
            foreach (var item in storage.childItems)
                if (item.identifier == kind) count += item.unitCount;
            return count;
        }
    }
}
