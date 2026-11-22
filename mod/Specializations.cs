using Newtonsoft.Json.Linq;

namespace WhiteDragon.Ledger
{
    internal static class Specializations
    {
        public static JObject View()
        {
            var options = new JArray();
            foreach (var scenario in StartingScenario.GetVisible())
            {
                options.Add(new JObject {
                    ["id"] = scenario.id, ["name"] = scenario.GetTitle(), ["hint"] = scenario.GetTag(),
                    ["description"] = scenario.GetDescription(), ["start_with"] = new JArray(scenario.GetStartWith()),
                    ["can_start"] = scenario.CanStart(), ["skip_intro"] = scenario.skipIntro
                });
            }
            return new JObject { ["selected_id"] = NewGameSpecializationUI.Selected?.id, ["options"] = options };
        }

        public static string Choose(string action, string id, JObject receipt)
        {
            if (action != "choose") return "unknown_action";
            var scenario = StartingScenario.GetVisible().Find(option => option.id == id);
            if (scenario == null || !scenario.CanStart()) return "specialization_unavailable";
            var menu = MainMenuUIController.Instance;
            if (menu == null) return "menu_unavailable";
            menu.OnScenarioContinue(scenario);
            receipt["scenario_id"] = scenario.id;
            receipt["name"] = scenario.GetTitle();
            return "specialization_submitted";
        }
    }
}
