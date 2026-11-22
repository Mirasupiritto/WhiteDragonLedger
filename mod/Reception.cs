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

        private string RingBlocker(PlayerStore store, int? expectedWaiting = null)
        {
            if (store.storeState != PlayerStore.StoreState.OPEN) return "shop_closed";
            if (WildNetworkUI.IsOpen) return "wild_network_open";
            if (ResponseInventory() != null) return "response_required";
            if (store.currentClientInstance != null) return "occupied";
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

        private bool CanDismissCustomer(PlayerStore store)
        {
            if (ResponseInventory() != null || WildNetworkUI.IsOpen || phase != "OPEN" || visit == null || store.currentClientInstance != visit ||
                !store.isClientArrived || store.lockNextBellFlag > 0 || !store.IsClientDimissable() ||
                Interactable.isPaused || order != null || store.isClientBeingArrested || store.isHandlingShootout)
                return false;
            var dialog = DialogUIManager.Instance;
            if (dialog != null && (dialog.GetCurrentDialog() != null || dialog.IsChoice() ||
                dialog.HasPausedConversation || dialog.isWaitingForManualAdvance)) return false;
            return !StoreUIManager.Instance.isAboutToStartMainDialog && !NegociationUIManager.IsTradeLocked() &&
                !EmporiumEntry.Instance.backInvinvElement.childItems.Exists(GeneralHelper.IsItemOwned);
        }

        private string DismissCustomer(JObject receipt)
        {
            var store = PlayerStore.Instance;
            if (!CanDismissCustomer(store)) return "dismiss_unavailable";
            var departing = visit;
            receipt["visit"] = visitId;
            receipt["customer"] = merchant.displayName;
            store.TryPlayerDismissCurrentClient(true);
            ObserveVisit();
            receipt["waiting"] = store.GetClientInQueueCount();
            return store.currentClientInstance != departing ? "dismissed" : "dismiss_intercepted";
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
            return new JObject { ["token"]=receptionToken, ["waiting"]=waiting, ["auto_receive"]=automatic,
                ["close_prompt"]=prompt, ["can_ring"]=RingBlocker(store) == null, ["can_close_shop"]=available, ["can_dismiss"]=CanDismissCustomer(store) };
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
            if ((string)request["reception_token"] != receptionToken) return "reception_changed";
            if ((string)request["action"] == "auto_receive")
            {
                bool enable = (bool)request["enabled"];
                if (enable && ClosePrompt) return "close_prompt_open";
                autoReceiveDay = enable ? StoreStation.Instance.dayCounter : 0;
                receipt["reception"] = ReceptionView();
                return enable ? "auto_receive_enabled" : "auto_receive_disabled";
            }
            if (!CanCloseShop()) return "shutter_unavailable";
            var shutter = StoreShutterButton.Instance;
            var confirmed = (int?)request["confirm_remaining"];
            if (confirmed.HasValue && (!ClosePrompt || confirmed.Value != waiting)) return "confirmation_changed";
            autoReceiveDay = 0;
            if (confirmed.HasValue)
                shutter.EndAndClose();
            else if (!ClosePrompt)
                shutter.OnClick();
            receipt["reception"] = ReceptionView();
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
