using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace WhiteDragon.Ledger
{
    internal static class StartingDifficulty
    {
        // The open UI keeps its own ordered snapshot of Difficulty.GetAll(), including modded cards.
        private const BindingFlags Flags = BindingFlags.Static | BindingFlags.NonPublic;
        private static readonly FieldInfo Cards = typeof(NewGameDifficultyUI).GetField("difficulties", Flags);
        private static readonly FieldInfo Page = typeof(NewGameDifficultyUI).GetField("page", Flags);
        private static readonly MethodInfo Turn = typeof(NewGameDifficultyUI).GetMethod("TurnPage", Flags);
        private static readonly MethodInfo Click = typeof(NewGameDifficultyUI).GetMethod("OnPlateClicked", Flags);
        internal static bool Supported => Cards != null && Page != null && Turn != null && Click != null;
        private static List<Difficulty> Catalog => (List<Difficulty>)Cards.GetValue(null);

        internal static JObject View()
        {
            var rows = new JArray();
            foreach (var card in Catalog)
                rows.Add(new JObject { ["id"] = card.id, ["name"] = card.GetTitle(), ["subtitle"] = card.GetSubtitle(),
                    ["effect_lines"] = new JArray(Difficulty.GetEffectLines(card)) });
            return new JObject { ["options"] = rows, ["baseline_id"] = "normal",
                ["selected_id"] = NewGameDifficultyUI.Selected?.id, ["page"] = (int)Page.GetValue(null) + 1,
                ["skip_intro"] = NewGameData.Instance.skipIntro, ["endless"] = NewGameData.Instance.endlessMode };
        }

        internal static string Select(string action, string id, JObject receipt)
        {
            if (action != "select") return "unknown_action";
            int index = Catalog.FindIndex(card => card.id == id);
            if (index < 0) return "unknown_difficulty";
            int offset = index / 3 - (int)Page.GetValue(null);
            if (offset != 0) Turn.Invoke(null, new object[] { offset });
            Click.Invoke(null, new object[] { index.ToString() });
            var chosen = NewGameDifficultyUI.Selected;
            if (!NewGameDifficultyUI.IsOpen || chosen?.id != id || NewGameData.Instance.difficultyId != id)
                return "selection_unconfirmed";
            receipt["selected_id"] = chosen.id;
            receipt["name"] = chosen.GetTitle();
            receipt["page"] = (int)Page.GetValue(null) + 1;
            return "difficulty_selected";
        }
    }
}
