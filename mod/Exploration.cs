using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace WhiteDragon.Ledger
{
    public sealed partial class InventoryObserver
    {
        // Native game IDs are scoped to this observed trip. No night item enters the shop ID map.
        private sealed class NightInventory
        {
            public readonly JObject View = new JObject();
            public readonly Dictionary<string, GameInventory> Slots = new Dictionary<string, GameInventory>();
            public GameItem Bag;
            public bool Inline;

            public NightInventory()
            {
                var shop = EmporiumEntry.Instance;
                bool visible = shop.afterhourWindow != null && shop.afterhourWindow.IsVisible();
                View["location"] = MapUIManager.Instance.Current.Id;
                View["inventory_visible"] = visible;
                View["can_move"] = visible && !shop.afterhourWindow.isLocked && PlayerStore.Instance.lockAllInventoryFlag == 0;
                View["can_return"] = shop.afterhourInventory.childItems.Count == 0;
                View["can_return_abandoning_ground"] = !MapUIManager.Instance.IsInTransit;
                if ((string)View["location"] == "dumping_ground")
                {
                    View["can_scavenge"] = ScavHelper.CanScavenge();
                    View["scav_time_left"] = ScavHelper.GetScavTimeLeft();
                    View["minor_wound_chance"] = ScavHelper.GetMinorWoundChance();
                    View["major_wound_chance"] = ScavHelper.GetMajorWoundChance();
                }
                if (!visible) return;
                Slots.Add("ground", shop.afterhourInventory);
                Slots.Add("left", shop.afterhourPocketSlotInvLeft);
                Slots.Add("right", shop.afterhourPocketSlotInvRight);
                var equipment = shop.afterhourPocketSlotInvBackpack;
                Slots.Add("backpack", equipment);
                Bag = equipment.childItems.FirstOrDefault(i => i.contentWindow != null && GeneralHelper.IsItemOwned(i) && !GeneralHelper.IsDisplayOnly(i));
                if (Bag != null)
                {
                    var window = Bag.contentWindow;
                    Inline = equipment.stealUI && window.childElement != null && equipment.children.Any(n => n == window.childElement);
                    View["bag_id"] = Bag.GetUniqueID();
                    View["bag_display"] = Inline ? "inline" : window.IsVisible() ? "window" : "closed";
                    if ((Inline || window.IsVisible()) && !GeneralHelper.IsItemLocked(Bag) && (Inline || !window.isLocked))
                        WalkVisible(window.childElement, "bag", null, null, (element, path, role, label) =>
                        {
                            if (element is GameInventory slot) Slots.Add(path, slot);
                        });
                }
                var slots = new JObject();
                foreach (var entry in Slots)
                    slots[entry.Key] = new JArray(entry.Value.childItems.Where(i => !GeneralHelper.IsDisplayOnly(i)).Select(i =>
                        new JObject { ["id"]=i.GetUniqueID(), ["name"]=i.GetDisplayName(true), ["kind"]=i.identifier, ["quantity"]=i.unitCount }));
                View["slots"] = slots;
            }
        }

        private string ActOutside(NightInventory night, JObject request, JObject receipt)
        {
            string action = (string)request["action"];
            if (PlayerStore.Instance.lockAllFlag != 0) return "locked";
            var map = MapUIManager.Instance;
            if (action == "return")
            {
                bool abandonGround = (bool?)request["abandon_ground"] == true;
                if (!(bool)night.View["can_return"] && !abandonGround) return "collect_ground_first";
                if (abandonGround && !(bool)night.View["can_return"])
                {
                    receipt["abandoned_ground_count"] = EmporiumEntry.Instance.afterhourInventory.childItems.Count;
                    map.ReturnNow();
                }
                else
                {
                    map.Return();
                }
                return "return_requested";
            }
            if (action == "scavenge")
            {
                if ((bool?)night.View["can_scavenge"] != true) return "scavenge_unavailable";
                map.ScavengeButtonClick();
                receipt["scav_time_left_before"] = night.View["scav_time_left"];
                if (map.Current?.Id == "dumping_ground") receipt["scav_time_left_after"] = ScavHelper.GetScavTimeLeft();
                return "scavenge_requested";
            }
            if (!(bool)night.View["can_move"]) return "inventory_unavailable";
            int id = (int)request["item_id"];
            if (action == "open_bag" || action == "close_bag")
            {
                var bag = night.Bag;
                if (bag == null || bag.GetUniqueID() != id || GeneralHelper.IsItemLocked(bag)) return "bag_unavailable";
                if (night.Inline) return "inline_bag";
                if (bag.contentWindow.isLocked) return "locked";
                bool open = action == "open_bag";
                if (open == bag.contentWindow.IsVisible()) return "already_in_state";
                if (open)
                {
                    if (ItemMouseDoubleClickHandler.current == null) return "bag_unavailable";
                    ItemMouseDoubleClickHandler.current.OpenContentAction(bag, (Vector2)Input.mousePosition);
                }
                else bag.CloseContentWindow();
                receipt["bag_id"] = id;
                return bag.contentWindow.IsVisible() == open ? (open ? "opened" : "closed") : "unconfirmed";
            }
            if (action != "move") return "unknown_action";
            string from = (string)request["source"], to = (string)request["target"];
            if (!night.Slots.TryGetValue(from, out var origin) || !night.Slots.TryGetValue(to, out var target) || origin == target)
                return "slot_unavailable";
            var item = origin.childItems.FirstOrDefault(i => i.GetUniqueID() == id && !GeneralHelper.IsDisplayOnly(i));
            if (item == null || !GeneralHelper.IsItemOwned(item) || GeneralHelper.IsItemLocked(item) || !item.MayRemove() ||
                origin.IsRemoveLocked() || target.IsInsertLocked() || (item == night.Bag && to.StartsWith("bag")) ||
                !GraphUtils.CanAccept(target, item)) return "transfer_blocked";
            receipt["source"] = from;
            receipt["target"] = to;
            receipt["item_id"] = id;
            return TransferAndMeasure(item, origin, target, receipt, false);
        }
    }
}
