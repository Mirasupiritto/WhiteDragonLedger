using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using TMPro;

namespace WhiteDragon.Ledger
{
    public partial class GameItem { public string GetString() => identifier; }
    public static class EventListCosmetic { public static StoreEvent AllQuiet() => new StoreEvent { newsName="Quiet", newsDescription="No headlines" }; }
    public class NewsUIElement
    {
        public Window gameObject=new Window();
        public StoreEvent savedStoreEvent;
        public TextMeshProUGUI title, textContent;
    }
    public class NewsUIManager
    {
        public static NewsUIManager Instance=new NewsUIManager();
        public Window uiGameObject=new Window();
        public List<NewsUIElement> allElements=new List<NewsUIElement>();
        public List<NewsUIElement> layout1Elements=new List<NewsUIElement>{null}, layout2Elements=new List<NewsUIElement>{null,null},
            layout3Elements=new List<NewsUIElement>{null,null,null}, layout4Elements=new List<NewsUIElement>{null,null,null,null};
    }
    public class ExchangeBarter
    {
        public string identifier;
        public List<BarterCondition> conditions=new List<BarterCondition>();
    }
    public class ExchangeManager
    {
        public List<ExchangeBarter> activeBarter=new List<ExchangeBarter>(),promotionalBarter=new List<ExchangeBarter>();
        public List<string> activePromotionalBarterIds=new List<string>();
    }
    public class ExchangeUIManager
    {
        public static ExchangeUIManager Instance;
        public ReceiptElements panel;
    }
    public partial class PlayerStore { public ExchangeManager exchangeManager=new ExchangeManager(); }
    public sealed partial class InventoryObserver
    {
        private void ReplayShopDocuments()
        {
            var store=StoreStation.Instance;
            var oldEvents=store.storeEventManager;
            store.storeEventManager=new StoreEventManager();
            var paper=Item(19080,"newspaper",0);
            var req=new JObject { ["action"]="inspect_window",["owner_id"]=LocalId(paper) };
            var result=new JObject();
            Expect(WorkstationAction(req,result)=="observed" &&
                (string)result["window"]["document"]["articles"][0]["title"]=="Quiet" &&
                store.storeEventManager.GetActiveEvents().Count==0, "empty newspaper remains read only");
            store.storeEventManager.Activate(new StoreEvent { newsName="hidden",isHiddenFromNewsPaper=true });
            store.storeEventManager.Activate(new StoreEvent { newsName="Headline",newsDescription="Article",duration=1 });
            req["since_token"]=result["window_token"];
            result=new JObject();
            Expect(WorkstationAction(req,result)=="observed" &&
                (string)result["window_delta"]["document"]["articles"][0]["body"]=="Article" &&
                ((JArray)result["window_delta"]["document"]["articles"]).Count==1,"newspaper only shows published articles");
            store.storeEventManager=oldEvents;

            var exchange=PlayerStore.Instance.exchangeManager;
            var standard=new ExchangeBarter { identifier="filter_barter" };
            for (int i=0;i<3;i++) standard.conditions.Add(new BarterCondition { requiredItemId="water_filter", requiredItemTag="" });
            exchange.activeBarter.Add(standard);
            exchange.promotionalBarter.Add(new ExchangeBarter { identifier="hidden_offer" });
            exchange.promotionalBarter.Add(new ExchangeBarter { identifier="today" });
            exchange.activePromotionalBarterIds.Add("today");
            req["owner_id"]=LocalId(Item(19081,"exchange_directive",0));
            result=new JObject();
            Expect(WorkstationAction(req,result)=="observed","read owned exchange directive");
            var offers=(JArray)result["window"]["document"]["offers"];
            Expect(offers.Count==2 && (int)offers[0]["requirements"][0]["quantity"]==3 &&
                (string)offers[0]["requirements"][0]["name"]=="water_filter" && (string)offers[1]["id"]=="today",
                "permanent and current promotional requirements");
            System.Console.WriteLine("Shop document replay passed.");
        }
    }
}
