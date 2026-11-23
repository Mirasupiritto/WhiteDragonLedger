using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using TMPro;
using System.Linq;

namespace UnityEngine
{
    public class Transform : List<Transform> { public GameObject gameObject; }
    public class GameObject
    {
        public bool activeInHierarchy, activeSelf;
        public readonly Transform transform;
        public TMPro.TextMeshProUGUI[] labels = new TMPro.TextMeshProUGUI[0];
        public GameObject() { transform = new Transform { gameObject = this }; }
        public T[] GetComponents<T>() => labels as T[] ?? new T[0];
        public T[] GetComponentsInChildren<T>(bool includeInactive = false) => GetComponents<T>();
    }
}

namespace WhiteDragon.Ledger
{
    public partial class GameItem
    {
        public Dictionary<string, string> stringTags = new Dictionary<string, string>();
        public string bookTagValue, shortDescription, longDescription, flavorText;
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
        public const string WATER_BOOK = "WATER_BOOK", INGOT_BOOK = "INGOT_BOOK";
        public string title;
        public string[] subtitle = new string[0], body = new string[0];
        public int pageCount;
        public static Dictionary<string, StoreBookData> bookDatas = new Dictionary<string, StoreBookData>();
        public static StoreBookData GetBookById(string id) => bookDatas[id];
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
        public Window contractPanel = new Window();
        public Window deedPanel = new Window();
        public Window lotteryInfo = new Window();
        public Window cigaretteGuidePanel = new Window();
        public Window foodStampGuidePanel = new Window();
        public Window injectorGuidePanel = new Window();
        public Window cigaretteColorPanel = new Window();
        public Window augIdGuide = new Window();
        public Window wantedPanel = new Window();
        public Window JoeWildBusinessCard = new Window();
        public Window cardBrewer = new Window();
        public Window cardMining = new Window();
        public Window cardPharma = new Window();
        public Window cardChem = new Window();
        public Window cardRev = new Window();
        public Window secCardPanel = new Window();
        public Window codeBookPanel = new Window();
    }
    public class RepublicLogoUIManager
    {
        public static RepublicLogoUIManager Instance = new RepublicLogoUIManager();
        public Window panel = new Window();
    }
    public class LoanPanelUIManager
    {
        public static LoanPanelUIManager Instance = new LoanPanelUIManager();
        public Window panel = new Window();
        public TextMeshProUGUI body = new TextMeshProUGUI { text = "Loan terms" };
        public void OpenUI() { panel.activeSelf = true; }
        public void CloseUI() { panel.activeSelf = false; }
    }
    public static class AppraisalRankHelper { public static bool IsCertificate(GameItem item) => false; public static int GetRank() => 1; }
    public class AppraisalItemType { public string GetDisplayName() => "Sample"; }
    public static class AppraisalItemRegistry
    {
        public static AppraisalItemType[] GetUnlocked(int rank) => new[] { new AppraisalItemType() };
        public static bool IsBlocked(AppraisalItemType type, int rank) => true;
    }
    public class SecData
    {
        public string note = "Officer notes";
        public int GetTotalCrimeValue() => 12;
        public string GetCaseOfficer(int value) => "Officer " + value;
        public string GetSuspectedCrimeDisplay() => "Case";
        public string GetAction() => "Action";
        public string GetOperation() => "Operation";
        public string GetEvidenceLevelDisplay() => "Evidence";
        public string GetProjectedSentence() => "Sentence";
    }
    public partial class PlayerStore { public SecData secData = new SecData(); }
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

            req.Remove("since_token");
            int nextId = 19100;
            JObject Inspect(GameItem item)
            {
                req["owner_id"] = LocalId(item);
                var response = new JObject();
                Expect(WorkstationAction(req, response) == "observed", "inspect " + item.identifier);
                return (JObject)response["window"]["document"];
            }
            GameItem Document(string kind) => Item(nextId++, kind, 0);
            Window Label(string text) => new Window { labels = new[] { new TextMeshProUGUI { text = text } } };
            var multi = MultiPageUIManager.Instance;
            var guides = new[] { "fuel_guide", "water_guide", "hydroponic_guide", "chemistry_guide", "ribwich_guide", "ingot_book", "hand_guide", "wine_book" };
            foreach (string kind in guides)
            {
                var root = new Window();
                root.transform.Add(Label(kind).transform);
                root.transform.Add(new Window().transform);
                multi.books.Add(root);
            }
            multi.OpenUI(2);
            foreach (string kind in guides)
            {
                var item = Document(kind);
                if (kind == "ingot_book") { item.waterTags.Add(BookHelper.BOOK_ID); item.bookTagValue = "INGOT_BOOK"; }
                var doc = Inspect(item);
                Expect((int)doc["page_count"] == 2 && (string)doc["pages"][0]["lines"][0] == kind &&
                    (string)doc["pages"][1]["reason"] == "image_only_guide" && item.responseClicks == 0,
                    "correct mixed pages with another guide open: " + kind);
            }
            Expect(multi.panel.activeInHierarchy, "keep player's reader open");
            multi.CloseUI();
            var paperUi = PaperUIManager.Instance;
            var panels = new Dictionary<string, Window> {
                ["mentor_contract"] = paperUi.contractPanel,
                ["deed"] = paperUi.deedPanel,
                ["lottery_info"] = paperUi.lotteryInfo,
                ["cigarette_guide"] = paperUi.cigaretteGuidePanel,
                ["stamp_guide"] = paperUi.foodStampGuidePanel,
                ["injector_guide"] = paperUi.injectorGuidePanel,
                ["cigarette_color"] = paperUi.cigaretteColorPanel,
                ["aug_id_guide"] = paperUi.augIdGuide,
                ["wanted_paper"] = paperUi.wantedPanel,
                ["joe_card"] = paperUi.JoeWildBusinessCard,
                ["card_brewer"] = paperUi.cardBrewer,
                ["card_mining"] = paperUi.cardMining,
                ["card_pharma"] = paperUi.cardPharma,
                ["card_chem"] = paperUi.cardChem,
                ["card_rev"] = paperUi.cardRev,
                ["sec_card"] = paperUi.secCardPanel,
                ["codebook"] = paperUi.codeBookPanel,
                ["logo_checker"] = RepublicLogoUIManager.Instance.panel
            };
            foreach (var pair in panels)
            {
                pair.Value.labels = Label(pair.Key).labels;
                pair.Value.transform.Add(Label("hidden text").transform);
                var doc = Inspect(Document(pair.Key));
                Expect((string)doc["pages"][0]["lines"][0] == pair.Key &&
                    ((JArray)doc["pages"][0]["lines"]).Count == 1, "read only this card or guide: " + pair.Key);
            }
            paperUi.wantedPanel.labels = new TextMeshProUGUI[0];
            Expect((string)Inspect(Document("wanted_paper"))["reason"] == "image_only_guide", "wanted pictures stay with player");
            var sheet = Document("deed");
            paperUi.deedPanel = null;
            req["owner_id"] = LocalId(sheet);
            result = new JObject();
            Expect(WorkstationAction(req,result)=="reader_unavailable" && (int)result["item_id"]==LocalId(sheet), "identify missing panel");
            paperUi.deedPanel = panels["deed"];

            GameItem Tagged(string kind)
            {
                var item = Document(kind);
                item.scratchTagReader = key => new GameItem {bookTagValue = key == "LOG_CONTENT_TAG" ? "A/B" : key, associatedId = 3};
                return item;
            }
            Expect((string)Inspect(Tagged("tutorial_page"))["signature"]=="TUTORIAL_PAGE_SIGNATURE", "tutorial page signature");
            Expect((string)Inspect(Tagged("handnote"))["signature"]=="HANDNOTE_SIGN", "handnote signature from native tag");
            Expect((string)Inspect(Tagged("rev_codeword"))["body"]=="SLIP_BODY" &&
                (string)Inspect(Tagged("slip"))["title"]=="SLIP_TITLE", "ordinary and generated slips");
            Expect((string)Inspect(Tagged("sec_slip"))["body"]=="body", "security slip");
            var alias = Tagged("custom_story_note");
            alias.waterTags.Add("SLIP_BODY");
            Expect((string)Inspect(alias)["body"]=="SLIP_BODY", "generated paper identified by its native tags");
            var order = Inspect(Tagged("procurement_slip"));
            Expect((int)order["reward"]==3 && (int)order["due_in_days"]==3 && (string)order["body"]=="PROCUREMENT_ORDER_BODY", "procurement terms");
            Expect((string)Inspect(Tagged("expedition_log"))["lines"][1]=="B", "ordered log entries");
            Expect((string)Inspect(Document("intel_sec"))["officer"]=="Officer 12", "native record data");
            Expect((string)Inspect(Document("loan_contract"))["body"] == "Loan terms" && !LoanPanelUIManager.Instance.panel.activeSelf, "loan terms restore closed panel");
            foreach (string cert in new[] {"cert1", "cert2", "cert3", "cert4"})
                Expect(!(bool)Inspect(Document(cert))["items"][0]["allowed"], "read certificate options without toggling");
            var permit = Tagged("permit_gun_1");
            permit.waterTags.Add("PERMIT_ITEM_TAG");
            Expect((int)Inspect(permit)["level"]==3 && (string)Inspect(permit)["types"][2]=="PERMIT_ITEM_TAG3", "permit restrictions");
            var note = Document("DOCUMENT");
            note.flavorText = "Furnace instructions";
            Expect((string)Inspect(note)["flavor_text"]=="Furnace instructions", "machine note stored in item description");
            note.flavorText = null;
            Expect((string)Inspect(note)["reason"]=="no_document_text", "document without readable fields");
            var textBook = Document("text_book");
            textBook.waterTags.Add(BookHelper.BOOK_ID);
            textBook.bookTagValue = StoreBookData.WATER_BOOK;
            StoreBookData.bookDatas[textBook.bookTagValue] = new StoreBookData { title="Water", body=new[] {"Page one"} };
            Expect((string)Inspect(textBook)["pages"][0]["body"]=="Page one", "native book table");
            StoreBookData.bookDatas.Remove(textBook.bookTagValue);
            Expect((string)Inspect(textBook)["reason"]=="missing_book_text", "missing text-table key");
            multi.books.Clear();
            System.Console.WriteLine("Shop document replay passed.");
        }
    }
}
