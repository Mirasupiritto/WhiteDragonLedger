using System.Linq;
using Newtonsoft.Json.Linq;

namespace WhiteDragon.Ledger
{
    public sealed partial class InventoryObserver
    {
        private JObject MoveCounterStock(JObject request, GameItem item, string source, string destination)
        {
            bool idleClear = request["visit"] == null && source == "back" && (destination == "inventory" || destination == "showcase");
            if (!idleClear && phase != "OPEN") return Receipt(request,"shop_closed");
            ObserveVisit();
            string stage = null;
            if (idleClear)
            {
                if (visitId != null || PlayerStore.Instance.currentClientInstance != null) return Receipt(request,"visit_changed");
                if (order != null || Interactable.isPaused) return Receipt(request,"trade_unavailable");
            }
            else
            {
                if (visitId == null || (string)request["visit"] != visitId) return Receipt(request,"visit_changed");
                stage = CounterStage(true);
                if ((stage != "ready" && stage != "barter") || order != null || Interactable.isPaused) return Receipt(request,"trade_unavailable");
            }
            if (!areas.TryGetValue(item.parentInventory,out var actual) || actual != source)
                return Receipt(request,"source_changed");
            var shop = EmporiumEntry.Instance;
            bool owned = GeneralHelper.IsItemOwned(item);
            if (stage == "barter" && !owned) return Receipt(request,"invalid_route");
            bool stock = source == "inventory" || source == "showcase";
            bool allowed = destination == "back" ? (owned ? stock : source == "front") :
                source == "back" && (owned ? destination == "inventory" || destination == "showcase" : destination == "front");
            if (!allowed) return Receipt(request,"invalid_route");
            if (destination == "back" && shop.backInvinvElement.childItems.Count != 0 &&
                (!owned || stage != "ready" || shop.backInvinvElement.childItems.Any(existing => !GeneralHelper.IsItemOwned(existing))))
                return Receipt(request,"table_occupied");
            var target = destination == "back" ? shop.backInvinvElement : destination == "front" ? shop.frontInvinvElement :
                destination == "showcase" ? shop.showcaseElement : shop.invElement;
            var window = destination == "back" ? shop.backInvWindow : destination == "front" ? shop.frontInvWindow :
                destination == "showcase" ? shop.showcaseWindow : shop.invWindow;
            if (window == null || !window.IsVisible() || window.isLocked || GeneralHelper.IsItemLocked(item) ||
                GeneralHelper.IsDisplayOnly(item) || item.parentInventory.IsRemoveLocked() || target.IsInsertLocked() ||
                !item.MayRemove() || !GraphUtils.CanAccept(target,item)) return Receipt(request,"transfer_blocked");
            var origin = item.parentInventory;
            var receipt = Receipt(request,"outcome_unknown");
            receipt["owned"] = owned;
            receipt["name"] = item.GetDisplayName(true);
            receipt["kind"] = item.identifier;
            string status = TransferAndMeasure(item,origin,target,receipt);
            FlushChanges();
            receipt["revision"] = sequence;
            receipt["status"] = status;
            receipt["actual_area"] = item.parentInventory == target || status == "moved" ? destination :
                item.parentInventory == origin ? source : null;
            receipt["quantity"] = receipt["requested_quantity"];
            nextView = 0;
            RefreshDesk();
            return receipt;
        }
    }
}
