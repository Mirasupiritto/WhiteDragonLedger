using Newtonsoft.Json.Linq;

namespace WhiteDragon.Ledger
{
    public sealed partial class InventoryObserver
    {
        private JObject TableQuote()
        {
            var table = EmporiumEntry.Instance.backInvinvElement;
            if (merchant == null || table.childItems.Count != 1 || CounterStage(true) != "ready") return null;
            var store = PlayerStore.Instance;
            var item = table.childItems[0];
            if (store.GetCurrentlyNegociatedItem() != item) return null;
            bool owned = GeneralHelper.IsItemOwned(item);
            return new JObject { ["item_id"]=LocalId(item), ["side"]=owned ? "sell" : "buy",
                ["price"]=item.GetNegociatedValue(), ["price_source"]="table_quote", ["allowed"]=owned ? store.CanSellItemFromTable(false) :
                    !merchant.isOfferWholesale && !merchant.isRegularSellDisable && store.CanBuyItemFromTable() };
        }

        private string ObserveTradeWindows(JObject receipt)
        {
            var windows = new JArray();
            foreach (var item in new System.Collections.Generic.List<GameItem>(itemsById.Values))
                if (item != null && item.contentWindow != null && item.contentWindow.IsVisible())
                    windows.Add(new JObject { ["item_id"]=LocalId(item), ["window"]=ReadWindow(item).View });
            receipt["windows"] = windows;
            receipt["dialog_choices"] = DialogChoices(DialogUIManager.Instance);
            receipt["response_choices"] = ResponseChoices(ResponseInventory());
            return "observed";
        }

        private void BuyOrderLine(JToken spec, long quote)
        {
            var store = PlayerStore.Instance;
            string refusal = spec["exact_price"] != null && (long)spec["exact_price"] != quote ? "quote_changed" :
                quote < 0 || quote > (long)spec["max_price"] ? "over_limit" :
                quote > store.playerCash ? "insufficient_cash" : !store.CanBuyItemFromTable() ? "declined" : null;
            if (refusal != null)
            {
                if (original == EmporiumEntry.Instance.frontInvinvElement) ModHelper.MoveItemToFrontCounter(staged);
                FlushChanges();
                if (staged.parentInventory != original) { StopLine("return_unconfirmed"); return; }
                line["actual_area"] = areas[original];
                line["status"] = "returned_" + refusal;
                FinishLine();
                return;
            }
            int before = store.playerCash;
            NegociationUIManager.Instance.BuyItem();
            changed.Add(staged);
            FlushChanges();
            line["cash_after"] = store.playerCash;
            line["paid"] = before - store.playerCash;
            if (!GeneralHelper.IsItemOwned(staged) || before - store.playerCash != quote)
            { StopLine("purchase_unconfirmed"); return; }
            line["actual_area"] = staged.parentInventory != null && areas.TryGetValue(staged.parentInventory, out var area)
                ? (JToken)area : JValue.CreateNull();
            line["status"] = "purchased";
            FinishLine();
        }
    }
}
