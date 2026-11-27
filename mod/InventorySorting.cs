using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace WhiteDragon.Ledger
{
    internal static class InventorySorting
    {
        private static ConfigEntry<string> mode;
        private static ConfigEntry<bool> appraisalAssist;
        private static ConfigEntry<bool> itemDetailsCompat;
        private static readonly List<LoadedMod> sorters = new List<LoadedMod>();

        public static bool AppraisalAssistEnabled => appraisalAssist?.Value == true;
        public static bool ItemDetailsCompatEnabled => itemDetailsCompat?.Value == true;

        public static void Configure(string id)
        {
            var cfg = ModHelper.GetConfig(id);
            mode = cfg.Define("inventory_sort", "auto", "庫存",
                () => "MCP 主庫存整理", new[] { "auto", "off", "on" },
                () => "auto：已知整理模組啟用時退讓；off：關閉；on：允許助手按需整理。");
            appraisalAssist = cfg.Define("appraisal_assist", false, "鑑定與輔助",
                () => "鑑定輔助估價",
                () => "預設關閉（由助手依放大鏡外觀特徵自行算價）。開啟後直接提供最佳估價供助手填表；變更後重連 MCP 生效。");
            itemDetailsCompat = cfg.Define("item_details_compat", false, "鑑定與輔助",
                () => "物品詳情模組相容",
                () => "預設關閉。開啟且已載入物品詳情模組時，允許助手讀取畫面顯示的懸停說明判斷真偽；變更後重連 MCP 生效。");
        }

        public static void Observe(LoadedMod mod)
        {
            switch (mod.WorkshopId)
            {
                case 3829206666UL: // Smart Inventory Arrange
                case 3829730652UL: // OneTap Sort
                case 3837294570UL: // Smart Sort
                    if (!sorters.Contains(mod)) sorters.Add(mod);
                    break;
            }
        }

        public static void Reset() => sorters.Clear();

        public static string Blocker(JObject receipt)
        {
            if (mode.Value == "off") return "inventory_sort_disabled";
            var external = sorters.FirstOrDefault(mod => mod.IsEnabled);
            if (mode.Value == "auto" && external != null)
            {
                receipt["provider"] = external.Manifest.Name;
                receipt["workshop_id"] = external.WorkshopId.ToString();
                return "external_sorter_active";
            }
            return null;
        }
    }

    public sealed partial class InventoryObserver
    {
        private string SortInventory(JObject receipt)
        {
            string blocker = InventorySorting.Blocker(receipt);
            if (blocker != null) return blocker;
            var grid = EmporiumEntry.Instance.invElement as GameGridInventory;
            if (grid == null) return "inventory_unavailable";
            receipt["before"] = InventoryCapacity(grid);
            if (Interactable.isPaused || PlayerStore.Instance.lockAllInventoryFlag > 0 ||
                order != null || staged != null || stagedBundle.Count > 0 || !InventorySortHelper.CanSort(grid))
                return "sort_unavailable";
            if (!InventorySortHelper.Sort(grid, true, false)) return "no_layout_found";
            TutorialUIManager.Instance?.OnInventorySorted();
            foreach (var item in grid.childItems) changed.Add(item);
            receipt["after"] = InventoryCapacity(grid);
            return "sorted";
        }
    }
}
