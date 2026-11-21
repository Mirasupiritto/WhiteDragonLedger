using System;
using Newtonsoft.Json.Linq;
using TMPro;

namespace WhiteDragon.Ledger
{
    public class WaterStripUIManager
    {
        public static WaterStripUIManager Instance = new WaterStripUIManager();
        public Window panel = new Window();
        public GameItem currentWaterContainer;
        public ReceiptButton matchButton = new ReceiptButton();
        public TextMeshProUGUI testedTMP = new TextMeshProUGUI { text = "Tested: rust water" };
        public TextMeshProUGUI labelTMP = new TextMeshProUGUI { text = "Label: base water" };
        public TextMeshProUGUI purityTMP = new TextMeshProUGUI { text = "60%" };
        public int matches;
        public bool CanMatch(GameItem bottle) => bottle.owned && bottle.waterLabel == null;
        public void Close() { panel.activeInHierarchy = false; }
        public void OnMatch()
        {
            matches++;
            currentWaterContainer.waterLabel = new ItemFeature { label = new ItemCondition { display = "rust water" } };
            labelTMP.text = "Label: rust water";
            matchButton.isActiveAndEnabled = false;
        }
    }

    public sealed partial class InventoryObserver
    {
        private void ReplayWaterPanel()
        {
            phase = "OPEN";
            var ui = WaterStripUIManager.Instance;
            var bottle = Item(961, "bottle", 2);
            var other = Item(962, "bottle", 2);
            var strip = Item(963, "water_test_strip", 0);
            stripSupplies.Add(strip);
            int unused = AvailableStrips();
            ui.currentWaterContainer = bottle;
            var request = new JObject { ["action"]="water_panel", ["mode"]="read", ["generation"]=generation,
                ["revision"]=sequence, ["expires_ms"]=DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds() };
            var result = new JObject();
            Expect(ActAtCounter(request, result) == "panel_closed" && result["panel"] == null, "closed hides test");
            ui.panel.activeInHierarchy = true;
            ui.purityTMP.isActiveAndEnabled = false;
            Expect(ActAtCounter(request, result) == "observed" && (int)result["panel"]["bottle_id"] == LocalId(bottle) &&
                (string)result["panel"]["tested"] == "Tested: rust water" && (string)result["panel"]["purity"] == null &&
                (int)result["available_strips"] == unused && ui.matches == 0, "read only visible assay");
            request["mode"] = "match";
            request["panel_token"] = result["panel_token"];
            request["bottle_id"] = LocalId(other);
            Expect(ActAtCounter(request, result) == "match_unavailable", "wrong bottle ID");
            request["bottle_id"] = LocalId(bottle);
            ui.currentWaterContainer = other;
            Expect(ActAtCounter(request, result) == "panel_changed", "player switched bottle");
            ui.currentWaterContainer = bottle;
            bottle.owned = false;
            request["mode"] = "read";
            ActAtCounter(request, result);
            Expect(!(bool)result["panel"]["can_match"], "merchant bottle is read only");
            request["mode"] = "match";
            request["panel_token"] = result["panel_token"];
            Expect(ActAtCounter(request, result) == "match_unavailable" && ui.matches == 0, "no relabeling merchant goods");
            bottle.owned = true;
            request["mode"] = "read";
            ActAtCounter(request, result);
            request["panel_token"] = result["panel_token"];
            request["mode"] = "match";
            Expect(ActAtCounter(request, result) == "label_updated" && (string)result["bottle_label"] == "rust water" &&
                (int)result["available_strips"] == unused && changed.Contains(bottle), "label confirmed and queued for ledger");
            Expect(ActAtCounter(request, result) == "panel_changed" && ui.matches == 1, "spent panel token");
            phase = "MORNING";
            request["mode"] = "read";
            ActAtCounter(request, result);
            request["mode"] = "close";
            request["panel_token"] = result["panel_token"];
            Expect(ActAtCounter(request, result) == "panel_closed" && !ui.panel.activeInHierarchy, "close without opening shop");
            Console.WriteLine("Water panel replay passed.");
        }
    }
}
