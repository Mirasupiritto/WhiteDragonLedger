using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace WhiteDragon.Ledger
{
    public sealed partial class InventoryObserver
    {
        private JObject waterPlan;
        private string waterUseToken;

        private string WaterArea(GameItem item)
        {
            var location = StockLocation(item);
            if (location == null) return null;
            var parents = (JArray)location["containers"];
            return parents.Count == 0 ? (string)location["area"] : "container/" + (int)parents.Last["owner_id"];
        }

        private static string WaterOperation(GameItem source)
        {
            if (source.identifier == "water_test_strip" && source.IsTag("WATER_TESTING_STRIP_TAG") &&
                !source.IsTag(WaterTestHelper.IS_SPENT)) return "test";
            if (source.identifier == "water_tablet" && source.IsTag("WATER_PURIFICATION_TABLET")) return "tablet";
            if (source.identifier == "portable_water_purifier") return "purify";
            if (source.identifier == "water_ration" || source.identifier == "water_ration_small") return "ration";
            return source.IsTag("LIQUID_CONTAINER_TAG") ? "pour" : null;
        }

        private string WaterUseAction(JObject request, JObject receipt)
        {
            bool apply = (string)request["mode"] == "apply";
            if (!apply && (string)request["mode"] != "preview") return "invalid_action";
            if (!apply) waterPlan = null;
            if (apply && (waterPlan == null || waterUseToken != (string)request["use_token"])) return "water_plan_changed";
            var shop = EmporiumEntry.Instance;
            if (!itemsById.TryGetValue((int)request["source_id"], out var source) || source == null ||
                !GeneralHelper.IsItemEffectivelyOwned(source))
            {
                receipt["reason"] = source == null ? "item_missing" : source == shop.faucet ? "use_faucet_as_target" : "not_owned";
                return "source_unavailable";
            }
            string origin = WaterArea(source);
            string operation = WaterOperation(source);
            if (origin == null || operation == null || GeneralHelper.IsDisplayOnly(source) ||
                GeneralHelper.IsItemLocked(source) || !source.MaySelectSlotItem() || !source.CanSelectSlotItem())
            {
                receipt["reason"] = source == shop.faucet ? "use_faucet_as_target" : origin == null ? "unknown_location" :
                    operation == null ? "unsupported_source" : "selection_locked";
                return "source_unavailable";
            }

            var destination = request["target_id"];
            GameItem target;
            string area;
            bool fixture = destination.Type == JTokenType.String;
            if (fixture)
            {
                area = (string)destination;
                if (operation != "pour") return "target_unavailable";
                if (area == "faucet" && shop.faucetWindow != null && shop.faucetWindow.IsVisible()) target = shop.faucet;
                else if (area == "drain" && shop.drainInvWindow != null && shop.drainInvWindow.IsVisible()) target = shop.drain;
                else return "target_unavailable";
            }
            else
            {
                if (!itemsById.TryGetValue((int)destination, out target) || target == null ||
                    !target.IsTag("LIQUID_CONTAINER_TAG")) return "target_unavailable";
                area = WaterArea(target);
            }
            if (area == null || target == null || source == target || GeneralHelper.IsDisplayOnly(target) ||
                GeneralHelper.IsItemLocked(target) || (operation == "ration" && target.IsTag("WINE_CONTAINER_TAG")))
                return "target_unavailable";
            bool offered = !fixture && !GeneralHelper.IsItemEffectivelyOwned(target);
            if (offered && (operation != "test" || !source.IsTag("CAN_USE_ON_NON_OWNED_TAG") ||
                CounterStage() != "ready" || !((area == "front" && shop.frontInvWindow.IsVisible()) ||
                    (area == "back" && shop.backInvWindow.IsVisible())))) return "offered_water_unavailable";
            var native = ItemBehaviourManager.current;
            var notice = GenericConfirmationUIManager.Instance;
            if (Interactable.isPaused || (notice?.panel != null && notice.panel.activeInHierarchy) || native == null ||
                !((source.MayTarget(target) && source.CanTarget(target)) ||
                  (native.MayTarget(source, target) && native.CanTarget(source, target))))
            {
                receipt["reason"] = Interactable.isPaused ? "paused" : notice?.panel?.activeInHierarchy == true ? "confirmation_open" :
                    !fixture && WaterHelper.GetCurrentCapacityUL(target) == 0 && (operation == "purify" || operation == "test" || operation == "tablet")
                        ? "container_empty" : "native_target_unavailable";
                return "target_blocked";
            }

            var plan = new JObject { ["revision"]=sequence, ["operation"]=operation, ["source_id"]=LocalId(source), ["from"]=origin,
                ["target_id"]=destination, ["target_item_id"]=LocalId(target), ["to"]=area, ["offered"]=offered,
                ["source_units"]=source.unitCount, ["phase"]=phase, ["visit"]=visitId };
            if (operation == "pour") plan["source_ul"] = WaterHelper.GetCurrentCapacityUL(source);
            if (!fixture) plan["target_ul"] = WaterHelper.GetCurrentCapacityUL(target);
            if (operation == "test") plan["available_strips"] = AvailableStrips();
            if (!apply)
            {
                waterPlan = plan;
                waterUseToken = Guid.NewGuid().ToString("N");
                receipt["plan"] = plan;
                receipt["use_token"] = waterUseToken;
                return "preview";
            }
            if (!JToken.DeepEquals(plan, waterPlan)) return "water_plan_changed";
            waterPlan = null;
            int charge = operation == "purify" ? DurabilityHelper.GetRealDurability(source) : 0;
            receipt["operation"] = operation;
            receipt["source_id"] = plan["source_id"];
            receipt["target_id"] = destination;
            // Native select/drag entry point runs registered behaviours first, then rechecks item callbacks.
            native.Target(source, target);
            if (source.MayTarget(target) && source.CanTarget(target)) source.Target(target);
            changed.Add(source);
            if (!fixture) changed.Add(target);
            if (operation == "test")
            {
                receipt["available_strips"] = AvailableStrips();
                if (!source.IsTag(WaterTestHelper.IS_SPENT) ||
                    source.GetTagReadonly(WaterTestHelper.ASSOCIATED_ITEM_ID).GetInt() != target.uniqueId)
                    return "use_unconfirmed";
                var ui = WaterStripUIManager.Instance;
                if (ui != null && ui.currentWaterContainer == target)
                    WaterPanelAction(new JObject { ["mode"]="read" }, receipt);
                return "sample_taken";
            }
            if (operation == "purify")
            {
                if (notice?.panel != null && notice.panel.activeInHierarchy)
                {
                    return PurifierNoticeAction("read",null,receipt);
                }
                int remaining = DurabilityHelper.GetRealDurability(source);
                receipt["charge_remaining"] = remaining;
                int used = charge - remaining;
                receipt["charge_used"] = used;
                return used > 0 ? "purifier_used" : "use_unconfirmed";
            }
            if (!fixture) receipt["target_change_ul"] = WaterHelper.GetCurrentCapacityUL(target) - (int)plan["target_ul"];
            if (operation == "pour")
            {
                int delta = WaterHelper.GetCurrentCapacityUL(source) - (int)plan["source_ul"];
                receipt["source_change_ul"] = delta;
                bool transferred = fixture ? (area == "drain" ? delta < 0 : delta != 0) :
                    delta < 0 && (int)receipt["target_change_ul"] > 0;
                return transferred ? "transferred" : "use_unconfirmed";
            }
            int units = source.parentInventory == null ? 0 : source.unitCount;
            int spent = (int)plan["source_units"] - units;
            receipt["units_used"] = spent;
            if (operation == "tablet") return spent == 1 ? "tablet_used" : "use_unconfirmed";
            return spent == 1 && (int)receipt["target_change_ul"] > 0 ? "ration_used" : "use_unconfirmed";
        }
    }
}
