using System;
using Newtonsoft.Json.Linq;

namespace WhiteDragon.Ledger
{
    public sealed partial class InventoryObserver
    {
        private void ReplayMixedTrades()
        {
            var shop = EmporiumEntry.Instance;
            var store = PlayerStore.Instance;
            store.currentClientInstance = new StoreClientInstance();
            foreach (var leftover in shop.backInvinvElement.childItems.ToArray()) leftover.To(null);
            store.playerCash = 100;
            ObserveVisit();
            merchant.budget = 80;
            DialogUIManager.Instance = new DialogUIManager();
            NegociationUIManager.Instance.IsOpen = true;
            var offer = Item(900,"soap",8);
            offer.owned = false;
            offer.To(shop.frontInvinvElement);
            var own = Item(901,"paper",3);
            var water = Item(902,"bottle",4);
            water.volume=250000;
            water.owned=false;
            water.To(shop.frontInvinvElement);
            var strip = WaterStripUIManager.Instance;
            strip.panel.activeInHierarchy=true;
            strip.currentWaterContainer=water;
            strip.testedTMP.text="Visible sample";
            strip.testedTMP.isActiveAndEnabled=true;
            var shelf = (JArray)CounterView()["offer"]["items"];
            var row = System.Linq.Enumerable.Single(shelf, r => (int)r["id"]==LocalId(water));
            Expect((int)row["capacity_ml"]==500 && (string)row["last_strip"]["tested"]=="Visible sample","visible strip and capacity");
            strip.Close();
            row = System.Linq.Enumerable.Single((JArray)CounterView()["offer"]["items"], r => (int)r["id"]==LocalId(water));
            Expect((string)row["last_strip"]["tested"]=="Visible sample","last observed sample remains associated with bottle");
            water.To(null);
            var ask=new JObject { ["action"]="request_quote", ["visit"]=visitId, ["generation"]=generation, ["revision"]=sequence,
                ["item_id"]=LocalId(offer), ["expires_ms"]=long.MaxValue };
            var quote=new JObject();
            Expect(ActAtCounter(ask,quote)=="quoted" && (int)quote["quote"]["price"]==8 && (bool)quote["returned_to_front"] &&
                offer.parentInventory==shop.frontInvinvElement && !offer.owned && store.playerCash==100,"quote without purchase");
            ask["action"]="tablet"; ask["revision"]=sequence;
            NegociationUIManager.Instance.IsOpen=false;
            NativeItemPanels.panel=new ItemPanelView {Key="chemical_scanner",Identity="scan"};
            var panelBlocked=new JObject();
            Expect(ActAtCounter(ask,panelBlocked)=="item_panel_open" && !NegociationUIManager.Instance.IsOpen &&
                (string)panelBlocked["visit"]==visitId && (string)panelBlocked["after_close_next_tool"]=="reopen_trade_tablet",
                "player-opened scanner blocks tablet and returns current visit");
            toolOpenedPanels.Add("chemical_scanner");
            NativeItemPanels.panel.Close=()=>{ NativeItemPanels.panel=null; };
            var autoClosed=new JObject();
            Expect(ActAtCounter(ask,autoClosed)=="tablet_requested" && (bool)autoClosed["tablet_open"] &&
                autoClosed["closed_panels"]!=null && NativeItemPanels.panel==null,
                "tool-opened scanner closes automatically before opening tablet");
            NegociationUIManager.Instance.IsOpen=false;
            NativeItemPanels.panel=null;
            var reopened=new JObject();
            Expect(ActAtCounter(ask,reopened)=="tablet_requested" && (bool)reopened["tablet_open"] && (string)reopened["visit"]==visitId,"same visit reopens tablet");
            var options = new JObject();
            var dialog = DialogUIManager.Instance;
            dialog.choice=true;
            var button = new DialogChoiceBubble { index=7, enabled=true };
            button.gameObject.activeInHierarchy=true;
            button.TMP.text="Pick one";
            dialog.dialogChoiceBubbles.Add(button);
            Expect(ObserveTradeWindows(options)=="observed" && (int)options["dialog_choices"][0]["id"]==7 &&
                   button.clicks==0, "read visible choice without clicking");
            dialog.choice=false;
            JObject Run(JArray rows, bool tableOnly=false)
            {
                string id = Guid.NewGuid().ToString("N");
                var request = new JObject { ["command_id"]=id, ["generation"]=generation, ["revision"]=sequence,
                    ["visit"]=visitId, ["lines"]=rows, ["on_table"]=tableOnly,
                    ["expires_ms"]=DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds() };
                string inbox = "sale-"+generation+".json";
                Write(inbox, request);
                for (int n=0; n<12; n++) { UnityEngine.Time.realtimeSinceStartup++; CheckOrder(); }
                string receipt = ModHelper.ReadModDataFileText(ModId,"receipt-"+id+".json");
                int after = store.playerCash;
                ModHelper.WriteModDataFileText(ModId,"receipt-"+id+".json","{");
                Write(inbox, request);
                UnityEngine.Time.realtimeSinceStartup++; CheckOrder();
                Expect(store.playerCash==after,"replay never charges twice");
                Expect(ModHelper.ReadModDataFileText(ModId,"receipt-"+id+".json")==receipt,"restore exact result after truncated receipt");
                return JObject.Parse(receipt);
            }
            JObject Buy(int max) => new JObject { ["item_id"]=LocalId(offer), ["side"]="buy", ["max_price"]=max };
            JObject Sell() => new JObject { ["item_id"]=LocalId(own), ["side"]="sell", ["min_price"]=3 };
            var refused = Run(new JArray(Buy(7)));
            Expect((string)refused["items"][0]["status"]=="returned_over_limit" && offer.parentInventory==shop.frontInvinvElement,
                "buy ceiling returns original merchant item: "+refused);
            var mixed = Run(new JArray(Buy(8), Sell()));
            Expect((int)mixed["items"][0]["paid"]==8 && (int)mixed["items"][1]["received"]==3 &&
                store.playerCash==95 && (int)mixed["remaining_budget"]==77, "mixed order actuals per row");
            offer.owned=false;
            offer.To(shop.backInvinvElement);
            var exact=Buy(8);
            exact["exact_price"]=8;
            offer.price=7;
            Expect((string)Run(new JArray(exact),true)["status"]=="quote_changed" && store.playerCash==95,
                "changed exact quote stops even if cheaper");
            offer.price=8;
            Expect((int)Run(new JArray(exact),true)["items"][0]["paid"]==8 && offer.owned,"exact buy confirmed");
            offer.To(shop.backInvinvElement);
            var sell = new JObject { ["item_id"]=LocalId(offer), ["side"]="sell", ["min_price"]=8, ["exact_price"]=8 };
            Expect((int)Run(new JArray(sell),true)["items"][0]["received"]==8, "exact sale confirmed");
            Console.WriteLine("Mixed trade replay passed: buy ceiling, returned item, mixed payment, exact quotes and duplicate orders.");
        }
    }
}
