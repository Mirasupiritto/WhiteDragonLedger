using System;
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

        private const string NuclearWasteGuidance =
            "交易檯（back）上有核廢料（nuclear_waste）。請先用 move_items 將核廢料移至 inventory（庫存）或 showcase（展示櫃）；垃圾桶無法消除核廢料。若庫存滿了或放不下，需由玩家清出空間或於夜間 CLOSED 後，在店內用 inspect_item_window＋transfer_slot_items 裝入夠大的背包（如 6x6 小背包以上，3x3 腰包放不下），再帶去垃圾場（dumping_ground）移到 ground 後以 abandon_ground=True 返店（或由玩家於 UI 放進背包／手部帶去垃圾場拋棄；拋棄達標會觸發喬治奧來店歸還一次）。";

        private static bool IsHazardousWasteItem(GameItem item) =>
            item != null && GeneralHelper.IsHazardousWaste(item);

        private static bool IsBackpackOrPouch(GameItem item)
        {
            if (item == null) return false;
            if (item.IsTag("BACKPACK_TAG") || item.IsTag("POUCH_TAG")) return true;
            string kind = item.identifier;
            return kind == "fanny_pack" || kind == "satchel" || kind == "kids_backpack" ||
                kind == "kids_backpack_large" || (kind != null && kind.StartsWith("backpack_"));
        }

        private static bool IsDocumentOrSlip(string kind)
        {
            switch (kind)
            {
                case "newspaper":
                case "handnote":
                case "tutorial_page":
                case "slip":
                case "sec_slip":
                case "procurement_slip":
                case "postit":
                case "wanted_paper":
                case "exchange_directive":
                case "lottery_info":
                case "deed":
                case "lou_old_orders":
                case "tutorial_book":
                case "ingot_book":
                case "wine_book":
                case "permit_gun_1":
                case "permit_gun_2":
                case "permit_gun_3":
                    return true;
                default:
                    return kind != null && kind.EndsWith("_guide");
            }
        }

        private static bool IsCounterClutter(GameItem item, bool includeBackpacks) =>
            item != null && !IsHazardousWasteItem(item) &&
            (IsDocumentOrSlip(item.identifier) || CardNumber(item.identifier) != 0 ||
                (includeBackpacks && IsBackpackOrPouch(item)));

        private void QueueCounterPapers()
        {
            var shop = EmporiumEntry.Instance;
            bool idle = PlayerStore.Instance.currentClientInstance == null && order == null;
            foreach (var item in shop.frontInvinvElement.childItems)
                if (GeneralHelper.IsItemOwned(item) && !IsHazardousWasteItem(item) &&
                    (idle || IsCounterClutter(item, true)))
                {
                    if (CardNumber(item.identifier) != 0) phoneCards.Add(item);
                    pendingPapers.Add(item);
                }
            if (idle)
                foreach (var item in shop.backInvinvElement.childItems)
                    if (GeneralHelper.IsItemOwned(item) && !IsHazardousWasteItem(item))
                    {
                        if (CardNumber(item.identifier) != 0) phoneCards.Add(item);
                        pendingPapers.Add(item);
                    }
            papersDirty = true;
        }

        private void QueueFrontPaper(GameItem item, GameInventory inventory, SlotMarker marker)
        {
            if (GeneralHelper.IsItemOwned(item) && IsCounterClutter(item, true))
            {
                if (CardNumber(item.identifier) != 0) phoneCards.Add(item);
                pendingPapers.Add(item);
                papersDirty = true;
            }
        }

        private bool IsActiveOrderItem(GameItem item) =>
            order != null && (item == staged || stagedBundle.Contains(item));

        private void FileCounterPapers(JObject receipt = null)
        {
            if (!papersDirty || order != null || Interactable.isPaused || NegociationUIManager.IsTradeLocked() ||
                PlayerStore.Instance.lockAllInventoryFlag != 0) return;
            var shop = EmporiumEntry.Instance;
            var rows = new JArray();
            foreach (var item in pendingPapers.ToArray())
            {
                if (item == null || !GeneralHelper.IsItemOwned(item) || IsHazardousWasteItem(item) ||
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

        private int ClearCounterInventoryToShop(GameInventory origin, string area, HashSet<GameItem> exclude,
            Func<GameItem, bool> filter, JArray clearedRows)
        {
            var shop = EmporiumEntry.Instance;
            if (origin == null || shop.invElement == null || origin.IsRemoveLocked() || shop.invElement.IsInsertLocked())
                return 0;
            int moved = 0;
            foreach (var item in origin.childItems.ToArray())
            {
                if (item == null || (exclude != null && exclude.Contains(item))) continue;
                if (!GeneralHelper.IsItemOwned(item) || IsHazardousWasteItem(item) || IsActiveOrderItem(item)) continue;
                if (filter != null && !filter(item)) continue;
                if (GeneralHelper.IsItemLocked(item) || GeneralHelper.IsDisplayOnly(item) || !item.MayRemove()) continue;
                if (!GraphUtils.CanAccept(shop.invElement, item)) continue;
                var row = new JObject { ["id"]=LocalId(item), ["save_id"]=item.uniqueId,
                    ["kind"]=item.identifier, ["name"]=item.GetDisplayName(true), ["from"]=area };
                string status = TransferAndMeasure(item, origin, shop.invElement, row);
                row["status"] = status;
                if (status == "moved")
                {
                    row["actual_area"] = "inventory";
                    pendingPapers.Remove(item);
                    clearedRows.Add(row);
                    moved++;
                }
            }
            return moved;
        }

        private int ClearNonOrderBackTable(HashSet<GameItem> exclude, JArray clearedRows)
        {
            var shop = EmporiumEntry.Instance;
            var table = shop.backInvinvElement;
            bool onlyOwnedNonWaste = true;
            int candidates = 0;
            foreach (var item in table.childItems)
            {
                if (exclude != null && exclude.Contains(item)) continue;
                candidates++;
                if (!GeneralHelper.IsItemOwned(item) || IsHazardousWasteItem(item) || IsActiveOrderItem(item))
                {
                    onlyOwnedNonWaste = false;
                    break;
                }
            }
            if (!onlyOwnedNonWaste || candidates == 0) return 0;
            return ClearCounterInventoryToShop(table, "back", exclude, null, clearedRows);
        }

        private void DescribeBlockedTable(JObject receipt, bool ownedOnly, bool alwaysAttach = false,
            GameItem blockedSource = null, string fallbackReason = null)
        {
            var shop = EmporiumEntry.Instance;
            var matching = shop.backInvinvElement.childItems
                .Where(i => !ownedOnly || GeneralHelper.IsItemOwned(i))
                .ToList();
            var rows = new JArray(matching.Select(i =>
                new JObject { ["id"]=LocalId(i), ["kind"]=i.identifier, ["area"]="back" }));
            bool hasWaste = matching.Any(IsHazardousWasteItem);
            if (hasWaste)
            {
                receipt["reason"] = "nuclear_waste_on_counter";
                receipt["guidance"] = NuclearWasteGuidance;
                receipt["blocking_items"] = rows;
                return;
            }
            if (rows.Count > 0)
            {
                receipt["reason"] = "trade_table_not_empty";
                receipt["blocking_items"] = rows;
                return;
            }
            if (!alwaysAttach) return;
            receipt["reason"] = fallbackReason ?? "trade_table_not_empty";
            if (blockedSource != null && areas.TryGetValue(blockedSource.parentInventory ?? shop.invElement, out var area))
                receipt["blocking_items"] = new JArray(new JObject {
                    ["id"]=LocalId(blockedSource), ["kind"]=blockedSource.identifier, ["area"]=area });
            else
                receipt["blocking_items"] = new JArray();
        }
    }
}
