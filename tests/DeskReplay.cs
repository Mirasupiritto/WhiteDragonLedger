// Compile with mod/ShopDesk.cs and mod/SaleOrder.cs for a deterministic game-callback replay.
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace UnityEngine {
    public static class Time { public static float realtimeSinceStartup = 1, timeScale = 1; }
    public static class Debug { public static void LogException(Exception e) { throw e; } }
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
    public class Window {
        public bool activeInHierarchy, activeSelf;
        public TMPro.TextMeshProUGUI[] labels = new TMPro.TextMeshProUGUI[0];
        public T[] GetComponentsInChildren<T>(bool includeInactive = false) => labels as T[] ?? new T[0];
    }
    public class NegotiationTabletFrame { public PanelNode infoHost; public Window confirmRoot; }
    public partial class StoreUIManager {
        public static StoreUIManager Instance = new StoreUIManager();
        public SceneFade blackscreen;
        public Window closeShutterConfirmationPanel = new Window();
        public bool IsTyping() => false;
        public StoreCalendar storeCalendar = new StoreCalendar();
        public bool isAboutToStartMainDialog;
    }
    public class DialogUIManager {
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
    public partial class GameInventory : PixelElement {
        public List<GameItem> childItems = new List<GameItem>();
        public bool IsInsertLocked() => false;
        public bool IsRemoveLocked() => false;
        public bool replayFull;
        public object TryFindOneValidInventorySlot(GameItem item) => replayFull ? null : new object();
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
        public GameInventory frontInvinvElement=new GameInventory();
        public GameInventory invElement=new GameInventory(), backInvinvElement=new GameInventory(), showcaseElement=new GameInventory();
    }
    public class ItemCondition { public string display; }
    public class ItemFeature {
        public ItemCondition label;
        public ItemCondition GetCurrentItemCondition() => label;
    }
    public static class ItemConditionList {
        public static ItemCondition CreateBaseWater() => new ItemCondition { display="base water" };
    }
    public partial class GameItem {
        public PixelWindow contentWindow;
        public ItemFeature waterLabel;
        public ItemFeature FindItemFeatureByCategory(string category) => waterLabel;
        public string identifier;
        public int uniqueId, price, unitCount=1;
        public bool owned=true;
        public GameInventory parentInventory;
        public int GetUniqueID() => uniqueId;
        public bool MayActivateSlotItem() => true;
        public bool CanActivateSlotItem() => true;
        public string GetDisplayName(bool custom) => identifier;
        public bool MayRemove() => true;
        public long GetNegociatedValue() => price;
        public bool IsTag(string key) => (key=="LIQUID_CONTAINER_TAG" && identifier=="bottle") || waterTags.Contains(key);
        public GameItem GetTagReadonly(string key) => scratchTagReader == null ? this : scratchTagReader(key);
        public int GetInt() => associatedId;
        public List<GameItem> FindAllChildItems() => new List<GameItem>();
        public void To(GameInventory target) {
            if (parentInventory is GameSlotInventory oldSlot) oldSlot.childItem = null;
            parentInventory?.childItems.Remove(this);
            parentInventory=target;
            target?.childItems.Add(this);
            if (target is GameSlotInventory newSlot) newSlot.childItem = this;
        }
    }
    public class StoreClient {
        public enum ClientIntent { MERCHANT, INFORMATION_DEALER, BARTER }
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
        public bool IsClientSelling() => true;
        public bool IsClientBuying() => true;
    }
    public class StoreClientInstance {
        public StoreClient client=new StoreClient();
        public StoreClient GetClientBlueprint() => client;
    }
    public partial class PlayerStore {
        public static bool HasInstance => Instance != null;
        public int saveSlotId=4;
        public static PlayerStore Instance=new PlayerStore();
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
    }
    public static class MachineryHelper { public static int GetMachinePowerUsage(GameItem machine) => 4; }
    public static class MachineHelper {
        public const string STANDARD_MACHINE_TAG="standard_machine";
        public static GameSlotInventory container=new GameSlotInventory(), battery=new GameSlotInventory();
        public static object[] furnaceSlots;
        public static object GetBodyStorage(GameItem item,int index) =>
            item.identifier=="furnace" && furnaceSlots!=null ? furnaceSlots[index] : index==2 ? container : null;
        public static GameSlotInventory GetBatterySlot(GameItem item) => battery;
        public static bool CanPower(GameItem item,GameSlotInventory power) => power.childItem!=null;
    }
    public static class PowerHelper { public const string POWER_SOURCE_ITEM_ENERGY="power", POWER_SOURCE_ITEM_MAX_ENERGY="max"; }
    public static class WaterTestHelper { public const string IS_SPENT="used", ASSOCIATED_ITEM_ID="associated"; }
    public static class ModHelper {
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
            var regular=Item(902,"soap",3); regular.owned=false; regular.To(EmporiumEntry.Instance.frontInvinvElement);
            buy["revision"]=sequence;
            buy["offer_token"]=CounterView()["offer_token"];
            Expect(ActAtCounter(buy,outcome)=="purchased" && regular.owned && (int)outcome["paid"]==3,"normal batch");
            buy["action"]="ring"; buy["revision"]=sequence; buy["waiting"]=1;
            Expect(ActAtCounter(buy,outcome)=="occupied","bell never dismisses current visit");
            phase="MORNING";
            Expect(ActAtCounter(buy,outcome)=="shop_closed","bell never opens the shop");
            phase="OPEN"; PlayerStore.Instance.currentClientInstance=null; ObserveVisit();
            Expect(ActAtCounter(buy,outcome)=="called" && outcome["visit"]!=null,"one new visit");
            ReplayInformation();
            ReplayBarter();
            ReplayPhone();
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
            ReplayMixedTrades();
            ReplayBundleSale();
            ReplayNetwork();
            ReplayDismiss();
            ReplayCounterDay();
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
            Expect(ActAtCounter(req,receipt)=="dismiss_unavailable","native dismissal locked");
            store.dismissable=true;
            EmporiumEntry.Instance.backInvinvElement.childItems.Clear();
            var delivered = Item(64001,"card_rev",1);
            delivered.To(EmporiumEntry.Instance.frontInvinvElement);
            QueueFrontPaper(delivered,delivered.parentInventory,null);
            delivered.To(EmporiumEntry.Instance.backInvinvElement);
            Expect(ActAtCounter(req,receipt)=="dismiss_unavailable" && (string)receipt["blocking_items"][0]["kind"]=="card_rev","diagnose the actual blocker");
            FileCounterPapers();
            Expect(delivered.parentInventory==EmporiumEntry.Instance.invElement,"recover this visit before dismissal");
            int waiting=store.waiting;
            store.interceptDismiss=true;
            Expect(ActAtCounter(req,receipt)=="dismiss_intercepted","intercept keeps original visit");
            store.interceptDismiss=false;
            Expect(ActAtCounter(req,receipt)=="dismissed" && visitId==null && store.waiting==waiting &&
                   (bool)receipt["revision_rechecked"],"same visit is dismissed once after the sale revision advances");
            store.currentClientInstance=new StoreClientInstance();
            Expect((string)receipt["customer"]=="喬治奧" && (int)receipt["waiting"]==waiting,"dismiss identifies guest and remaining queue");
            Expect(!observedVariety,"dismissal clears the previous visitor's variety rule");
            // The new arrival has the same display name. Only the visit identity is authoritative.
            Expect(ActAtCounter(req,receipt)=="visit_changed" && store.currentClientInstance!=null,"old visit cannot dismiss new arrival");
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
        public static int Main()
        {
            try { new InventoryObserver().Run(); return 0; }
            catch (Exception error) { Console.Error.WriteLine(error.GetType().Name + ": " + error.Message); return 1; }
        }
    }
}
