using System;
using Newtonsoft.Json.Linq;

namespace WhiteDragon.Ledger
{
    public sealed partial class InventoryObserver
    {
        private readonly CommandMailbox reportMailbox = new CommandMailbox();
        private string reportGeneration, reportedVisit, reportStatus;
        private int cashAtReport;
        // Bumped on every native alarm press, so a token read after an interception differs from the
        // first one even when the published revision and the light stay the same.
        private int reportPresses;
        private StoreClient reportCustomer;
        private bool sawGuard, augSequence;
        private readonly JArray reportLines = new JArray();
        private JObject completedReport;
        private object lastReportDialogue;

        private static bool ReportLight(bool present, bool eligible, bool arresting, bool reported, bool shootout) =>
            present && eligible && !arresting && !reported && !shootout;

        private void ObserveReport()
        {
            if (reportGeneration != generation || completedReport != null || reportCustomer == null) return;
            var store = PlayerStore.Instance;
            var ui = StoreUIManager.Instance;
            bool guard = ui.secGameObject.gameObject.activeInHierarchy;
            if (phase != "OPEN" || (visitId != null && visitId != reportedVisit))
            {
                reportStatus = "outcome_pending";
                reportCustomer = null;
                return;
            }
            sawGuard |= guard;
            var dialog = DialogUIManager.Instance;
            var line = dialog?.GetCurrentDialog();
            if (line != null && line != lastReportDialogue && !dialog.IsLineStillDisplaying(line))
            {
                var bubble = dialog.secBubble;
                if (bubble == null || !bubble.gameObject.activeInHierarchy) bubble = dialog.clientBubble;
                if (bubble != null && bubble.gameObject.activeInHierarchy && !string.IsNullOrWhiteSpace(bubble.textfield?.text))
                {
                    lastReportDialogue = line;
                    reportLines.Add(bubble.textfield.text);
                    if (augSequence && dialog.isSecMonologuePlaying) reportStatus = "aug_response";
                }
            }
            if (store.isHandlingShootout) reportStatus = "shootout";
            if (sawGuard && !guard && store.onSecAlarmLeftAction == null)
            {
                completedReport = new JObject { ["lines"]=reportLines.DeepClone(), ["cash_delta"]=store.playerCash-cashAtReport,
                    ["customer_removed"]=store.currentClientInstance?.GetClientBlueprint()!=reportCustomer };
                reportStatus = "arrest_finished";
            }
        }

        private string ReportAction(JObject request, JObject receipt)
        {
            if ((long)request["expires_ms"] < DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) return "expired";
            if ((string)request["generation"] != generation) return "stale_observation";
            string visit = (string)request["visit"];
            receipt["visit"] = visit;
            receipt["revision"] = sequence;
            var store = PlayerStore.Instance;
            var button = StoreSecButton.Instance;
            bool read = (string)request["mode"] == "read";
            if (read && visit != null && visit == reportedVisit && reportGeneration == generation)
            {
                if (!(reportStatus == "report_intercepted" && CounterStage() != "speaking"))
                {
                    receipt["next_tool"] = ReportNextTool();
                    if (completedReport != null) receipt.Merge(completedReport);
                    return reportStatus;
                }
            }
            if (visit != visitId || (!read && visit == null)) return VisitMismatchStatus(receipt);
            if (button == null) return "report_unavailable";
            var customer = store.currentClientInstance?.GetClientBlueprint();
            bool present = store.IsClientPresent();
            bool reported = present && customer.reported;
            bool light = ReportLight(present,present && button.IsClientArrestable(),store.isClientBeingArrested,reported,store.isHandlingShootout);
            string token = generation + "/" + sequence + "/" + visit + "/" + light + "/" + reportPresses;
            if (read)
            {
                receipt["light"] = light;
                receipt["reported"] = reported;
                receipt["tooltip"] = button.GetTooltip();
                receipt["requires_hold"] = button.ShouldRequireHold();
                receipt["report_token"] = token;
                if (reportStatus == "report_intercepted" && visit == reportedVisit && reportGeneration == generation)
                {
                    receipt["intercepted_before"] = true;
                    receipt["choice_hint"] = "再按一次即舉報；送客即放走";
                }
                return "observed";
            }
            if ((string)request["mode"] != "report") return "invalid_mode";
            if ((int)request["revision"] != sequence || token != (string)request["report_token"]) return "stale_observation";
            if (!light) return store.isHandlingShootout ? "shootout" : "report_unavailable";
            bool couldIntercept = customer.tryInterceptReport != null;
            cashAtReport = store.playerCash;
            lastReportDialogue = DialogUIManager.Instance?.GetCurrentDialog();
            button.OnClick();
            reportPresses++;
            reportedVisit = visit;
            reportGeneration = generation;
            reportLines.Clear();
            completedReport = null;
            sawGuard = false;
            augSequence = StoreUIManager.Instance.isReportedAug;
            reportCustomer = customer.reported || store.isHandlingShootout ? customer : null;
            reportStatus = store.isHandlingShootout ? "shootout" : customer.reported ? "reported" :
                couldIntercept && customer.tryInterceptReport == null ? "report_intercepted" : "outcome_pending";
            receipt["next_tool"] = ReportNextTool();
            return reportStatus;
        }

        private string ReportNextTool() =>
            reportStatus == "report_intercepted" ? "get_trade_context" :
            completedReport == null ? "report_client" : "feedback_box";

        private void CheckReport()
        {
            ObserveReport();
            reportMailbox.Process("report",generation,slot,ReportAction);
        }
    }
}
