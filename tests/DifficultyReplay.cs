using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace WhiteDragon.Ledger
{
    public class Difficulty
    {
        public string id;
        public List<string> lines = new List<string>();
        public string GetTitle() => "title " + id;
        public string GetSubtitle() => "subtitle " + id;
        public static List<string> GetEffectLines(Difficulty card) => card.lines;
    }

    public static class NewGameDifficultyUI
    {
        public static bool IsOpen;
        private static List<Difficulty> difficulties = new List<Difficulty>();
        private static int page, selected;
        public static int clicks;
        public static Difficulty Selected => difficulties[selected];
        private static void TurnPage(int delta) { page += delta; }
        private static void OnPlateClicked(string key)
        {
            int index = int.Parse(key);
            if (index / 3 != page) throw new Exception("Card not on current page");
            selected = index;
            NewGameData.Instance.difficultyId = Selected.id;
            clicks++;
        }
        public static void Open()
        {
            difficulties.Clear();
            foreach (string id in new[] { "cozy", "normal", "hard", "challenge", "modded" })
                difficulties.Add(new Difficulty { id = id, lines = new List<string> { "condition " + id, "note " + id } });
            page = clicks = 0;
            selected = 1;
            NewGameData.Instance.difficultyId = Selected.id;
            IsOpen = true;
        }
    }

    public sealed partial class InventoryObserver
    {
        private void ReplayDifficulty()
        {
            NewGameDifficultyUI.Open();
            Expect(!CanObserveStore() && sceneStatus == DifficultyPicker, "route to difficulty before store");
            var receipt = new JObject();
            var request = JourneyRequest("starting_difficulty");
            string Send(string mode, string id = "")
            {
                request["action"] = mode;
                request["difficulty_id"] = id;
                request["view_token"] = (string)receipt["view_token"];
                receipt = new JObject();
                return ActOnJourney(request, receipt);
            }
            Expect(Send("read") == "observed", "read all cards");
            var view = receipt["view"];
            Expect((string)view["selected_id"] == "normal" && (string)view["baseline_id"] == "normal" &&
                ((JArray)view["options"]).Count == 5 && (string)view["options"][4]["effect_lines"][1] == "note modded" &&
                (int)view["page"] == 1 && NewGameDifficultyUI.clicks == 0, "read second page without changing selection");
            Expect(Send("read") == "unchanged", "compact reread");
            NewGameData.Instance.endlessMode = true;
            Expect(Send("select","hard") == "view_changed" && NewGameDifficultyUI.clicks == 0, "reject stale settings");
            Expect(Send("read") == "observed" && Send("select","absent") == "unknown_difficulty", "unknown id");
            Expect(Send("read") == "observed" && Send("start") == "unknown_action" && NewGameDifficultyUI.clicks == 0,
                "no start action");
            Expect(Send("read") == "observed" && Send("select","challenge") == "difficulty_selected" &&
                (int)receipt["page"] == 2 && (string)receipt["selected_id"] == "challenge" &&
                NewGameDifficultyUI.IsOpen && NewGameData.Instance.endlessMode, "native cross-page card click only");
            Expect(ActOnJourney(request,new JObject()) == "view_changed" && NewGameDifficultyUI.clicks == 1,
                "old token cannot click twice");
            Expect(Send("read") == "observed" && Send("select","normal") == "difficulty_selected" &&
                (int)receipt["page"] == 1 && NewGameDifficultyUI.IsOpen, "return to normal without starting");
            NewGameDifficultyUI.IsOpen = false;
            Expect(Send("read") == "unavailable_in_scene", "leave difficulty page");
            CanObserveStore();
            MarkSceneReady(ShopScene,"當鋪");
            Console.WriteLine("Difficulty replay passed.");
        }
    }
}
