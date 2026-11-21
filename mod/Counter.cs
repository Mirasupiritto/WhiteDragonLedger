using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace WhiteDragon.Ledger
{
    public sealed partial class InventoryObserver
    {
        private float nextCounterPoll;
        private JObject offerSnapshot;
        private string offerToken, lastCounterCommand, counterVisit;
        private JObject lastCounterReceipt;
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

        private string CounterStage()
        {
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
            if (!merchant.IsClientSelling()) return "not_selling";
            var ui = NegociationUIManager.Instance;
            if (ui == null || !ui.IsOpen) return "tablet_closed";
            var frame = UnityEngine.Object.FindObjectOfType<NegotiationTabletFrame>();
            if (frame != null && frame.confirmRoot != null && frame.confirmRoot.activeInHierarchy) return "confirmation_required";
            return "ready";
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
            var view = new JObject { ["stage"] = stage, ["can_open_shop"] = CanOpenShop(), ["cash"] = store.playerCash, ["visit"] = visitId,
                ["customer"] = merchant?.displayName, ["lines"] = openingLines.DeepClone() };
            if (stage == "advance_required")
            {
                var page = DialogUIManager.Instance.GetCurrentDialog();
                if (page != lastPage || pageToken == null) pageToken = Guid.NewGuid().ToString("N");
                lastPage = page;
                view["page"] = pageToken;
            }
            else { lastPage = null; pageToken = null; }
            if (phase == "OPEN") view["waiting"] = store.GetClientInQueueCount();
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
                if (BottleLabel(item) is string label) row["bottle_label"] = label;
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
            if ((string)request["generation"] != generation || (int)request["revision"] != sequence) return "stale_observation";
            string action = (string)request["action"];
            if (action == "open_shop")
            {
                if (!CanOpenShop()) return "shutter_unavailable";
                StoreShutterButton.Instance.OnClick();
                var opened = PlayerStore.Instance.storeState == PlayerStore.StoreState.OPEN;
                receipt["phase"] = PlayerStore.Instance.storeState.ToString();
                if (opened) receipt["waiting"] = PlayerStore.Instance.GetClientInQueueCount();
                return opened ? "opened" : "open_unconfirmed";
            }
            if (action == "dial" || action == "phone_choice" || action == "phone_advance")
                return ActOnPhone(request, receipt);
            if (phase != "OPEN") return "shop_closed";
            var store = PlayerStore.Instance;
            if (action == "ring")
            {
                if (store.currentClientInstance != null) return "occupied";
                if (store.GetClientInQueueCount() != (int)request["waiting"]) return "queue_changed";
                if (store.lockNextBellFlag > 0 || !store.CanNextClient()) return "bell_unavailable";
                var bell = UnityEngine.Object.FindObjectOfType<StoreBell>();
                if (bell == null) return "bell_unavailable";
                bell.RingBell();
                ObserveVisit();
                receipt["visit"] = visitId;
                return visitId == null ? "ring_unconfirmed" : "called";
            }
            if (visitId == null || (string)request["visit"] != visitId) return "visit_changed";
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
            if (merchant.isOfferWholesale)
                ui.OnWholesaleClick();
            else
            {
                // The native buy-all button operates only on the back counter.
                foreach (var item in items)
                {
                    if (item.parentInventory == shop.backInvinvElement) continue;
                    if (item.parentInventory != shop.frontInvinvElement || !item.MayRemove()) return "placement_blocked";
                    ModHelper.MoveItemToBackCounter(item);
                    if (item.parentInventory != shop.backInvinvElement) return "placement_failed";
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
            CloseTransactions();
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
            string file = "counter-" + generation + ".json";
            if (!ModHelper.ModDataFileExists(ModId, file)) return;
            var request = JObject.Parse(ModHelper.ReadModDataFileText(ModId, file));
            string id = (string)request["command_id"];
            if (Guid.TryParseExact(id, "N", out var key) && id == key.ToString("N") &&
                !ModHelper.ModDataFileExists(ModId, "receipt-" + id + ".json"))
            {
                if (id != lastCounterCommand)
                {
                    lastCounterCommand = id;
                    lastCounterReceipt = new JObject { ["generation"] = generation, ["slot"] = slot,
                        ["command_id"] = id, ["status"] = "outcome_unknown" };
                    try { lastCounterReceipt["status"] = ActAtCounter(request, lastCounterReceipt); }
                    catch (Exception error) { Debug.LogException(error); }
                    FlushChanges();
                    lastCounterReceipt["revision"] = sequence;
                    lastCounterReceipt["load_scans"] = loadScans;
                    nextView = 0;
                    RefreshDesk();
                }
                Write("receipt-" + id + ".json", lastCounterReceipt);
            }
            ModHelper.DeleteModDataFile(ModId, file);
        }
    }
}
