using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
namespace WhiteDragon.Ledger
{
    public class WantedElement { public string identifier; }
    public class WantedUIManager { public static WantedUIManager Instance; private List<WantedElement> wantedElements=new List<WantedElement>(); }
    public class SavePreviewData { public int dayCount; public DateTime lastSaveDateTime; }
    public static class SaveUIManager { public static SavePreviewData preview; public static SavePreviewData GetPreview(int slot)=>preview; }
    public static class StoreClientListWanted { public static HashSet<string> POSTER_IDS=new HashSet<string>{"wanted4"}; }
    public partial class PlayerStore { public Dictionary<string,int> wantedStatus=new Dictionary<string,int>(); }
    public class SlotMarker {}
    public partial class GameInventory {
        public Action<GameItem,GameInventory,SlotMarker> onSlotAddItemLateFunc,onSlotRemoveItemLateFunc,onSlotChangeAmountItemFunc;
    }
    public partial class EmporiumEntry { public GameInventory trashcanInvElement=new GameInventory(); }
    public class StoreService { public bool unlocked=true,isUsing=true,isMandatory; public int cost,currentCooldown; public string id,displayName; public bool IsOnCooldown()=>currentCooldown>0; }
    public class StoreCalendar { public string GetDayOfWeek() => "local weekday"; }
    public partial class StartingPerk { public static bool IsUnsubscribed() => false; }
    public class StoreEvent
    {
        public enum EventType { Normal, COSMETIC }
        public string newsName, newsDescription;
        public bool isHiddenFromNewsPaper, isPermanent;
        public int duration;
        public EventType eventType = EventType.Normal;
        public bool IsKnown;
        public int startDay = 10, totalDuration = 1;
        public string GetDisplayName() => "calendar entry";
    }
    public class StoreEventManager
    {
        private List<StoreEvent> activeEvents = new List<StoreEvent>();
        public List<StoreEvent> futurEvents = new List<StoreEvent>();
        public List<int> futurEventsDay = new List<int>();
        public int calls;
        public List<StoreEvent> GetActiveEvents() => new List<StoreEvent>(activeEvents);
        public void Activate(StoreEvent entry) => activeEvents.Add(entry);
        public List<StoreEvent> GetActiveEventOnDay(int date, bool include, bool known)
        {
            calls++;
            var visible = activeEvents.FindAll(entry => entry.IsKnown && date >= entry.startDay && date < entry.startDay + entry.totalDuration);
            for (int i = 0; i < futurEvents.Count; i++)
                if (futurEvents[i].IsKnown && date == StoreStation.Instance.dayCounter + futurEventsDay[i])
                    visible.Add(futurEvents[i]);
            return visible;
        }
    }
    public partial class StoreStation { public StoreEventManager storeEventManager=new StoreEventManager(); }
    public class CommissaryData { public int redeemedCredit; }
    public class StartOfDayUIManager
    {
        public static StartOfDayUIManager Instance = new StartOfDayUIManager();
        public Window contentGroupObject = new Window(), displayCaseReminder = new Window(), rentReminder = new Window();
        public TMPro.TextMeshProUGUI rentReminderTMP = new TMPro.TextMeshProUGUI();
    }
    public partial class PlayerStore {
        public const int DAY_IN_WEEK = 7;
        public bool IsPropertyPaid,isPayingMortgage,isStampIntroduced;
        public int mortgageWeeklyPayment=20,garbageLevel,baseStoreAttractiveness=25,projectorAttractivenessBonus,dayUntilRent=4,rentValue=100;
        public List<StoreService> storeService=new List<StoreService>();
        public List<string> nightLogs=new List<string>();
        public CommissaryData commissaryData=new CommissaryData();
        public List<StartingPerk> startingPerks=new List<StartingPerk>();
        public int GetRentValue()=>rentValue;
        public int GetRentDayCounter()=>(7-StoreStation.Instance.dayCounter%7)%7;
        public int GetTrashAttPenalty()=>garbageLevel*5;
        public int GetCurrentStoreAttractiveness()=>Math.Max(0,baseStoreAttractiveness-GetTrashAttPenalty()+projectorAttractivenessBonus);
        public int GetTotalEstimatedValue()=>playerCash+150;
    }
    public partial class EndOfDayUIManager {
        public static EndOfDayUIManager Instance=new EndOfDayUIManager();
        public bool IsOpen; public int submissions;
        public void OnEndDayButtonClick(){submissions++;IsOpen=false;}
    }
    public class StoreBedroll { public void OnClick(){EndOfDayUIManager.Instance.IsOpen=true;} }
    public sealed partial class InventoryObserver {
        private void ReplayDay() {
            var store=PlayerStore.Instance;
            var station=StoreStation.Instance;
            station.dayCounter=10;
            ResetCalendar();
            var manager = station.storeEventManager;
            int before = manager.calls;
            var day=DayView();
            DayView();
            store.playerCash = 1;
            var calendar = DayView()["calendar"];
            Expect(manager.calls-before == 14 && (int)calendar["event_scans"] == 1 &&
                (int)calendar["event_day_reads"] == 14 && (int)calendar["cash_shortfall"] == 99 &&
                (string)calendar["weekday"] == "local weekday", "three reads reuse native calendar, cash stays live");
            var upcoming = new StoreEvent { IsKnown = true };
            manager.futurEvents.Add(upcoming);
            manager.futurEventsDay.Add(2);
            calendar = DayView()["calendar"];
            Expect((int)calendar["event_scans"] == 2 && (int)calendar["scheduled_events"][0]["day"] == 12, "new future event");
            manager.futurEventsDay[0] = 3;
            Expect((int)DayView()["calendar"]["scheduled_events"][0]["day"] == 13, "event rescheduled");
            upcoming.IsKnown = false;
            Expect(!DayView()["calendar"]["scheduled_events"].HasValues, "disclosure changed in place");
            manager.Activate(new StoreEvent { IsKnown = true });
            Expect((int)DayView()["calendar"]["scheduled_events"][0]["day"] == 10, "same-day active event");
            station.dayCounter++;
            Expect((int)DayView()["calendar"]["event_scans"] == 6, "new day");
            station.dayCounter=10;
            ResetCalendar();
            Expect((int)DayView()["calendar"]["event_day_reads"] == 14, "new load resets native counts");
            Expect((int)day["calendar"]["days_until_due"]==4 && (int)day["calendar"]["due_day"]==14,"rent due");
            // Past days are filtered out of scheduled_events; paying rent on due day moves next raised rent to day+7.
            var spanning = new StoreEvent { IsKnown = true, startDay = 8, totalDuration = 5 };
            manager.Activate(spanning);
            var trimmedEvents = (JArray)DayView()["calendar"]["scheduled_events"];
            Expect((int)trimmedEvents[0]["day"] == 10, "calendar trims past-day events before current day");
            station.dayCounter = 14; store.dayUntilRent = 0; store.rentValue = 100;
            var dueToday = DayView()["calendar"];
            Expect((int)dueToday["due_day"] == 14 && (int)dueToday["days_until_due"] == 0 && (int)dueToday["amount"] == 100,
                "rent due today uses dayUntilRent=0");
            store.dayUntilRent = 7; store.rentValue = 110;
            var afterPaid = DayView()["calendar"];
            Expect((int)afterPaid["due_day"] == 21 && (int)afterPaid["days_until_due"] == 7 && (int)afterPaid["amount"] == 110,
                "after paying rent on due day, next raised rent attaches to new due_day=21 instead of old 14");
            station.dayCounter = 10; store.dayUntilRent = 4; store.rentValue = 100;
            var bin=EmporiumEntry.Instance.trashcanInvElement;
            var piece=Item(1378,"waste",0);
            piece.To(bin);bin.onSlotAddItemLateFunc(piece,bin,null);
            Expect((int)DayView()["trash"]["stacks"]==1,"item discarded");
            station.dayCounter++;piece.To(null);
            Expect((int)DayView()["trash"]["stacks"]==0,"observed next-day disposal");
            station.dayCounter++;piece.To(bin);
            Expect((int)DayView()["trash"]["stacks"]==1,"overnight survivor retained");
            var request=new JObject { ["day"]=station.dayCounter,["service_cost"]=0 };
            Expect(SubmitSleep(request,new JObject())=="sleep_requires_closed_shop","no sleep in store hours");
            store.storeState=PlayerStore.StoreState.CLOSED; store.playerCash=40;
            store.storeService.Add(new StoreService {cost=5});EndOfDayUIManager.Instance.IsOpen=true;
            Expect(SubmitSleep(request,new JObject())=="service_cost_changed","charges have changed");
            request["service_cost"]=5;
            Expect(SubmitSleep(request,new JObject())=="sleep_submitted" && SubmitSleep(request,new JObject())=="day_changed_or_pending" &&
                EndOfDayUIManager.Instance.submissions==1,"one night only");
            // Night report, appeal, and status summary
            var startUi = StartOfDayUIManager.Instance;
            startUi.contentGroupObject.labels = new[] {
                new TMPro.TextMeshProUGUI { text = "<color=#FF5555>- 昨晚店面没有被打扫</color>" },
                new TMPro.TextMeshProUGUI { text = "- 电费已结清。" }
            };
            startUi.displayCaseReminder.activeSelf = true;
            startUi.displayCaseReminder.labels = new[] { new TMPro.TextMeshProUGUI { text = "别忘了补充展示柜存货！" } };
            startUi.rentReminder.activeSelf = true;
            startUi.rentReminderTMP.text = "明日交租";
            store.garbageLevel = 3;
            store.projectorAttractivenessBonus = 4;
            store.isStampIntroduced = true;
            store.commissaryData.redeemedCredit = 12;
            store.startingPerks.Add(new StartingPerk { id = "thrifty" });
            AdvCalendarUIManager.Instance.storeAttractivenessTMP.text = "店铺吸引力: 250";
            store.nightLogs.Add("<color=#FF5555> 昨晚店面没有被打扫</color>");
            store.nightLogs.Add(" 电费已结清。");
            CaptureMorningReport();
            store.nightLogs.Clear();
            FinishMorningReport();
            var fullDay = DayView();
            var nr = (JObject)fullDay["night_report"];
            Expect(((JArray)nr["lines"]).Count == 2 && (string)nr["lines"][0] == "- 昨晚店面没有被打扫" &&
                ((JArray)nr["reminders"]).Count == 2 && (string)nr["reminders"][0] == "别忘了补充展示柜存货！", "night report lines & reminders");
            var ap = (JObject)fullDay["appeal"];
            Expect((int)ap["total"] == 14 && (int)ap["base"] == 25 && (int)ap["trash_penalty"] == 15 &&
                (int)ap["projector_bonus"] == 4 && (int)ap["garbage_level"] == 3 && (string)ap["display"] == "店铺吸引力: 14", "shop appeal breakdown");
            var ss = (JObject)fullDay["status_summary"];
            Expect((int)ss["estimated_net_worth"] == 190 && (int)ss["redeemed_tokens"] == 12 &&
                (string)ss["active_perks"][0] == "thrifty", "status summary");
            station.dayCounter++;
            Expect((string)DayView()["night_report"]["status"] == "awaiting_report", "new day never reuses yesterday's report");
            store.nightLogs.Add("Garbage bags piled up");
            store.nightLogs.Add("Protection: 2 nights");
            store.nightLogs.Add("Wanted: Odin visited yesterday.");
            CaptureMorningReport();
            store.nightLogs.Clear();
            Expect((string)DayView()["night_report"]["status"] == "awaiting_report", "wait for panel initialization");
            FinishMorningReport();
            var today = DayView()["night_report"];
            Expect((int)today["day"] == station.dayCounter && (string)today["lines"][0] == "-Garbage bags piled up" &&
                (string)today["lines"][1] == "-Protection: 2 nights" &&
                (string)today["alerts"][0]["type"] == "garbage_warning" &&
                (string)today["alerts"][1]["type"] == "wanted_visited" &&
                (string)today["alerts"][1]["name"] == "Odin", "second morning parses garbage_warning and wanted_visited");
            // P1-4: expanded alert templates & live reminder localization refresh
            station.dayCounter++;
            store.nightLogs.Add("Storefront not cleaned: Trash everywhere");
            store.nightLogs.Add("Insurance unpaid for 3 day(s).");
            store.nightLogs.Add("Insurance paid.");
            store.nightLogs.Add("Your wound has fully healed.");
            startUi.displayCaseReminder.labels[0].text = "Remeber to stock the display case";
            CaptureMorningReport();
            store.nightLogs.Clear();
            FinishMorningReport();
            var reminderLabel = startUi.displayCaseReminder.labels[0];
            reminderLabel.localizer = new TMPLocalizer { postfix = "！" };
            reminderLabel.localized = new UnityEngine.Localization.Components.LocalizeStringEvent {
                StringReference = new UnityEngine.Localization.Components.ReplayString { text = "記得補滿展示櫃" }
            };
            var alertsReport = DayView();
            var alertList = (JArray)alertsReport["night_report"]["alerts"];
            Expect(alertList.Count == 4 &&
                (string)alertList[0]["type"] == "garbage_warning" && (string)alertList[0]["detail"] == "Trash everywhere" &&
                (string)alertList[1]["type"] == "insurance_lapsed" && (int)alertList[1]["days_unpaid"] == 3 &&
                (string)alertList[2]["type"] == "insurance_paid" &&
                (string)alertList[3]["type"] == "injury_healed", "expanded night report alert matching");
            Expect((string)alertsReport["night_report"]["reminders"][0] == "記得補滿展示櫃！",
                "NightReportView reads refreshed localized reminder text instead of stale English");
            Expect((int)alertsReport["capacities"]["inventory"]["w"] == 24 &&
                (int)alertsReport["capacities"]["inventory"]["h"] == 10 &&
                (int)alertsReport["capacities"]["showcase"]["w"] == 7, "DayView exposes shop area capacities");
            station.dayCounter--;
            // P0-2 injury in status_summary + P0-3 nuclear_waste counts
            Expect((string)DayView()["status_summary"]["injury"]["severity"] == "none", "healthy status summary injury");
            store.healthData.woundState = 2; store.healthData.isWoundedFresh = true; store.healthData.isWoundStable = false;
            var minorDay = DayView();
            Expect((string)minorDay["status_summary"]["injury"]["severity"] == "minor" &&
                (string)minorDay["status_summary"]["injury"]["name"] == "輕傷", "minor injury in status summary");
            store.healthData.woundState = 6;
            Expect((string)DayView()["status_summary"]["injury"]["severity"] == "major" &&
                (bool)DayView()["status_summary"]["injury"]["serious"], "major injury in status summary");
            store.healthData = new HealthData();
            var shop = EmporiumEntry.Instance;
            var wasteFront = Item(88101, "nuclear_waste", 0); wasteFront.To(shop.frontInvinvElement);
            var wasteInv = Item(88102, "nuclear_waste", 0); wasteInv.To(shop.invElement);
            var wasteBag = Item(88103, "backpack_small", 0); wasteBag.To(shop.invElement);
            var bagInv = new GameInventory();
            wasteBag.contentWindow = new PixelWindow { childElement = bagInv };
            var wasteInside = Item(88104, "nuclear_waste", 0); wasteInside.To(bagInv);
            store.storeClientData.nuclearWasteLeftOnCounter = 1;
            store.storeClientData.nuclearWasteDumped = 2;
            wasteBag.identifier="smuggler_bay";
            wasteInside.waterTags.Add(TypeHelper.CONTRABAND);
            wasteInv.waterTags.Add(TypeHelper.CONTRABAND);
            wasteInside.unitCount=2;
            store.secData.evidenceLevel=73;
            var counts=SecurityView();
            Expect((int)counts["evidence_level"]==73 && (int)counts["contraband"]["smuggler_bay"]==2 &&
                (int)counts["contraband"]["inventory"]>=1,"security counts hidden and inventory contraband separately");
            SaveUIManager.preview=new SavePreviewData {dayCount=3,lastSaveDateTime=new DateTime(2020,1,2,3,0,0)};
            Expect((int)LastSave(12)["day"]==3 && (string)LastSave(12)["phase"]=="MORNING","saved morning from preview");
            SaveUIManager.preview=null;
            var nw = DayView()["nuclear_waste"];
            Expect((int)nw["on_counter"] == 2 && (int)nw["in_shop"] == 4 && (int)nw["dumped"] == 2,
                "nuclear_waste counts front, pending morning counter, inventory, backpack and dumped totals");
            wasteFront.To(null); wasteInv.To(null); wasteInside.To(null); wasteBag.To(null);
            store.storeClientData.nuclearWasteLeftOnCounter = 0;
            store.storeClientData.nuclearWasteDumped = 0;
            station.dayCounter--;
            startUi.displayCaseReminder.activeSelf = false;
            startUi.rentReminder.activeSelf = false;
            store.garbageLevel = 0;
            store.projectorAttractivenessBonus = 0;
            store.isStampIntroduced = false;
            store.startingPerks.Clear();
            AdvCalendarUIManager.Instance.storeAttractivenessTMP.text = null;
        }
    }
}
