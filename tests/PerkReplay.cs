using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine.UI;

namespace WhiteDragon.Ledger
{
    public partial class StartingPerk
    {
        public string id, type = "NEUTRAL";
        public int cost, maxSlot;
        public List<string> incompatiblePerks = new List<string>();
        public string GetLocalizedDisplayName() => id;
        public string GetLocalizedDescription() => "effect " + id;
    }

    public class NewGameData
    {
        public string difficultyId;
        public bool skipIntro, endlessMode;
        public static NewGameData Instance = new NewGameData();
        public List<StartingPerk> startingPerks = new List<StartingPerk>();
    }

    public class NewGamePerksFrame
    {
        public NetworkRoot availableSlots = new NetworkRoot(), selectedSlots = new NetworkRoot();
        public Button continueButton = new Button();
    }


    public static class NewGamePerksUI
    {
        public const int MAX_PERK_POINT = 3;
        public static bool IsOpen;
        private static NewGamePerksFrame frame = new NewGamePerksFrame();
        private static List<StartingPerk> sorted;
        private static bool firstRun;
        private static int page, selectedPage;
        private static List<StartingPerk> Selected => NewGameData.Instance.startingPerks;
        private static int UsedPoints() => Selected.Sum(p => p.cost);
        private static int MaxPerkCount() => 4 + Selected.Sum(p => p.maxSlot);
        private static StartingPerk LockedBy(StartingPerk p) => Selected.FirstOrDefault(s =>
            s.id != p.id && (s.incompatiblePerks.Contains(p.id) || p.incompatiblePerks.Contains(s.id)));
        private static void TurnPage(int by) { page += by; Build(); }
        private static void TurnSelectedPage(int by) { selectedPage += by; Build(); }

        private static void Build()
        {
            var selectedHover = new HashSet<string>();
            WildNetworkSlot Slot(StartingPerk p, bool selectedRow)
            {
                return new WildNetworkSlot {
                    id = p.id,
                    onHover = (id, over) => { if (selectedRow && over) selectedHover.Add(id); },
                    onClick = id => {
                        if (firstRun) return;
                        if (selectedRow && selectedHover.Contains(id)) Selected.Remove(p);
                        else if (!Selected.Contains(p) && LockedBy(p) == null) Selected.Add(p);
                        Build();
                    }
                };
            }
            frame.availableSlots.children = sorted.Skip(page * 36).Take(36).Select(p => Slot(p,false)).ToArray();
            frame.selectedSlots.children = Selected.Skip(selectedPage * 10).Take(10).Select(p => Slot(p,true)).ToArray();
            frame.continueButton.interactable = UsedPoints() <= MAX_PERK_POINT && Selected.Count <= MaxPerkCount();
            frame.continueButton.onClick = () => { IsOpen = false; NewGameDifficultyUI.IsOpen = true; };
        }

        public static void Open(bool first)
        {
            firstRun = first;
            sorted = Enumerable.Range(0,37).Select(i => new StartingPerk { id = "p" + i }).ToList();
            sorted[0].cost = 4;
            sorted[1].cost = -1;
            sorted[1].incompatiblePerks.Add("p2");
            sorted[36].maxSlot = 3;
            Selected.Clear();
            page = selectedPage = 0;
            IsOpen = true;
            Build();
        }
    }

    public sealed partial class InventoryObserver
    {
        private void ReplayPerks()
        {
            NewGamePerksUI.Open(true);
            Expect(!CanObserveStore() && sceneStatus == PerkPicker, "perks page");
            var result = new JObject();
            var request = JourneyRequest("starting_perks");
            string Send(string action, string id = "")
            {
                request["action"] = action;
                request["perk_id"] = id;
                request["view_token"] = (string)result["view_token"];
                result = new JObject();
                return ActOnJourney(request, result);
            }
            Expect(Send("read") == "observed" && ((JArray)result["view"]["options"]).Count == 37, "full catalog");
            Expect(Send("add","p0") == "first_run_locked", "first run");
            NewGamePerksUI.Open(false);
            Expect(Send("add","p0") == "view_changed", "manual edit invalidates token");
            Expect(Send("read") == "observed" && Send("read") == "unchanged", "compact reread");
            Expect(Send("add","p0") == "added" && (int)result["state"]["points_left"] == -1, "tentative overspend");
            Expect(Send("continue") == "selection_invalid" && NewGamePerksUI.IsOpen, "continue blocked");
            Expect(Send("add","p1") == "added" && (bool)result["state"]["can_continue"], "negative perk pays cost");
            Expect(Send("add","p2") == "incompatible_perk" && (string)result["state"]["locked_by"]["p2"] == "p1", "native conflict");
            Expect(Send("add","p36") == "added" && (int)result["state"]["max_count"] == 7, "second page extends slots");
            Expect(Send("add","p36") == "already_selected" && Send("remove","p36") == "removed", "remove on overlapping rows");
            Expect(Send("add","p3") == "added" && Send("add","p4") == "added" && Send("add","p5") == "added", "over count");
            Expect(Send("continue") == "selection_invalid" && Send("remove","p5") == "removed", "fix count");
            Expect(Send("continue") == "difficulty_open" && Send("continue") == "unavailable_in_scene", "continue only once");
            NewGameDifficultyUI.IsOpen = false;
            CanObserveStore();
            MarkSceneReady(ShopScene,"當鋪");
            Console.WriteLine("Starting perks replay passed.");
        }
    }
}
