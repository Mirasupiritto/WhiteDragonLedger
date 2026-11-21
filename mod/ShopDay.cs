using System;
using Newtonsoft.Json.Linq;

namespace WhiteDragon.Ledger
{
    public sealed partial class InventoryObserver
    {
        private GameInventory trackedTrash;
        private bool trashDirty;
        private int trashDay, trashVersion, sleepSubmittedDay;
        private JArray trashItems = new JArray();

        private void DetachTrash()
        {
            if (trackedTrash != null)
            {
                trackedTrash.onSlotAddItemLateFunc -= TrashChanged;
                trackedTrash.onSlotRemoveItemLateFunc -= TrashChanged;
                trackedTrash.onSlotChangeAmountItemFunc -= TrashChanged;
            }
            trackedTrash = null;
        }

        private void TrashChanged(GameItem item, GameInventory inventory, SlotMarker marker)
        { trashDirty = true; }

        private JObject TrashSummary(int day)
        {
            var bin = EmporiumEntry.Instance.trashcanInvElement;
            if (bin != trackedTrash)
            {
                DetachTrash();
                trackedTrash = bin;
                if (bin != null)
                {
                    bin.onSlotAddItemLateFunc += TrashChanged;
                    bin.onSlotRemoveItemLateFunc += TrashChanged;
                    bin.onSlotChangeAmountItemFunc += TrashChanged;
                }
                trashDirty = true;
            }
            if (bin == null) return new JObject { ["status"]="unavailable" };
            if (trashDirty || trashDay != day)
            {
                var rows = new JArray();
                foreach (var item in bin.childItems)
                    rows.Add(new JObject { ["id"]=LocalId(item), ["name"]=item.GetDisplayName(true),
                        ["kind"]=item.identifier, ["quantity"]=item.unitCount });
                if (trashDay != day || !JToken.DeepEquals(rows, trashItems) || trashVersion == 0)
                {
                    ++trashVersion;
                    Write("trash-" + generation + "-" + trashVersion + ".json", new JObject {
                        ["generation"]=generation, ["version"]=trashVersion, ["day"]=day, ["items"]=rows });
                    trashItems = rows;
                }
                trashDirty = false;
                trashDay = day;
            }
            return new JObject { ["status"]="observed", ["version"]=trashVersion,
                ["stacks"]=trashItems.Count, ["garbage_level"]=PlayerStore.Instance.garbageLevel };
        }

        private static JObject CalendarView(int day)
        {
            var store = PlayerStore.Instance;
            int daysLeft = store.GetRentDayCounter();
            string type = store.IsPropertyPaid ? "paid_off" : store.isPayingMortgage ? "mortgage" : "rent";
            int amount = type == "rent" ? store.GetRentValue() : type == "mortgage" ? store.mortgageWeeklyPayment : 0;
            int monday = day - (day-1)%7;
            var events = new JArray();
            var manager = StoreStation.Instance.storeEventManager;
            for (int date=monday; date<monday+14; ++date)
            {
                var names = new JArray();
                foreach (var entry in manager.GetActiveEventOnDay(date, true, true))
                    names.Add(entry.GetDisplayName());
                if (names.Count > 0) events.Add(new JObject { ["day"]=date, ["events"]=names });
            }
            return new JObject { ["day"]=day, ["weekday"]=new[]{"星期日","星期一","星期二","星期三","星期四","星期五","星期六"}[day%7],
                ["period_days"]=7, ["payment_type"]=type, ["due_day"]=type=="paid_off" ? JValue.CreateNull() : (JToken)(day+daysLeft),
                ["days_until_due"]=type=="paid_off" ? JValue.CreateNull() : (JToken)daysLeft, ["amount"]=amount,
                ["cash_shortfall"]=Math.Max(0,amount-store.playerCash), ["scheduled_events"]=events };
        }

        private static int ServiceCost()
        {
            int total=0;
            foreach (var service in PlayerStore.Instance.storeService)
                if (service.unlocked && service.isUsing) total += service.cost;
            return total;
        }

        private JObject DayView()
        {
            int day = StoreStation.Instance.dayCounter;
            int cost = ServiceCost();
            return new JObject { ["calendar"]=CalendarView(day), ["trash"]=TrashSummary(day),
                ["sleep"]=new JObject { ["service_cost"]=cost, ["available"]=phase=="CLOSED" &&
                    sleepSubmittedDay!=day && PlayerStore.Instance.playerCash >= cost } };
        }

        private string SubmitSleep(JObject request, JObject reply)
        {
            var store = PlayerStore.Instance;
            if (store.storeState != PlayerStore.StoreState.CLOSED) return "sleep_requires_closed_shop";
            int day = StoreStation.Instance.dayCounter;
            if (day != (int)request["day"] || sleepSubmittedDay == day) return "day_changed_or_pending";
            int cost = ServiceCost();
            reply["day_before"] = day;
            reply["service_cost"] = cost;
            if (cost != (int)request["service_cost"]) return "service_cost_changed";
            if (store.playerCash < cost) return "insufficient_cash";
            var summary = EndOfDayUIManager.Instance;
            if (summary == null) return "bedroll_unavailable";
            if (!summary.IsOpen)
            {
                var bed = UnityEngine.Object.FindObjectOfType<StoreBedroll>();
                if (bed == null) return "bedroll_unavailable";
                bed.OnClick();
            }
            if (!summary.IsOpen || ServiceCost()!=cost) return "review_nightly_cost";
            sleepSubmittedDay = day;
            summary.OnEndDayButtonClick();
            return "sleep_submitted";
        }
    }
}
