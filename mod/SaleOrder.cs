using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace WhiteDragon.Ledger
{
    public sealed partial class InventoryObserver
    {
        private JObject order, line;
        private JArray results;
        private int index, orderRevision;
        private GameItem staged, observedSale;
        private int observedRevenue;
        private GameInventory original;
        private readonly HashSet<string> blockedKinds = new HashSet<string>();
        private float nextOrderPoll;
        private JObject completedOrder;

        private void EndOrder(string status)
        {
            var rows = (JArray)order["lines"];
            while (results.Count < rows.Count)
                results.Add(new JObject { ["item_id"] = (int)rows[results.Count]["item_id"], ["status"] = "not_attempted" });
            completedOrder = new JObject { ["command_id"] = (string)order["command_id"], ["generation"] = generation,
                ["status"] = status, ["slot"] = slot, ["revision"] = sequence, ["visit"] = (string)order["visit"],
                ["load_scans"] = loadScans, ["items"] = results,
                ["remaining_budget"] = merchant != null && visitId == (string)order["visit"] && merchant.useClientBudget
                    ? (JToken)merchant.GetBudget() : JValue.CreateNull() };
            staged = null;
            order = null;
        }

        private bool CheckBuyer()
        {
            var dialog = DialogUIManager.Instance;
            var ui = NegociationUIManager.Instance;
            var frame = UnityEngine.Object.FindObjectOfType<NegotiationTabletFrame>();
            return phase == "OPEN" && visitId == (string)order["visit"] && merchant != null &&
                merchant.IsClientBuying() && !merchant.isBusinessComplete && PlayerStore.Instance.isClientArrived &&
                ui != null && ui.IsOpen && !NegociationUIManager.IsTradeLocked() &&
                (frame == null || frame.confirmRoot == null || !frame.confirmRoot.activeInHierarchy) &&
                (dialog == null || (!dialog.isWaitingForManualAdvance && !dialog.IsChoice() &&
                (dialog.GetCurrentDialog() == null || (dialog.IsCurrentDialogEnd() && !dialog.IsLineStillDisplaying(dialog.GetCurrentDialog())))));
        }

        private void StopLine(string reason)
        {
            if (line != null)
            {
                line["status"] = reason;
                if (staged != null)
                    line["actual_area"] = staged.parentInventory != null && areas.TryGetValue(staged.parentInventory, out var area)
                        ? (JToken)area : JValue.CreateNull();
                results.Add(line);
            }
            EndOrder(reason);
        }

        private void OrderStep()
        {
            if (orderRevision != sequence) { StopLine("state_changed"); return; }
            if (!CheckBuyer()) { StopLine("buyer_or_ui_changed"); return; }
            var rows = (JArray)order["lines"];
            var table = EmporiumEntry.Instance.backInvinvElement;
            if (staged == null)
            {
                int id = (int)rows[index]["item_id"];
                line = new JObject { ["item_id"] = id };
                if (!itemsById.TryGetValue(id, out var item) || !GeneralHelper.IsItemOwned(item) ||
                    item.parentInventory == null || !areas.TryGetValue(item.parentInventory, out var area) ||
                    (area != "inventory" && area != "showcase")) { StopLine("source_changed"); return; }
                line["kind"] = item.identifier;
                line["actual_area"] = area;
                if (blockedKinds.Contains(item.identifier))
                {
                    line["status"] = "skipped_same_kind_budget";
                    FinishLine();
                    return;
                }
                if (table.childItems.Count != 0 || table.IsInsertLocked() || !item.MayRemove())
                { StopLine("counter_unavailable"); return; }
                original = item.parentInventory;
                staged = item;
                ModHelper.MoveItemToBackCounter(item);
                FlushChanges();
                orderRevision = sequence;
                if (item.parentInventory != table) StopLine("placement_failed");
                return;
            }

            if (table.childItems.Count != 1 || table.childItems[0] != staged ||
                PlayerStore.Instance.GetCurrentlyNegociatedItem() != staged)
            { StopLine("counter_changed"); return; }

            var store = PlayerStore.Instance;
            long quote = staged.GetNegociatedValue();
            line["quote"] = quote;
            line["budget_before"] = merchant.useClientBudget ? (JToken)merchant.GetBudget() : JValue.CreateNull();
            bool otherwiseEligible = store.CanSellThisItem(staged, true);
            bool overBudget = otherwiseEligible && merchant.useClientBudget && quote > merchant.GetBudget();
            string refusal = overBudget ? "over_budget" : !otherwiseEligible ? "declined" :
                quote < (long)rows[index]["min_price"] ? "below_minimum" : null;
            if (refusal != null)
            {
                if (original == EmporiumEntry.Instance.invElement) ModHelper.MoveItemToInventory(staged);
                else ModHelper.MoveItemToDisplayCase(staged);
                FlushChanges();
                if (staged.parentInventory != original) { StopLine("return_unconfirmed"); return; }
                line["status"] = "returned_" + refusal;
                line["actual_area"] = areas[original];
                if (overBudget && (bool)order["stop_same_kind_on_budget"]) blockedKinds.Add(staged.identifier);
                FinishLine();
                return;
            }

            if (!store.CanSellItemFromTable(false)) { StopLine("needs_attention"); return; }
            int beforeCash = store.playerCash;
            observedSale = null;
            NegociationUIManager.Instance.OnOfferAcceptClick();
            FlushChanges();
            if (observedSale != staged || GeneralHelper.IsItemOwned(staged) || store.playerCash - beforeCash != observedRevenue)
            { StopLine("sale_unconfirmed"); return; }
            line["status"] = "sold";
            line["actual_area"] = "buyer";
            line["received"] = observedRevenue;
            FinishLine();
        }

        private void FinishLine()
        {
            line["budget_after"] = merchant.useClientBudget ? (JToken)merchant.GetBudget() : JValue.CreateNull();
            results.Add(line);
            line = null;
            staged = null;
            orderRevision = sequence;
            if (++index == ((JArray)order["lines"]).Count) EndOrder("finished");
        }

        private void CheckOrder()
        {
            string inbox = "sale-" + generation + ".json";
            if (completedOrder != null)
            {
                Write("receipt-" + (string)completedOrder["command_id"] + ".json", completedOrder);
                ModHelper.DeleteModDataFile(ModId, inbox);
                completedOrder = null;
                return;
            }
            if (order == null)
            {
                if (Time.realtimeSinceStartup < nextOrderPoll) return;
                nextOrderPoll = Time.realtimeSinceStartup + 0.2f;
                if (!ModHelper.ModDataFileExists(ModId, inbox)) return;
                var request = JObject.Parse(ModHelper.ReadModDataFileText(ModId, inbox));
                string command = (string)request["command_id"];
                if (!Guid.TryParseExact(command,"N",out var valid) || command != valid.ToString("N") ||
                    ModHelper.ModDataFileExists(ModId, "receipt-" + command + ".json"))
                { ModHelper.DeleteModDataFile(ModId, inbox); return; }
                order = request;
                results = new JArray();
                line = null;
                index = 0;
                blockedKinds.Clear();
                orderRevision = (int)order["revision"];
                if ((string)order["generation"] != generation || DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() > (long)order["expires_ms"])
                { EndOrder("expired"); return; }
            }
            try { OrderStep(); }
            catch (Exception error) { Debug.LogException(error); StopLine("outcome_unknown"); }
        }
    }
}
