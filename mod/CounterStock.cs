using System.Linq;
using Newtonsoft.Json.Linq;

namespace WhiteDragon.Ledger
{
    public sealed partial class InventoryObserver
    {
        private JObject MoveCounterStock(JObject request, GameItem item, string source, string destination)
        {
            bool collect = destination == "inventory" && (source == "front" || source == "back");
            bool idleClear = request["visit"] == null && (collect || source == "back" && destination == "showcase");
            if (!idleClear && phase != "OPEN") return Receipt(request,"shop_closed");
            ObserveVisit();
            string stage = null;
            bool dialogueStage = false;
            if (idleClear)
            {
                if (visitId != null || PlayerStore.Instance.currentClientInstance != null) return Receipt(request,"visit_changed");
                if (order != null || Interactable.isPaused) return Receipt(request,"trade_unavailable");
            }
            else
            {
                if (visitId == null || (string)request["visit"] != visitId)
                    return Receipt(request, VisitMismatchStatus());
                stage = CounterStage(true);
                bool canCollect = collect && PlayerStore.Instance.isClientArrived && PlayerStore.Instance.lockAllInventoryFlag == 0 &&
                    !WildNetworkUI.IsOpen && ResponseInventory() == null;
                dialogueStage = stage == "choice_required" && merchant?.clientIntent == StoreClient.ClientIntent.DIALOGUE;
                if (dialogueStage)
                {
                    string lockReason = NegociationUIManager.IsTradeLocked() ? "trade_locked"
                        : (PlayerStore.Instance.lockAllInventoryFlag != 0 || PlayerStore.Instance.lockAllFlag != 0) ? "inventory_locked"
                        : Interactable.isPaused ? "paused"
                        : (WildNetworkUI.IsOpen || ResponseInventory() != null) ? "panel_open"
                        : (EmporiumEntry.Instance.backInvWindow == null || !EmporiumEntry.Instance.backInvWindow.IsVisible() ||
                           EmporiumEntry.Instance.backInvWindow.isLocked) ? "counter_locked"
                        : null;
                    if (lockReason != null)
                    {
                        var blocked = Receipt(request, "trade_unavailable");
                        blocked["reason"] = lockReason;
                        return blocked;
                    }
                }
                else if ((!canCollect && stage != "ready" && stage != "barter") || order != null || Interactable.isPaused ||
                    NegociationUIManager.IsTradeLocked()) return Receipt(request,"trade_unavailable");
            }
            TryParseBayArea(source, out var sourceBay, out var sourceBayGrid);
            TryParseBayArea(destination, out var destBay, out var destBayGrid);
            if (sourceBayGrid != null)
            {
                if (item.parentInventory != sourceBayGrid)
                    return Receipt(request, "source_changed");
            }
            else if (!areas.TryGetValue(item.parentInventory,out var actual) || actual != source)
                return Receipt(request,"source_changed");
            var shop = EmporiumEntry.Instance;
            bool owned = GeneralHelper.IsItemOwned(item);
            if ((stage == "barter" || dialogueStage) && !owned) return Receipt(request,"invalid_route");
            bool stock = source == "inventory" || source == "showcase" || sourceBay != null;
            bool allowed = destination == "back" ? (owned ? stock : source == "front") :
                source == "back" && (owned ? (destination == "inventory" || destination == "showcase" || destBay != null) : destination == "front");
            if (!allowed && !(collect && owned)) return Receipt(request,"invalid_route");
            if (destination == "back" && shop.backInvinvElement.childItems.Count != 0 &&
                (!owned || (stage != "ready" && !dialogueStage) ||
                 shop.backInvinvElement.childItems.Any(existing => !GeneralHelper.IsItemOwned(existing) || IsHazardousWasteItem(existing))))
            {
                var occupied = Receipt(request, "table_occupied");
                DescribeBlockedTable(occupied, false, true);
                return occupied;
            }
            var target = destBayGrid != null ? destBayGrid :
                destination == "back" ? shop.backInvinvElement : destination == "front" ? shop.frontInvinvElement :
                destination == "showcase" ? shop.showcaseElement : shop.invElement;
            var window = destBayGrid != null ? null :
                destination == "back" ? shop.backInvWindow : destination == "front" ? shop.frontInvWindow :
                destination == "showcase" ? shop.showcaseWindow : shop.invWindow;
            if ((window != null && (!window.IsVisible() || window.isLocked)) || (destBayGrid == null && window == null) ||
                GeneralHelper.IsItemLocked(item) ||
                GeneralHelper.IsDisplayOnly(item) || item.parentInventory.IsRemoveLocked() || target.IsInsertLocked() ||
                !item.MayRemove()) return Receipt(request,"transfer_blocked");
            if (!GraphUtils.CanAccept(target, item))
            {
                if (!IsSpaceFailure(target, item)) return Receipt(request, "transfer_blocked");
                var full = Receipt(request, destination == "showcase" ? "showcase_full" : "no_space");
                full["reason"] = destination == "showcase" ? "showcase_full" : "inventory_full";
                full["actual_area"] = source;
                AttachSpaceFailure(item, target, full);
                return full;
            }
            var origin = item.parentInventory;
            var receipt = Receipt(request,"outcome_unknown");
            receipt["owned"] = owned;
            receipt["name"] = item.GetDisplayName(true);
            receipt["kind"] = item.identifier;
            string status = TransferAndMeasure(item,origin,target,receipt);
            if (destination == "back" && status == "moved") pendingPapers.Remove(item);
            ScanKnownIdentities();
            FlushChanges();
            receipt["revision"] = sequence;
            receipt["status"] = status;
            if (status == "not_moved" && IsSpaceFailure(target, item))
            {
                receipt["status"] = destination == "showcase" ? "showcase_full" : "no_space";
                receipt["reason"] = destination == "showcase" ? "showcase_full" : destBay != null ? "bay_full" : "inventory_full";
                AttachSpaceFailure(item, target, receipt);
            }
            if (sourceBay != null) receipt["from_container"] = FromContainerJson(sourceBay);
            AttachReceiptIdentityChanges(receipt);
            receipt["actual_area"] = item.parentInventory == target || status == "moved" ? destination :
                item.parentInventory == origin ? source : null;
            receipt["quantity"] = receipt["requested_quantity"];
            nextView = 0;
            RefreshDesk();
            return receipt;
        }
    }
}
