using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace WhiteDragon.Ledger
{
    public sealed partial class InventoryObserver
    {
        private JObject ShopActivityView()
        {
            ObserveVisit();
            var store = PlayerStore.Instance;
            var desk = StoreUIManager.Instance;
            var door = UnityEngine.Object.FindObjectOfType<StoreDoor>();
            var prop = UnityEngine.Object.FindObjectOfType<Shotgun>();
            bool packing = MapUIManager.Instance.IsShowingGoOutsideConfirm();
            bool report = AdvCalendarUIManager.Instance?.UIPanel?.activeInHierarchy == true;
            bool idle = store.lockAllFlag == 0 && store.lockStorePhysicalFlag == 0 && !store.IsClientPresent() &&
                !WildNetworkUI.IsOpen && PhoneUIManager.Instance?.phonePanel.activeInHierarchy != true && ResponseInventory() == null;
            return new JObject {
                ["can_request_door"] = door != null && idle && !report && !packing && store.storeState == PlayerStore.StoreState.CLOSED &&
                    store.isAfterhoursIntroduced && store.visitLeftTonight > 0 && EmporiumEntry.Instance.backInvinvElement.childItems.Count == 0,
                ["packing_confirmation"] = packing, ["report_open"] = report, ["visit"] = visitId, ["customer"] = merchant?.displayName,
                ["aiming"] = desk.isShootingMode, ["can_equip"] = visitId != null && !desk.isShootingMode &&
                    store.lockAllFlag == 0 && store.lockStorePhysicalFlag == 0 && prop != null && prop.gameObject.activeInHierarchy && prop.CanPickup(),
                ["can_holster"] = desk.isShootingMode && !desk.isShot,
                ["can_fire"] = visitId != null && desk.isShootingMode && desk.shootingUI.activeInHierarchy && desk.CanShoot()
            };
        }

        private void AttachResponseChoicesIfOpen(JObject receipt)
        {
            if (ResponseInventory() == null) return;
            ObserveDecision("response_required", receipt);
            if (generation != null && deskFile != null)
            {
                receipt["shop_generation"] = generation;
                receipt["shop_revision"] = sequence;
                nextView = 0;
                RefreshDesk();
            }
        }

        private string ActInShop(string action, JObject view, JObject request, JObject receipt)
        {
            switch (action)
            {
                case "door":
                    if (!(bool)view["can_request_door"]) return "door_unavailable";
                    UnityEngine.Object.FindObjectOfType<StoreDoor>().OnClick();
                    AttachResponseChoicesIfOpen(receipt);
                    return "door_requested";
                case "confirm_packing":
                    if (!(bool)view["packing_confirmation"]) return "no_confirmation";
                    MapUIManager.Instance.OnGoOutsideConfirm();
                    AttachResponseChoicesIfOpen(receipt);
                    return "packing_requested";
                case "close_report":
                    if (!(bool)view["report_open"]) return "report_closed";
                    AdvCalendarUIManager.Instance.CloseUI();
                    return "close_requested";
            }
            if (visitId == null || (string)request["visit"] != visitId) return "visit_changed";
            switch (action)
            {
                case "equip":
                    if (!(bool)view["can_equip"]) return "equip_unavailable";
                    UnityEngine.Object.FindObjectOfType<Shotgun>().OnClick();
                    return "equip_requested";
                case "holster":
                    if (!(bool)view["can_holster"]) return "holster_unavailable";
                    StoreUIManager.Instance.ExitShootingMode();
                    return "holster_requested";
                case "fire":
                    if ((bool?)request["confirm_fire"] != true || !(bool)view["can_fire"]) return "fire_unavailable";
                    StoreUIManager.Instance.Shoot();
                    return "fire_requested";
                default: return "unknown_action";
            }
        }
    }
}
