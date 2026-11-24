using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace WhiteDragon.Ledger
{
    public sealed partial class InventoryObserver
    {
        private string itemMenuToken;
        private JObject previousItemMenu;
        private bool itemMenuUsed;
        private static readonly string[] MenuVerbs = { "open", "use", "equip", "activate", "configure", "toggle", "unload", "pin" };

        private static JObject BaseItemMenu()
        {
            var labels = new JObject();
            labels["unequip"] = "解除裝備";
            foreach (var verb in MenuVerbs)
                labels[verb] = LocHelper.GetLocalizedMechanic("handler_btn_" + verb);
            return labels;
        }

        private static JObject ItemOptions(GameItem item)
        {
            bool opens = item.contentWindow != null && !item.contentWindow.isLocked && GeneralHelper.IsItemOwned(item);
            bool selects = item.MaySelectSlotItem() && item.CanSelectSlotItem();
            var available = new JArray();
            if (opens) available.Add("open");
            if (selects) { available.Add("use"); available.Add("equip"); }
            if (item.MayActivateSlotItem() && item.CanActivateSlotItem()) available.Add("activate");
            if (item.MayConfigureSlotItem() && item.CanConfigureSlotItem()) available.Add("configure");
            if (item.MayToggleSlotItem() && item.CanToggleSlotItem()) available.Add("toggle");
            if (opens && !item.IsTag("IS_UNLOAD_DISABLED_TAG")) available.Add("unload");
            if (ItemPinHelper.MayPin(item)) available.Add("pin");
            var labels = new JObject();
            if (available.Values<string>().Contains("activate") && !AppraisalRankHelper.IsCertificate(item))
            {
                if (item.IsTag("DOCUMENT")) labels["activate"] = LocHelper.GetLocalizedMechanic("handler_btn_read");
                else if (item.IsTag("NARCOTIC") || item.IsTag("SUBSTANCE") || item.IsTag("ALCOHOL") ||
                    item.IsTag("MEDICAL") || item.IsTag("LIQUID_LUCK_TAG"))
                    labels["activate"] = LocHelper.GetLocalizedMechanic("handler_btn_consume");
            }
            if (available.Values<string>().Contains("toggle") && item.identifier == "ammo_box")
                labels["toggle"] = LocHelper.GetLocalizedMechanic(item.IsTag("AMMO_BOX_PACKAGED_TAG") ? "handler_btn_unpack" : "handler_btn_pack");
            if (available.Values<string>().Contains("pin") && ItemPinHelper.IsPinned(item))
                labels["pin"] = LocHelper.GetLocalizedMechanic("handler_btn_unpin");
            return new JObject { ["enabled"]=available, ["labels"]=labels };
        }

        private string TargetStatus(GameItem source, int targetId, out GameItem target)
        {
            if (!itemsById.TryGetValue(targetId,out target) || target == null || target == source ||
                GeneralHelper.IsDisplayOnly(target) || GeneralHelper.IsItemLocked(target)) return "target_unavailable";
            var location=StockLocation(target);
            if (location==null) return "target_unavailable";
            if (!GeneralHelper.IsItemEffectivelyOwned(target))
            {
                var shop=EmporiumEntry.Instance;
                bool offered=target.parentInventory==shop.frontInvinvElement && shop.frontInvWindow.IsVisible() ||
                    target.parentInventory==shop.backInvinvElement && shop.backInvWindow.IsVisible();
                if (!offered || CounterStage()!="ready" || !source.IsTag("CAN_USE_ON_NON_OWNED_TAG"))
                    return "target_not_offered";
            }
            return CanUseOnItem(source,target) ? "available" : "target_blocked";
        }

        private string RefreshMenuToken(bool seen, bool same, JObject view, JObject options, JObject receipt)
        {
            if (seen && same && !itemMenuUsed) return null;
            bool optionsChanged = seen && !itemMenuUsed && previousItemMenu != null &&
                !JToken.DeepEquals(previousItemMenu["options"], options);
            string reason = optionsChanged ? "options_changed"
                : !seen ? "token_expired"
                : itemMenuUsed ? "token_already_used"
                : "state_changed";
            itemMenuToken = Guid.NewGuid().ToString("N");
            itemMenuUsed = false;
            previousItemMenu = view;
            receipt["menu_token"] = itemMenuToken;
            receipt["enabled"] = options["enabled"];
            receipt["labels"] = options["labels"];
            receipt["reason"] = reason;
            return optionsChanged ? "menu_changed" : "stale_menu_token";
        }

        private string ItemMenuAction(JObject request, JObject receipt)
        {
            int id = (int)request["item_id"];
            if (!itemsById.TryGetValue(id, out var item) || item == null || GeneralHelper.IsDisplayOnly(item) ||
                !GeneralHelper.IsItemEffectivelyOwned(item)) return "item_unavailable";
            var location = StockLocation(item);
            string area = (string)location?["area"];
            if (area != "inventory" && area != "showcase" && !(area == "back" && phase == "CLOSED")) return "item_unavailable";
            var options = ItemOptions(item);
            if (ItemSelectHandler.current?.IsEquipped == true && ItemSelectHandler.current.GetEquippedItem() == item)
                ((JArray)options["enabled"]).Add("unequip");
            if (ModuleRemoval(item,id) is JObject removal) options["removal"]=removal;
            var view = new JObject { ["item_id"]=id, ["location"]=location, ["generation"]=generation,
                ["revision"]=sequence, ["phase"]=phase, ["visit"]=visitId, ["options"]=options };
            string token = (string)request["menu_token"];
            bool seen = token != null && token == itemMenuToken;
            bool same = JToken.DeepEquals(view,previousItemMenu);
            receipt["item_id"] = id;
            if ((string)request["mode"] == "read")
            {
                if (!seen || !same) DescribeFilter(item,receipt);
                var priorRemoval = seen ? previousItemMenu["options"]["removal"] : null;
                if (!JToken.DeepEquals(priorRemoval,options["removal"]))
                    receipt["removal"] = options["removal"]?.DeepClone() ?? JValue.CreateNull();
                if (!seen) receipt["base_actions"] = BaseItemMenu();
                if (seen && same && !itemMenuUsed) { receipt["menu_token"]=token; return "unchanged"; }
                if (seen)
                {
                    var old = previousItemMenu["options"];
                    receipt["added"] = new JArray(options["enabled"].Values<string>().Except(old["enabled"].Values<string>()));
                    receipt["removed"] = new JArray(old["enabled"].Values<string>().Except(options["enabled"].Values<string>()));
                    var labelChanges = new JObject();
                    foreach (var key in MenuVerbs)
                        if (!JToken.DeepEquals(old["labels"][key],options["labels"][key]))
                            labelChanges[key] = options["labels"][key]?.DeepClone() ?? JValue.CreateNull();
                    if (labelChanges.HasValues) receipt["label_changes"] = labelChanges;
                }
                else { receipt["enabled"]=options["enabled"]; receipt["labels"]=options["labels"]; }
                if (!same || itemMenuUsed) itemMenuToken = Guid.NewGuid().ToString("N");
                itemMenuUsed = false;
                previousItemMenu = view;
                receipt["menu_token"] = itemMenuToken;
                return "observed";
            }
            if ((string)request["mode"] == "targets")
            {
                if (RefreshMenuToken(seen, same, view, options, receipt) is string staleStatus) return staleStatus;
                if (!options["enabled"].Values<string>().Contains("use")) return "option_unavailable";
                if (!(request["target_ids"] is JArray candidates) || candidates.Count==0 || candidates.Count>64 ||
                    candidates.Any(n=>n.Type!=JTokenType.Integer || (long)n<=0 || (long)n>int.MaxValue)) return "invalid_targets";
                var targets=new JArray();
                foreach (int candidate in candidates.Values<int>().Distinct())
                {
                    string state=TargetStatus(item,candidate,out var targetItem);
                    var row=new JObject { ["id"]=candidate, ["status"]=state };
                    if (state=="available") row["name"]=targetItem.GetDisplayName(true);
                    targets.Add(row);
                }
                receipt["targets"]=targets;
                receipt["menu_token"]=token;
                return "observed";
            }
            if ((string)request["mode"] != "choose") return "invalid_action";
            if (RefreshMenuToken(seen, same, view, options, receipt) is string chooseStale) return chooseStale;
            string verb = (string)request["verb"];
            if (!options["enabled"].Values<string>().Contains(verb)) return "option_unavailable";
            if (Interactable.isPaused || GeneralHelper.IsItemLocked(item) || PlayerStore.Instance.lockAllInventoryFlag != 0 ||
                PlayerStore.Instance.lockAllFlag != 0 || order != null || staged != null || stagedBundle.Count > 0 ||
                GenericConfirmationUIManager.Instance?.panel?.activeInHierarchy == true ||
                ItemPinHelper.BlocksClicks || ItemMouseDragHandler.current?.IsDraggingItem == true)
                return "interaction_blocked";
            // The native secondary click first cancels an equipped tool.
            if (verb != "unequip" && ItemSelectHandler.current?.IsEquipped == true &&
                !(verb=="use" && ItemSelectHandler.current.GetEquippedItem()==item))
            {
                receipt["equipped_id"] = LocalId(ItemSelectHandler.current.GetEquippedItem());
                receipt["next_action"] = "unequip";
                return "tool_equipped";
            }
            receipt["verb"] = verb;
            int targetId = (int?)request["target_id"] ?? 0;
            string water = WaterOperation(item);
            if (verb == "use" && water != null && (targetId==0 || water!="pour" ||
                (itemsById.TryGetValue(targetId,out var vessel) && IsWaterContainerItem(vessel) && IsWaterContainerItem(item))))
            {
                receipt["next_tool"]="water_use";
                receipt["source_id"]=id;
                receipt["operation"]=water;
                if (targetId>0) receipt["target_id"]=targetId;
                return "target_required";
            }
            GameItem target=null;
            if (verb=="use")
            {
                receipt["menu_token"]=token;
                if (targetId==0)
                {
                    receipt["next_mode"]="targets";
                    return "target_required";
                }
                string state=TargetStatus(item,targetId,out target);
                if (state!="available") return state;
                receipt.Remove("menu_token");
            }
            itemMenuUsed=true;
            if (verb=="open")
                return WorkstationAction(new JObject {["action"]="inspect_window", ["owner_id"]=id, ["open_if_closed"]=true},receipt);
            bool confirmationWasOpen=NativeItemPanels.ConfirmationOpen;
            switch (verb)
            {
                case "activate": item.ActivateSlotItem(); break;
                case "configure": item.ConfigureSlotItem(); break;
                case "toggle": item.ToggleSlotItem(); break;
                case "pin": ItemPinHelper.TogglePinned(item); receipt["pinned"]=ItemPinHelper.IsPinned(item); break;
                case "unload": item.TryUnloadAll(skipPinned:true); break;
                case "equip":
                    if (ItemSelectHandler.current == null) return "interaction_blocked";
                    ItemSelectHandler.current.TryEquipItem(item);
                    return ItemSelectHandler.current.GetEquippedItem() == item ? "equipped" : "equip_unconfirmed";
                case "unequip": ItemSelectHandler.current.EndSelect(); return "unequipped";
                case "use":
                    bool wasStuck = target.IsTag("MODULE_STUCK_TAG");
                    UseOnItem(item,target);
                    if (wasStuck)
                    {
                        receipt["module_stuck"]=target.IsTag("MODULE_STUCK_TAG");
                        if (!(bool)receipt["module_stuck"] && target.parentInventory?.GetParentItem() is GameItem machine)
                        {
                            receipt["owner_id"]=LocalId(machine);
                            receipt["next_tool"]="inspect_item_window";
                        }
                    }
                    changed.Add(target);
                    receipt["target_id"]=targetId;
                    break;
            }
            changed.Add(item);
            receipt["remaining_units"] = item.parentInventory == null ? 0 : item.unitCount;
            NativeItemPanels.CaptureConfirmation(confirmationWasOpen);
            string panel=NativeItemPanels.ActiveKey();
            if (panel!=null)
            {
                receipt["panel"]=panel;
                receipt["next_tool"]="item_panel";
                // A scan is complete as soon as the native scanner opens; return it without another read.
                if (panel.Split(new[]{','}).Contains("chemical_scanner") && NativeItemPanels.ScannerReading() is JObject scan)
                    receipt["scan"]=scan;
            }
            return "invoked";
        }
    }
}
