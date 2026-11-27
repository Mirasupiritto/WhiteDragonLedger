using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace WhiteDragon.Ledger
{
    public sealed partial class InventoryObserver
    {
        private int autoReceiveDay;
        private float nextReceptionCheck;
        private string receptionToken;
        private (string load, int day, int waiting, bool prompt, bool available, bool automatic, string visit) receptionState;

        private bool ClosePrompt => StoreUIManager.Instance.closeShutterConfirmationPanel.activeInHierarchy;

        private static bool CustomerDeparting(PlayerStore store) =>
            store.currentClientInstance == null && StoreUIManager.Instance.clientGameObject?.gameObject.activeInHierarchy == true;

        private void DescribeHandoff(JObject receipt)
        {
            var store = PlayerStore.Instance;
            if (store.currentClientInstance != null)
            {
                var next = new JObject { ["stage"] = CounterStage(), ["visit"] = visitId, ["customer"] = merchant?.displayName,
                    ["lines"] = openingLines.DeepClone() };
                receipt["next_customer"] = next;
                receipt["next_action"] = next["stage"].ToString() == "arriving" ? "新客正在進場，等候開場。" : "接續這位客人的當前狀態。";
            }
            else if (CustomerDeparting(store))
            {
                receipt["next_stage"] = "customer_departing";
                receipt["next_action"] = "上一位客人正在離場，等候即可。";
            }
            else if (store.GetClientInQueueCount() == 0)
                receipt["next_action"] = "已無客人排隊。";
            else if (RingBlocker(store) == null)
                receipt["next_action"] = "可以接下一位客人。";
        }

        private string RingBlocker(PlayerStore store, int? expectedWaiting = null)
        {
            if (store.storeState != PlayerStore.StoreState.OPEN) return "shop_closed";
            if (WildNetworkUI.IsOpen) return "wild_network_open";
            if (ResponseInventory() != null) return "response_required";
            if (store.currentClientInstance != null) return "occupied";
            if (CustomerDeparting(store)) return "customer_departing";
            if (expectedWaiting.HasValue && store.GetClientInQueueCount() != expectedWaiting.Value) return "queue_changed";
            if (store.lockNextBellFlag > 0 || !store.CanNextClient()) return "bell_unavailable";
            return null;
        }

        private bool CanCloseShop()
        {
            var store = PlayerStore.Instance;
            return ResponseInventory() == null && !WildNetworkUI.IsOpen && store.storeState == PlayerStore.StoreState.OPEN && StoreShutterButton.Instance != null &&
                store.lockShutterButtonFlag <= 0 && !store.isClientBeingArrested && !store.isHandlingShootout &&
                store.currentClientInstance == null && EmporiumEntry.Instance.backInvinvElement.childItems.Count == 0 &&
                store.CanEndDay();
        }

        private bool CanDismissCustomer(PlayerStore store, bool interrupt = false)
        {
            if (ResponseInventory() != null || WildNetworkUI.IsOpen || phase != "OPEN" || visit == null || store.currentClientInstance != visit ||
                !store.isClientArrived || store.lockNextBellFlag > 0 || !store.IsClientDimissable() ||
                Interactable.isPaused || order != null || staged != null || stagedBundle.Count > 0 ||
                store.isClientBeingArrested || store.isHandlingShootout)
                return false;
            var dialog = DialogUIManager.Instance;
            if (dialog != null && dialog.IsChoice()) return false;
            if (!interrupt)
            {
                if (dialog != null && (dialog.GetCurrentDialog() != null ||
                    dialog.HasPausedConversation || dialog.isWaitingForManualAdvance)) return false;
                if (StoreUIManager.Instance.isAboutToStartMainDialog) return false;
            }
            return !NegociationUIManager.IsTradeLocked();
        }

        private string VisitMismatchStatus(JObject receipt = null)
        {
            if (receipt != null)
            {
                receipt["visit"] = visitId;
                if (merchant != null) receipt["customer"] = merchant.displayName;
            }
            return visitId == null ? "customer_left" : "customer_replaced";
        }

        private string DismissBlockerReason(PlayerStore store, bool interrupt = false)
        {
            if (WildNetworkUI.IsOpen) return "wild_network_open";
            if (ResponseInventory() != null) return "response_required";
            if (phase != "OPEN") return "shop_closed";
            if (visit == null || store.currentClientInstance != visit)
                return store.currentClientInstance == null ? "customer_left" : "customer_replaced";
            if (!store.isClientArrived) return "client_arriving";
            if (store.isClientBeingArrested) return "client_being_arrested";
            if (store.isHandlingShootout) return "shootout_in_progress";
            if (store.lockNextBellFlag > 0) return "bell_locked";
            if (!store.IsClientDimissable()) return "client_not_dismissable";
            if (order != null || staged != null || stagedBundle.Count > 0) return "order_in_progress";
            var dialog = DialogUIManager.Instance;
            if (dialog != null && dialog.IsChoice()) return "dialogue_in_progress";
            if (!interrupt && ((dialog != null && (dialog.GetCurrentDialog() != null ||
                dialog.HasPausedConversation || dialog.isWaitingForManualAdvance)) ||
                StoreUIManager.Instance.isAboutToStartMainDialog))
                return "dialogue_in_progress";
            if (Interactable.isPaused) return "paused";
            if (NegociationUIManager.IsTradeLocked()) return "trade_locked";
            return "dismiss_unavailable";
        }

        private void DescribeDismissBlock(PlayerStore store, JObject target, bool interrupt = false)
        {
            string reason = DismissBlockerReason(store, interrupt);
            target["reason"] = reason;
            if (reason == "client_not_dismissable") target["reason_detail"] = "story_npc";
            var rows = new JArray();
            var shop = EmporiumEntry.Instance;
            foreach (var item in shop.backInvinvElement.childItems)
            {
                if (!GeneralHelper.IsItemOwned(item) || !IsActiveOrderItem(item)) continue;
                rows.Add(new JObject { ["id"]=LocalId(item), ["kind"]=item.identifier, ["area"]="back" });
            }
            target["blocking_items"] = rows;
        }

        private string DismissCustomer(JObject request, JObject receipt)
        {
            var store = PlayerStore.Instance;
            bool interrupt = (bool?)request["interrupt"] == true;
            if (order == null && !Interactable.isPaused && !NegociationUIManager.IsTradeLocked() &&
                store.lockAllInventoryFlag == 0)
            {
                var cleared = new JArray();
                int moved = ClearCounterInventoryToShop(EmporiumEntry.Instance.frontInvinvElement, "front", null,
                    i => IsCounterClutter(i, true), cleared);
                moved += ClearNonOrderBackTable(null, cleared);
                if (moved > 0)
                {
                    FlushChanges();
                    receipt["cleared_items"] = cleared;
                }
            }
            if (!CanDismissCustomer(store, interrupt))
            {
                DescribeDismissBlock(store, receipt, interrupt);
                receipt["visit"] = visitId;
                if (merchant != null) receipt["customer"] = merchant.displayName;
                var rec = ReceptionView();
                receipt["reception"] = rec;
                receipt["reception_token"] = rec["token"];
                receipt["waiting"] = rec["waiting"];
                return "dismiss_unavailable";
            }
            var departing = visit;
            string latestLine = openingLines.Count > 0 ? (string)openingLines[openingLines.Count - 1] : null;
            receipt["visit"] = visitId;
            receipt["customer"] = merchant.displayName;
            if (interrupt)
            {
                receipt["interrupted"] = true;
                if (latestLine != null) receipt["latest_line"] = latestLine;
            }
            store.TryPlayerDismissCurrentClient(true);
            ObserveVisit();
            var after = ReceptionView();
            receipt["reception"] = after;
            receipt["reception_token"] = after["token"];
            receipt["waiting"] = after["waiting"];
            receipt["can_ring"] = after["can_ring"];
            receipt["can_close_shop"] = after["can_close_shop"];
            if (store.currentClientInstance == departing) return "dismiss_intercepted";
            DescribeHandoff(receipt);
            return "dismissed";
        }

        private JObject ReceptionView()
        {
            var store = PlayerStore.Instance;
            int day = StoreStation.Instance.dayCounter;
            bool open = store.storeState == PlayerStore.StoreState.OPEN;
            int waiting = open ? store.GetClientInQueueCount() : 0;
            bool prompt = open && ClosePrompt;
            bool available = open && CanCloseShop();
            bool automatic = open && autoReceiveDay == day;
            var stamp = (generation, day, waiting, prompt, available, automatic, visitId);
            if (receptionToken == null || !receptionState.Equals(stamp))
            {
                receptionState = stamp;
                receptionToken = Guid.NewGuid().ToString("N");
            }
            var view = new JObject { ["token"]=receptionToken, ["waiting"]=waiting, ["auto_receive"]=automatic,
                ["close_prompt"]=prompt, ["can_ring"]=RingBlocker(store) == null, ["can_close_shop"]=available, ["can_dismiss"]=CanDismissCustomer(store) };
            if (visitId != null && !(bool)view["can_dismiss"]) DescribeDismissBlock(store, view);
            return view;
        }

        private void TickReception()
        {
            if (autoReceiveDay == 0) return;
            var store = PlayerStore.Instance;
            if (store.storeState != PlayerStore.StoreState.OPEN || autoReceiveDay != StoreStation.Instance.dayCounter)
            {
                autoReceiveDay = 0;
                return;
            }
            if (Time.realtimeSinceStartup < nextReceptionCheck) return;
            nextReceptionCheck = Time.realtimeSinceStartup + 0.25f;
            if (RingBlocker(store) != null || ClosePrompt || Interactable.isPaused || StoreUIManager.Instance.IsTyping() ||
                StoreUIManager.Instance.isAboutToStartMainDialog || EmporiumEntry.Instance.backInvinvElement.childItems.Count > 0) return;
            var talk = DialogUIManager.Instance;
            if (talk != null && (talk.GetCurrentDialog() != null || talk.HasPausedConversation || talk.IsChoice())) return;
            store.TryCallNextClient();
            ObserveVisit();
            nextView = 0;
        }

        private string ActOnReception(JObject request, JObject receipt)
        {
            var view = ReceptionView();
            int waiting = (int)view["waiting"];
            receipt["waiting"] = waiting;
            receipt["reception"] = view;
            receipt["reception_token"] = view["token"];
            if ((string)request["reception_token"] != receptionToken) return "reception_changed";
            if ((string)request["action"] == "auto_receive")
            {
                bool enable = (bool)request["enabled"];
                if (enable && ClosePrompt) return "close_prompt_open";
                autoReceiveDay = enable ? StoreStation.Instance.dayCounter : 0;
                var afterAuto = ReceptionView();
                receipt["reception"] = afterAuto;
                receipt["reception_token"] = afterAuto["token"];
                return enable ? "auto_receive_enabled" : "auto_receive_disabled";
            }
            if (!CanCloseShop())
            {
                if (PlayerStore.Instance.storeState == PlayerStore.StoreState.OPEN &&
                    PlayerStore.Instance.currentClientInstance == null &&
                    EmporiumEntry.Instance.backInvinvElement.childItems.Count > 0)
                    DescribeBlockedTable(receipt, false);
                return "shutter_unavailable";
            }
            var shutter = StoreShutterButton.Instance;
            var confirmed = (int?)request["confirm_remaining"];
            if (confirmed.HasValue && (!ClosePrompt || confirmed.Value != waiting)) return "confirmation_changed";
            autoReceiveDay = 0;
            if (confirmed.HasValue)
                shutter.EndAndClose();
            else if (!ClosePrompt)
                shutter.OnClick();
            var afterClose = ReceptionView();
            receipt["reception"] = afterClose;
            receipt["reception_token"] = afterClose["token"];
            receipt["phase"] = PlayerStore.Instance.storeState.ToString();
            if (PlayerStore.Instance.storeState == PlayerStore.StoreState.CLOSED)
            {
                receipt["forfeited"] = waiting;
                return "closed";
            }
            return ClosePrompt ? "confirm_early_close" : "close_unconfirmed";
        }
    }
}
