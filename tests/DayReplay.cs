using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
namespace WhiteDragon.Ledger
{
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
        public int mortgageWeeklyPayment=20,garbageLevel,baseStoreAttractiveness=25,projectorAttractivenessBonus;
        public List<StoreService> storeService=new List<StoreService>();
        public List<string> nightLogs=new List<string>();
        public CommissaryData commissaryData=new CommissaryData();
        public List<StartingPerk> startingPerks=new List<StartingPerk>();
        public int GetRentValue()=>100;
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
            AdvCalendarUIManager.Instance.storeAttractivenessTMP.text = "店铺吸引力: 14";
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
            startUi.contentGroupObject.labels = new TMPro.TextMeshProUGUI[0];
            store.nightLogs.Add("<b>- 保护期还剩1晚</b>");
            Expect((string)DayView()["night_report"]["lines"][0] == "- 保护期还剩1晚", "fallback to nightLogs");
            store.nightLogs.Clear();
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
