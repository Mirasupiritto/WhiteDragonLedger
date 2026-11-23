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

        private static void WalkVisible(PixelElement element, string path, string role, string label, Action<PixelElement,string,string,string> leaf)
        {
            if (element == null || MachineryContainerUtils.IsHidden(element)) return;
            if (element is MachineryPanel panel)
            {
                WalkVisible(panel.content, path + "/body", panel.role ?? role, panel.label?.GetText() ?? label, leaf);
                return;
            }
            if (element is MachineryIntake intake)
            {
                WalkVisible(intake.content, path + "/intake", role, label, leaf);
                return;
            }
            if (element is MachineryTabs tabs)
            {
                if (tabs.selected >= 0)
                    WalkVisible(tabs.GetPage(tabs.selected), path + "/tab" + tabs.selected, role, tabs.GetCaption(tabs.selected), leaf);
                return;
            }
            if (element is MachineryPanelRow panels)
            {
                for (int i = 0; i < panels.Panels.Count; i++)
                    WalkVisible(panels.Panels[i], path + "/" + i, role, label, leaf);
                return;
            }
            if (element is MachineryStack stack)
            {
                for (int i = 0; i < stack.elements.Count; i++)
                    WalkVisible(stack.elements[i], path + "/" + i, role, label, leaf);
                return;
            }

            leaf(element, path, role, label);
        }

        private void DescribeControl(PixelElement element, string path, string role, string label, WindowRead read, JArray rows)
        {
            var row = new JObject { ["control"]=path, ["role"]=role, ["label"]=label };
            if (element is GameInventory storage)
            {
                row["type"] = "slot";
                row["insert_allowed"] = role != "output" && !storage.IsInsertLocked() && !read.Window.isLocked;
                row["remove_allowed"] = !storage.IsRemoveLocked() && !read.Window.isLocked;
                if (InventoryCapacity(storage) is JObject cap) row["capacity"] = cap;
                var items = new JArray();
                foreach (var child in storage.childItems)
                {
                    if (GeneralHelper.IsDisplayOnly(child)) continue;
                    var entry = new JObject { ["id"]=LocalId(child), ["save_id"]=child.GetUniqueID(), ["name"]=child.GetDisplayName(true),
                        ["kind"]=child.identifier, ["quantity"]=child.unitCount };
                    AddGeometryFields(child, entry);
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
            WalkVisible(read.Window.childElement, "content", null, null,
                (element, path, role, label) => DescribeControl(element, path, role, label, read, rows));
            DescribeMachine(owner,read,rows);
            read.View = new JObject { ["owner_id"]=LocalId(owner), ["save_id"]=owner.GetUniqueID(), ["name"]=owner.GetDisplayName(true),
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
            string action = (string)request["action"];
            var shop = EmporiumEntry.Instance;
            bool archiveRequest = action == "inspect_archive" || action == "close_archive";
            GameItem owner;
            if (archiveRequest) owner = shop.dossier;
            else if (!itemsById.TryGetValue((int)request["owner_id"], out owner)) return "owner_unavailable";
            if (owner == null) return "owner_unavailable";
            bool inspecting = action == "inspect_window" || action == "inspect_archive";
            bool archive = owner == shop.dossier;
            JObject location = null;
            if (archive)
            {
                if (shop.docInvWindow == null || !shop.docInvWindow.IsVisible() || shop.docInvElement?.childItem != owner)
                    return "archive_unavailable";
                receipt["fixture"] = "archive";
            }
            else
            {
                location = StockLocation(owner);
                string area = (string)location?["area"];
                bool inspectBack = inspecting && phase == "CLOSED" && area == "back" &&
                    owner.parentInventory == shop.backInvinvElement && GeneralHelper.IsItemOwned(owner);
                if ((!inspectBack && area != "inventory" && area != "showcase") || !GeneralHelper.IsItemEffectivelyOwned(owner))
                    return "owner_unavailable";
            }
            int id = LocalId(owner);
            receipt["owner_id"] = id;
            var window = owner.contentWindow;
            if (window == null) return inspecting ? ReadDocument(owner, location, (string)request["since_token"], receipt) : "no_content_window";
            if (!window.IsVisible())
            {
                workstations.Remove(id);
                if (!inspecting || (bool?)request["open_if_closed"] != true) return "window_closed";
                if (phase == "OPEN" || window.isLocked || (!archive && GeneralHelper.IsItemLocked(owner)) || Interactable.isPaused ||
                    ItemMouseDoubleClickHandler.current == null) return "opening_unavailable";
                ItemMouseDoubleClickHandler.current.OpenContentAction(owner, (Vector2)Input.mousePosition);
                if (!window.IsVisible()) return "window_closed";
            }
            var read = ReadWindow(owner);
            if (location != null) read.View["location"] = location;
            workstations.TryGetValue(id, out var prior);
            if (inspecting)
            {
                bool continueView = prior != null && prior.Token == (string)request["since_token"];
                PublishWindow(id, read, receipt, continueView ? prior : null);
                return "observed";
            }
            if (phase != "MORNING" && phase != "CLOSED") return "shop_open";
            if (window.isLocked || (!archive && GeneralHelper.IsItemLocked(owner)) || Interactable.isPaused) return "locked";
            if (action == "close_archive")
            {
                if (prior == null || prior.Token != (string)request["window_token"] || prior.Window != window)
                    return "window_changed";
                window.Hide();
                workstations.Remove(id);
                return window.IsVisible() ? "close_unconfirmed" : "window_closed";
            }
            string control = (string)request["control"];
            if (prior == null || prior.Token != (string)request["window_token"] ||
                prior.Window != window || !JToken.DeepEquals(prior.View["location"],location) || !DecisionControls(prior).SequenceEqual(DecisionControls(read), JToken.EqualityComparer) ||
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
                if (action == "transfer_slots")
                    status = TransferBatch(owner, storage, row, location, request, receipt);
                else
                    status = TransferOne(owner, storage, row, location, (string)request["source"], (int)request["item_id"], receipt);
            }
            FlushChanges();
            receipt["owner_id"] = id;
            receipt["control"] = control;
            if (owner.contentWindow == window && window.IsVisible())
            {
                var updated = ReadWindow(owner);
                if (location != null) updated.View["location"] = StockLocation(owner);
                PublishWindow(id,updated,receipt,prior);
            }
            else receipt["window_closed"] = true;
            return status;
        }

        private string TransferBatch(GameItem owner, GameInventory storage, JObject row, JObject location,
            JObject request, JObject receipt)
        {
            var ids = request["item_ids"] as JArray;
            if (ids == null || ids.Count == 0 || ids.Any(id => id.Type != JTokenType.Integer || (long)id <= 0 || (long)id > int.MaxValue) ||
                ids.Values<int>().Distinct().Count() != ids.Count) return "invalid_item_ids";
            var results = new JArray();
            receipt["items"] = results;
            var window = owner.contentWindow;
            string source = (string)request["source"];
            string control = (string)row["control"];
            bool stopped = false;
            foreach (int id in ids.Values<int>())
            {
                var item = new JObject { ["item_id"] = id };
                results.Add(item);
                string status = "skipped";
                if (!stopped)
                {
                    if (owner.contentWindow != window || !window.IsVisible() || window.isLocked ||
                        (owner != EmporiumEntry.Instance.dossier && (GeneralHelper.IsItemLocked(owner) ||
                         !GeneralHelper.IsItemEffectivelyOwned(owner) || !JToken.DeepEquals(location, StockLocation(owner)))))
                        status = "window_changed";
                    else
                    {
                        // A native transfer can rebuild the window. Recheck the slot binding without relisting its contents.
                        bool stillBound = false;
                        WalkVisible(window.childElement, "content", null, null, (element, path, role, label) => {
                            if (path == control && ReferenceEquals(element, storage) && (source == "slot" || role != "output"))
                                stillBound = true;
                        });
                        status = stillBound ? TransferOne(owner, storage, row, location, source, id, item) : "window_changed";
                    }
                    stopped = status != "moved";
                }
                item["status"] = status;
            }
            return stopped ? "batch_incomplete" : "moved";
        }

        private string TransferOne(GameItem owner, GameInventory storage, JObject row, JObject location,
            string source, int id, JObject receipt)
        {
            if (source != "slot" && location?["containers"] is JArray parents &&
                parents.Any(parent => (int)parent["owner_id"] == id)) return "insert_blocked";
            bool retrieve = source == "slot";
            if (!retrieve && source != "inventory" && source != "showcase") return "invalid_source";
            var shop = EmporiumEntry.Instance;
            bool archive = owner == shop.dossier;
            if (!itemsById.TryGetValue(id, out var item) || item == owner || GeneralHelper.IsDisplayOnly(item))
                return "item_unavailable";
            // A fixed filing box is a fixture. Verify the document's ownership and its actual source slot.
            if (!(retrieve && archive ? GeneralHelper.IsItemOwned(item) : GeneralHelper.IsItemEffectivelyOwned(item)))
                return "item_unavailable";
            GameInventory origin = retrieve ? storage : source == "inventory" ? shop.invElement : shop.showcaseElement;
            GameInventory target = retrieve ? (GameInventory)shop.invElement : storage;
            if (item.parentInventory != origin) return "source_changed";
            if (!retrieve && (bool)row["insert_allowed"] == false) return "insert_blocked";
            if (origin.IsRemoveLocked() || target.IsInsertLocked() || !item.MayRemove())
                return "transfer_blocked";
            if (!GraphUtils.CanAccept(target, item))
            {
                if (!IsSpaceFailure(target, item)) return "transfer_blocked";
                receipt["reason"] = "no_space";
                AttachSpaceFailure(item, target, receipt);
                return "no_space";
            }
            string status = TransferAndMeasure(item,origin,target,receipt);
            if (status == "not_moved" && IsSpaceFailure(target, item))
            {
                receipt["reason"] = "no_space";
                AttachSpaceFailure(item, target, receipt);
                status = "no_space";
            }
            receipt["item_id"] = id;
            receipt["source"] = source;
            receipt["destination"] = retrieve ? "inventory" : "slot";
            if (archive && item.parentInventory == target)
            {
                receipt["name"] = item.GetDisplayName(true);
                receipt["actual_area"] = retrieve ? "inventory" : "archive";
            }
            return status;
        }
    }
}
