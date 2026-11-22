using Newtonsoft.Json.Linq;

namespace WhiteDragon.Ledger
{
    public sealed partial class InventoryObserver
    {
        private string TransferAndMeasure(GameItem item, GameInventory origin, GameInventory target, JObject receipt, bool trackShop = true)
        {
            int before = item.unitCount;
            int destinationBefore = KindQuantity(target, item.identifier);
            int sourceBefore = KindQuantity(origin, item.identifier);
            GraphUtils.TryAcceptOnce(target, item);
            if (trackShop) changed.Add(item);
            int arrived = KindQuantity(target, item.identifier) - destinationBefore;
            int left = sourceBefore - KindQuantity(origin, item.identifier);
            receipt["requested_quantity"] = before;
            receipt["observed_quantity"] = arrived;
            receipt["same_item_at_destination"] = item.parentInventory == target;
            if (left != arrived || arrived < 0 || arrived > before) return "outcome_unknown";
            if (arrived == 0) return "not_moved";
            return arrived == before ? "moved" : "partial";
        }

        private static int KindQuantity(GameInventory storage, string kind)
        {
            int count = 0;
            foreach (var item in storage.childItems)
                if (item.identifier == kind) count += item.unitCount;
            return count;
        }
    }
}
