using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace WhiteDragon.Ledger
{
    public sealed partial class InventoryObserver
    {
        private readonly HashSet<GameItem> pendingPapers = new HashSet<GameItem>();
        private JArray paperCleanup;
        private bool papersDirty;

        private static bool IsCounterPaper(GameItem item) => item.identifier == "newspaper" ||
            item.identifier == "handnote" || item.identifier == "tutorial_page" || CardNumber(item.identifier) != 0;

        private void QueueCounterPapers()
        {
            var shop = EmporiumEntry.Instance;
            foreach (var item in shop.frontInvinvElement.childItems.Where(IsCounterPaper))
                if (GeneralHelper.IsItemOwned(item)) pendingPapers.Add(item);
            if (PlayerStore.Instance.currentClientInstance == null && order == null)
                foreach (var item in shop.backInvinvElement.childItems.Where(IsCounterPaper)) pendingPapers.Add(item);
            papersDirty = true;
        }

        private void QueueFrontPaper(GameItem item, GameInventory inventory, SlotMarker marker)
        {
            if (IsCounterPaper(item) && GeneralHelper.IsItemOwned(item))
            {
                pendingPapers.Add(item);
                papersDirty = true;
            }
        }

        private void FileCounterPapers(JObject receipt = null)
        {
            if (!papersDirty || order != null || Interactable.isPaused || NegociationUIManager.IsTradeLocked() ||
                PlayerStore.Instance.lockAllInventoryFlag != 0) return;
            var shop = EmporiumEntry.Instance;
            var rows = new JArray();
            foreach (var item in pendingPapers.ToArray())
            {
                if (item == null || !GeneralHelper.IsItemOwned(item) ||
                    (item.parentInventory != shop.frontInvinvElement && item.parentInventory != shop.backInvinvElement))
                {
                    pendingPapers.Remove(item);
                    continue;
                }
                var origin = item.parentInventory;
                var row = new JObject { ["id"]=LocalId(item), ["kind"]=item.identifier,
                    ["from"]=origin == shop.frontInvinvElement ? "front" : "back" };
                string status;
                if (GeneralHelper.IsItemLocked(item) || GeneralHelper.IsDisplayOnly(item) ||
                    origin.IsRemoveLocked() || shop.invElement.IsInsertLocked() || !item.MayRemove())
                    status = "locked";
                else if (!GraphUtils.CanAccept(shop.invElement, item)) status = "inventory_full";
                else status = TransferAndMeasure(item, origin, shop.invElement, row);
                row["status"] = status;
                rows.Add(row);
                if (status == "moved" || status == "outcome_unknown") pendingPapers.Remove(item);
            }
            paperCleanup = rows.Count == 0 ? null : rows;
            if (receipt != null && paperCleanup != null) receipt["document_cleanup"] = paperCleanup.DeepClone();
            papersDirty = false;
            nextView = 0;
        }

        private void DescribeBlockedTable(JObject receipt, bool ownedOnly)
        {
            var items = EmporiumEntry.Instance.backInvinvElement.childItems
                .Where(i => !ownedOnly || GeneralHelper.IsItemOwned(i))
                .Select(i => new JObject { ["id"]=LocalId(i), ["kind"]=i.identifier, ["area"]="back" });
            var rows = new JArray(items);
            if (rows.Count == 0) return;
            receipt["reason"] = "trade_table_not_empty";
            receipt["blocking_items"] = rows;
        }
    }
}
