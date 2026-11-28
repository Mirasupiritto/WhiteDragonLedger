using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace WhiteDragon.Ledger
{
    public partial class GameItem
    {
        public Func<string, GameItem> scratchTagReader;
        public Action onTestActivate;
    }

    public class ScratchInput
    {
        public static bool isScratching;
        public int slot, starts, reveals;
        public void OnAnyScratch() { starts++; }
        public Window gameObject = new Window { activeInHierarchy = true };
        public void RevealAll() { if (starts != 1) throw new Exception("missing native scratch start"); reveals++; }
    }

    public class ScratcherUIManager
    {
        public static ScratcherUIManager Instance = new ScratcherUIManager();
        public Window ui = new Window(), back = new Window();
        public GameItem currentActiveScratcherItem;
        public List<ScratchInput> scratchInputs = new List<ScratchInput>();
        public class Texture { public string name; }
        public List<Texture> symbolTextures = new List<Texture> { new Texture { name = "fruit" } };
        public bool[] scratched = new bool[4];
        public void OnScratchComplete(int slot) { scratched[slot] = true; }
        public void CloseUI() { ui.activeInHierarchy = false; currentActiveScratcherItem = null; ScratchInput.isScratching = false; }
    }

    public sealed partial class InventoryObserver
    {
        private void ReplayScratchCard()
        {
            var ui = ScratcherUIManager.Instance;
            var card = new GameItem { uniqueId = 122, parentInventory = EmporiumEntry.Instance.invElement };
            card.waterTags.Add("SCRATCHER_TAG");
            var index = LocalId(card);
            foreach (int position in new[] {1, 2, 3}) ui.scratchInputs.Add(new ScratchInput { slot = position });
            card.onTestActivate = () => { ui.currentActiveScratcherItem = card; ui.ui.activeInHierarchy = true; };
            card.scratchTagReader = key =>
            {
                if (key.StartsWith("SCRATCHER_STATE"))
                    return new GameItem { associatedId = ui.scratched[int.Parse(key.Substring("SCRATCHER_STATE".Length))] ? 1 : 0 };
                if (key != "SCRATCHER_SYMBOL2" || !ui.scratched[2])
                    throw new Exception("read a covered or unselected symbol: " + key);
                return new GameItem { associatedId = 1 };
            };
            var request = JourneyRequest("scratch_card");
            request["item_id"] = index;
            request["save_id"] = 121;
            var result = new JObject();
            Expect(ActOnJourney(request, result) == "card_unavailable", "reject different persistent ID");
            request["save_id"] = 122;
            Expect(ActOnJourney(request, result) == "observed" && !((JArray)result["view"]["scratched_positions"]).HasValues, "blank card");
            request["view_token"] = result["view_token"];
            request["action"] = "open";
            result = new JObject();
            Expect(ActOnJourney(request, result) == "opened" && result["symbol"] == null, "open without exposing symbols");
            request["view_token"] = result["view_token"];
            request["action"] = "reveal";
            request["position"] = 2;
            result = new JObject();
            Expect(ActOnJourney(request, result) == "revealed" && (int)result["symbol"]["id"] == 1 && (int)result["position"] == 2, "selected result");
            Expect(ActOnJourney(request, new JObject()) == "view_changed", "reject consumed view");
            request["view_token"] = result["view_token"];
            Expect(ActOnJourney(request, new JObject()) == "already_revealed", "reject second scratch");
            Expect(ui.scratchInputs[0].reveals == 0 && ui.scratchInputs[1].reveals == 1 && ui.scratchInputs[2].reveals == 0 &&
                !ui.scratched[1] && ui.scratched[2] && !ui.scratched[3], "other two coatings intact");
            request.Remove("position");
            request["action"] = "read";
            result = new JObject();
            Expect(ActOnJourney(request, result) == "observed" && (string)result["view"]["panel"] == "front", "re-read open card");
            request["action"] = "close";
            request["view_token"] = result["view_token"];
            result = new JObject();
            Expect(ActOnJourney(request, result) == "closed" && !ui.ui.activeInHierarchy && ui.currentActiveScratcherItem == null, "close open card panel");
            request["view_token"] = result["view_token"];
            result = new JObject();
            Expect(ActOnJourney(request, result) == "already_closed", "close when already closed");
            var card2 = new GameItem { uniqueId = 123, parentInventory = EmporiumEntry.Instance.invElement };
            card2.waterTags.Add("SCRATCHER_TAG");
            var index2 = LocalId(card2);
            card2.onTestActivate = () => { ui.currentActiveScratcherItem = card2; ui.ui.activeInHierarchy = true; };
            card2.scratchTagReader = key => new GameItem { associatedId = 0 };
            var request2 = JourneyRequest("scratch_card");
            request2["item_id"] = index2;
            request2["save_id"] = 123;
            result = new JObject();
            Expect(ActOnJourney(request2, result) == "observed" && (string)result["view"]["panel"] == "closed", "second card sees closed panel");
            request2["view_token"] = result["view_token"];
            request2["action"] = "open";
            result = new JObject();
            Expect(ActOnJourney(request2, result) == "opened" && ui.currentActiveScratcherItem == card2, "open second card after closing first");
            request["action"] = "read";
            result = new JObject();
            Expect(ActOnJourney(request, result) == "observed" && (string)result["view"]["panel"] == "occupied", "first card sees occupied panel");
            request["action"] = "open";
            request["view_token"] = result["view_token"];
            Expect(ActOnJourney(request, new JObject()) == "another_card_open", "cannot open while occupied");
            request["action"] = "read";
            result = new JObject();
            ActOnJourney(request, result);
            request["action"] = "close";
            request["view_token"] = result["view_token"];
            result = new JObject();
            Expect(ActOnJourney(request, result) == "closed" && !ui.ui.activeInHierarchy && ui.currentActiveScratcherItem == null, "close occupied panel cleanly");
            // Test reveal_all on a card with slot 2 already revealed (reveals slots 1 and 3 in one call)
            card.scratchTagReader = key =>
            {
                if (key.StartsWith("SCRATCHER_STATE"))
                    return new GameItem { associatedId = ui.scratched[int.Parse(key.Substring("SCRATCHER_STATE".Length))] ? 1 : 0 };
                int s = int.Parse(key.Substring("SCRATCHER_SYMBOL".Length));
                if (!ui.scratched[s]) throw new Exception("read covered symbol: " + key);
                return new GameItem { associatedId = 1 };
            };
            request["action"] = "read";
            result = new JObject();
            Expect(ActOnJourney(request, result) == "observed", "read before reveal_all");
            request["action"] = "open";
            request["view_token"] = result["view_token"];
            result = new JObject();
            Expect(ActOnJourney(request, result) == "opened", "reopen card for reveal_all");
            request["action"] = "reveal_all";
            request["view_token"] = result["view_token"];
            result = new JObject();
            Expect(ActOnJourney(request, result) == "revealed" &&
                ((JArray)result["symbols"]).Count == 2 &&
                (int)result["symbols"][0]["position"] == 1 && (int)result["symbols"][1]["position"] == 3 &&
                (string)result["view_token"] != null && ui.scratched[1] && ui.scratched[2] && ui.scratched[3],
                "reveal_all scratches remaining slots and returns symbols + view_token");
            request["view_token"] = result["view_token"];
            Expect(ActOnJourney(request, new JObject()) == "already_revealed", "reveal_all rejects already finished card");
            request["action"] = "close";
            request["view_token"] = result["view_token"];
            ActOnJourney(request, new JObject());
            itemsById.Remove(index2);
            itemsById.Remove(index);
        }
    }
}
