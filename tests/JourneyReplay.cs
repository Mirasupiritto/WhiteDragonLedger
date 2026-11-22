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
        public void Return(){IsInTransit=true;}
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
            Expect(ActOnJourney(r,receipt)=="door_requested" && map.packing,"closed store opens native confirmation");
            r=JourneyRequest("shop_activity");ActOnJourney(r,receipt);
            r["view_token"]=receipt["view_token"];r["action"]="confirm_packing";
            Expect(ActOnJourney(r,receipt)=="packing_requested" && !map.packing,"confirm native packing prompt");
            var forest=new TransitCardView {location=AfterhourLocation.Get("forest"),available=true};
            TransitUI.Show(0,false,forest);
            CanObserveStore();r=JourneyRequest("travel_menu");ActOnJourney(r,receipt);
            r["view_token"]=receipt["view_token"];r["action"]="select";r["location_id"]="forest";
            forest.available=false;
            Expect(ActOnJourney(r,receipt)=="view_changed","availability changes without a page switch");
            forest.available=true;r["action"]="read";ActOnJourney(r,receipt);
            r["action"]="select";r["view_token"]=receipt["view_token"];
            Expect(ActOnJourney(r,receipt)=="selection_submitted","choose visible destination");
            CanObserveStore();r=JourneyRequest("travel_menu");ActOnJourney(r,receipt);
            r["view_token"]=receipt["view_token"];r["action"]="depart";r["location_id"]="forest";
            int departures=0;TransitUI.Root.frame.travelButton.onClick=()=>departures++;
            Expect(ActOnJourney(r,receipt)=="departure_requested" && departures==1 && (string)receipt["destination"]=="forest" &&
                (string)receipt["destination_title"]==forest.location.GetTitle(),"departure receipt names the selected destination");
            Expect(ActOnJourney(r,receipt)=="view_changed" && departures==1,"no second departure");
            TransitUI.IsOpen=false;
            map.Current=AfterhourLocation.Get("dumping_ground");
            CanObserveStore();shop.afterhourWindow.visible=true;
            var loot=new GameItem {uniqueId=9801,identifier="scrap"};
            loot.To(shop.afterhourInventory);
            var bag=new GameItem {uniqueId=9802,identifier="bag",contentWindow=new PixelWindow { childElement=new GameInventory() }};
            bag.To(shop.afterhourPocketSlotInvBackpack);
            r=JourneyRequest("explore");ActOnJourney(r,receipt);
            r["action"]="return";r["view_token"]=receipt["view_token"];
            Expect(ActOnJourney(r,receipt)=="collect_ground_first" && !map.IsInTransit,"protect ground loot");
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
