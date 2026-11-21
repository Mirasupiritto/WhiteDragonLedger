using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace WhiteDragon.Ledger
{
    public sealed partial class InventoryObserver
    {
        private readonly List<(StoreEvent entry, int start, int duration, StoreEvent.EventType type, bool known, string name)> observedEvents =
            new List<(StoreEvent, int, int, StoreEvent.EventType, bool, string)>();
        private JArray scheduledEvents;
        private int calendarDay, calendarScans, calendarDayReads;
        private bool unsubscribed;
        private string calendarWeekday;

        private void ResetCalendar()
        {
            observedEvents.Clear();
            scheduledEvents = null;
            calendarScans = calendarDayReads = 0;
        }

        private bool ObserveEvent(StoreEvent entry, int start, int index)
        {
            var state = (entry, start, entry.totalDuration, entry.eventType, entry.IsKnown, entry.GetDisplayName());
            if (index == observedEvents.Count)
            {
                observedEvents.Add(state);
                return true;
            }
            if (observedEvents[index].Equals(state)) return false;
            observedEvents[index] = state;
            return true;
        }

        private void RefreshCalendar(int day, string weekday)
        {
            var manager = StoreStation.Instance.storeEventManager;
            var active = manager.GetActiveEvents();
            bool subscriptionDisabled = StartingPerk.IsUnsubscribed();
            bool changed = scheduledEvents == null || calendarDay != day || calendarWeekday != weekday ||
                unsubscribed != subscriptionDisabled;
            int index = 0;
            foreach (var entry in active)
                changed |= ObserveEvent(entry, entry.startDay, index++);
            for (int i = 0; i < manager.futurEvents.Count; i++)
                changed |= ObserveEvent(manager.futurEvents[i], day + manager.futurEventsDay[i], index++);
            if (index < observedEvents.Count)
            {
                observedEvents.RemoveRange(index, observedEvents.Count - index);
                changed = true;
            }
            if (!changed) return;

            int weekLength = PlayerStore.DAY_IN_WEEK;
            int monday = day - (day - 1) % weekLength;
            var events = new JArray();
            for (int date = monday; date < monday + 2 * weekLength; date++)
            {
                var names = new JArray();
                foreach (var entry in manager.GetActiveEventOnDay(date, true, true))
                    names.Add(entry.GetDisplayName());
                calendarDayReads++;
                if (names.Count > 0) events.Add(new JObject { ["day"] = date, ["events"] = names });
            }
            scheduledEvents = events;
            calendarDay = day;
            calendarWeekday = weekday;
            unsubscribed = subscriptionDisabled;
            calendarScans++;
        }

        private JObject CalendarView(int day)
        {
            var store = PlayerStore.Instance;
            string weekday = StoreUIManager.Instance.storeCalendar.GetDayOfWeek();
            RefreshCalendar(day, weekday);
            int daysLeft = store.GetRentDayCounter();
            string type = store.IsPropertyPaid ? "paid_off" : store.isPayingMortgage ? "mortgage" : "rent";
            int amount = type == "rent" ? store.GetRentValue() : type == "mortgage" ? store.mortgageWeeklyPayment : 0;
            return new JObject { ["day"]=day, ["weekday"]=weekday, ["period_days"]=PlayerStore.DAY_IN_WEEK,
                ["payment_type"]=type, ["due_day"]=type=="paid_off" ? JValue.CreateNull() : (JToken)(day+daysLeft),
                ["days_until_due"]=type=="paid_off" ? JValue.CreateNull() : (JToken)daysLeft, ["amount"]=amount,
                ["cash_shortfall"]=System.Math.Max(0,amount-store.playerCash), ["scheduled_events"]=scheduledEvents,
                ["event_scans"]=calendarScans, ["event_day_reads"]=calendarDayReads };
        }
    }
}
