using Newtonsoft.Json.Linq;
using TMPro;
using System.Linq;

namespace WhiteDragon.Ledger
{
    public sealed partial class InventoryObserver
    {
        private bool observedVariety;
        private const string VarietyHint = "種類多樣：每種只出一件";

        private JObject PanelDemand()
        {
            var result = new JObject { ["source"] = "trade_panel", ["status"] = "panel_closed" };
            var ui = NegociationUIManager.Instance;
            if (ui == null || !ui.IsOpen || ui.uiObject == null || !ui.uiObject.activeInHierarchy) return result;
            var host = ui.uiObject.GetComponent<NegotiationTabletFrame>()?.infoHost;
            result["status"] = "panel_unavailable";
            if (host == null || !host.gameObject.activeInHierarchy) return result;

            var restrictions = new JArray();
            var interests = new JArray();
            JArray row = null;
            string restrictionHeading = LocHelper.GetLocalizedUI("ui_nego_restrictions");
            string interestHeading = LocHelper.GetLocalizedUI("ui_nego_interests");
            for (int i = 0; i < host.childCount; ++i)
            {
                var child = host.GetChild(i);
                if (!child.gameObject.activeInHierarchy) continue;
                // InfoRow creates temporary Probe/Tag copies while laying out a fresh panel.
                if (child.name == "Probe") { result["status"] = "panel_updating"; return result; }
                if (child.name == "InfoLabel")
                {
                    var header = child.GetComponent<TextMeshProUGUI>();
                    string title = header != null && header.isActiveAndEnabled ? header.text : null;
                    row = title == restrictionHeading ? restrictions : title == interestHeading ? interests : null;
                }
                else if (row != null && child.name == "Tag")
                {
                    var label = child.Find("Text")?.GetComponent<TextMeshProUGUI>();
                    if (label != null && label.isActiveAndEnabled && !string.IsNullOrWhiteSpace(label.text))
                        row.Add(label.text);
                }
            }
            result["status"] = "visible";
            result["restrictions"] = restrictions;
            result["interests"] = interests;
            bool showsVariety = restrictions.Values<string>().Contains(LocHelper.GetLocalizedUI("ui_nego_tag_variety"));
            if (restrictions.Count > 0) observedVariety = showsVariety;
            if (showsVariety)
            {
                result["max_per_kind"] = 1;
                result["allocation_hint"] = VarietyHint;
                result["sold_kinds"] = new JArray(merchant.varietyTrackingList.Distinct());
            }
            result["exclude_contraband"] = restrictions.Values<string>().Contains(LocHelper.GetLocalizedUI("ui_nego_tag_no_contraband"));
            return result;
        }
    }
}
