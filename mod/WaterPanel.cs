using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using TMPro;

namespace WhiteDragon.Ledger
{
    public sealed partial class InventoryObserver
    {
        private JObject observedWaterPanel;
        private string waterPanelToken;
        private readonly Dictionary<GameItem, JObject> stripReadings = new Dictionary<GameItem, JObject>();

        private static string VisibleText(TextMeshProUGUI label)
        {
            return label != null && label.isActiveAndEnabled ? label.text : null;
        }

        private string WaterPanelAction(JObject request, JObject receipt)
        {
            string mode = (string)request["mode"];
            var notice = GenericConfirmationUIManager.Instance;
            if (mode == "notice_close" || (notice?.panel != null && notice.panel.activeInHierarchy))
                return PurifierNoticeAction(mode,(string)request["panel_token"],receipt);
            var ui = WaterStripUIManager.Instance;
            if (ui == null || ui.panel == null || !ui.panel.activeInHierarchy)
            {
                observedWaterPanel = null;
                receipt["hint"]="To sample, use water_use with one unused strip as source_id and the bottle as target_id. For a boxed strip, inspect_item_window(owner_id=box id, open_if_closed=true). To view a spent strip, choose activate in item_menu.";
                return "panel_closed";
            }
            var bottle = ui.currentWaterContainer;
            bool owned = bottle != null && GeneralHelper.IsItemOwned(bottle);
            string tested = VisibleText(ui.testedTMP);
            var panel = new JObject {
                ["bottle_id"] = bottle == null ? JValue.CreateNull() : (JToken)LocalId(bottle),
                ["owned"] = owned, ["tested"] = tested, ["label"] = VisibleText(ui.labelTMP),
                ["purity"] = VisibleText(ui.purityTMP),
                ["can_match"] = owned && !string.IsNullOrWhiteSpace(tested) &&
                    ui.matchButton != null && ui.matchButton.isActiveAndEnabled &&
                    ui.matchButton.IsInteractable() && ui.CanMatch(bottle)
            };
            if (bottle != null && !string.IsNullOrWhiteSpace(tested))
                stripReadings[bottle] = new JObject { ["tested"]=tested, ["purity"]=panel["purity"] };
            if (mode == "read")
            {
                waterPanelToken = Guid.NewGuid().ToString("N");
                observedWaterPanel = panel;
                receipt["panel_token"] = waterPanelToken;
                receipt["panel"] = panel;
                receipt["available_strips"] = AvailableStrips();
                return "observed";
            }
            if (observedWaterPanel == null || waterPanelToken != (string)request["panel_token"] ||
                !JToken.DeepEquals(panel, observedWaterPanel)) return "panel_changed";
            if (mode != "close" && mode != "match") return "invalid_action";
            if (mode == "match" && ((bool)panel["can_match"] == false ||
                (int?)request["bottle_id"] != (int?)panel["bottle_id"])) return "match_unavailable";
            observedWaterPanel = null;
            receipt["bottle_id"] = panel["bottle_id"];
            if (mode == "close")
            {
                ui.Close();
                return ui.panel.activeInHierarchy ? "close_unconfirmed" : "panel_closed";
            }
            string before = BottleLabel(bottle);
            ui.OnMatch();
            string after = BottleLabel(bottle);
            changed.Add(bottle);
            receipt["previous_label"] = before;
            receipt["bottle_label"] = after;
            receipt["available_strips"] = AvailableStrips();
            return after != null && before != after ? "label_updated" : "match_unconfirmed";
        }
    }
}
