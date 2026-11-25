// Compile with mod/ShopDesk.cs and mod/SaleOrder.cs for a deterministic game-callback replay.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace UnityEngine {
    public static class Time { public static float realtimeSinceStartup = 1, timeScale = 1; }
    public static class Debug { public static void LogWarning(string message) { Console.WriteLine(message); } public static void LogException(Exception e) { throw e; } }
    public class Object { public static T FindObjectOfType<T>() where T:class { if (typeof(T)==typeof(WhiteDragon.Ledger.StoreDoor)) return new WhiteDragon.Ledger.StoreDoor() as T; if (typeof(T)==typeof(WhiteDragon.Ledger.StoreBedroll)) return new WhiteDragon.Ledger.StoreBedroll() as T; return typeof(T)==typeof(WhiteDragon.Ledger.StoreBell)?new WhiteDragon.Ledger.StoreBell() as T:null; } }
}
namespace WhiteDragon.Ledger {
    public class Label { public string text; }
    public class Bubble { public Window gameObject; public Label textfield; }
    public class StoreBell { public void RingBell() { PlayerStore.Instance.currentClientInstance = new StoreClientInstance(); } }
    public static class Interactable { public static bool isPaused; }
    public static class GraphUtils {
        public static bool CanAccept(GameInventory slot, GameItem item) => !slot.IsInsertLocked() && slot.TryFindOneValidInventorySlot(item) != null;
        public static int TryAcceptOnce(GameInventory slot, GameItem item) { item.To(slot); return 0; }
        public static bool CanAccept(GameSlotInventory slot, GameItem item) => slot.childItem == null && item.identifier == "bottle";
        public static int TryAcceptOnce(GameSlotInventory slot, GameItem item) { item.To(slot); return 1; }
    }
    public class Window : UnityEngine.GameObject { }
    public class NegotiationTabletFrame { public PanelNode infoHost; public Window confirmRoot; }
    public partial class ReplayClientAvatar { public Window gameObject = new Window(); }
    public partial class StoreUIManager {
        public ReplayClientAvatar clientGameObject = new ReplayClientAvatar();
        public static StoreUIManager Instance = new StoreUIManager();
        public SceneFade blackscreen;
        public Window closeShutterConfirmationPanel = new Window();
        public bool IsTyping() => false;
        public StoreCalendar storeCalendar = new StoreCalendar();
        public bool isAboutToStartMainDialog;
    }
    public partial class DialogUIManager {
        public List<DialogChoiceBubble> dialogChoiceBubbles=new List<DialogChoiceBubble>();
        public static DialogUIManager Instance;
        public Label clientNameTMP;
        public Bubble clientBubble;
        public void OnAdvancePressed() { isWaitingForManualAdvance=false; }
        public bool isWaitingForManualAdvance;
        public bool choice, typing, HasPausedConversation;
        public object current;
        public bool IsChoice() => choice;
        public object GetCurrentDialog() => current;
        public bool IsCurrentDialogEnd() => true;
        public bool IsLineStillDisplaying(object d) => typing;
    }
    public partial class StoreStation { public static StoreStation Instance = new StoreStation(); public int dayCounter=10; }
    public class GridShape {
        public int minX { get; set; }
        public int minY { get; set; }
        public int width { get; set; }
        public int height { get; set; }
        public int globalWidth { get; set; }
        public int globalHeight { get; set; }
        public int maxX => minX + globalWidth - 1;
        public int maxY => minY + globalHeight - 1;
        public int orientation { get; set; }
        public bool flipped { get; set; }
        public byte defaultValue = 1;
        public byte[,] cells;
        public GridShape(int w = 1, int h = 1) { width = globalWidth = w; height = globalHeight = h; }
        public byte Get(int x, int y) {
            if (x < minX || x > maxX || y < minY || y > maxY) return 0;
            return GetLocal(x - minX, y - minY);
        }
        public byte GetLocal(int x, int y) {
            if (x < 0 || x >= width || y < 0 || y >= height) return 0;
            return cells != null ? cells[x, y] : defaultValue;
        }
    }
    public partial class GameInventory : PixelElement {
        public List<GameItem> childItems = new List<GameItem>();
        public Func<GameItem, GameInventory, bool> mayInventoryAddItemFunc;
        public bool IsInsertLocked() => false;
        public bool IsRemoveLocked() => false;
        public bool replayFull;
        public object TryFindOneValidInventorySlot(GameItem item) => replayFull ? null : new object();
    }
    public class GameGridInventory : GameInventory {
        public GridShape inventoryShape;
        public GameGridInventory() : this(1, 1) {}
        public GameGridInventory(int w, int h) { inventoryShape = new GridShape(w, h) { defaultValue = 0 }; }
    }
    public partial class GameSlotInventory:GameInventory { public GameItem childItem; }
    public partial class EmporiumEntry {
        public PixelWindow responseWindowClosable,responseWindow;
        public GameInventory responseInventoryClosable,responseInventory;
        public static EmporiumEntry Instance = new EmporiumEntry();
        public List<GameItem> GetAllNonOwnedItem() {
            var result=frontInvinvElement.childItems.FindAll(i=>!i.owned);
            result.AddRange(backInvinvElement.childItems.FindAll(i=>!i.owned));
            return result;
        }
        public GameGridInventory frontInvinvElement=new GameGridInventory(18, 4);
        public GameGridInventory invElement=new GameGridInventory(24, 10), backInvinvElement=new GameGridInventory(18, 4),
            showcaseElement=new GameGridInventory(7, 5), hiddenElement=new GameGridInventory(1, 1);
        public List<GameItem> GetAllOwnedItemsByTag(string tag) {
            var result = new List<GameItem>();
            foreach (var inv in new GameInventory[] { invElement, showcaseElement, frontInvinvElement, backInvinvElement })
                foreach (var item in inv.childItems)
                    if (item != null && item.owned && item.IsTag(tag)) result.Add(item);
            return result;
        }
        public GameItem trashcan;
    }
    public class StorePoster
    {
        public static StorePoster Instance = new StorePoster();
        public bool isPulled;
    }
    public class ItemCondition { public string identifier, display; }
    public partial class ItemFeature {
        public ItemCondition label;
        public ItemCondition GetCurrentItemCondition() => label;
    }
    public static class ItemConditionList {
        public static ItemCondition CreateBaseWater() => new ItemCondition { display="base water" };
    }
    public partial class GameItem {
        public PixelWindow contentWindow;
        public GridShape shape = new GridShape(1, 1), modifiedShape = new GridShape(1, 1);
        public Func<GameItem, GameInventory, bool> mayInventoryAddItemFunc;
        public ItemFeature waterLabel;
        public ItemFeature FindItemFeatureByCategory(string category) => waterLabel;
        public string identifier, name;
        public int uniqueId, price, unitCount=1;
        public bool owned=true;
        public GameInventory parentInventory;
        public int GetUniqueID() => uniqueId;
        public bool MayActivateSlotItem() => true;
        public bool CanActivateSlotItem() => true;
        public string GetDisplayName(bool custom) => string.IsNullOrEmpty(name) ? identifier : name;
        public bool mayRemove = true;
        public bool MayRemove() => mayRemove;
        public long GetNegociatedValue() => price;
        public long GetCurrentValue(bool useRetailMarkup = false, bool withChild = true, bool includeEvents = true, bool forceMarkup = false, long extraMarketMod = 0L) => price;
        public long GetBaseValue() => price;
        public List<string> GetGameItemType() => new List<string> { identifier=="bottle" ? "WATER" : identifier ?? "misc" };
        public bool IsTag(string key) => (key=="LIQUID_CONTAINER_TAG" && identifier=="bottle") || waterTags.Contains(key);
        public GameItem GetTagReadonly(string key) => key=="water_part" ? new GameItem {associatedId=waterTags.Contains("replay_solvent") ? 0 : volume - alcoholUl} : key=="alcohol_part" ? new GameItem {associatedId=alcoholUl} : scratchTagReader == null ? this : scratchTagReader(key);
        public int GetInt() => associatedId;
        public List<GameItem> FindAllChildItems()
        {
            var found = new List<GameItem>();
            if (contentWindow?.childElement is GameInventory childInv)
            {
                foreach (var child in childInv.childItems)
                {
                    if (child == null) continue;
                    found.Add(child);
                    found.AddRange(child.FindAllChildItems());
                }
            }
            return found;
        }
        public void To(GameInventory target) {
            if (parentInventory is GameSlotInventory oldSlot) oldSlot.childItem = null;
            parentInventory?.childItems.Remove(this);
            parentInventory=target;
            target?.childItems.Add(this);
            if (target is GameSlotInventory newSlot) newSlot.childItem = this;
        }
    }
    public partial class StoreClient {
        public enum ClientIntent { MERCHANT, INFORMATION_DEALER, BARTER, DIALOGUE, APPRAISAL_SERVICE, EXPEDITION, GUNSMITH, INSPECTION, LOAN_SHARK, PROCUREMENT_COLLECT, PROCUREMENT_OFFER, RENT }
        public ClientIntent clientIntent;
        public BarterOffer barterOffer;
        public bool isIntroduced;
        public int serviceCost;
        public string displayName="喬治奧", identifier="georgio";
        public bool isOfferWholesale, useClientBudget=true, isBusinessComplete, isRequireVariety;
        public List<string> clientBuyingTagList = new List<string>(), clientBlackTagList = new List<string>(),
            clientBuyingIdList = new List<string>(), clientBlackIdList = new List<string>();
        public List<string> varietyTrackingList=new List<string>();
        public int budget=5;
        public Action<GameItem,int> onItemSold, onItemBought;
        public int GetBudget() => budget;
        public object mainDialogue;
        public bool isMainDialogueStarted, isRegularSellDisable, isMultiBuyDisabled;
        public bool isClientSelling = true;
        public bool isClientBuying = true;
        public bool IsClientSelling() => isClientSelling;
        public bool IsClientBuying() => isClientBuying;
    }
    public class StoreClientInstance {
        public StoreClient client=new StoreClient();
        public StoreClient GetClientBlueprint() => client;
    }
    public partial class HealthData {
        public const int SERIOUS_WOUND_THRESHOLD = 5;
        public int woundState;
        public bool isWoundStable = true, isWoundedFresh;
        public static bool IsSeriouslyWounded() => PlayerStore.Instance?.healthData?.woundState > SERIOUS_WOUND_THRESHOLD;
    }
    public class StoreClientData {
        public int nuclearWasteLeftOnCounter, nuclearWasteDumped;
    }
    public class ReputationUIManager {
        public static ReputationUIManager Instance = new ReputationUIManager();
        public Window reputationNotifier = new Window();
        public TMPro.TextMeshProUGUI reputationNotifierTMP = new TMPro.TextMeshProUGUI();
    }
    public partial class PlayerStore {
        public static bool HasInstance => Instance != null;
        public int saveSlotId=4;
        public static PlayerStore Instance=new PlayerStore();
        public HealthData healthData = new HealthData();
        public StoreClientData storeClientData = new StoreClientData();
        public static StoreClientData GetStoreClientData() => Instance?.storeClientData;
        public StoreClientInstance currentClientInstance=new StoreClientInstance();
        public int playerCash=100;
        public bool CanBuyItemFromTable() => !GetCurrentlyNegociatedItem().owned;
        public bool CanPay(int price) => playerCash >= price;
        public int lockNextBellFlag;
        public int waiting = 1;
        public int GetClientInQueueCount() => waiting;
        public bool CanEndDay() => true;
        public void TryCallNextClient() { if (CanNextClient()) { waiting--; currentClientInstance=new StoreClientInstance(); } }
        public bool CanNextClient() => currentClientInstance==null && waiting>0;
        public bool isClientArrived=true, dismissable=true, interceptDismiss;
        public bool IsClientDimissable() => currentClientInstance != null && dismissable;
        public void TryPlayerDismissCurrentClient(bool callHooks) { if (!interceptDismiss) currentClientInstance=null; }
        public int GetWholesalePrice() => 9;
        public GameItem GetCurrentlyNegociatedItem() => EmporiumEntry.Instance.backInvinvElement.childItems[0];
        public bool CanSellThisItem(GameItem item, bool ignoreBudget) => ignoreBudget || item.price<=currentClientInstance.client.budget;
        public bool CanSellItemFromTable(bool ignoreBudget) => CanSellThisItem(GetCurrentlyNegociatedItem(),ignoreBudget);
    }
    public partial class NegociationUIManager {
        public static NegociationUIManager Instance=new NegociationUIManager();
        public bool IsOpen=true;
        public ReceiptElements uiObject = new ReceiptElements();
        public int extraFee, paymentCalls, sellAllCalls;
        public void OpenUI() { IsOpen=true; }
        public void OnSellAllClick() {
            ++sellAllCalls;
            var store=PlayerStore.Instance;
            var client=store.currentClientInstance.client;
            var items=EmporiumEntry.Instance.backInvinvElement.childItems.FindAll(i => i.owned);
            for (int idx=items.Count-1; idx>=0; idx--) {
                var item=items[idx];
                if (!store.CanSellThisItem(item, false)) continue;
                if (client.isRequireVariety) client.varietyTrackingList.Add(item.identifier);
                client.budget-=item.price;
                client.onItemBought?.Invoke(item, item.price);
                store.playerCash+=item.price;
                item.owned=false;
                item.To(null);
            }
        }
        public void OnWholesaleClick() { Purchase(true); }
        public void BuyItem() {
            var store=PlayerStore.Instance;
            var item=store.GetCurrentlyNegociatedItem();
            store.playerCash-=item.price;
            store.currentClientInstance.client.onItemSold?.Invoke(item,item.price);
            item.owned=true;
            item.To(EmporiumEntry.Instance.invElement);
        }
        public void OnBuyAllClick() { Purchase(false); }
        private void Purchase(bool wholesale) {
            var store=PlayerStore.Instance;
            var batch=EmporiumEntry.Instance.GetAllNonOwnedItem();
            int sum=0;
            foreach(var item in batch) sum+=item.price;
            store.playerCash-=wholesale?store.GetWholesalePrice():sum;
            var client=store.currentClientInstance.client;
            foreach(var item in batch) {
                client.onItemSold?.Invoke(item,item.price);
                item.owned=true;
                item.To(EmporiumEntry.Instance.invElement);
            }
            client.isOfferWholesale=false;
        }
        public static bool IsTradeLocked() => false;
        public void OnOfferAcceptClick() {
            var store=PlayerStore.Instance;
            var client=store.currentClientInstance.client;
            if (client.clientIntent == StoreClient.ClientIntent.BARTER) { AcceptReplayBarter(client); return; }
            if (client.clientIntent == StoreClient.ClientIntent.INFORMATION_DEALER) {
                ++paymentCalls;
                store.playerCash -= client.serviceCost + extraFee;
                client.isBusinessComplete = true;
                return;
            }
            var item=store.GetCurrentlyNegociatedItem();
            client.budget-=item.price;
            client.onItemBought?.Invoke(item,item.price);
            store.playerCash+=item.price;
            item.owned=false;
            item.To(null);
        }
    }
    public static class GeneralHelper {
        public static bool IsItemOwned(GameItem item) => item.owned;
        public static bool IsItemEffectivelyOwned(GameItem item) => item.owned;
        public static bool IsDisplayOnly(GameItem item) => false;
        public static bool IsItemLocked(GameItem item) => false;
        public static bool IsHazardousWaste(GameItem item) =>
            item != null && (item.identifier == "nuclear_waste" || item.IsTag("HAZARDOUS_WASTE_TAG"));
    }
    public static class MachineryHelper {
        public static int GetMachinePowerUsage(GameItem machine) => 4;
        public static int GetCurrentQualityBonus(GameItem machine) => machine.associatedId;
    }
    public static class MachineHelper {
        public const string STANDARD_MACHINE_TAG="standard_machine";
        public static GameSlotInventory container=new GameSlotInventory(), battery=new GameSlotInventory();
        public static object[] furnaceSlots, chemistrySlots;
        public static object GetBodyStorage(GameItem item,int index) =>
            (item.identifier=="chem_finisher" || item.identifier=="heating_plate" || item.identifier=="evaporator") && chemistrySlots!=null ? chemistrySlots[index] : item.identifier=="furnace" && furnaceSlots!=null ? furnaceSlots[index] : index==2 ? container : null;
        public static GameSlotInventory GetBatterySlot(GameItem item) => battery;
        public static bool CanPower(GameItem item,GameSlotInventory power) => power.childItem!=null;
    }
    public static class PowerHelper { public const string POWER_SOURCE_ITEM_ENERGY="power", POWER_SOURCE_ITEM_MAX_ENERGY="max"; }
    public static class WaterTestHelper { public const string IS_SPENT="used", ASSOCIATED_ITEM_ID="associated"; }
    public static partial class ModHelper {
        static Dictionary<string,string> data=new Dictionary<string,string>();
        public static List<int> placed=new List<int>();
        public static string GetModData(string m,string k,string d) => data.ContainsKey(k)?data[k]:d;
        public static void SetModData(string m,string k,string v) { data[k]=v; }
        public static bool ModDataFileExists(string m,string k) => data.ContainsKey(k);
        public static string ReadModDataFileText(string m,string k) => data[k];
        public static void WriteModDataFileText(string m,string k,string v) { data[k]=v; }
        public static void DeleteModDataFile(string m,string k) { data.Remove(k); }
        public static void MoveItemToFrontCounter(GameItem i) { i.To(EmporiumEntry.Instance.frontInvinvElement); }
        public static void MoveItemToInventory(GameItem i) { i.To(EmporiumEntry.Instance.invElement); }
        public static void MoveItemToDisplayCase(GameItem i) { i.To(EmporiumEntry.Instance.showcaseElement); }
        public static Func<GameItem, bool> blockBackPlacement;
        public static void MoveItemToBackCounter(GameItem i) {
            placed.Add(i.uniqueId);
            if (blockBackPlacement != null && blockBackPlacement(i)) return;
            i.To(EmporiumEntry.Instance.backInvinvElement);
        }
    }
    public sealed partial class InventoryObserver {
        private const string ModId="replay";
        private string generation="load", phase="OPEN";
        private int slot=4, sequence=1, loadScans=1, fullScans=1, nextId;
        private Dictionary<GameInventory,string> areas=new Dictionary<GameInventory,string>();
        private Dictionary<GameItem,int> ids=new Dictionary<GameItem,int>();
        private Dictionary<int,GameItem> itemsById=new Dictionary<int,GameItem>();
        private HashSet<GameItem> changed=new HashSet<GameItem>();
        private void FlushChanges() { ++sequence; }
        private static void Write(string name,JObject data) { ModHelper.WriteModDataFileText(ModId,name,data.ToString()); }
        private static void Expect(bool b,string msg) { if(!b) throw new Exception(msg); }
        private GameItem Item(int key,string kind,int price) {
            var i=new GameItem { uniqueId=key, identifier=kind, price=price };
            i.To(EmporiumEntry.Instance.invElement);
            LocalId(i);
            return i;
        }
        private void Run() {
            areas.Add(EmporiumEntry.Instance.invElement,"inventory");
            areas.Add(EmporiumEntry.Instance.showcaseElement,"showcase");
            areas.Add(EmporiumEntry.Instance.frontInvinvElement,"front");
            areas.Add(EmporiumEntry.Instance.backInvinvElement,"back");
            LoadDesk();
            var water=new GameItem[4];
            merchant.isOfferWholesale=true;
            PlayerStore.Instance.playerCash-=9;
            for(int i=0;i<4;i++) {
                water[i]=Item(100+i,"dehydrated_water",10);
                water[i].owned=false;
                merchant.onItemSold(water[i],2);
                water[i].owned=true;
            }
            merchant.isOfferWholesale=false;
            CloseTransactions();
            Expect(lots.Count==1 && (int)lots[0]["paid"]==9,"actual wholesale total, not four rounded unit quotes");
            var cheap=Item(200,"soap",3);
            var rows=new JArray();
            foreach(var i in new[]{water[0],water[1],cheap})
                rows.Add(new JObject {["item_id"]=LocalId(i),["min_price"]=1});
            string cmd=Guid.NewGuid().ToString("N");
            Write("sale-load.json",new JObject { ["command_id"]=cmd,["generation"]=generation,["revision"]=sequence,
                ["visit"]=visitId,["lines"]=rows,["expires_ms"]=DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds(),
                ["stop_same_kind_on_budget"]=true });
            for(int n=0;n<12;n++){UnityEngine.Time.realtimeSinceStartup++;CheckOrder();CloseTransactions();}
            var reply=JObject.Parse(ModHelper.ReadModDataFileText(ModId,"receipt-"+cmd+".json"));
            Expect((string)reply["items"][0]["status"]=="returned_over_budget" &&
                   (string)reply["items"][1]["status"]=="skipped_same_kind_budget" &&
                   (string)reply["items"][2]["status"]=="sold" && (int)reply["remaining_budget"]==2,"budget policy");
            Expect(ModHelper.placed.Count==2 && ModHelper.placed[0]==100 && ModHelper.placed[1]==200,"skipped bottle touched counter");
            // Default orders try both items. A lower-than-requested quote on an
            // opted-in order must also leave the following item eligible.
            foreach (bool enabled in new[]{false,true}) {
                var first=Item(enabled?401:403,"ore",10);
                var second=Item(enabled?402:404,"ore",10);
                merchant.budget=enabled?15:5;
                var checkRows=new JArray(new JObject {["item_id"]=LocalId(first),["min_price"]=enabled?12:1},
                                          new JObject {["item_id"]=LocalId(second),["min_price"]=1});
                cmd=Guid.NewGuid().ToString("N");
                var request=new JObject { ["command_id"]=cmd,["generation"]=generation,["revision"]=sequence,
                    ["visit"]=visitId,["lines"]=checkRows,["expires_ms"]=DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds(),
                    ["stop_same_kind_on_budget"]=enabled };
                Write("sale-load.json",request);
                int count=ModHelper.placed.Count;
                for(int n=0;n<10;n++){UnityEngine.Time.realtimeSinceStartup++;CheckOrder();CloseTransactions();}
                reply=JObject.Parse(ModHelper.ReadModDataFileText(ModId,"receipt-"+cmd+".json"));
                Expect(ModHelper.placed.Count==count+2,"both items should be offered");
                Expect((string)reply["items"][0]["status"]==(enabled?"returned_below_minimum":"returned_over_budget"),"first refusal reason");
                Expect((string)reply["items"][1]["status"]==(enabled?"sold":"returned_over_budget"),"second item not prematurely discarded");
                Write("sale-load.json",request);
                UnityEngine.Time.realtimeSinceStartup++;CheckOrder();
                Expect(ModHelper.placed.Count==count+2,"duplicate command acted again");
            }
            for(int i=0;i<4;i++){
                merchant.onItemBought(water[i],10); water[i].owned=false; CloseTransactions();
                Expect((int)lots[0]["members"][i]["sale"]==10,"individual sale recovery");
            }
            DisconnectVisit();LoadDesk();
            Expect(unsold.Count==0 && lots.Count==1,"save-bound lot recovery");
            var purifier=Item(300,"water_purifier",0);
            purifiers.Add(purifier);
            var bottle=Item(301,"bottle",0);
            bottle.volume=200000;
            bottle.waterLabel=new ItemFeature { label=new ItemCondition { display="label before" } };
            bottle.To(MachineHelper.container);
            BeforeSleep();
            MachineHelper.battery.childItem=Item(302,"battery",0);
            bottle.waterLabel.label.display="label after";
            var now=MachineRows();
            Expect((string)now[0]["bottle_label"]=="label after" && (string)night[0]["bottle_label"]=="label before","visible bottle labels");
            Expect((int)now[0]["bottle_key"]==(int)night[0]["bottle_key"] && (bool)now[0]["can_power"],"overnight bottle and power");
            phase="MORNING";
            for(int i=0;i<6;i++) stripSupplies.Add(Item(501+i,"water_test_strip",0));
            var take = new JObject { ["generation"]=generation, ["revision"]=sequence,
                ["command_id"]=Guid.NewGuid().ToString("N"), ["source"]="purifier", ["destination"]="inventory",
                ["machine_id"]=LocalId(purifier), ["item_id"]=LocalId(cheap),
                ["expires_ms"]=DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds() };
            Expect((string)ExecuteMove(take)["status"]=="source_changed" && MachineHelper.container.childItem==bottle,"wrong bottle");
            take["item_id"]=LocalId(bottle);
            take["machine_id"]=LocalId(cheap);
            Expect((string)ExecuteMove(take)["status"]=="machine_changed" && MachineHelper.container.childItem==bottle,"wrong machine");
            take["machine_id"]=LocalId(purifier);
            var extracted=ExecuteMove(take);
            Expect((string)extracted["status"]=="moved" && (string)extracted["actual_area"]=="inventory" &&
                   (bool)extracted["source_slot_empty"] && (int)extracted["available_strips"]==6 &&
                   (int)extracted["bottle_key"]==301 && (int)extracted["machine_key"]==300 &&
                   (string)extracted["bottle_label"]=="label after" && (int)extracted["load_scans"]==1 && extracted["name"]==null,"extraction receipt");
            foreach (string source in new[]{"inventory","showcase"})
            {
                if (source=="showcase") bottle.To(EmporiumEntry.Instance.showcaseElement);
                take["item_id"] = LocalId(bottle);
                take["source"] = source;
                take["destination"] = "purifier";
                take["revision"] = sequence;
                bottle.owned=false;
                Expect((string)ExecuteMove(take)["status"]=="not_owned","no insertion of someone else's bottle");
                bottle.owned=true;
                var inserted=ExecuteMove(take);
                Expect((string)inserted["status"]=="moved" && (int)inserted["slot_bottle_id"]==LocalId(bottle) &&
                    (int)inserted["machine_id"]==LocalId(purifier) && bottle.parentInventory==MachineHelper.container,"direct insert from "+source);
                take["item_id"] = LocalId(Item(610,"bottle",0));
                take["revision"] = sequence;
                Expect((string)ExecuteMove(take)["status"]=="purifier_occupied","occupied slot stays intact");
                bottle.To(EmporiumEntry.Instance.invElement);
            }
            phase="OPEN";
            var product=Item(901,"ore",12);
            product.owned=false;
            product.To(EmporiumEntry.Instance.frontInvinvElement);
            merchant.isOfferWholesale=true;
            var buy=new JObject { ["action"]="buy",["generation"]=generation,["revision"]=sequence,["visit"]=visitId,
                ["expires_ms"]=DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds(),["max_total"]=8 };
            var outcome=new JObject();
            var dialog=new DialogUIManager();
            DialogUIManager.Instance=dialog;
            merchant.mainDialogue=new object();
            StoreUIManager.Instance.isAboutToStartMainDialog=true;
            PlayerStore.Instance.isClientArrived=false;
            Expect((string)CounterView()["stage"]=="arriving" && CounterView()["offer"]==null,"walk-in with stock");
            PlayerStore.Instance.isClientArrived=true;
            Expect((string)CounterView()["stage"]=="awaiting_opening" && CounterView()["offer"]==null,"greeting scheduled");
            StoreUIManager.Instance.isAboutToStartMainDialog=false;
            Expect(ActAtCounter(buy,outcome)=="awaiting_opening" && !product.owned,"stock cannot unlock purchase before greeting");
            merchant.isMainDialogueStarted=true;
            dialog.current=merchant.mainDialogue;
            dialog.typing=true;
            Expect((string)CounterView()["stage"]=="speaking" && CounterView()["offer_token"]==null,"final line typing");
            dialog.typing=false;
            dialog.choice=true;
            Expect((string)CounterView()["stage"]=="choice_required","choice never becomes an offer");
            dialog.choice=false;
            Expect(ActAtCounter(buy,outcome)=="speaking" && CounterView()["offer"]==null,"last line typed, conversation still active");
            dialog.current=null;
            dialog.HasPausedConversation=true;
            Expect((string)CounterView()["stage"]=="conversation_paused" && CounterView()["offer"]==null,"paused opening");
            merchant.clientBuyingTagList.Add("FOOD");
            merchant.clientBlackTagList.Add("MATERIAL");
            Expect(CounterView()["demand"]==null,"wait for speech before publishing demand");
            dialog.HasPausedConversation=false;
            ReplayPanelDemand();
            buy["offer_token"]=CounterView()["offer_token"];
            Expect(buy["offer_token"].Type==JTokenType.String,"offer after conversation end");
            int cash=PlayerStore.Instance.playerCash;
            Expect(ActAtCounter(buy,outcome)=="over_limit" && !product.owned && cash==PlayerStore.Instance.playerCash,"purchase cap");
            buy["max_total"]=9;
            DialogUIManager.Instance=new DialogUIManager { isWaitingForManualAdvance=true };
            Expect(ActAtCounter(buy,outcome)=="advance_required" && !product.owned,"opening unfinished");
            DialogUIManager.Instance=dialog;
            buy["offer_token"]="stale";
            Expect(ActAtCounter(buy,outcome)=="offer_changed" && !product.owned,"stale offer");
            buy["offer_token"]=CounterView()["offer_token"];
            Expect(ActAtCounter(buy,outcome)=="purchased" && product.owned && (int)outcome["paid"]==9,"confirmed wholesale");
            Expect((string)CounterView()["stage"]=="no_goods" && CounterView()["offer"]==null,"no zero-price empty offer");
            Expect((long)outcome["quoted_total"]==9 && (string)outcome["price_source"]=="wholesale_quote","whole lot has confirmed asking price");
            var regular=Item(902,"soap",3); regular.owned=false; regular.To(EmporiumEntry.Instance.frontInvinvElement);
            buy["revision"]=sequence;
            var regularOffer = CounterView();
            Expect((long)regularOffer["offer"]["items"][0]["reference_price"]==3 && regularOffer["offer"]["quoted_total"].Type==JTokenType.Null,
                "unstaged regular stock has a reference price only");
            buy["offer_token"]=regularOffer["offer_token"];
            Expect(ActAtCounter(buy,outcome)=="purchased" && regular.owned && (int)outcome["paid"]==3,"normal batch");
            buy["action"]="ring"; buy["revision"]=sequence; buy["waiting"]=1;
            Expect(ActAtCounter(buy,outcome)=="occupied","bell never dismisses current visit");
            phase="MORNING";
            Expect(ActAtCounter(buy,outcome)=="shop_closed","bell never opens the shop");
            phase="OPEN"; PlayerStore.Instance.currentClientInstance=null; ObserveVisit();
            Expect(ActAtCounter(buy,outcome)=="called" && outcome["visit"]!=null,"one new visit");
            ReplayInventorySorting();
            ReplayItemMenu();
            ReplayItemPanel();
            ReplayChemist();
            ReplayMachineWaitAndInbox();
            ReplayHeatControl();
            new InventoryObserver().ReplayBagStock();
            ReplayInformation();
            ReplayBarter();
            ReplayBarter(true);
            ReplayBargain();
            ReplayPhone();
            ReplayHealth();
            ReplayReport();
            ReplayScenes();
            ReplayScratchCard();
            ReplayJourney();
            ReplayDay();
            ReplayReception();
            ReplayWorkstation();
            ReplayShopDocuments();
            ReplayWaterPanel();
            ReplayWaterUse();
            ReplaySaleResume();
            ReplayP0();
            ReplayMixedTrades();
            ReplayBundleSale();
            ReplayNetwork();
            ReplayDismiss();
            ReplayCounterDay();
            ReplayDialogueStage();
            ReplayStockSection();
            Console.WriteLine("Counter replay passed: bell, opening guard, cap, stale offer, wholesale and normal purchase.");
            Console.WriteLine("Native replay passed: paid 9, four sales of 10, budget-return/skip/cheap sale, same bottle after night.");
        }
        private void ReplayBundleSale()
        {
            var store = PlayerStore.Instance;
            var shop = EmporiumEntry.Instance;
            var ui = NegociationUIManager.Instance;
            phase = "OPEN";
            store.storeState = PlayerStore.StoreState.OPEN;
            store.currentClientInstance = new StoreClientInstance();
            ObserveVisit();
            DialogUIManager.Instance = new DialogUIManager();
            shop.backInvinvElement.childItems.Clear();

            JObject RunBundle(JArray lines)
            {
                string cmd = Guid.NewGuid().ToString("N");
                Write("sale-" + generation + ".json", new JObject {
                    ["command_id"] = cmd, ["generation"] = generation, ["revision"] = sequence,
                    ["visit"] = visitId, ["lines"] = lines, ["bundle"] = true,
                    ["expires_ms"] = DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds()
                });
                for (int n = 0; n < 8; n++) { UnityEngine.Time.realtimeSinceStartup++; CheckOrder(); CloseTransactions(); }
                return JObject.Parse(ModHelper.ReadModDataFileText(ModId, "receipt-" + cmd + ".json"));
            }

            // 1. Success: stage from inventory and showcase, settle in one click.
            var a = Item(8101, "soap", 4);
            var b = Item(8102, "ore", 6);
            b.To(shop.showcaseElement);
            merchant.budget = 15;
            int cashBefore = store.playerCash;
            int callsBefore = ui.sellAllCalls;
            var reply = RunBundle(new JArray(
                new JObject { ["item_id"] = LocalId(a), ["min_price"] = 4 },
                new JObject { ["item_id"] = LocalId(b), ["min_price"] = 5 }
            ));
            Expect((string)reply["status"] == "finished" && ui.sellAllCalls == callsBefore + 1 &&
                store.playerCash == cashBefore + 10 && merchant.budget == 5 &&
                (string)reply["items"][0]["status"] == "sold" && (int)reply["items"][0]["received"] == 4 &&
                (string)reply["items"][1]["status"] == "sold" && (int)reply["items"][1]["received"] == 6 &&
                shop.backInvinvElement.childItems.Count == 0, "bundle sell settles in one click");

            // 2. Below minimum rollback: returns all items to original areas.
            var c = Item(8103, "soap", 4);
            var d = Item(8104, "ore", 3);
            d.To(shop.showcaseElement);
            merchant.budget = 20;
            cashBefore = store.playerCash;
            callsBefore = ui.sellAllCalls;
            reply = RunBundle(new JArray(
                new JObject { ["item_id"] = LocalId(c), ["min_price"] = 4 },
                new JObject { ["item_id"] = LocalId(d), ["min_price"] = 5 }
            ));
            Expect((string)reply["status"] == "finished" && ui.sellAllCalls == callsBefore &&
                store.playerCash == cashBefore && c.parentInventory == shop.invElement &&
                d.parentInventory == shop.showcaseElement && shop.backInvinvElement.childItems.Count == 0 &&
                (string)reply["items"][0]["status"] == "returned_below_minimum" && (string)reply["items"][0]["actual_area"] == "inventory" &&
                (string)reply["items"][1]["status"] == "returned_below_minimum" && (string)reply["items"][1]["actual_area"] == "showcase",
                "bundle below-min rolls back all items to original areas");

            // 3. Over combined budget rollback (each item individually <= budget).
            merchant.budget = 5;
            reply = RunBundle(new JArray(
                new JObject { ["item_id"] = LocalId(c), ["min_price"] = 1 },
                new JObject { ["item_id"] = LocalId(d), ["min_price"] = 1 }
            ));
            Expect((string)reply["status"] == "finished" && ui.sellAllCalls == callsBefore &&
                store.playerCash == cashBefore && c.parentInventory == shop.invElement &&
                d.parentInventory == shop.showcaseElement &&
                (string)reply["items"][0]["status"] == "returned_over_budget" &&
                (string)reply["items"][1]["status"] == "returned_over_budget",
                "bundle over-budget rolls back all items");

            // 4. Duplicate variety rollback.
            var dup = Item(8105, "soap", 2);
            merchant.budget = 20;
            merchant.isRequireVariety = true;
            reply = RunBundle(new JArray(
                new JObject { ["item_id"] = LocalId(c), ["min_price"] = 1 },
                new JObject { ["item_id"] = LocalId(dup), ["min_price"] = 1 }
            ));
            merchant.isRequireVariety = false;
            Expect((string)reply["status"] == "finished" && ui.sellAllCalls == callsBefore &&
                c.parentInventory == shop.invElement && dup.parentInventory == shop.invElement &&
                (string)reply["items"][1]["status"] == "returned_duplicate_variety",
                "bundle duplicate variety rolls back all items");

            // 5. Partial/space placement failure rollback.
            var third = Item(8106, "battery", 2);
            ModHelper.blockBackPlacement = item => item == d;
            reply = RunBundle(new JArray(
                new JObject { ["item_id"] = LocalId(c), ["min_price"] = 1 },
                new JObject { ["item_id"] = LocalId(d), ["min_price"] = 1 },
                new JObject { ["item_id"] = LocalId(third), ["min_price"] = 1 }
            ));
            ModHelper.blockBackPlacement = null;
            Expect((string)reply["status"] == "placement_failed" && ui.sellAllCalls == callsBefore &&
                c.parentInventory == shop.invElement && d.parentInventory == shop.showcaseElement &&
                third.parentInventory == shop.invElement && shop.backInvinvElement.childItems.Count == 0 &&
                (string)reply["items"][0]["status"] == "returned_placement_failed" &&
                (string)reply["items"][1]["status"] == "placement_failed" &&
                (string)reply["items"][2]["status"] == "not_attempted",
                "bundle placement failure rolls back already-staged items");

            Console.WriteLine("Bundle sale replay passed: one-click sale, below-min, over-budget, variety and placement-failure rollbacks.");
        }

        private void ReplayDismiss()
        {
            var store=PlayerStore.Instance;
            store.currentClientInstance=new StoreClientInstance();
            ObserveVisit();
            DialogUIManager.Instance=new DialogUIManager();
            var req=new JObject { ["action"]="dismiss",["generation"]=generation,["revision"]=sequence,["visit"]=visitId,
                ["expires_ms"]=DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds() };
            var receipt=new JObject();
            // The sale changed the ledger after MCP observed this visitor.
            ++sequence;
            observedVariety=true;
            store.dismissable=false;
            Expect(ActAtCounter(req,receipt)=="dismiss_unavailable" && (string)receipt["reason"]=="client_not_dismissable" &&
                ((JArray)receipt["blocking_items"]).Count==0,"native dismissal locked attaches reason and empty blocking_items");
            store.dismissable=true;
            EmporiumEntry.Instance.backInvinvElement.childItems.Clear();
            var orderItem = Item(64002,"mug",5);
            orderItem.To(EmporiumEntry.Instance.backInvinvElement);
            order = new JObject { ["command_id"]="active_order", ["lines"]=new JArray() };
            staged = orderItem;
            Expect(ActAtCounter(req,receipt)=="dismiss_unavailable" && (string)receipt["reason"]=="order_in_progress" &&
                (string)receipt["blocking_items"][0]["kind"]=="mug","active order staged item blocks dismiss with reason and blocking_items");
            order = null;
            staged = null;
            orderItem.To(EmporiumEntry.Instance.invElement);
            var delivered = Item(64001,"card_rev",1);
            delivered.To(EmporiumEntry.Instance.frontInvinvElement);
            QueueFrontPaper(delivered,delivered.parentInventory,null);
            delivered.To(EmporiumEntry.Instance.backInvinvElement);
            Expect((bool)ReceptionView()["can_dismiss"],"stray non-order card on back does not block dismissal");
            int waiting=store.waiting;
            store.interceptDismiss=true;
            Expect(ActAtCounter(req,receipt)=="dismiss_intercepted" &&
                delivered.parentInventory==EmporiumEntry.Instance.invElement &&
                (string)receipt["cleared_items"][0]["kind"]=="card_rev","dismiss auto-collects stray owned counter clutter");
            store.interceptDismiss=false;
            Expect(ActAtCounter(req,receipt)=="dismissed" && visitId==null && store.waiting==waiting &&
                   (bool)receipt["revision_rechecked"],"same visit is dismissed once after the sale revision advances");
            store.currentClientInstance=new StoreClientInstance();
            Expect((string)receipt["customer"]=="喬治奧" && (int)receipt["waiting"]==waiting,"dismiss identifies guest and remaining queue");
            Expect(!observedVariety,"dismissal clears the previous visitor's variety rule");
            // The new arrival has the same display name. Only the visit identity is authoritative.
            Expect(ActAtCounter(req,receipt)=="customer_replaced" && store.currentClientInstance!=null,"old visit cannot dismiss new arrival");
            var savedForLeft = store.currentClientInstance;
            store.currentClientInstance = null;
            Expect(ActAtCounter(req,receipt)=="customer_left", "dismiss reports customer_left when client is gone");
            store.currentClientInstance = savedForLeft;
            ObserveVisit();
            req["visit"]=visitId;
            req["revision"]=sequence+1;
            Expect(ActAtCounter(req,receipt)=="stale_observation" && store.currentClientInstance!=null,"future revision rejected");
            Console.WriteLine("Dismiss replay passed: older revision, same visitor, ineligible visitor, replacement and future revision.");
        }

        private void ReplayReception()
        {
            var store=PlayerStore.Instance;
            phase="OPEN";
            store.storeState=PlayerStore.StoreState.OPEN;
            store.currentClientInstance=null;
            store.waiting=3;
            ObserveVisit();
            EmporiumEntry.Instance.backInvinvElement.childItems.Clear();
            var request=new JObject { ["action"]="auto_receive", ["enabled"]=true, ["reception_token"]=ReceptionView()["token"] };
            var receipt=new JObject();
            Expect(ActOnReception(request,receipt)=="auto_receive_enabled","enable today");
            var ring = new JObject { ["action"]="ring", ["generation"]=generation, ["revision"]=sequence,
                ["waiting"]=3, ["expires_ms"]=DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds() };
            var shop = EmporiumEntry.Instance;
            shop.responseWindow=new PixelWindow {visible=true};
            shop.responseInventory=new GameInventory();
            Expect(!(bool)ReceptionView()["can_ring"] && ActAtCounter(ring,receipt)=="response_required",
                "visible response blocks both ring preview and manual bell");
            TickReception();
            Expect(store.waiting==3 && store.currentClientInstance==null,"auto receive waits for response");
            shop.responseWindow.visible=false;
            UnityEngine.Time.realtimeSinceStartup++;
            StoreUIManager.Instance.clientGameObject.gameObject.activeInHierarchy = true;
            Expect(CounterStage()=="customer_departing" && ActAtCounter(ring,receipt)=="customer_departing" &&
                (string)receipt["next_stage"]=="customer_departing","departing animation is not a new bell call");
            StoreUIManager.Instance.clientGameObject.gameObject.activeInHierarchy = false;
            store.lockNextBellFlag=1;
            Expect(!(bool)ReceptionView()["can_ring"] && ActAtCounter(ring,receipt)=="bell_unavailable","bell locked");
            TickReception();
            Expect(store.currentClientInstance==null,"auto receive respects bell lock");
            store.lockNextBellFlag=0;
            UnityEngine.Time.realtimeSinceStartup++;
            DialogUIManager.Instance=new DialogUIManager { HasPausedConversation=true };
            Expect((bool)ReceptionView()["can_ring"],"manual eligibility restored");
            TickReception();
            Expect(store.currentClientInstance==null,"auto receive also waits for paused speech");
            UnityEngine.Time.realtimeSinceStartup++;
            DialogUIManager.Instance=null;
            TickReception();
            Expect(store.currentClientInstance!=null && store.waiting==2,"receive exactly one guest");
            UnityEngine.Time.realtimeSinceStartup++;
            TickReception();
            Expect(store.waiting==2,"never dismiss occupied counter");
            store.currentClientInstance=null;
            ObserveVisit();
            request["action"]="close_shop";
            request["reception_token"]=ReceptionView()["token"];
            Expect(ActOnReception(request,receipt)=="confirm_early_close" && (int)receipt["waiting"]==2 &&
                store.storeState==PlayerStore.StoreState.OPEN && autoReceiveDay==0,"warn and pause before early close");
            request["reception_token"]=receipt["reception"]["token"];
            request["confirm_remaining"]=2;
            store.waiting=1;
            Expect(ActOnReception(request,receipt)=="reception_changed" && (int)receipt["waiting"]==1,"queue changes invalidate consent");
            request["reception_token"]=receipt["reception"]["token"];
            Expect(ActOnReception(request,receipt)=="confirmation_changed","wrong remaining count");
            request["confirm_remaining"]=1;
            Expect(ActOnReception(request,receipt)=="closed" && (int)receipt["forfeited"]==1,"confirmed early close");
            store.storeState=PlayerStore.StoreState.OPEN;
            store.waiting=0;
            request.Remove("confirm_remaining");
            var stray=Item(99001,"newspaper",1);
            stray.To(EmporiumEntry.Instance.backInvinvElement);
            request["reception_token"]=ReceptionView()["token"];
            var blocked=new JObject();
            Expect(ActOnReception(request,blocked)=="shutter_unavailable" && (string)blocked["reason"]=="trade_table_not_empty",
                "occupied trade table reports clear close blocker");
            stray.To(EmporiumEntry.Instance.invElement);
            request["reception_token"]=ReceptionView()["token"];
            Expect(ActOnReception(request,receipt)=="closed" && (int)receipt["forfeited"]==0,"empty queue closes directly");
        }

        private void ReplayP0()
        {
            var store = PlayerStore.Instance;
            var shop = EmporiumEntry.Instance;
            phase = "OPEN";
            store.storeState = PlayerStore.StoreState.OPEN;
            DialogUIManager.Instance = new DialogUIManager();
            NegociationUIManager.Instance.IsOpen = true;
            shop.frontInvinvElement.childItems.Clear();
            shop.backInvinvElement.childItems.Clear();

            // P0-1 acceptance 1: newspaper on back + customer wants unavailable kind -> dismiss succeeds first try.
            store.currentClientInstance = new StoreClientInstance();
            store.currentClientInstance.client.clientBuyingIdList.Add("rare_unstocked_widget");
            ObserveVisit();
            var news = Item(91001, "newspaper", 0);
            news.To(shop.backInvinvElement);
            Expect((bool)ReceptionView()["can_dismiss"], "newspaper on back leaves customer dismissable");
            var dismissReq = new JObject {
                ["action"] = "dismiss", ["generation"] = generation, ["revision"] = sequence, ["visit"] = visitId,
                ["expires_ms"] = DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds()
            };
            var dismissReceipt = new JObject();
            Expect(ActAtCounter(dismissReq, dismissReceipt) == "dismissed" &&
                news.parentInventory == shop.invElement &&
                (string)dismissReceipt["cleared_items"][0]["kind"] == "newspaper",
                "newspaper on back auto-clears and dismiss succeeds on first try");

            // P0-1 acceptance 2: wholesale/batch purchase followed immediately by sell_order succeeds without counter_unavailable.
            store.currentClientInstance = new StoreClientInstance();
            ObserveVisit();
            var wholesaleGood = Item(91002, "lantern", 4);
            wholesaleGood.owned = false;
            wholesaleGood.To(shop.frontInvinvElement);
            var offerView = CounterView();
            var buyReq = new JObject {
                ["action"] = "buy", ["generation"] = generation, ["revision"] = sequence, ["visit"] = visitId,
                ["offer_token"] = offerView["offer_token"], ["max_total"] = 10,
                ["expires_ms"] = DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds()
            };
            var buyReceipt = new JObject();
            Expect(ActAtCounter(buyReq, buyReceipt) == "purchased", "wholesale/batch buy completes cleanly");
            // Even if stray owned goods remain on back right before sell_order, sell_order clears them before staging.
            var strayBought = Item(91003, "bottle", 3);
            strayBought.To(shop.backInvinvElement);
            var sellTarget = Item(91004, "lamp", 5);
            string sellCmd = Guid.NewGuid().ToString("N");
            Write("sale-" + generation + ".json", new JObject {
                ["command_id"] = sellCmd, ["generation"] = generation, ["revision"] = sequence, ["visit"] = visitId,
                ["lines"] = new JArray(new JObject { ["item_id"] = LocalId(sellTarget), ["min_price"] = 1 }),
                ["expires_ms"] = DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds()
            });
            for (int n = 0; n < 8; n++) { UnityEngine.Time.realtimeSinceStartup++; CheckOrder(); CloseTransactions(); }
            var sellReceipt = JObject.Parse(ModHelper.ReadModDataFileText(ModId, "receipt-" + sellCmd + ".json"));
            Expect((string)sellReceipt["status"] == "finished" && strayBought.parentInventory == shop.invElement &&
                (string)sellReceipt["cleared_items"][0]["kind"] == "bottle",
                "sell_order immediately after purchase clears non-order owned back items instead of counter_unavailable");

            // P0-1: counter_unavailable always attaches reason + blocking_items even on source lock.
            var lockedItem = Item(91005, "locked_gem", 5);
            lockedItem.mayRemove = false;
            string lockedCmd = Guid.NewGuid().ToString("N");
            Write("sale-" + generation + ".json", new JObject {
                ["command_id"] = lockedCmd, ["generation"] = generation, ["revision"] = sequence, ["visit"] = visitId,
                ["lines"] = new JArray(new JObject { ["item_id"] = LocalId(lockedItem), ["min_price"] = 1 }),
                ["expires_ms"] = DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds()
            });
            for (int n = 0; n < 4; n++) { UnityEngine.Time.realtimeSinceStartup++; CheckOrder(); CloseTransactions(); }
            var lockedReceipt = JObject.Parse(ModHelper.ReadModDataFileText(ModId, "receipt-" + lockedCmd + ".json"));
            Expect((string)lockedReceipt["status"] == "counter_unavailable" &&
                (string)lockedReceipt["reason"] == "source_item_locked" &&
                (string)lockedReceipt["blocking_items"][0]["kind"] == "locked_gem" &&
                (string)lockedReceipt["blocking_items"][0]["area"] == "inventory",
                "locked source item reports reason and blocking_items");

            // P0-3: forced_choice on radioactiveWasteCustomer + nuclear_waste_on_counter blockers + table_items exposure.
            var wasteClient = new StoreClient {
                identifier = "radioactiveWasteCustomer", displayName = "企業廢料主管", clientIntent = StoreClient.ClientIntent.MERCHANT
            };
            store.currentClientInstance = new StoreClientInstance { client = wasteClient };
            ObserveVisit();
            DialogUIManager.Instance = new DialogUIManager { current = new object(), choice = true };
            var wasteChoiceView = CounterView();
            Expect((bool)wasteChoiceView["forced_choice"] == true &&
                !string.IsNullOrEmpty((string)wasteChoiceView["forced_choice_reason"]),
                "radioactiveWasteCustomer dialogue choice marks forced_choice with consequences");
            DialogUIManager.Instance = new DialogUIManager();

            store.currentClientInstance = new StoreClientInstance();
            ObserveVisit();
            var barrel = Item(91006, "nuclear_waste", 0);
            barrel.To(shop.backInvinvElement);
            var withWasteView = CounterView();
            Expect((string)withWasteView["table_items"][0]["kind"] == "nuclear_waste" &&
                (bool)withWasteView["table_items"][0]["hazardous"] == true,
                "CounterView exposes back-table hazardous waste for MCP stale_order inspection");
            var wasteSaleTarget = Item(91007, "vase", 5);
            string wasteSellCmd = Guid.NewGuid().ToString("N");
            Write("sale-" + generation + ".json", new JObject {
                ["command_id"] = wasteSellCmd, ["generation"] = generation, ["revision"] = sequence, ["visit"] = visitId,
                ["lines"] = new JArray(new JObject { ["item_id"] = LocalId(wasteSaleTarget), ["min_price"] = 1 }),
                ["expires_ms"] = DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds()
            });
            for (int n = 0; n < 4; n++) { UnityEngine.Time.realtimeSinceStartup++; CheckOrder(); CloseTransactions(); }
            var wasteSellReceipt = JObject.Parse(ModHelper.ReadModDataFileText(ModId, "receipt-" + wasteSellCmd + ".json"));
            Expect((string)wasteSellReceipt["status"] == "counter_unavailable" &&
                (string)wasteSellReceipt["reason"] == "nuclear_waste_on_counter" &&
                (string)wasteSellReceipt["blocking_items"][0]["kind"] == "nuclear_waste" &&
                !string.IsNullOrEmpty((string)wasteSellReceipt["guidance"]),
                "nuclear_waste on counter blocks sell_order with nuclear_waste_on_counter and guidance");

            store.currentClientInstance = null;
            ObserveVisit();
            var closeBlocked = new JObject();
            ActOnReception(new JObject { ["action"] = "close_shop", ["reception_token"] = ReceptionView()["token"] }, closeBlocked);
            Expect((string)closeBlocked["reason"] == "nuclear_waste_on_counter" &&
                (string)closeBlocked["blocking_items"][0]["kind"] == "nuclear_waste" &&
                !string.IsNullOrEmpty((string)closeBlocked["guidance"]),
                "nuclear_waste on back reports nuclear_waste_on_counter when closing shop");
            barrel.To(shop.invElement);
            barrel.To(null);
            ReplayItemDetailsAndIdentityChanges(shop);
            Console.WriteLine("P0 replay passed: clutter cleanup, post-wholesale sale, forced waste choice and nuclear_waste blockers.");
        }

        private void ReplayItemDetailsAndIdentityChanges(EmporiumEntry shop)
        {
            ResetItemDetails();
            var tall = Item(92001, "water_jug", 45);
            tall.shape = new GridShape(1, 2);
            tall.modifiedShape = new GridShape(1, 2) { minX = 3, minY = 4 };
            var tallRow = new JObject { ["id"] = LocalId(tall), ["name"] = tall.GetDisplayName(true), ["kind"] = tall.identifier };
            EnrichStockRow(tall, tallRow);
            Expect((int)tallRow["size"]["w"] == 1 && (int)tallRow["size"]["h"] == 2 &&
                (int)tallRow["slot"]["x"] == 3 && (int)tallRow["slot"]["y"] == 4 &&
                (bool)tallRow["rotatable"] && (long)tallRow["estimated_value"] == 45,
                "item row exposes compact size, slot, rotatable and estimated_value");

            var small = Item(92002, "coin", 10);
            var smallRow = new JObject { ["id"] = LocalId(small), ["name"] = small.GetDisplayName(true), ["kind"] = small.identifier };
            EnrichStockRow(small, smallRow);
            Expect(!(bool)smallRow["rotatable"], "1x1 item marks rotatable false");

            var boxGrid = new GameGridInventory(5, 4);
            var placed = new GameItem { uniqueId = 92003, identifier = "crate",
                shape = new GridShape(2, 3), modifiedShape = new GridShape(2, 3) { minX = 0, minY = 0 } };
            placed.To(boxGrid);
            var boxCap = InventoryCapacity(boxGrid);
            Expect((int)boxCap["w"] == 5 && (int)boxCap["h"] == 4 && (int)boxCap["free_cells"] == 14 &&
                (int)boxCap["largest_free"]["w"] == 3 && (int)boxCap["largest_free"]["h"] == 4,
                "InventoryCapacity computes exact free_cells and largest_free rectangle");

            var portable = Item(92004, "portable_water_purifier", 120);
            portable.charge = 25; portable.maxCharge = 300;
            var purRow = new JObject { ["id"] = LocalId(portable), ["name"] = portable.GetDisplayName(true), ["kind"] = portable.identifier };
            EnrichStockRow(portable, purRow);
            var purSummary = PortablePurifierRows();
            var purEntry = purSummary.OfType<JObject>().FirstOrDefault(p => (int)p["id"] == LocalId(portable));
            Expect((int)purRow["charge_remaining"] == 25 && (int)purRow["charge_max"] == 300 &&
                Math.Abs((double)purRow["liters_remaining"] - 2.5) < 0.01 &&
                purEntry != null && (int)purEntry["charge_remaining"] == 25 &&
                Math.Abs((double)purEntry["liters_remaining"] - 2.5) < 0.01,
                "portable_water_purifier exposes charge and liters_remaining");

            var ditchBottle = Item(92005, "bottle", 10);
            ditchBottle.name = "溝渠水瓶";
            ditchBottle.volume = 0;
            var emptyRow = new JObject { ["id"] = LocalId(ditchBottle), ["name"] = ditchBottle.GetDisplayName(true), ["kind"] = ditchBottle.identifier };
            EnrichStockRow(ditchBottle, emptyRow);
            Expect((bool)emptyRow["water_container"] && (int)emptyRow["water_ml"] == 0 &&
                (bool)emptyRow["empty"] && !(bool)emptyRow["contains_water"],
                "empty liquid bottle reports water_ml 0 and empty true");
            ditchBottle.volume = 250000;
            var filledRow = new JObject { ["id"] = LocalId(ditchBottle), ["name"] = ditchBottle.GetDisplayName(true), ["kind"] = ditchBottle.identifier };
            EnrichStockRow(ditchBottle, filledRow);
            Expect((int)filledRow["water_ml"] == 250 && !(bool)filledRow["empty"] && (bool)filledRow["contains_water"],
                "filled liquid bottle reports water_ml and contains_water");

            var cig = Item(92006, "red_cigarette", 100);
            cig.name = "紅牌香菸";
            var cigRow = new JObject { ["id"] = LocalId(cig), ["name"] = cig.GetDisplayName(true), ["kind"] = cig.identifier };
            EnrichStockRow(cig, cigRow);
            cig.name = "紅牌香菸（贗品）";
            ScanKnownIdentities();
            var idReceipt = new JObject();
            AttachReceiptIdentityChanges(idReceipt);
            Expect((int)idReceipt["identity_changed"][0]["id"] == LocalId(cig) &&
                (string)idReceipt["identity_changed"][0]["from"] == "紅牌香菸" &&
                (string)idReceipt["identity_changed"][0]["to"] == "紅牌香菸（贗品）" &&
                journalIdentityChanges.Count == 1,
                "ScanKnownIdentities emits identity_changed for both receipts and journal delta");
            foreach (var tmp in new[] { tall, small, portable, ditchBottle, cig }) tmp.To(null);
            ResetItemDetails();
        }
        public static int Main()
        {
            try { new InventoryObserver().Run(); return 0; }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
    }
}
