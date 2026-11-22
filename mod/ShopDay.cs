using System;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using TMPro;

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

        private static int ServiceCost()
        {
            int total=0;
            foreach (var service in PlayerStore.Instance.storeService)
                if (service.unlocked && service.isUsing) total += service.cost;
            return total;
        }

        private static string CleanReportText(string raw) =>
            string.IsNullOrWhiteSpace(raw) ? "" : Regex.Replace(raw, "<.*?>", "").Trim();

        private static JObject NightReportView()
        {
            var lines = new JArray();
            var reminders = new JArray();
            var startOfDay = StartOfDayUIManager.Instance;
            if (startOfDay?.contentGroupObject != null)
            {
                foreach (var label in startOfDay.contentGroupObject.GetComponentsInChildren<TextMeshProUGUI>(true))
                {
                    string text = CleanReportText(label?.text);
                    if (text.Length > 0) lines.Add(text);
                }
            }
            if (lines.Count == 0 && PlayerStore.Instance.nightLogs != null)
            {
                foreach (var entry in PlayerStore.Instance.nightLogs)
                {
                    string text = CleanReportText(entry);
                    if (text.Length > 0) lines.Add(text);
                }
            }
            if (startOfDay?.displayCaseReminder != null && startOfDay.displayCaseReminder.activeSelf)
            {
                string reminder = "";
                foreach (var label in startOfDay.displayCaseReminder.GetComponentsInChildren<TextMeshProUGUI>(true))
                {
                    string text = CleanReportText(label?.text);
                    if (text.Length > 0) reminder = reminder.Length == 0 ? text : reminder + " " + text;
                }
                reminders.Add(reminder.Length > 0 ? reminder : "display_case_reminder");
            }
            if (startOfDay?.rentReminder != null && startOfDay.rentReminder.activeSelf)
            {
                string text = CleanReportText(startOfDay.rentReminderTMP?.text);
                if (text.Length > 0) reminders.Add(text);
            }
            var report = new JObject { ["lines"] = lines };
            if (reminders.Count > 0) report["reminders"] = reminders;
            if (AdvCalendarUIManager.Instance?.UIPanel?.activeInHierarchy == true) report["panel_open"] = true;
            return report;
        }

        private static JObject AppealView()
        {
            var store = PlayerStore.Instance;
            var result = new JObject {
                ["total"] = store.GetCurrentStoreAttractiveness(),
                ["base"] = store.baseStoreAttractiveness,
                ["trash_penalty"] = store.GetTrashAttPenalty(),
                ["projector_bonus"] = store.projectorAttractivenessBonus,
                ["garbage_level"] = store.garbageLevel
            };
            string display = CleanReportText(AdvCalendarUIManager.Instance?.storeAttractivenessTMP?.text);
            if (display.Length > 0) result["display"] = display;
            return result;
        }

        private static JObject StatusSummaryView()
        {
            var store = PlayerStore.Instance;
            var result = new JObject { ["estimated_net_worth"] = store.GetTotalEstimatedValue() };
            if (store.isStampIntroduced && store.commissaryData != null)
                result["redeemed_tokens"] = store.commissaryData.redeemedCredit;
            if (store.startingPerks != null && store.startingPerks.Count > 0)
            {
                var perks = new JArray();
                foreach (var perk in store.startingPerks)
                    if (perk != null && !string.IsNullOrEmpty(perk.id)) perks.Add(perk.id);
                if (perks.Count > 0) result["active_perks"] = perks;
            }
            return result;
        }

        private JObject DayView()
        {
            int day = StoreStation.Instance.dayCounter;
            int cost = ServiceCost();
            var sleep = new JObject { ["service_cost"]=cost, ["available"]=phase=="CLOSED" &&
                sleepSubmittedDay!=day && PlayerStore.Instance.playerCash >= cost };
            if (phase == "CLOSED") sleep["bill"] = NightBill();
            return new JObject {
                ["calendar"]=CalendarView(day), ["trash"]=TrashSummary(day), ["sleep"]=sleep,
                ["night_report"]=NightReportView(), ["appeal"]=AppealView(), ["status_summary"]=StatusSummaryView()
            };
        }

        private string SubmitSleep(JObject request, JObject reply)
        {
            var store = PlayerStore.Instance;
            if (store.storeState != PlayerStore.StoreState.CLOSED) return "sleep_requires_closed_shop";
            if (ResponseInventory() != null) return "response_required";
            int day = StoreStation.Instance.dayCounter;
            if (day != (int)request["day"] || sleepSubmittedDay == day) return "day_changed_or_pending";
            int cost = ServiceCost();
            reply["day_before"] = day;
            reply["service_cost"] = cost;
            if (cost != (int)request["service_cost"]) return "service_cost_changed";
            if (store.playerCash < cost) return "insufficient_cash";
            var summary = EndOfDayUIManager.Instance;
            if (request["bill_token"] != null && ((string)NightBill()["bill_token"] != (string)request["bill_token"] ||
                summary == null || !summary.IsOpen)) return "bill_changed";
            if (!OpenBill(summary)) return "bedroll_unavailable";
            if (ServiceCost()!=cost) return "review_nightly_cost";
            sleepSubmittedDay = day;
            summary.OnEndDayButtonClick();
            return "sleep_submitted";
        }
    }
}
