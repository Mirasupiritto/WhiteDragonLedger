using System;
using Newtonsoft.Json.Linq;

namespace WhiteDragon.Ledger
{
    public class ConfigEntry<T> { public T Value; }
    public class ReplaySortConfig
    {
        public readonly ConfigEntry<string> entry = new ConfigEntry<string>();
        public readonly ConfigEntry<bool> appraisalEntry = new ConfigEntry<bool>();
        public readonly ConfigEntry<bool> detailsEntry = new ConfigEntry<bool>();
        public readonly ConfigEntry<bool> highlightEntry = new ConfigEntry<bool>();
        public ConfigEntry<string> Define(string key, string initial, string category, Func<string> label, string[] choices, Func<string> hint)
        {
            entry.Value = initial;
            return entry;
        }
        public ConfigEntry<bool> Define(string key, bool initial, string category, Func<string> label, Func<string> hint)
        {
            var target = key == "appraisal_assist" ? appraisalEntry : key == "demand_highlight_compat" ? highlightEntry : detailsEntry;
            target.Value = initial;
            return target;
        }
    }
    public static partial class ModHelper
    {
        public static readonly ReplaySortConfig sortConfig = new ReplaySortConfig();
        public static ReplaySortConfig GetConfig(string id) => sortConfig;
    }
    public class LoadedMod
    {
        public ulong WorkshopId;
        public bool IsEnabled;
        public ModManifest Manifest = new ModManifest();
        public object Instance = new object();
        public string LoadError = "";
    }
    public class ModManifest { public string ID, Name, ModVersion; }
    public class TutorialUIManager
    {
        public static TutorialUIManager Instance;
        public void OnInventorySorted() {}
    }
    public static class InventorySortHelper
    {
        public static int calls;
        public static bool CanSort(GameGridInventory grid) => true;
        public static bool Sort(GameGridInventory grid, bool bigFirst, bool fromEnd)
        {
            if (!bigFirst || fromEnd) throw new Exception("unexpected sort order");
            calls++;
            grid.childItems[0].modifiedShape.minX = 0;
            return true;
        }
    }
    public sealed partial class InventoryObserver
    {
        private void ReplayInventorySorting()
        {
            var shop = EmporiumEntry.Instance;
            var previous = shop.invElement;
            var grid = new GameGridInventory(5, 1);
            var item = new GameItem {identifier="part"};
            item.To(grid);
            item.modifiedShape.minX = 2;
            shop.invElement = grid;
            var request = new JObject { ["action"]="sort_inventory", ["generation"]=generation,
                ["revision"]=sequence, ["expires_ms"]=long.MaxValue };
            var receipt = new JObject();
            try
            {
                InventorySorting.Configure("replay");
                foreach (ulong id in new[] {3829206666UL, 3829730652UL, 3837294570UL})
                {
                    var external = new LoadedMod {WorkshopId=id, IsEnabled=true};
                    external.Manifest.Name = "External";
                    InventorySorting.Observe(external);
                    Expect(ActAtCounter(request, receipt) == "external_sorter_active" &&
                        (string)receipt["workshop_id"] == id.ToString(), "yield to running sorter");
                    external.IsEnabled = false;
                }
                InventorySorting.Observe(new LoadedMod {WorkshopId=1, IsEnabled=true});
                ModHelper.sortConfig.entry.Value = "off";
                Expect(ActAtCounter(request, receipt) == "inventory_sort_disabled", "manual opt out");
                ModHelper.sortConfig.entry.Value = "auto";
                Interactable.isPaused = true;
                Expect(ActAtCounter(request, receipt) == "sort_unavailable", "respect the inventory lock");
                Interactable.isPaused = false;
                Expect(InventorySortHelper.calls == 0, "blocked commands did not rearrange inventory");
                receipt = new JObject();
                Expect(ActAtCounter(request, receipt) == "sorted" && InventorySortHelper.calls == 1 &&
                    (int)receipt["before"]["largest_free"]["w"] == 2 && (int)receipt["after"]["largest_free"]["w"] == 4 &&
                    (int)receipt["before"]["free_cells"] == 4 && (int)receipt["after"]["free_cells"] == 4 &&
                    changed.Contains(item), "return fragmentation and publish layout changes");
                InventorySorting.Observe(new LoadedMod {WorkshopId=3829206666UL, IsEnabled=true});
                ModHelper.sortConfig.entry.Value = "on";
                Expect(ActAtCounter(request, new JObject()) == "sorted", "explicit player override");
            }
            finally
            {
                shop.invElement = previous;
                changed.Remove(item);
                Interactable.isPaused = false;
                ModHelper.sortConfig.entry.Value = "auto";
                InventorySorting.Reset();
            }
        }
    }
}
