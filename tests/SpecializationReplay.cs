using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace WhiteDragon.Ledger
{
    public class StartingScenario
    {
        public string id;
        public bool playable = true, skipIntro;
        public static readonly List<StartingScenario> visible = new List<StartingScenario>();
        public static List<StartingScenario> GetVisible() => visible;
        public string GetTitle() => id + " title";
        public string GetTag() => "hint";
        public string GetDescription() => "description";
        public List<string> GetStartWith() => new List<string> { "credits", "storage" };
        public bool CanStart() => playable;
    }

    public static class NewGameSpecializationUI
    {
        public static bool IsOpen;
        public static StartingScenario Selected;
    }

    public class MainMenuUIController
    {
        public static MainMenuUIController Instance = new MainMenuUIController();
        public string submitted;
        public void OnScenarioContinue(StartingScenario scenario)
        {
            submitted = scenario.id;
            NewGameSpecializationUI.IsOpen = false;
        }
    }

    public sealed partial class InventoryObserver
    {
        private void ReplaySpecializations()
        {
            for (int i = 0; i < 12; i++)
                StartingScenario.visible.Add(new StartingScenario { id = "scenario_" + i, playable = i != 11 });
            NewGameSpecializationUI.Selected = StartingScenario.visible[0];
            NewGameSpecializationUI.IsOpen = true;
            Expect(!CanObserveStore() && sceneStatus == SpecializationPicker, "new game bypasses store");
            var request = JourneyRequest("specializations");
            var result = new JObject();
            Expect(ActOnJourney(request, result) == "observed", "read native catalog");
            var options = (JArray)result["view"]["options"];
            Expect(options.Count == 12 && (string)options[10]["start_with"][1] == "storage" &&
                !(bool)options[11]["can_start"] && (string)result["view"]["selected_id"] == "scenario_0", "all pages and availability");
            request["view_token"] = result["view_token"];
            Expect(ActOnJourney(request, new JObject()) == "unchanged", "compact repeat read");
            request["action"] = "choose";
            request["scenario_id"] = "scenario_11";
            Expect(ActOnJourney(request, result) == "specialization_unavailable", "visible disabled choice");
            request = JourneyRequest("specializations");
            ActOnJourney(request, result);
            request["view_token"] = result["view_token"];
            request["action"] = "choose";
            request["scenario_id"] = "scenario_10";
            NewGameSpecializationUI.Selected = StartingScenario.visible[1];
            Expect(ActOnJourney(request, result) == "view_changed", "manual changes require reread");
            request = JourneyRequest("specializations");
            ActOnJourney(request, result);
            request["action"] = "choose";
            request["view_token"] = result["view_token"];
            request["scenario_id"] = "scenario_10";
            Expect(ActOnJourney(request, result) == "specialization_submitted" &&
                MainMenuUIController.Instance.submitted == "scenario_10", "choose on second page");
            Expect(ActOnJourney(request, result) == "unavailable_in_scene", "closed panel cannot resubmit");
            CanObserveStore();
            MarkSceneReady(ShopScene, "當鋪");
            System.Console.WriteLine("Specialization replay passed.");
        }
    }
}
