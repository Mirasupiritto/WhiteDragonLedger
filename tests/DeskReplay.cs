// Compile with mod/ShopDesk.cs and mod/SaleOrder.cs for a deterministic game-callback replay.
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace UnityEngine {
    public static class Time { public static float realtimeSinceStartup = 1; }
    public static class Debug { public static void LogException(Exception e) { throw e; } }
    public class Object { public static T FindObjectOfType<T>() where T:class { return null; } }
}
namespace WhiteDragon.Ledger {
    public class Label { public string text; }
    public class Window { public bool activeInHierarchy; }
    public class NegotiationTabletFrame { public Window confirmRoot; }
    public class DialogUIManager {
        public static DialogUIManager Instance;
        public Label clientNameTMP;
        public bool isWaitingForManualAdvance;
        public bool IsChoice() => false;
        public object GetCurrentDialog() => null;
        public bool IsCurrentDialogEnd() => true;
        public bool IsLineStillDisplaying(object d) => false;
    }
    public class StoreStation { public static StoreStation Instance = new StoreStation(); public int dayCounter=10; }
    public class GameInventory {
        public List<GameItem> childItems = new List<GameItem>();
        public bool IsInsertLocked() => false;
        public object TryFindOneValidInventorySlot(GameItem item) => new object();
    }
    public class GameSlotInventory:GameInventory { public GameItem childItem; }
    public class EmporiumEntry {
        public static EmporiumEntry Instance = new EmporiumEntry();
        public GameInventory invElement=new GameInventory(), backInvinvElement=new GameInventory(), showcaseElement=new GameInventory();
    }
    public class GameItem {
        public string identifier;
        public int uniqueId, price;
        public bool owned=true;
        public GameInventory parentInventory;
        public int GetUniqueID() => uniqueId;
        public string GetDisplayName(bool custom) => identifier;
        public bool MayRemove() => true;
        public long GetNegociatedValue() => price;
        public bool IsTag(string key) => false;
        public GameItem GetTagReadonly(string key) => this;
        public int GetInt() => 0;
        public List<GameItem> FindAllChildItems() => new List<GameItem>();
        public void To(GameInventory target) {
            parentInventory?.childItems.Remove(this);
            parentInventory=target;
            target?.childItems.Add(this);
        }
    }
    public class StoreClient {
        public string displayName="喬治奧", identifier="georgio";
        public bool isOfferWholesale, useClientBudget=true, isBusinessComplete;
        public int budget=5;
        public Action<GameItem,int> onItemSold, onItemBought;
        public int GetBudget() => budget;
        public bool IsClientBuying() => true;
    }
    public class StoreClientInstance {
        public StoreClient client=new StoreClient();
        public StoreClient GetClientBlueprint() => client;
    }
    public class PlayerStore {
        public static PlayerStore Instance=new PlayerStore();
        public StoreClientInstance currentClientInstance=new StoreClientInstance();
        public int playerCash=100;
        public bool isClientArrived=true;
        public int GetWholesalePrice() => 9;
        public GameItem GetCurrentlyNegociatedItem() => EmporiumEntry.Instance.backInvinvElement.childItems[0];
        public bool CanSellThisItem(GameItem item, bool ignoreBudget) => ignoreBudget || item.price<=currentClientInstance.client.budget;
        public bool CanSellItemFromTable(bool ignoreBudget) => CanSellThisItem(GetCurrentlyNegociatedItem(),ignoreBudget);
    }
    public class NegociationUIManager {
        public static NegociationUIManager Instance=new NegociationUIManager();
        public bool IsOpen=true;
        public static bool IsTradeLocked() => false;
        public void OnOfferAcceptClick() {
            var store=PlayerStore.Instance;
            var item=store.GetCurrentlyNegociatedItem();
            var client=store.currentClientInstance.client;
            client.budget-=item.price;
            client.onItemBought?.Invoke(item,item.price);
            store.playerCash+=item.price;
            item.owned=false;
            item.To(null);
        }
    }
    public static class GeneralHelper { public static bool IsItemOwned(GameItem item) => item.owned; }
    public static class MachineHelper {
        public static GameSlotInventory container=new GameSlotInventory(), battery=new GameSlotInventory();
        public static object GetBodyStorage(GameItem item,int index) => index==2 ? container : null;
        public static GameSlotInventory GetBatterySlot(GameItem item) => battery;
        public static bool CanPower(GameItem item,GameSlotInventory power) => power.childItem!=null;
    }
    public static class PowerHelper { public const string POWER_SOURCE_ITEM_ENERGY="power", POWER_SOURCE_ITEM_MAX_ENERGY="max"; }
    public static class WaterTestHelper { public const string IS_SPENT="used"; }
    public static class ModHelper {
        static Dictionary<string,string> data=new Dictionary<string,string>();
        public static List<int> placed=new List<int>();
        public static string GetModData(string m,string k,string d) => data.ContainsKey(k)?data[k]:d;
        public static void SetModData(string m,string k,string v) { data[k]=v; }
        public static bool ModDataFileExists(string m,string k) => data.ContainsKey(k);
        public static string ReadModDataFileText(string m,string k) => data[k];
        public static void WriteModDataFileText(string m,string k,string v) { data[k]=v; }
        public static void DeleteModDataFile(string m,string k) { data.Remove(k); }
        public static void MoveItemToInventory(GameItem i) { i.To(EmporiumEntry.Instance.invElement); }
        public static void MoveItemToDisplayCase(GameItem i) { i.To(EmporiumEntry.Instance.showcaseElement); }
        public static void MoveItemToBackCounter(GameItem i) { placed.Add(i.uniqueId); i.To(EmporiumEntry.Instance.backInvinvElement); }
    }
    public sealed partial class InventoryObserver {
        private const string ModId="replay";
        private string generation="load", phase="OPEN";
        private int slot=4, sequence=1, loadScans=1, nextId;
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
            MachineHelper.container.childItem=Item(301,"bottle",0);
            BeforeSleep();
            MachineHelper.battery.childItem=Item(302,"battery",0);
            var now=MachineRows();
            Expect((int)now[0]["bottle_key"]==(int)night[0]["bottle_key"] && (bool)now[0]["can_power"],"overnight bottle and power");
            Console.WriteLine("Native replay passed: paid 9, four sales of 10, budget-return/skip/cheap sale, same bottle after night.");
        }
        public static void Main() { new InventoryObserver().Run(); }
    }
}
