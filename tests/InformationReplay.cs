using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using TMPro;

namespace TMPro
{
    public class TextMeshProUGUI { public string text; public bool isActiveAndEnabled = true; }
}

namespace WhiteDragon.Ledger
{
    public class ReceiptElements
    {
        public bool activeInHierarchy = true;
        public object[] elements = new object[0];
        public T GetComponent<T>() where T:class => elements.OfType<T>().FirstOrDefault();
        public T[] GetComponentsInChildren<T>() => elements.OfType<T>().ToArray();
    }
    public class TabletListRow : ReceiptElements {}
    public static class TabletUI { public static string Credits(long value) => value + " credits"; }
    public class ReceiptButton
    {
        public bool enabled = true, isActiveAndEnabled = true;
        public bool IsInteractable() => enabled;
    }
    public class TabletButton
    {
        public TextMeshProUGUI label = new TextMeshProUGUI();
        public ReceiptButton button = new ReceiptButton();
    }

    public sealed partial class InventoryObserver
    {
        private void ReplayInformation()
        {
            var ui = NegociationUIManager.Instance;
            var store = PlayerStore.Instance;
            int originalCash = store.playerCash;
            store.playerCash = 100;
            var button = new TabletButton();
            button.label.text = "ui_nego_purchase_info";
            var description = new TextMeshProUGUI { text = "ui_nego_info_body" };
            var amount = new TextMeshProUGUI { text = TabletUI.Credits(12) };
            var row = new TabletListRow { elements = new object[] { description, amount } };
            ui.uiObject.elements = new object[] { button, row };
            Expect(CounterView()["info_receipt"] == null, "ordinary customer");
            merchant.clientIntent = StoreClient.ClientIntent.INFORMATION_DEALER;
            merchant.serviceCost = 12;
            Expect(CounterView()["info_receipt"] == null, "before introduction");
            merchant.isIntroduced = true;
            var dialog = DialogUIManager.Instance;
            dialog.current = new object();
            dialog.typing = true;
            Expect(CounterView()["info_receipt"] == null, "still speaking");
            dialog.typing = false;
            Expect(CounterView()["info_receipt"] == null, "final line still on screen");
            dialog.current = null;
            ui.IsOpen = false;
            Expect((string)CounterView()["stage"] == "tablet_closed", "wait for tablet");
            ui.IsOpen = true;
            ui.uiObject.activeInHierarchy = false;
            Expect(CounterView()["info_receipt"] == null, "hidden tablet");
            ui.uiObject.activeInHierarchy = true;
            var quote = CounterView()["info_receipt"];
            var request = new JObject { ["action"] = "pay_info", ["generation"] = generation, ["revision"] = sequence,
                ["visit"] = visitId, ["receipt_token"] = quote["receipt_token"], ["price"] = quote["price"],
                ["expires_ms"] = DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds() };
            var result = new JObject();
            Expect((int)quote["price"] == 12 && (bool)quote["can_pay"] && store.playerCash == 100, "read only");
            request["price"] = 11;
            Expect(ActAtCounter(request, result) == "receipt_changed", "must agree exact price");
            request["price"] = 12;
            merchant.serviceCost = 13;
            Expect(ActAtCounter(request, result) == "awaiting_information_receipt", "stale visible row");
            amount.text = TabletUI.Credits(13);
            Expect(ActAtCounter(request, result) == "receipt_changed", "price changed");
            request["receipt_token"] = CounterView()["info_receipt"]["receipt_token"];
            request["price"] = 13;
            store.playerCash = 5;
            Expect(!(bool)CounterView()["info_receipt"]["can_pay"] &&
                ActAtCounter(request, result) == "insufficient_cash", "unaffordable quote");
            store.playerCash = 100;
            button.button.enabled = false;
            Expect(ActAtCounter(request, result) == "payment_unavailable", "button disabled");
            button.button.enabled = true;
            request["visit"] = "previous";
            Expect(ActAtCounter(request, result) == "customer_replaced" && ui.paymentCalls == 0, "wrong dealer visit");
            request["visit"] = visitId;
            Expect(ActAtCounter(request, result) == "information_purchased" &&
                (int)result["paid"] == 13 && (int)result["cash_after"] == 87, "confirmed debit");
            ActAtCounter(request, result);
            Expect(ui.paymentCalls == 1 && store.playerCash == 87, "no second debit");
            store.currentClientInstance = new StoreClientInstance();
            ObserveVisit();
            merchant.clientIntent = StoreClient.ClientIntent.INFORMATION_DEALER;
            merchant.isIntroduced = true;
            merchant.serviceCost = 13;
            request["visit"] = visitId;
            Expect(ActAtCounter(request, result) == "receipt_changed", "quote bound to visit");
            request["receipt_token"] = CounterView()["info_receipt"]["receipt_token"];
            ui.extraFee = 1;
            Expect(ActAtCounter(request, result) == "payment_unconfirmed" &&
                (int)result["paid"] == 14 && (int)result["cash_after"] == 73, "report actual unexpected debit");
            merchant.isBusinessComplete = false;
            Expect(ActAtCounter(request, result) == "payment_already_submitted" && ui.paymentCalls == 2, "uncertain click cannot repeat");
            store.playerCash = originalCash;
            store.currentClientInstance = new StoreClientInstance();
            ObserveVisit();
            Console.WriteLine("Information receipt replay passed.");
        }
    }
}
