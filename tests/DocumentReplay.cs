using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using TMPro;

namespace WhiteDragon.Ledger
{
    public partial class GameItem
    {
        public Dictionary<string, string> stringTags = new Dictionary<string, string>();
        public string bookTagValue;
        public string GetString() => bookTagValue ?? identifier;
    }
    public static class MiscItemDirectory
    {
        public static string GUIDE_PAPER_TAG = "GUIDE_PAPER_TAG";
        public static string TUTORIAL_PAPER_TAG = "TUTORIAL_PAPER_TAG";
    }
    public static class BookHelper
    {
        public static string BOOK_ID = "BOOK_ID";
        public static string BOOK_TAG = "BOOK_TAG";
    }
    public class StoreBookData
    {
        public static string WATER_BOOK = "WATER_BOOK";
        public string title;
        public string[] subtitle = new string[0], body = new string[0];
        public int pageCount;
        public static Dictionary<string, StoreBookData> bookDatas = new Dictionary<string, StoreBookData>();
        public static StoreBookData GetBookById(string id) => id != null && bookDatas.TryGetValue(id, out var d) ? d : null;
    }
    public class MultiPageUIManager
    {
        public static MultiPageUIManager Instance = new MultiPageUIManager();
        public Window panel = new Window();
        public List<Window> books = new List<Window>();
        private int currentBookIndex, currentPage;
        public Action<int> onFlip;
        public void OpenUI(int index) { currentBookIndex = index; currentPage = 0; panel.activeInHierarchy = panel.activeSelf = true; onFlip?.Invoke(0); }
        public void FlipRightClicked() { if (currentPage + 1 < 2) { currentPage++; onFlip?.Invoke(currentPage); } }
        public void CloseUI() { panel.activeInHierarchy = panel.activeSelf = false; }
    }
    public class PaperUIManager
    {
        public static PaperUIManager Instance = new PaperUIManager();
        public Window bookPanel = new Window(), foodStampGuidePanel = new Window(), augIdGuide = new Window(),
            cigaretteGuidePanel = new Window(), cigaretteColorPanel = new Window(), injectorGuidePanel = new Window(),
            codeBookPanel = new Window();
        public TextMeshProUGUI bookTitle = new TextMeshProUGUI(), bookSubtitle = new TextMeshProUGUI(),
            bookBody = new TextMeshProUGUI(), bookPage = new TextMeshProUGUI();
        public int bookCurrentPage;
        public static GameItem currentBook;
        public void CloseBook() { bookPanel.activeInHierarchy = bookPanel.activeSelf = false; }
        public void CloseAllPaperUI() {
            foodStampGuidePanel.activeInHierarchy = foodStampGuidePanel.activeSelf = false;
            augIdGuide.activeInHierarchy = augIdGuide.activeSelf = false;
            cigaretteGuidePanel.activeInHierarchy = cigaretteGuidePanel.activeSelf = false;
            cigaretteColorPanel.activeInHierarchy = cigaretteColorPanel.activeSelf = false;
            injectorGuidePanel.activeInHierarchy = injectorGuidePanel.activeSelf = false;
            codeBookPanel.activeInHierarchy = codeBookPanel.activeSelf = false;
        }
    }
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

            // P2-12: handbook / guide reading via MultiPageUIManager & StoreBookData, and honest image-only guide reporting.
            var multi = MultiPageUIManager.Instance;
            var bookWindow = new Window();
            multi.books.Add(bookWindow);
            multi.onFlip = page => {
                bookWindow.labels = page == 0
                    ? new[] { new TextMeshProUGUI { text = "Page 1: Check purity before buying." } }
                    : new[] { new TextMeshProUGUI { text = "Page 2: Boil or use purifier." } };
            };
            var waterGuide = Item(19082, "water_guide", 0);
            waterGuide.waterTags.Add(MiscItemDirectory.GUIDE_PAPER_TAG);
            waterGuide.onTestActivate = () => multi.OpenUI(0);
            req["owner_id"] = LocalId(waterGuide);
            req.Remove("since_token");
            result = new JObject();
            Expect(WorkstationAction(req, result) == "observed" &&
                (bool)result["window"]["document"]["readable"] &&
                ((JArray)result["window"]["document"]["pages"]).Count == 2 &&
                (string)result["window"]["document"]["pages"][1]["lines"][0] == "Page 2: Boil or use purifier." &&
                !multi.panel.activeInHierarchy,
                "multi-page guide reads all pages and restores closed UI state");

            var imageGuide = Item(19083, "visual_guide", 0);
            imageGuide.waterTags.Add(MiscItemDirectory.GUIDE_PAPER_TAG);
            bookWindow.labels = new TextMeshProUGUI[0];
            multi.onFlip = null;
            imageGuide.onTestActivate = () => multi.OpenUI(0);
            req["owner_id"] = LocalId(imageGuide);
            result = new JObject();
            Expect(WorkstationAction(req, result) == "observed" &&
                !(bool)result["window"]["document"]["readable"] &&
                (string)result["window"]["document"]["reason"] == "image_only_guide" &&
                !multi.panel.activeInHierarchy,
                "image-only guide reports readable=false cleanly instead of fabricating text");

            StoreBookData.bookDatas["ingot_book"] = new StoreBookData {
                title = "Ingot Guide", pageCount = 1,
                subtitle = new[] { "Density" }, body = new[] { "Weigh before accepting." }
            };
            var ingotBook = Item(19084, "ingot_guide", 0);
            ingotBook.waterTags.Add(BookHelper.BOOK_ID);
            ingotBook.bookTagValue = "ingot_book";
            req["owner_id"] = LocalId(ingotBook);
            result = new JObject();
            Expect(WorkstationAction(req, result) == "observed" &&
                (string)result["window"]["document"]["title"] == "Ingot Guide" &&
                (string)result["window"]["document"]["pages"][0]["body"] == "Weigh before accepting.",
                "StoreBookData guide reads book text");
            StoreBookData.bookDatas.Remove("ingot_book");
            multi.books.Clear();
            System.Console.WriteLine("Shop document replay passed.");
        }
    }
}
