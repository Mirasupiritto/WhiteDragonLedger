using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace WhiteDragon.Ledger
{
    public partial class PlayerStore
    {
        public int lockAllFlag, lockAllInventoryFlag, lockStorePhysicalFlag, visitLeftTonight=1;
        public bool isAfterhoursIntroduced=true;
        public bool IsClientPresent() => currentClientInstance!=null;
    }
    public partial class AfterhourLocation { public bool IsAvailable() => true; }
    public partial class MapUIManager
    {
        public bool packing;
        public bool IsShowingGoOutsideConfirm() => packing;
        public void OnGoOutsideConfirm(){packing=false;}
        public void CloseUI(){TransitUI.IsOpen=false;}
        public int returnNowCalls;
        public void Return(){IsInTransit=true;}
        public void ReturnNow(){returnNowCalls++; IsInTransit=true;}
        public int attempts;
        public void ScavengeButtonClick(){attempts++;}
    }
    public class StoreDoor { public void OnClick(){MapUIManager.Instance.packing=true;} }
    public class Shotgun
    {
        public Window gameObject=new Window();
        public bool CanPickup()=>true;
        public void OnClick(){StoreUIManager.Instance.isShootingMode=true;}
    }
    public class AdvCalendarUIManager
    {
        public static AdvCalendarUIManager Instance=new AdvCalendarUIManager();
        public Window UIPanel = new Window();
        public TMPro.TextMeshProUGUI storeAttractivenessTMP = new TMPro.TextMeshProUGUI();
        public void CloseUI(){UIPanel.activeInHierarchy=false;}
    }
    public partial class StoreUIManager
    {
        public bool isShootingMode,isShot;
        public Window shootingUI = new Window();
        public bool CanShoot()=>!isShot;
        public void Shoot(){isShot=true;}
        public void ExitShootingMode(){isShootingMode=false;}
    }
    public partial class EmporiumEntry
    {
        public PixelWindow afterhourWindow = new PixelWindow();
        public GameInventory afterhourInventory = new GameInventory();
        public GameSlotInventory afterhourPocketSlotInvLeft=new GameSlotInventory(),afterhourPocketSlotInvRight=new GameSlotInventory(),
            afterhourPocketSlotInvBackpack=new GameSlotInventory();
    }
    public partial class GameSlotInventory
    {
        public bool stealUI;
        public List<PixelElement> children=new List<PixelElement>();
    }
    public partial class GameItem { public void CloseContentWindow(){contentWindow.Hide();} }
    public static class ScavHelper
    {
        public static bool CanScavenge()=>GetScavTimeLeft()>0;
        public static int GetScavTimeLeft()=>2-MapUIManager.Instance.attempts;
        public static float GetMinorWoundChance()=>.1f;
        public static float GetMajorWoundChance()=>.02f;
    }
    public sealed partial class InventoryObserver
    {
        private JObject JourneyRequest(string tool)
        {
            return new JObject { ["tool"]=tool, ["action"]="read", ["generation"]=sceneSession, ["revision"]=sceneRevision,
                ["expires_ms"]=DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds() };
        }

        private void ReplayJourney()
        {
            var map=MapUIManager.Instance;
            var shop=EmporiumEntry.Instance;
            var desk=StoreUIManager.Instance;
            var report=AdvCalendarUIManager.Instance;
            var person=PlayerStore.Instance;
            var originalPhase=person.storeState;
            var originalClient=person.currentClientInstance;
            person.currentClientInstance=new StoreClientInstance();
            report.UIPanel.activeInHierarchy=true;
            var r=JourneyRequest("shop_activity");
            var receipt=new JObject();
            Expect(ActOnJourney(r,receipt)=="observed" && (bool)receipt["view"]["report_open"],"visible report");
            r["view_token"]=receipt["view_token"];r["action"]="close_report";
            Expect(ActOnJourney(r,receipt)=="close_requested" && !report.UIPanel.activeInHierarchy,"close native report");
            desk.isShootingMode=true;desk.shootingUI.activeInHierarchy=true;
            r["action"]="read";ActOnJourney(r,receipt);
            r["action"]="fire";r["view_token"]=receipt["view_token"];r["visit"]=visitId;
            Expect(ActOnJourney(r,receipt)=="fire_unavailable" && !desk.isShot,"no firing without explicit confirmation");
            r["action"]="read";ActOnJourney(r,receipt);
            r["action"]="fire";r["view_token"]=receipt["view_token"];r["confirm_fire"]=true;
            person.currentClientInstance=new StoreClientInstance();
            Expect(ActOnJourney(r,receipt)=="view_changed" && !desk.isShot,"changed visit invalidates command");
            desk.isShootingMode=false;
            person.currentClientInstance=null;
            person.storeState=PlayerStore.StoreState.CLOSED;
            PhoneUIManager.Instance.CloseUI();
            foreach (var item in shop.backInvinvElement.childItems.ToArray()) item.To(shop.invElement);
            r=JourneyRequest("shop_activity");ActOnJourney(r,receipt);
            r["view_token"]=receipt["view_token"];r["action"]="door";
            Expect(ActOnJourney(r,receipt)=="door_requested" && map.packing && receipt["response_choices"]==null,"closed store opens native confirmation");
            r=JourneyRequest("shop_activity");ActOnJourney(r,receipt);
            r["view_token"]=receipt["view_token"];r["action"]="confirm_packing";
            var packBag = new GameItem { uniqueId=9799, identifier="small_bag" };
            shop.responseWindow = new PixelWindow { visible=true };
            shop.responseInventory = new GameInventory();
            packBag.To(shop.responseInventory);
            var packReceipt = new JObject();
            Expect(ActOnJourney(r,packReceipt)=="packing_requested" && !map.packing &&
                (string)packReceipt["page"]!=null && ((JArray)packReceipt["response_choices"]).Count==1 &&
                (string)packReceipt["response_choices"][0]["text"]=="small_bag","confirm packing attaches immediate bag choices");
            shop.responseWindow = null; shop.responseInventory = null;
            var forest=new TransitCardView {location=AfterhourLocation.Get("forest"),available=true};
            TransitUI.Show(0,false,forest);
            CanObserveStore();r=JourneyRequest("travel_menu");ActOnJourney(r,receipt);
            r["view_token"]=receipt["view_token"];r["action"]="select";r["location_id"]="forest";
            forest.available=false;
            Expect(ActOnJourney(r,receipt)=="view_changed","availability changes without a page switch");
            forest.available=true;r["action"]="read";ActOnJourney(r,receipt);
            r["action"]="select";r["view_token"]=receipt["view_token"];
            var selectReceipt = new JObject();
            Expect(ActOnJourney(r,selectReceipt)=="selection_submitted" &&
                (string)selectReceipt["view"]["selected_id"]=="forest" &&
                (string)selectReceipt["view_token"]!=null,"select returns chainable view_token and view");
            // CanObserveStore does not invalidate the revision/token minted by select
            CanObserveStore();
            r=JourneyRequest("travel_menu");
            r["revision"]=selectReceipt["revision"];
            r["view_token"]=selectReceipt["view_token"];r["action"]="depart";r["location_id"]="forest";
            int departures=0;TransitUI.Root.frame.travelButton.onClick=()=>departures++;
            Expect(ActOnJourney(r,receipt)=="departure_requested" && departures==1 && (string)receipt["destination"]=="forest" &&
                (string)receipt["destination_title"]==forest.location.GetTitle(),"depart directly using select receipt token");
            Expect(ActOnJourney(r,receipt)=="view_changed" && departures==1,"no second departure");
            // Also verify one-step depart without prior select on an unselected available card
            TransitUI.Show(0,false,forest);
            CanObserveStore();r=JourneyRequest("travel_menu");ActOnJourney(r,receipt);
            r["view_token"]=receipt["view_token"];r["action"]="depart";r["location_id"]="forest";
            Expect(ActOnJourney(r,receipt)=="departure_requested" && departures==2 &&
                (string)receipt["destination"]=="forest","one-step select+depart on unselected available card");
            TransitUI.IsOpen=false;
            map.Current=AfterhourLocation.Get("dumping_ground");
            CanObserveStore();shop.afterhourWindow.visible=true;
            var loot=new GameItem {uniqueId=9801,identifier="scrap"};
            loot.To(shop.afterhourInventory);
            var bag=new GameItem {uniqueId=9802,identifier="bag",contentWindow=new PixelWindow { childElement=new GameInventory() }};
            bag.To(shop.afterhourPocketSlotInvBackpack);
            r=JourneyRequest("explore");ActOnJourney(r,receipt);
            Expect(!(bool)receipt["view"]["can_return"] && (bool)receipt["view"]["can_return_abandoning_ground"],"ground blocks default return but allows abandon");
            r["action"]="return";r["view_token"]=receipt["view_token"];
            Expect(ActOnJourney(r,receipt)=="collect_ground_first" && !map.IsInTransit,"protect ground loot");
            r["action"]="return";r["abandon_ground"]=true;r["view_token"]=receipt["view_token"];
            var abandonReceipt=new JObject();
            Expect(ActOnJourney(r,abandonReceipt)=="return_requested" && map.IsInTransit && map.returnNowCalls==1 &&
                (int)abandonReceipt["abandoned_ground_count"]==1,"abandon ground items on explicit return");
            map.IsInTransit=false;
            r.Remove("abandon_ground");
            r["action"]="read";ActOnJourney(r,receipt);
            r["action"]="open_bag";r["item_id"]=9802;r["view_token"]=receipt["view_token"];
            Expect(ActOnJourney(r,receipt)=="opened","native backpack open");
            Expect((string)receipt["view"]["bag_display"]=="window","open bag returns updated view");
            r["action"]="move";r["source"]="ground";r["target"]="bag";r["item_id"]=9801;r["view_token"]=receipt["view_token"];
            Expect(ActOnJourney(r,receipt)=="moved" && (int)receipt["observed_quantity"]==1 && !changed.Contains(loot),"night transfer outside shop ledger");
            Expect((int)receipt["view"]["slots"]["bag"][0]["id"]==9801 && ((JArray)receipt["view"]["slots"]["ground"]).Count==0,"move returns current slots");
            shop.afterhourPocketSlotInvBackpack.stealUI=true;
            shop.afterhourPocketSlotInvBackpack.children.Add(bag.contentWindow.childElement);
            bag.contentWindow.Hide();r["action"]="read";ActOnJourney(r,receipt);
            Expect((string)receipt["view"]["bag_display"]=="inline","native inline slots");
            r["action"]="move";r["source"]="bag";r["target"]="left";r["view_token"]=receipt["view_token"];
            Expect(ActOnJourney(r,receipt)=="moved" && loot.parentInventory==shop.afterhourPocketSlotInvLeft,"inline bag to pocket");
            r["action"]="read";ActOnJourney(r,receipt);
            r["action"]="scavenge";r["view_token"]=receipt["view_token"];
            string id=Guid.NewGuid().ToString("N");
            r["command_id"]=id;
            string inbox="journey-"+sceneSession+".json";
            Write(inbox,r);nextJourneyPoll=0;CheckJourney();
            Write(inbox,r);nextJourneyPoll=0;CheckJourney();
            Expect(map.attempts==1 && ModHelper.ModDataFileExists(ModId,"receipt-"+id+".json"),"repeat envelope spends one night attempt");
            receipt=JObject.Parse(ModHelper.ReadModDataFileText(ModId,"receipt-"+id+".json"));
            Expect(receipt["view"]["slots"]!=null && (string)receipt["view_token"]!=null,"scavenge returns chainable view");
            r["action"]="return";r["view_token"]=receipt["view_token"];
            Expect(ActOnJourney(r,receipt)=="return_requested" && map.IsInTransit,"return once ground empty");
            Console.WriteLine("Journey replay passed: door, packing, report, visit identity, destination, bag slots, scavenging, replay and return.");
            person.storeState=originalPhase;person.currentClientInstance=originalClient;ObserveVisit();
            map.IsInTransit=false;map.Current=null;MarkSceneReady(ShopScene,"當鋪");
        }
    }
}
