using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using TMPro;

namespace WhiteDragon.Ledger
{
    public sealed partial class InventoryObserver
    {
        private string informationToken, attemptedInformationVisit;
        private int informationPrice;

        private void ObserveInformationReceipt(JObject view)
        {
            if (attemptedInformationVisit == visitId)
            {
                view["stage"] = "payment_already_submitted";
                return;
            }
            var root = NegociationUIManager.Instance.uiObject;
            int price = merchant.serviceCost;
            string description = LocHelper.GetLocalizedUI("ui_nego_info_body");
            string amount = TabletUI.Credits(price);
            string purchase = LocHelper.GetLocalizedUI("ui_nego_purchase_info");
            var button = root == null || !root.activeInHierarchy ? null :
                root.GetComponentsInChildren<TabletButton>().FirstOrDefault(b => b.label != null &&
                    b.label.isActiveAndEnabled && b.label.text == purchase && b.button != null);
            bool visible = price >= 0 && button != null && root.GetComponentsInChildren<TabletListRow>().Any(row =>
            {
                var labels = row.GetComponentsInChildren<TextMeshProUGUI>();
                return labels.Any(t => t.isActiveAndEnabled && t.text == description) &&
                    labels.Any(t => t.isActiveAndEnabled && t.text == amount);
            });
            if (!visible)
            {
                informationToken = null;
                view["stage"] = "awaiting_information_receipt";
                return;
            }
            if (informationToken == null || informationPrice != price)
            {
                informationPrice = price;
                informationToken = Guid.NewGuid().ToString("N");
            }
            view["stage"] = "information_quote";
            view["info_receipt"] = new JObject { ["price"] = price, ["receipt_token"] = informationToken,
                ["can_pay"] = button.button.isActiveAndEnabled && button.button.IsInteractable() && PlayerStore.Instance.CanPay(price) };
        }

        private string PayInformationReceipt(JObject request, JObject receipt)
        {
            var view = CounterView();
            var quote = view["info_receipt"];
            if (quote == null) return (string)view["stage"] == "ready" ? "not_information_dealer" : (string)view["stage"];
            if ((string)request["receipt_token"] != informationToken || (int?)request["price"] != informationPrice)
                return "receipt_changed";
            var store = PlayerStore.Instance;
            if (!store.CanPay(informationPrice)) return "insufficient_cash";
            if (!(bool)quote["can_pay"]) return "payment_unavailable";

            int before = store.playerCash;
            var dealer = merchant;
            attemptedInformationVisit = visitId;
            receipt["visit"] = visitId;
            receipt["quote"] = informationPrice;
            try { NegociationUIManager.Instance.OnOfferAcceptClick(); }
            finally
            {
                receipt["cash_after"] = store.playerCash;
                receipt["paid"] = (long)before - store.playerCash;
            }
            return dealer.isBusinessComplete && (long)receipt["paid"] == informationPrice ?
                "information_purchased" : "payment_unconfirmed";
        }
    }
}
