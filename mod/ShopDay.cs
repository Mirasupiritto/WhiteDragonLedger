using System;
using System.Collections.Generic;
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

        private JObject morningReport;
        private bool morningReportReady;

        private static string SafeLocMechanic(string key, params object[] args)
        {
            try { return CleanReportText(LocHelper.GetLocalizedMechanic(key, args)); }
            catch { return ""; }
        }

        private static bool MatchTemplateMechanic(string text, string key, out string captured)
        {
            captured = null;
            const string marker = "__WDL_ARG__";
            string template = SafeLocMechanic(key, marker);
            int split = template.IndexOf(marker, StringComparison.Ordinal);
            if (split < 0) return false;
            string prefix = template.Substring(0, split);
            string suffix = template.Substring(split + marker.Length);
            if (text.Length < prefix.Length + suffix.Length ||
                !text.StartsWith(prefix, StringComparison.Ordinal) ||
                !text.EndsWith(suffix, StringComparison.Ordinal)) return false;
            captured = text.Substring(split, text.Length - split - suffix.Length);
            return true;
        }

        private static readonly (string Key, string AlertType)[] ExactNightAlertRules = new[] {
            ("mech_nightlog_insurance_paid", "insurance_paid"),
            ("mech_nightlog_garbage_some", "garbage_warning"),
            ("mech_nightlog_garbage_lot", "garbage_warning"),
            ("mech_nightlog_garbage_extreme", "garbage_warning"),
            ("health_wound_healed", "injury_healed"),
            ("health_wound_stabilized", "injury_stabilized"),
            ("health_wound_mobility_regained", "injury_mobility_regained"),
            ("health_wound_minor_healing", "injury_healing"),
            ("health_wound_major_lost_mobility", "injury_worsened"),
            ("health_wound_major_worsened", "injury_worsened"),
            ("health_wound_minor_worsened", "injury_worsened"),
            ("mech_nightlog_sting_warning", "sting_warning"),
            ("mech_nightlog_inspection_2days", "inspection_warning"),
            ("mech_nightlog_inspection_tomorrow", "inspection_warning"),
            ("mech_nightlog_inspection_imminent", "inspection_warning"),
            ("mech_nightlog_investigation_near", "investigation_near"),
            ("mech_nightlog_power_off", "power_off"),
            ("mech_nightlog_reactor_no_power", "reactor_no_power"),
            ("mech_nightlog_reactor_critical", "reactor_critical"),
            ("mech_nightlog_burglary_alarm_stopped", "burglary_alarm_stopped"),
            ("mech_nightlog_aug_incoming", "aug_incoming"),
            ("mech_nightlog_aug_left_alive", "aug_left_alive"),
            ("mech_nightlog_store_vandalized", "vandalized"),
            ("mech_nightlog_vandalized_lower", "vandalized"),
            ("mech_nightlog_vandalized_security", "vandalized"),
            ("mech_nightlog_vandalized_upper", "vandalized"),
            ("mech_nightlog_vandalized_revolution", "vandalized"),
            ("mech_nightlog_vandalized_cartel", "vandalized"),
            ("mech_nightlog_vandalized_generic", "vandalized")
        };

        private static void CollectNightAlerts(string text, JArray alerts)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (MatchTemplateMechanic(text, "mech_nightlog_wanted_visited", out string wanted) && wanted.Length > 0)
            {
                alerts.Add(new JObject { ["type"] = "wanted_visited", ["name"] = wanted });
                return;
            }
            if (MatchTemplateMechanic(text, "mech_nightlog_storefront_not_cleaned", out string garbageDetail))
            {
                var alert = new JObject { ["type"] = "garbage_warning" };
                if (!string.IsNullOrWhiteSpace(garbageDetail)) alert["detail"] = garbageDetail.Trim();
                alerts.Add(alert);
                return;
            }
            if (MatchTemplateMechanic(text, "mech_nightlog_insurance_unpaid", out string daysRaw))
            {
                var alert = new JObject { ["type"] = "insurance_lapsed" };
                if (int.TryParse(daysRaw.Trim(), out int days)) alert["days_unpaid"] = days;
                else if (!string.IsNullOrWhiteSpace(daysRaw)) alert["detail"] = daysRaw.Trim();
                alerts.Add(alert);
                return;
            }
            foreach (var (key, alertType) in new[] {
                ("mech_nightlog_burglary_insured", "burglary_insured"),
                ("mech_nightlog_burglary_alarm_failed", "burglary_alarm_failed"),
                ("mech_nightlog_burglary_no_alarm", "burglary_no_alarm") })
            {
                if (MatchTemplateMechanic(text, key, out string stolenItem) && stolenItem.Length > 0)
                {
                    alerts.Add(new JObject { ["type"] = alertType, ["item"] = stolenItem });
                    return;
                }
            }
            foreach (var (key, alertType) in ExactNightAlertRules)
            {
                string expected = SafeLocMechanic(key);
                if (expected.Length > 0 && string.Equals(text, expected, StringComparison.Ordinal))
                {
                    alerts.Add(new JObject { ["type"] = alertType });
                    return;
                }
            }
        }

        // InitPanel prefixes each native line, then clears nightLogs. Capture before it runs.
        private void CaptureMorningReport()
        {
            var lines = new JArray();
            var alerts = new JArray();
            foreach (string entry in PlayerStore.Instance.nightLogs)
            {
                string line = CleanReportText("-" + entry);
                lines.Add(line);
                CollectNightAlerts(line.Substring(1).Trim(), alerts);
            }
            morningReport = new JObject { ["day"] = StoreStation.Instance.dayCounter, ["lines"] = lines, ["alerts"] = alerts };
            morningReportReady = false;
        }

        private static string ReadLocalizedLabel(TextMeshProUGUI label)
        {
            if (label == null) return "";
            var localizer = label.GetComponent<TMPLocalizer>();
            if (localizer != null && !localizer.ChangeFontOnly && !localizer.IsTooltipContainer)
            {
                var reference = label.GetComponent<UnityEngine.Localization.Components.LocalizeStringEvent>()?.StringReference;
                if (reference != null && !reference.IsEmpty)
                {
                    string localized = reference.GetLocalizedString();
                    if (!string.IsNullOrEmpty(localized)) return CleanReportText(localized + localizer.postfix);
                }
            }
            return CleanReportText(label.text);
        }

        private static JArray ReadMorningReminders()
        {
            var reminders = new JArray();
            var startOfDay = StartOfDayUIManager.Instance;
            if (startOfDay?.displayCaseReminder != null && startOfDay.displayCaseReminder.activeSelf)
            {
                string reminder = "";
                foreach (var label in startOfDay.displayCaseReminder.GetComponentsInChildren<TextMeshProUGUI>(true))
                {
                    string text = ReadLocalizedLabel(label);
                    if (text.Length > 0) reminder = reminder.Length == 0 ? text : reminder + " " + text;
                }
                reminders.Add(reminder.Length > 0 ? reminder : "display_case_reminder");
            }
            if (startOfDay?.rentReminder != null && startOfDay.rentReminder.activeSelf)
            {
                string text = CleanReportText(startOfDay.rentReminderTMP?.text);
                if (text.Length > 0) reminders.Add(text);
            }
            return reminders;
        }

        private void FinishMorningReport()
        {
            if (morningReport == null) return;
            morningReportReady = true;
            nextView = 0;
            var reminders = ReadMorningReminders();
            if (reminders.Count > 0) morningReport["reminders"] = reminders;
        }

        private JObject NightReportView()
        {
            int day = StoreStation.Instance.dayCounter;
            if (!morningReportReady || (int?)morningReport?["day"] != day)
                return new JObject { ["status"] = "awaiting_report", ["day"] = day, ["lines"] = new JArray() };
            var report = (JObject)morningReport.DeepClone();
            var liveReminders = ReadMorningReminders();
            if (liveReminders.Count > 0) report["reminders"] = liveReminders;
            report["status"] = "ready";
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
            result["display"] = LocHelper.GetLocalizedMechanic("mech_store_attractiveness") + ": " + (int)result["total"];
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
            result["injury"] = InjuryView();
            return result;
        }

        private static void CollectWasteInItem(GameItem item, HashSet<GameItem> seen, ref int count)
        {
            if (item == null) return;
            if (seen.Add(item) && IsHazardousWasteItem(item)) count++;
            foreach (var child in item.FindAllChildItems())
                if (child != null && seen.Add(child) && IsHazardousWasteItem(child)) count++;
        }

        private static void CollectWasteInInventory(GameInventory inventory, HashSet<GameItem> seen, ref int count)
        {
            if (inventory == null) return;
            foreach (var item in inventory.childItems)
                CollectWasteInItem(item, seen, ref count);
        }

        private static JObject NuclearWasteSummary()
        {
            var shop = EmporiumEntry.Instance;
            var data = PlayerStore.GetStoreClientData();
            var seenCounter = new HashSet<GameItem>();
            int counterCount = data?.nuclearWasteLeftOnCounter ?? 0;
            if (shop != null)
            {
                CollectWasteInInventory(shop.frontInvinvElement, seenCounter, ref counterCount);
                CollectWasteInInventory(shop.backInvinvElement, seenCounter, ref counterCount);
            }
            var seenShop = new HashSet<GameItem>(seenCounter);
            int shopCount = counterCount;
            if (shop != null)
            {
                CollectWasteInInventory(shop.invElement, seenShop, ref shopCount);
                CollectWasteInInventory(shop.showcaseElement, seenShop, ref shopCount);
                CollectWasteInInventory(shop.hiddenElement, seenShop, ref shopCount);
                CollectWasteInInventory(shop.trashcanInvElement, seenShop, ref shopCount);
                CollectWasteInItem(shop.trashcan, seenShop, ref shopCount);
            }
            return new JObject {
                ["in_shop"] = shopCount,
                ["on_counter"] = counterCount,
                ["dumped"] = data?.nuclearWasteDumped ?? 0
            };
        }

        private JObject DayView()
        {
            int day = StoreStation.Instance.dayCounter;
            int cost = ServiceCost();
            var sleep = new JObject { ["service_cost"]=cost, ["available"]=phase=="CLOSED" &&
                sleepSubmittedDay!=day && PlayerStore.Instance.playerCash >= cost };
            if (phase == "CLOSED") sleep["bill"] = NightBill();
            var alerts=new JArray();
            var health=HealthView(day,alerts);
            var result=new JObject {
                ["health"]=health, ["security"]=SecurityView(), ["wanted"]=WantedRows(), ["last_save"]=LastSave(PlayerStore.Instance.saveSlotId),
                ["calendar"]=CalendarView(day), ["trash"]=TrashSummary(day), ["sleep"]=sleep,
                ["night_report"]=NightReportView(), ["appeal"]=AppealView(), ["status_summary"]=StatusSummaryView(),
                ["nuclear_waste"]=NuclearWasteSummary(), ["capacities"]=ShopCapacities()
            };
            if (alerts.Count>0) result["alerts"]=alerts;
            return result;
        }

        private string SubmitSleep(JObject request, JObject reply)
        {
            var store = PlayerStore.Instance;
            if (store.storeState != PlayerStore.StoreState.CLOSED) return "sleep_requires_closed_shop";
            if (ResponseInventory() != null) return "response_required";
            int day = StoreStation.Instance.dayCounter;
            if (day != (int)request["day"] || sleepSubmittedDay == day) return "day_changed_or_pending";
            int cost = ServiceCost();
            AttachHealthAlerts(day,reply);
            reply["day_before"] = day;
            reply["service_cost"] = cost;
            reply["bill_token"] = (string)NightBill()["bill_token"];
            if (cost != (int)request["service_cost"]) return "service_cost_changed";
            if (store.playerCash < cost) return "insufficient_cash";
            var summary = EndOfDayUIManager.Instance;
            if (request["bill_token"] != null && ((string)reply["bill_token"] != (string)request["bill_token"] ||
                summary == null || !summary.IsOpen)) return "bill_changed";
            if (!OpenBill(summary)) return "bedroll_unavailable";
            if (ServiceCost()!=cost) return "review_nightly_cost";
            sleepSubmittedDay = day;
            summary.OnEndDayButtonClick();
            return "sleep_submitted";
        }
    }
}
