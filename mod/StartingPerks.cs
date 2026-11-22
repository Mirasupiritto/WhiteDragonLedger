using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace WhiteDragon.Ledger
{
    internal static class StartingPerks
    {
        // This native panel exposes IsOpen publicly; the live page model and its counters are private.
        private const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Static;
        private static FieldInfo Field(string name) => typeof(NewGamePerksUI).GetField(name, Flags);
        private static MethodInfo Method(string name) => typeof(NewGamePerksUI).GetMethod(name, Flags);
        private static readonly FieldInfo Frame = Field("frame"), Sorted = Field("sorted"), FirstRun = Field("firstRun");
        private static readonly FieldInfo Page = Field("page"), SelectedPage = Field("selectedPage");
        private static readonly MethodInfo Turn = Method("TurnPage"), TurnSelected = Method("TurnSelectedPage");
        private static readonly MethodInfo Used = Method("UsedPoints"), Limit = Method("MaxPerkCount"), Locked = Method("LockedBy");
        internal static bool Supported => Frame != null && Sorted != null && FirstRun != null && Page != null &&
            SelectedPage != null && Turn != null && TurnSelected != null && Used != null && Limit != null && Locked != null;
        private static List<StartingPerk> Catalog => (List<StartingPerk>)Sorted.GetValue(null);
        private static NewGamePerksFrame Panel => (NewGamePerksFrame)Frame.GetValue(null);

        internal static JObject View()
        {
            var options = new JArray();
            var blocked = new JObject();
            foreach (var perk in Catalog)
            {
                options.Add(new JObject { ["id"] = perk.id, ["name"] = perk.GetLocalizedDisplayName(),
                    ["description"] = perk.GetLocalizedDescription(), ["type"] = perk.type.ToString(),
                    ["cost"] = perk.cost, ["extra_slots"] = perk.maxSlot,
                    ["incompatible_ids"] = new JArray(perk.incompatiblePerks ?? Enumerable.Empty<string>()) });
                var other = (StartingPerk)Locked.Invoke(null, new object[] { perk });
                if (other != null) blocked[perk.id] = other.id;
            }
            var selected = NewGameData.Instance.startingPerks;
            var state = new JObject { ["selected_ids"] = new JArray(selected.Select(p => p.id)),
                ["selected_count"] = selected.Count, ["max_count"] = (int)Limit.Invoke(null, null),
                ["points_left"] = NewGamePerksUI.MAX_PERK_POINT - (int)Used.Invoke(null, null),
                ["locked_by"] = blocked, ["first_run_locked"] = (bool)FirstRun.GetValue(null),
                ["can_continue"] = NativeButton.Enabled(Panel.continueButton) };
            return new JObject { ["options"] = options, ["state"] = state };
        }

        internal static string Act(string action, string id, JObject view, JObject receipt)
        {
            var state = view["state"];
            if (action == "continue")
            {
                if (!(bool)state["can_continue"]) return "selection_invalid";
                Panel.continueButton.onClick.Invoke();
                return NewGameDifficultyUI.IsOpen ? "difficulty_open" : "continue_unconfirmed";
            }
            bool removing = action == "remove";
            if (!removing && action != "add") return "unknown_action";
            if ((bool)state["first_run_locked"]) return "first_run_locked";
            var selected = NewGameData.Instance.startingPerks;
            int index = (removing ? selected : Catalog).FindIndex(p => p.id == id);
            if (index < 0) return removing ? "not_selected" : "unknown_perk";
            if (!removing && selected.Any(p => p.id == id)) return "already_selected";
            if (!removing && state["locked_by"][id] != null) return "incompatible_perk";

            // Use the UI's two independently paged rows and the same callbacks as a player click.
            int pageSize = removing ? 10 : 36;
            int offset = index / pageSize - (int)(removing ? SelectedPage : Page).GetValue(null);
            if (offset != 0) (removing ? TurnSelected : Turn).Invoke(null, new object[] { offset });
            var frame = Panel;
            var row = removing ? frame.selectedSlots : frame.availableSlots;
            // Rebuild schedules old children for end-of-frame destruction; the newest matching slot is last.
            var slot = row.GetComponentsInChildren<WildNetworkSlot>().LastOrDefault(p => p.id == id);
            if (slot == null) return "slot_unavailable";
            if (removing)
            {
                var duplicate = frame.availableSlots.GetComponentsInChildren<WildNetworkSlot>().LastOrDefault(p => p.id == id);
                duplicate?.onHover?.Invoke(id, false);
                slot.onHover?.Invoke(id, true);
            }
            slot.onClick.Invoke(id);
            receipt["perk_id"] = id;
            if (selected.Any(p => p.id == id) == removing) return "selection_unconfirmed";
            return removing ? "removed" : "added";
        }
    }
}
