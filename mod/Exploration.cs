using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace WhiteDragon.Ledger
{
    public sealed partial class InventoryObserver
    {
        private static JObject InjuryView()
        {
            var health = PlayerStore.Instance?.healthData;
            int woundState = health?.woundState ?? 0;
            bool stable = health?.isWoundStable ?? true;
            bool fresh = health?.isWoundedFresh ?? false;
            bool serious = woundState > 0 && HealthData.IsSeriouslyWounded();
            string severity = woundState <= 0 ? "none" : serious ? "major" : "minor";
            string name = severity == "none" ? "無傷勢" : severity == "major" ? "重傷" : "輕傷";
            string effect;
            if (severity == "none")
                effect = "無負面效果";
            else if (severity == "major")
                effect = "重傷無法夜間出門" + (fresh ? "且本次外出無法繼續拾荒" : "") +
                    (stable ? "；傷勢已穩定（睡覺每晚恢復，降至輕傷後可恢復夜間行動）"
                            : "；傷勢未穩定（過夜有 10% 機率惡化，可用繃帶止血穩定）");
            else
                effect = (fresh ? "本次外出受傷，今夜無法繼續在垃圾場拾荒" : "輕傷可夜間出門拾荒") +
                    (stable ? "；傷勢已穩定（睡覺每晚恢復）"
                            : "；傷勢未穩定（過夜有 10% 機率惡化為重傷，可用繃帶止血穩定）");
            return new JObject {
                ["severity"] = severity, ["name"] = name, ["effect"] = effect,
                ["wound_state"] = woundState, ["serious"] = serious, ["stable"] = stable, ["fresh"] = fresh
            };
        }

        private static string ScavengeBlockedReason()
        {
            if (PlayerStore.Instance?.healthData?.isWoundedFresh == true) return "injured";
            if (ScavHelper.GetMaxScavAttempts() <= 0) return "no_attempts";
            if (ScavHelper.GetScavTimeLeft() <= 0) return "out_of_time";
            if (MapUIManager.Instance != null && MapUIManager.Instance.IsInTransit) return "searching";
            return "unavailable";
        }

        private static string VisibleNotification()
        {
            var notifier = ReputationUIManager.Instance;
            if (notifier == null || notifier.reputationNotifier == null || !notifier.reputationNotifier.activeInHierarchy)
                return null;
            string text = notifier.reputationNotifierTMP?.text;
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

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
                View["injury"] = InjuryView();
                if (VisibleNotification() is string notice) View["notification"] = notice;
                if ((string)View["location"] == "dumping_ground")
                {
                    bool canScavenge = ScavHelper.CanScavenge();
                    View["can_scavenge"] = canScavenge;
                    View["scav_time_left"] = ScavHelper.GetScavTimeLeft();
                    View["minor_wound_chance"] = ScavHelper.GetMinorWoundChance();
                    View["major_wound_chance"] = ScavHelper.GetMajorWoundChance();
                    if (!canScavenge) View["scavenge_blocked_reason"] = ScavengeBlockedReason();
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
                if ((bool?)night.View["can_scavenge"] != true)
                {
                    if (night.View["scavenge_blocked_reason"] != null)
                        receipt["scavenge_blocked_reason"] = night.View["scavenge_blocked_reason"].DeepClone();
                    receipt["injury"] = night.View["injury"].DeepClone();
                    return "scavenge_unavailable";
                }
                int woundBefore = PlayerStore.Instance?.healthData?.woundState ?? 0;
                bool freshBefore = PlayerStore.Instance?.healthData?.isWoundedFresh ?? false;
                map.ScavengeButtonClick();
                receipt["scav_time_left_before"] = night.View["scav_time_left"];
                if (map.Current?.Id == "dumping_ground")
                {
                    receipt["scav_time_left_after"] = ScavHelper.GetScavTimeLeft();
                    if (!ScavHelper.CanScavenge()) receipt["scavenge_blocked_reason"] = ScavengeBlockedReason();
                }
                var afterInjury = InjuryView();
                receipt["injury"] = afterInjury;
                int woundAfter = (int)afterInjury["wound_state"];
                bool freshAfter = (bool)afterInjury["fresh"];
                if (woundAfter > woundBefore)
                    receipt["scavenge_outcome"] = (bool)afterInjury["serious"] ? "major_wound" : "minor_wound";
                else if (freshAfter && !freshBefore)
                    receipt["scavenge_outcome"] = "injured";
                else
                    receipt["scavenge_outcome"] = "completed";
                if (VisibleNotification() is string notice) receipt["notification"] = notice;
                if (ResponseInventory() != null) ObserveDecision("response_required", receipt);
                var confirm = GenericConfirmationUIManager.Instance;
                if (confirm != null && confirm.panel != null && confirm.panel.activeInHierarchy)
                    receipt["confirmation"] = new JObject {
                        ["message"] = confirm.bodyTMP?.text,
                        ["confirm_label"] = confirm.confirmButtonTMP?.text,
                        ["cancel_label"] = confirm.cancelButtonTMP?.text
                    };
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
