using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace WhiteDragon.Ledger
{
    public sealed partial class InventoryObserver
    {
        private float nextCounterPoll;
        private JObject offerSnapshot;
        private string offerToken, counterVisit;
        private readonly CommandMailbox counterMailbox = new CommandMailbox();
        private readonly JArray openingLines = new JArray();
        private object lastSpokenLine, lastPage;
        private string pageToken;

        private void ObserveOpening()
        {
            if (counterVisit != visitId)
            {
                counterVisit = visitId;
                openingLines.Clear();
                lastSpokenLine = null;
                offerSnapshot = null;
                nextView = 0;
            }
            var dialog = DialogUIManager.Instance;
            if (merchant == null || dialog == null) return;
            var current = dialog.GetCurrentDialog();
            if (current == null || current == lastSpokenLine || dialog.IsLineStillDisplaying(current)) return;
            lastSpokenLine = current;
            var bubble = dialog.clientBubble;
            if (bubble == null || !bubble.gameObject.activeInHierarchy || bubble.textfield == null) return;
            string text = bubble.textfield.text;
            if (!string.IsNullOrWhiteSpace(text) && (openingLines.Count == 0 || (string)openingLines.Last != text))
                openingLines.Add(text);
        }

        private string CounterStage(bool sellingToBuyer = false)
        {
            if (WildNetworkUI.IsOpen) return "wild_network";
            if (ResponseInventory() != null) return "response_required";
            if (phase != "OPEN") return "shop_closed";
            var store = PlayerStore.Instance;
            if (merchant == null) return store.currentClientInstance == null ? "idle" : "arriving";
            if (!store.isClientArrived) return "arriving";
            var dialog = DialogUIManager.Instance;
            if (dialog != null)
            {
                var current = dialog.GetCurrentDialog();
                if (current != null && dialog.IsLineStillDisplaying(current)) return "speaking";
                if (dialog.IsChoice()) return "choice_required";
                if (dialog.isWaitingForManualAdvance) return "advance_required";
                // EndConversation clears the last line after the typewriter has finished.
                if (current != null) return "speaking";
                if (dialog.HasPausedConversation) return "conversation_paused";
            }
            if (StoreUIManager.Instance.isAboutToStartMainDialog ||
                (merchant.mainDialogue != null && (!merchant.isMainDialogueStarted || dialog == null)))
                return "awaiting_opening";
            if (merchant.isBusinessComplete) return "business_complete";
            if (NegociationUIManager.IsTradeLocked()) return "trade_locked";
            bool dealer = merchant.clientIntent == StoreClient.ClientIntent.INFORMATION_DEALER;
            if (dealer && (!merchant.isIntroduced || dialog == null)) return "awaiting_opening";
            bool barter = IsFoodBarter();
            if (barter && (!merchant.isIntroduced || dialog == null)) return "awaiting_opening";
            if (!sellingToBuyer && !dealer && !barter && !merchant.IsClientSelling()) return "not_selling";
            var ui = NegociationUIManager.Instance;
            if (ui == null || !ui.IsOpen) return "tablet_closed";
            var frame = UnityEngine.Object.FindObjectOfType<NegotiationTabletFrame>();
            if (frame != null && frame.confirmRoot != null && frame.confirmRoot.activeInHierarchy) return "confirmation_required";
            return barter ? "barter" : dealer ? "information" : "ready";
        }

        private bool CanOpenShop()
        {
            var store = PlayerStore.Instance;
            return store.storeState == PlayerStore.StoreState.MORNING && StoreShutterButton.Instance != null &&
                store.lockShutterButtonFlag <= 0 && !store.isClientBeingArrested && !store.isHandlingShootout && store.CanStartDay();
        }

        private JObject CounterView()
        {
            var store = PlayerStore.Instance;
            string stage = CounterStage();
            var view = new JObject { ["stage"] = stage, ["barter_protocol"]=1, ["dialogue_protocol"]=1, ["night_bill_protocol"]=1, ["dismiss_recheck_protocol"] = 1, ["wild_network_protocol"] = 1, ["trade_tools_protocol"] = 1, ["sale_resume_protocol"] = 1, ["water_use_protocol"] = 1, ["purifier_notice_protocol"]=1, ["water_panel_protocol"] = 1, ["information_protocol"] = 1, ["batch_slot_protocol"] = 1, ["workstation_protocol"] = 1, ["archive_protocol"] = 1, ["can_open_shop"] = CanOpenShop(), ["reception"] = ReceptionView(), ["cash"] = store.playerCash, ["visit"] = visitId,
                ["customer"] = merchant?.displayName, ["lines"] = openingLines.DeepClone() };
            if (paperCleanup != null && paperCleanup.Any(row => (string)row["status"] != "moved"))
                view["document_cleanup"] = new JArray(paperCleanup.Where(row => (string)row["status"] != "moved"));
            if (stage == "ready" || stage == "not_selling" || stage == "tablet_closed" || stage == "confirmation_required")
                view["demand"] = PanelDemand();
            if (observedVariety && visitId != null && view["demand"]?["max_per_kind"] == null)
                view["visit_variety"] = new JObject { ["source"]="trade_panel", ["status"]="last_visible_for_visit",
                    ["visit"]=visitId, ["max_per_kind"]=1, ["allocation_hint"]=VarietyHint,
                    ["sold_kinds"]=new JArray(merchant.varietyTrackingList.Distinct()) };
            view["quote_ready"] = merchant != null && CounterStage(true) == "ready";
            view["quote"] = TableQuote();
            var backTableItems = new JArray();
            foreach (var item in EmporiumEntry.Instance.backInvinvElement.childItems)
                backTableItems.Add(new JObject {
                    ["id"]=LocalId(item), ["kind"]=item.identifier, ["area"]="back",
                    ["owned"]=GeneralHelper.IsItemOwned(item), ["hazardous"]=IsHazardousWasteItem(item)
                });
            if (backTableItems.Count > 0) view["table_items"] = backTableItems;
            ObserveDecision(stage, view);
            if (phase == "OPEN") view["waiting"] = store.GetClientInQueueCount();
            if (stage == "information")
                ObserveInformationReceipt(view);
            else
                informationToken = null;
            if (stage != "ready") { offerSnapshot = null; return view; }

            var rows = new JArray();
            long sum = 0;
            var offered = EmporiumEntry.Instance.GetAllNonOwnedItem();
            if (offered.Count == 0)
            {
                offerSnapshot = null;
                view["stage"] = "no_goods";
                return view;
            }
            foreach (var item in offered)
            {
                var row = new JObject { ["id"] = LocalId(item), ["name"] = item.GetDisplayName(true),
                    ["kind"] = item.identifier, ["quantity"] = item.unitCount };
                if (BottleLabel(item) is string label)
                {
                    row["bottle_label"] = label;
                    row["capacity_ml"] = WaterHelper.GetTotalCapacityML(item);
                    var strip = WaterStripUIManager.Instance;
                    if (strip != null && strip.panel != null && strip.panel.activeInHierarchy && strip.currentWaterContainer == item)
                    {
                        string tested = VisibleText(strip.testedTMP);
                        if (!string.IsNullOrWhiteSpace(tested))
                            stripReadings[item] = new JObject { ["tested"]=tested, ["purity"]=VisibleText(strip.purityTMP) };
                    }
                    if (stripReadings.TryGetValue(item, out var reading)) row["last_strip"] = reading.DeepClone();
                }
                rows.Add(row);
                sum += item.GetNegociatedValue();
            }
            bool wholesale = merchant.isOfferWholesale;
            var offer = new JObject { ["visit"] = visitId, ["items"] = rows, ["wholesale"] = wholesale,
                ["total"] = wholesale ? store.GetWholesalePrice() : sum };
            if (!JToken.DeepEquals(offerSnapshot, offer))
            {
                offerToken = Guid.NewGuid().ToString("N");
                offerSnapshot = offer;
            }
            view["offer"] = offer.DeepClone();
            if (!wholesale && (merchant.isRegularSellDisable || (merchant.isMultiBuyDisabled && rows.Count > 1)))
                view["stage"] = "whole_batch_unavailable";
            else view["offer_token"] = offerToken;
            return view;
        }

        private string ActAtCounter(JObject request, JObject receipt)
        {
            if ((long)request["expires_ms"] < DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) return "expired";
            string action = (string)request["action"];
            int requestedRevision = (int)request["revision"];
            bool recheckDismiss = action == "dismiss" && requestedRevision > 0 && requestedRevision < sequence;
            if ((string)request["generation"] != generation || (requestedRevision != sequence && !recheckDismiss))
                return "stale_observation";
            if (action == "wild_network") return NetworkAction(request, receipt);
            if (WildNetworkUI.IsOpen && action != "visible_windows") return "wild_network_open";
            if (action == "inspect_window" || action == "transfer_slot" || action == "transfer_slots" || action == "press_control" ||
                action == "inspect_archive" || action == "close_archive")
                return WorkstationAction(request, receipt);
            if (action == "visible_windows") return ObserveTradeWindows(receipt);
            if (action == "barter") return ActOnBarter(request, receipt);
            if (action == "water_use") return WaterUseAction(request, receipt);
            if (action == "water_panel") return WaterPanelAction(request, receipt);
            if (action == "prepare_sleep" || action == "night_service") return ActOnBill(request, receipt);
            if (action == "response_choice" || action == "choose_dialogue") return SubmitDecision(request, receipt);
            if (action == "sleep") return SubmitSleep(request, receipt);
            if (action == "open_shop")
            {
                if (!CanOpenShop()) return "shutter_unavailable";
                StoreShutterButton.Instance.OnClick();
                var opened = PlayerStore.Instance.storeState == PlayerStore.StoreState.OPEN;
                receipt["phase"] = PlayerStore.Instance.storeState.ToString();
                if (opened)
                {
                    FileCounterPapers(receipt);
                    receipt["waiting"] = PlayerStore.Instance.GetClientInQueueCount();
                }
                return opened ? "opened" : "open_unconfirmed";
            }
            if (action == "phone_keypad" || action == "dial" || action == "phone_choice" || action == "phone_advance")
                return ActOnPhone(request, receipt);
            if (phase != "OPEN") return "shop_closed";
            var store = PlayerStore.Instance;
            if (action == "auto_receive" || action == "close_shop") return ActOnReception(request, receipt);
            if (action == "ring")
            {
                string blocked = RingBlocker(store, (int)request["waiting"]);
                if (blocked != null) return blocked;
                var bell = UnityEngine.Object.FindObjectOfType<StoreBell>();
                if (bell == null) return "bell_unavailable";
                bell.RingBell();
                ObserveVisit();
                receipt["visit"] = visitId;
                return visitId == null ? "ring_unconfirmed" : "called";
            }
            if (action == "advance") ObserveVisit();
            if (action == "dismiss")
            {
                ObserveVisit();
                receipt["revision_rechecked"] = recheckDismiss;
            }
            if (visitId == null || (string)request["visit"] != visitId) return "visit_changed";
            if (action == "pay_info") return PayInformationReceipt(request, receipt);
            if (action == "dismiss") return DismissCustomer(receipt);
            string stage = CounterStage();
            if (action == "advance")
            {
                if (stage != "advance_required") return stage;
                if ((string)CounterView()["page"] != (string)request["page"]) return "page_changed";
                DialogUIManager.Instance.OnAdvancePressed();
                return "advanced";
            }
            if (action == "tablet")
            {
                if (stage != "tablet_closed" || NegociationUIManager.Instance == null) return stage;
                NegociationUIManager.Instance.OpenUI();
                return "tablet_requested";
            }
            if (action != "buy") return "invalid_action";
            var view = CounterView();
            if ((string)view["stage"] != "ready") return (string)view["stage"];
            if (view["offer_token"] == null || (string)request["offer_token"] != offerToken) return "offer_changed";
            long total = (long)view["offer"]["total"];
            receipt["quote"] = total;
            receipt["max_total"] = (long)request["max_total"];
            if (total < 0 || total > (long)request["max_total"]) return "over_limit";
            int before = store.playerCash;
            if (total > before) return "insufficient_cash";
            var shop = EmporiumEntry.Instance;
            var items = shop.GetAllNonOwnedItem();
            var ui = NegociationUIManager.Instance;
            var cleared = new JArray();
            if (!merchant.isOfferWholesale && ClearNonOrderBackTable(null, cleared) > 0)
                FlushChanges();
            if (merchant.isOfferWholesale)
                ui.OnWholesaleClick();
            else
            {
                // The native buy-all button operates only on the back counter.
                foreach (var item in items)
                {
                    if (item.parentInventory == shop.backInvinvElement) continue;
                    if (item.parentInventory != shop.frontInvinvElement || !item.MayRemove())
                    {
                        DescribeBlockedTable(receipt, false);
                        return "placement_blocked";
                    }
                    ModHelper.MoveItemToBackCounter(item);
                    if (item.parentInventory != shop.backInvinvElement)
                    {
                        DescribeBlockedTable(receipt, false);
                        return "placement_failed";
                    }
                }
                long stagedTotal = 0;
                foreach (var item in items) stagedTotal += item.GetNegociatedValue();
                var stagedItems = shop.GetAllNonOwnedItem();
                if (stagedTotal != total || stagedItems.Count != items.Count || !items.TrueForAll(stagedItems.Contains) ||
                    !items.TrueForAll(item => item.parentInventory == shop.backInvinvElement) || CounterStage() != "ready")
                    return "offer_changed";
                if (store.playerCash < total) return "insufficient_cash";
                ui.OnBuyAllClick();
            }
            foreach (var item in items)
                if (GeneralHelper.IsItemOwned(item) && !IsHazardousWasteItem(item) &&
                    (item.parentInventory == shop.frontInvinvElement || item.parentInventory == shop.backInvinvElement))
                {
                    pendingPapers.Add(item);
                    papersDirty = true;
                }
            CloseTransactions();
            FileCounterPapers(receipt);
            if (cleared.Count > 0) receipt["cleared_items"] = cleared;
            foreach (var item in items) changed.Add(item);
            FlushChanges();
            receipt["cash_after"] = store.playerCash;
            receipt["paid"] = before - store.playerCash;
            receipt["items"] = view["offer"]["items"].DeepClone();
            return (long)receipt["paid"] == total && items.TrueForAll(GeneralHelper.IsItemOwned) ? "purchased" : "purchase_unconfirmed";
        }

        private void CheckCounter()
        {
            if (Time.realtimeSinceStartup < nextCounterPoll) return;
            nextCounterPoll = Time.realtimeSinceStartup + 0.15f;
            counterMailbox.Process("counter", generation, slot, (request, receipt) =>
            {
                try { return ActAtCounter(request, receipt); }
                finally
                {
                    FlushChanges();
                    receipt["revision"] = sequence;
                    receipt["load_scans"] = loadScans;
                    nextView = 0;
                    RefreshDesk();
                }
            });
        }
    }
}
