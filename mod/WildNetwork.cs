using System;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine.UI;

namespace WhiteDragon.Ledger
{
    public sealed partial class InventoryObserver
    {
        private const BindingFlags NetworkFields = BindingFlags.Static | BindingFlags.NonPublic;
        private static readonly FieldInfo NetworkFrameField = typeof(WildNetworkUI).GetField("frame", NetworkFields);
        private static readonly FieldInfo NetworkTabField = typeof(WildNetworkUI).GetField("currentTab", NetworkFields);
        private static readonly FieldInfo NetworkSelectionField = typeof(WildNetworkUI).GetField("selectedId", NetworkFields);
        private WildNetworkFrame observedNetworkFrame;
        private JObject previousNetwork;
        private string networkToken;

        private JObject ReadNetwork(WildNetworkFrame frame)
        {
            var store = PlayerStore.Instance;
            var tabs = new JArray();
            foreach (var slot in frame.tabBar.GetComponentsInChildren<WildNetworkSlot>())
            {
                var label = slot.GetComponentInChildren<TextMeshProUGUI>();
                tabs.Add(new JObject { ["id"]=slot.id, ["label"]=VisibleText(label),
                    ["enabled"]=NativeButton.Enabled(slot.GetComponent<Button>()) });
            }
            var services = new JArray();
            foreach (var slot in frame.gridContent.GetComponentsInChildren<WildNetworkSlot>())
                if (store.networkUpgrade.TryGetValue(slot.id, out var upgrade))
                    services.Add(new JObject { ["id"]=slot.id, ["title"]=upgrade.GetLocalizedTitle(),
                        ["unlocked"]=upgrade.IsUnlocked(), ["ready"]=upgrade.IsReady() });

            string selected = (string)NetworkSelectionField.GetValue(null);
            var view = new JObject { ["stage"]="wild_network", ["tab_id"]=NetworkTabField.GetValue(null).ToString(),
                ["tabs"]=tabs, ["services"]=services, ["selected_id"]=selected,
                ["title"]=VisibleText(frame.infoTitle), ["subtitle"]=VisibleText(frame.infoSubtitle),
                ["description"]=VisibleText(frame.infoDescription), ["message"]=VisibleText(frame.infoMessage),
                ["cooldown"]=VisibleText(frame.infoCooldown), ["price_text"]=VisibleText(frame.usePrice),
                ["button_text"]=VisibleText(frame.useLabel), ["cash"]=store.playerCash, ["favor"]=store.wildFavor,
                ["visit"]=visitId, ["can_buy"]=false };
            if (selected != null && services.Any(row => (string)row["id"] == selected))
            {
                var upgrade = store.networkUpgrade[selected];
                view["cost"] = upgrade.GetCost();
                view["can_buy"] = NativeButton.Enabled(frame.useButton) && upgrade.IsReady() && !upgrade.IsUnlocked();
            }
            if (observedNetworkFrame != frame || previousNetwork == null || !JToken.DeepEquals(previousNetwork, view))
            {
                observedNetworkFrame = frame;
                previousNetwork = (JObject)view.DeepClone();
                networkToken = Guid.NewGuid().ToString("N");
            }
            view["panel_token"] = networkToken;
            return view;
        }

        private string NetworkAction(JObject request, JObject receipt)
        {
            if (!WildNetworkUI.IsOpen) return "panel_closed";
            if (NetworkFrameField == null || NetworkSelectionField == null || NetworkTabField == null)
                return "unsupported_layout";
            var frame = (WildNetworkFrame)NetworkFrameField.GetValue(null);
            if (frame == null || frame.tabBar == null || frame.gridContent == null) return "panel_loading";
            var view = ReadNetwork(frame);
            string mode = (string)request["mode"];
            if (mode == "read")
            {
                if ((string)request["since_token"] == networkToken)
                { receipt["panel_token"] = networkToken; return "unchanged"; }
                receipt["panel"] = view;
                return "observed";
            }
            if ((string)request["panel_token"] != networkToken) return "panel_changed";
            string id = (string)request["target"];
            if (mode == "close")
            {
                WildNetworkUI.Close();
                previousNetwork = null;
                return WildNetworkUI.IsOpen ? "close_unconfirmed" : "closed";
            }
            if (mode == "tab" || mode == "select")
            {
                bool tab = mode == "tab";
                var root = tab ? frame.tabBar : frame.gridContent;
                var slot = root.GetComponentsInChildren<WildNetworkSlot>().FirstOrDefault(row => row.id == id);
                if (slot == null) return "unknown_target";
                if (tab)
                {
                    var button = slot.GetComponent<Button>();
                    if (!NativeButton.Enabled(button)) return "tab_unavailable";
                    button.onClick.Invoke();
                }
                else
                {
                    if (slot.onClick == null || !PlayerStore.Instance.networkUpgrade.ContainsKey(id)) return "service_unavailable";
                    slot.onClick(id);
                }
                previousNetwork = null;
                receipt["target"] = id;
                // Grid children from the old tab are destroyed by Unity at the end of this frame.
                string actual = tab ? NetworkTabField.GetValue(null).ToString() : (string)NetworkSelectionField.GetValue(null);
                return actual == id ? (tab ? "tab_selected" : "service_selected") : "selection_unconfirmed";
            }
            if (mode != "buy") return "invalid_action";
            if (id == null || id != (string)view["selected_id"] || view["cost"] == null ||
                (int?)request["cost"] != (int)view["cost"]) return "quote_changed";
            if (!(bool)view["can_buy"]) return "purchase_unavailable";
            var store = PlayerStore.Instance;
            var service = store.networkUpgrade[id];
            int beforeCash = store.playerCash, beforeFavor = store.wildFavor, beforeState = service.state;
            int beforeCooldown = service.cooldownCurrent;
            previousNetwork = null;
            frame.useButton.onClick.Invoke();
            int cashPaid = beforeCash - store.playerCash, favorPaid = beforeFavor - store.wildFavor;
            receipt["service_id"] = id;
            receipt["cost"] = view["cost"];
            receipt["cash_paid"] = cashPaid;
            receipt["favor_spent"] = favorPaid;
            receipt["cash_after"] = store.playerCash;
            receipt["favor_after"] = store.wildFavor;
            receipt["unlocked"] = service.IsUnlocked();
            receipt["cooldown"] = service.cooldownCurrent;
            bool applied = service.IsUnlocked() || service.state != beforeState || service.cooldownCurrent != beforeCooldown;
            return applied && cashPaid >= 0 && favorPaid >= 0 && cashPaid + favorPaid == (int)view["cost"]
                ? "purchased" : "purchase_unconfirmed";
        }
    }
}
