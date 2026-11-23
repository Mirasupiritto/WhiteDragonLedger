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
        private readonly List<GameItem> stagedBundle = new List<GameItem>();
        private readonly Dictionary<GameItem, GameInventory> bundleOrigins = new Dictionary<GameItem, GameInventory>();
        private readonly Dictionary<GameItem, int> bundleQuantities = new Dictionary<GameItem, int>();
        private readonly Dictionary<GameItem, int> observedBundleSales = new Dictionary<GameItem, int>();
        private readonly HashSet<string> blockedKinds = new HashSet<string>();
        private readonly JArray orderClearedItems = new JArray();
        private GameItem blockedSourceItem;
        private string blockedOrderReason;
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
            if (orderClearedItems.Count > 0) completedOrder["cleared_items"] = orderClearedItems.DeepClone();
            if (status == "counter_unavailable")
                DescribeBlockedTable(completedOrder, false, true, blockedSourceItem, blockedOrderReason);
            staged = null;
            blockedSourceItem = null;
            blockedOrderReason = null;
            orderClearedItems.Clear();
            stagedBundle.Clear();
            bundleOrigins.Clear();
            bundleQuantities.Clear();
            observedBundleSales.Clear();
            order = null;
            // LateUpdate publishes the desk before CheckOrder releases the finished receipt.
            nextView = 0;
        }

        private string saleStage;
        private int saleSceneRevision, stagedQuantity;
        private object advancedSalePage;

        private void ReportSaleStage(string stage)
        {
            if (saleStage == stage) return;
            saleStage = stage;
            Write("sale-progress-" + generation + ".json", new JObject {
                ["status"]="sale_pending", ["stage"]=stage, ["command_id"]=order["command_id"],
                ["generation"]=generation, ["visit"]=visitId, ["slot"]=slot, ["revision"]=sequence,
                ["current_line"]=order["lines"][index].DeepClone(), ["item_id"]=order["lines"][index]["item_id"],
                ["min_price"]=order["lines"][index]["min_price"], ["items"]=results.DeepClone(),
                ["remaining_budget"]=merchant.useClientBudget ? (JToken)merchant.GetBudget() : JValue.CreateNull()
            });
        }

        private bool SaleTabletReady()
        {
            string stage = CounterStage(sellingToBuyer: true);
            if (stage != "advance_required") advancedSalePage = null;
            if (stage == "ready") return true;
            var dialog = DialogUIManager.Instance;
            if (stage == "advance_required")
            {
                var page = dialog.GetCurrentDialog();
                if (page != null && page != advancedSalePage)
                {
                    advancedSalePage = page;
                    dialog.OnAdvancePressed();
                }
            }
            else if (stage == "tablet_closed" && stage != saleStage)
            {
                var ui = NegociationUIManager.Instance;
                if (ui == null) { StopLine("tablet_unavailable"); return false; }
                ui.OpenUI();
            }
            else if (stage != "speaking" && stage != "awaiting_opening" && stage != "trade_locked" &&
                     stage != "wild_network" && stage != "choice_required" && stage != "response_required" && stage != "confirmation_required" &&
                     stage != "conversation_paused" && stage != "tablet_closed")
            {
                StopLine("buyer_or_ui_changed");
                return false;
            }
            ReportSaleStage(stage);
            return false;
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

        private static void ReturnToOrigin(GameItem item, GameInventory origin)
        {
            if (origin == EmporiumEntry.Instance.invElement) ModHelper.MoveItemToInventory(item);
            else if (origin == EmporiumEntry.Instance.showcaseElement) ModHelper.MoveItemToDisplayCase(item);
        }

        private void StopBundle(string reason)
        {
            var rows = (JArray)order["lines"];
            for (int i = results.Count; i < rows.Count; i++)
            {
                var row = (JObject)rows[i].DeepClone();
                row["status"] = reason;
                if (i < stagedBundle.Count)
                {
                    var item = stagedBundle[i];
                    row["kind"] = item.identifier;
                    row["actual_area"] = item.parentInventory != null && areas.TryGetValue(item.parentInventory, out var area)
                        ? (JToken)area : JValue.CreateNull();
                }
                results.Add(row);
            }
            EndOrder(reason);
        }

        private void OrderBundleStep()
        {
            if (saleSceneRevision != sceneRevision || (stagedBundle.Count == 0 && orderRevision != sequence))
            { StopBundle("state_changed"); return; }
            if (phase != "OPEN" || visitId != (string)order["visit"] || merchant == null ||
                merchant.isBusinessComplete || !PlayerStore.Instance.isClientArrived || !merchant.IsClientBuying())
            { StopBundle("buyer_or_ui_changed"); return; }
            var rows = (JArray)order["lines"];
            var table = EmporiumEntry.Instance.backInvinvElement;
            if (stagedBundle.Count > 0)
            {
                if (table.childItems.Count != stagedBundle.Count)
                { StopBundle("counter_changed"); return; }
                for (int i = 0; i < stagedBundle.Count; i++)
                {
                    var item = stagedBundle[i];
                    if (item.parentInventory != table || item.unitCount != bundleQuantities[item] || !GeneralHelper.IsItemOwned(item))
                    { StopBundle("counter_changed"); return; }
                }
            }
            if (!SaleTabletReady()) return;
            if (stagedBundle.Count == 0)
            {
                if (ClearNonOrderBackTable(null, orderClearedItems) > 0)
                {
                    FlushChanges();
                    orderRevision = sequence;
                }
                if (table.childItems.Count != 0 || table.IsInsertLocked())
                {
                    if (table.IsInsertLocked() && table.childItems.Count == 0) blockedOrderReason = "table_insert_locked";
                    StopBundle("counter_unavailable");
                    return;
                }
                var toStage = new List<GameItem>(rows.Count);
                var origins = new List<GameInventory>(rows.Count);
                for (int i = 0; i < rows.Count; i++)
                {
                    int id = (int)rows[i]["item_id"];
                    if (!itemsById.TryGetValue(id, out var item) || !GeneralHelper.IsItemOwned(item) ||
                        item.parentInventory == null || !areas.TryGetValue(item.parentInventory, out var area) ||
                        (area != "inventory" && area != "showcase"))
                    { StopBundle("source_changed"); return; }
                    if (GeneralHelper.IsItemLocked(item) || !item.MayRemove() || item.parentInventory.IsRemoveLocked())
                    {
                        blockedSourceItem = item;
                        blockedOrderReason = "source_item_locked";
                        StopBundle("counter_unavailable");
                        return;
                    }
                    toStage.Add(item);
                    origins.Add(item.parentInventory);
                }
                for (int i = 0; i < toStage.Count; i++)
                {
                    var item = toStage[i];
                    var origin = origins[i];
                    int qty = item.unitCount;
                    ModHelper.MoveItemToBackCounter(item);
                    if (item.parentInventory != table)
                    {
                        for (int j = stagedBundle.Count - 1; j >= 0; j--)
                            ReturnToOrigin(stagedBundle[j], bundleOrigins[stagedBundle[j]]);
                        FlushChanges();
                        for (int j = 0; j < stagedBundle.Count; j++)
                        {
                            var rolled = (JObject)rows[j].DeepClone();
                            var prev = stagedBundle[j];
                            rolled["kind"] = prev.identifier;
                            rolled["actual_area"] = prev.parentInventory != null && areas.TryGetValue(prev.parentInventory, out var a)
                                ? (JToken)a : JValue.CreateNull();
                            rolled["status"] = prev.parentInventory == bundleOrigins[prev] ? "returned_placement_failed" : "return_unconfirmed";
                            results.Add(rolled);
                        }
                        var failed = (JObject)rows[i].DeepClone();
                        failed["kind"] = item.identifier;
                        failed["actual_area"] = item.parentInventory != null && areas.TryGetValue(item.parentInventory, out var fa)
                            ? (JToken)fa : JValue.CreateNull();
                        failed["status"] = "placement_failed";
                        results.Add(failed);
                        EndOrder("placement_failed");
                        return;
                    }
                    stagedBundle.Add(item);
                    bundleOrigins[item] = origin;
                    bundleQuantities[item] = qty;
                }
                FlushChanges();
                orderRevision = sequence;
                ReportSaleStage("staged");
                return;
            }

            var store = PlayerStore.Instance;
            long totalQuote = 0;
            JToken budgetBefore = merchant.useClientBudget ? (JToken)merchant.GetBudget() : JValue.CreateNull();
            var seenKinds = new HashSet<string>();
            string bundleRefusal = null;
            var quotes = new long[rows.Count];
            var refusals = new string[rows.Count];
            for (int i = 0; i < rows.Count; i++)
            {
                var spec = (JObject)rows[i];
                var item = stagedBundle[i];
                long quote = item.GetNegociatedValue();
                quotes[i] = quote;
                totalQuote += quote;
                bool eligible = store.CanSellThisItem(item, true);
                bool duplicateVariety = merchant.isRequireVariety && !seenKinds.Add(item.identifier);
                string itemRefusal = !eligible ? "declined" : duplicateVariety ? "duplicate_variety" :
                    quote < (long)spec["min_price"] ? "below_minimum" : null;
                refusals[i] = itemRefusal;
                if (bundleRefusal == null && itemRefusal != null) bundleRefusal = itemRefusal;
            }
            if (bundleRefusal == null && merchant.useClientBudget && totalQuote > merchant.GetBudget())
                bundleRefusal = "over_budget";
            if (bundleRefusal != null)
            {
                for (int i = stagedBundle.Count - 1; i >= 0; i--)
                    ReturnToOrigin(stagedBundle[i], bundleOrigins[stagedBundle[i]]);
                FlushChanges();
                bool allReturned = true;
                JToken budgetAfter = merchant.useClientBudget ? (JToken)merchant.GetBudget() : JValue.CreateNull();
                for (int i = 0; i < rows.Count; i++)
                {
                    var row = (JObject)rows[i].DeepClone();
                    var item = stagedBundle[i];
                    row["kind"] = item.identifier;
                    row["quote"] = quotes[i];
                    row["budget_before"] = budgetBefore;
                    row["budget_after"] = budgetAfter;
                    bool returned = item.parentInventory == bundleOrigins[item];
                    if (!returned) allReturned = false;
                    row["actual_area"] = item.parentInventory != null && areas.TryGetValue(item.parentInventory, out var area)
                        ? (JToken)area : JValue.CreateNull();
                    row["status"] = returned ? "returned_" + (refusals[i] ?? bundleRefusal) : "return_unconfirmed";
                    results.Add(row);
                }
                EndOrder(allReturned ? "finished" : "return_unconfirmed");
                return;
            }

            int beforeCash = store.playerCash;
            observedBundleSales.Clear();
            NegociationUIManager.Instance.OnSellAllClick();
            FlushChanges();
            long totalReceived = 0;
            bool allSold = true;
            for (int i = 0; i < stagedBundle.Count; i++)
            {
                var item = stagedBundle[i];
                if (observedBundleSales.TryGetValue(item, out int rev) && !GeneralHelper.IsItemOwned(item))
                    totalReceived += rev;
                else
                    allSold = false;
            }
            bool cashMatches = store.playerCash - beforeCash == totalReceived;
            JToken finalBudget = merchant.useClientBudget ? (JToken)merchant.GetBudget() : JValue.CreateNull();
            for (int i = 0; i < rows.Count; i++)
            {
                var row = (JObject)rows[i].DeepClone();
                var item = stagedBundle[i];
                row["kind"] = item.identifier;
                row["quote"] = quotes[i];
                row["budget_before"] = budgetBefore;
                row["budget_after"] = finalBudget;
                if (observedBundleSales.TryGetValue(item, out int rev) && !GeneralHelper.IsItemOwned(item))
                {
                    row["status"] = "sold";
                    row["actual_area"] = "buyer";
                    row["received"] = rev;
                    row["cash_after"] = store.playerCash;
                }
                else
                {
                    row["status"] = "sale_unconfirmed";
                    row["actual_area"] = item.parentInventory != null && areas.TryGetValue(item.parentInventory, out var area)
                        ? (JToken)area : JValue.CreateNull();
                }
                results.Add(row);
            }
            EndOrder(allSold && cashMatches ? "finished" : "sale_unconfirmed");
        }

        private void OrderStep()
        {
            if ((bool?)order["bundle"] == true)
            {
                OrderBundleStep();
                return;
            }
            if (saleSceneRevision != sceneRevision || (staged == null && orderRevision != sequence))
            { StopLine("state_changed"); return; }
            var spec = order["lines"][index];
            bool buying = (string)spec["side"] == "buy";
            bool onTable = (bool?)order["on_table"] == true;
            if (phase != "OPEN" || visitId != (string)order["visit"] || merchant == null ||
                merchant.isBusinessComplete || !PlayerStore.Instance.isClientArrived)
            { StopLine("buyer_or_ui_changed"); return; }
            bool sideAvailable = buying ? merchant.IsClientSelling() && !merchant.isOfferWholesale && !merchant.isRegularSellDisable
                                        : merchant.IsClientBuying();
            if (!sideAvailable)
            {
                if (staged != null || (string)order["tool"] != "trade_batch") { StopLine("buyer_or_ui_changed"); return; }
                line = (JObject)spec.DeepClone();
                line["status"] = "skipped_side_unavailable";
                FinishLine();
                return;
            }
            var rows = (JArray)order["lines"];
            var table = EmporiumEntry.Instance.backInvinvElement;
            if (staged != null && (table.childItems.Count != 1 || table.childItems[0] != staged ||
                staged.parentInventory != table || staged.unitCount != stagedQuantity || GeneralHelper.IsItemOwned(staged) == buying))
            { StopLine("counter_changed"); return; }
            if (!SaleTabletReady()) return;
            if (staged == null)
            {
                int id = (int)rows[index]["item_id"];
                HashSet<GameItem> exclude = null;
                if ((onTable || buying) && itemsById.TryGetValue(id, out var preItem) && preItem.parentInventory == table)
                    exclude = new HashSet<GameItem> { preItem };
                if (ClearNonOrderBackTable(exclude, orderClearedItems) > 0)
                {
                    FlushChanges();
                    orderRevision = sequence;
                }
                line = (JObject)spec.DeepClone();
                if (!itemsById.TryGetValue(id, out var item) || GeneralHelper.IsItemOwned(item) == buying ||
                    item.parentInventory == null || !areas.TryGetValue(item.parentInventory, out var area) ||
                    (onTable ? area != "back" : buying ? area != "front" && area != "back" : area != "inventory" && area != "showcase")) { StopLine("source_changed"); return; }
                line["kind"] = item.identifier;
                line["actual_area"] = area;
                if (!buying && blockedKinds.Contains(item.identifier))
                {
                    line["status"] = "skipped_same_kind_budget";
                    FinishLine();
                    return;
                }
                if (onTable)
                {
                    var visible = TableQuote();
                    if (visible == null || (int)visible["item_id"] != id || (bool)visible["allowed"] == false ||
                        (string)visible["side"] != (string)spec["side"] || (long)visible["price"] != (long)spec["exact_price"])
                    { line["quote"] = visible?["price"]; StopLine("quote_changed"); return; }
                }
                bool placed = item.parentInventory == table;
                if (placed ? table.childItems.Count != 1 : table.childItems.Count != 0 || table.IsInsertLocked() || !item.MayRemove())
                {
                    if (!placed && table.childItems.Count == 0)
                    {
                        blockedSourceItem = item;
                        blockedOrderReason = table.IsInsertLocked() ? "table_insert_locked" : "source_item_locked";
                    }
                    StopLine("counter_unavailable");
                    return;
                }
                original = item.parentInventory;
                staged = item;
                stagedQuantity = item.unitCount;
                if (!placed) ModHelper.MoveItemToBackCounter(item);
                FlushChanges();
                orderRevision = sequence;
                if (item.parentInventory != table) StopLine("placement_failed");
                else ReportSaleStage("staged");
                return;
            }

            if (PlayerStore.Instance.GetCurrentlyNegociatedItem() != staged)
            { StopLine("counter_changed"); return; }

            var store = PlayerStore.Instance;
            long quote = staged.GetNegociatedValue();
            line["quote"] = quote;
            line["budget_before"] = merchant.useClientBudget ? (JToken)merchant.GetBudget() : JValue.CreateNull();
            if (buying)
            {
                BuyOrderLine(spec, quote);
                return;
            }
            bool otherwiseEligible = store.CanSellThisItem(staged, true);
            bool overBudget = otherwiseEligible && merchant.useClientBudget && quote > merchant.GetBudget();
            string refusal = spec["exact_price"] != null && (long)spec["exact_price"] != quote ? "quote_changed" : overBudget ? "over_budget" : !otherwiseEligible ? "declined" :
                quote < (long)rows[index]["min_price"] ? "below_minimum" : null;
            if (refusal != null)
            {
                if (original == EmporiumEntry.Instance.invElement) ModHelper.MoveItemToInventory(staged);
                else if (original == EmporiumEntry.Instance.showcaseElement) ModHelper.MoveItemToDisplayCase(staged);
                FlushChanges();
                if (staged.parentInventory != original) { StopLine("return_unconfirmed"); return; }
                line["status"] = "returned_" + refusal;
                line["actual_area"] = areas[original];
                if (overBudget && (bool?)order["stop_same_kind_on_budget"] == true) blockedKinds.Add(staged.identifier);
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
            line["cash_after"] = store.playerCash;
            FinishLine();
        }

        private void FinishLine()
        {
            line["budget_after"] = merchant.useClientBudget ? (JToken)merchant.GetBudget() : JValue.CreateNull();
            results.Add(line);
            saleStage = null;
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
                if (ModHelper.ModDataFileExists(ModId, "sale-progress-" + generation + ".json"))
                    ModHelper.DeleteModDataFile(ModId, "sale-progress-" + generation + ".json");
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
                orderClearedItems.Clear();
                blockedSourceItem = null;
                blockedOrderReason = null;
                saleStage = null;
                advancedSalePage = null;
                saleSceneRevision = sceneRevision;
                orderRevision = (int)order["revision"];
                if ((string)order["generation"] != generation || DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() > (long)order["expires_ms"])
                { EndOrder("expired"); return; }
            }
            try { OrderStep(); }
            catch (Exception error) { Debug.LogException(error); StopLine("outcome_unknown"); }
        }
    }
}
