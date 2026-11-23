using System;
using Newtonsoft.Json.Linq;

namespace WhiteDragon.Ledger
{
    public sealed partial class InventoryObserver
    {
        private const string ShopScene = "pawnshop";
        private string sceneSession, sceneStatus, currentLocation, destination;
        private string currentTitle, destinationTitle;
        private int sceneRevision;
        private bool awaitingLateHook, shopBoundary;
        private JObject lastJourney, travelOptions;
        private TransitCardView firstTravelCard;
        private const string DifficultyPicker = "selecting_difficulty";
        private const string PerkPicker = "selecting_perks";
        private const string SpecializationPicker = "selecting_specialization";
        private const string Picker = "selecting_destination";

        private static bool IsSetupScene(string status) =>
            status == SpecializationPicker || status == PerkPicker || status == DifficultyPicker;

        private void ResetScene()
        {
            sceneSession = Guid.NewGuid().ToString("N");
            sceneRevision = 0;
            currentLocation = currentTitle = destination = destinationTitle = null;
            lastJourney = null;
            awaitingLateHook = shopBoundary = false;
            PublishScene("waiting_for_load");
        }

        private void PublishScene(string status)
        {
            journeyToken = null;
            sceneStatus = status;
            if (status != Picker)
            {
                firstTravelCard = null;
                travelOptions = null;
            }
            var receipt = new JObject {
                ["scratch_card_protocol"] = 2, ["feedback_protocol"] = 1, ["journey_protocol"] = 1, ["session"] = sceneSession, ["revision"] = ++sceneRevision, ["status"] = status,
                ["location"] = currentLocation, ["title"] = currentTitle,
                ["tool_region"] = status == DifficultyPicker ? "new_game_difficulty" : status == PerkPicker ? "new_game_perks" : status == SpecializationPicker ? "new_game" : status == Picker ? "destination_selection" : status == "ready" ? (currentLocation == ShopScene ? "pawnshop" : "afterhours") : "transition",
                ["destination"] = destination, ["destination_title"] = destinationTitle,
                ["slot"] = PlayerStore.HasInstance && status != "waiting_for_load" && !IsSetupScene(status) ? (JToken)PlayerStore.Instance.saveSlotId : JValue.CreateNull(),
                ["last_transition"] = lastJourney?.DeepClone(),
                ["message"] = status == DifficultyPicker ? "新遊戲：選擇難度" : status == PerkPicker ? "新遊戲：選擇初始特性" : status == SpecializationPicker ? "新遊戲：選擇專精" : status == Picker ? "正在選擇夜間目的地" : status == "ready" ? "目前位於" + currentTitle :
                    status == "transitioning" ? (destinationTitle == null ? "正在轉場" : "前往" + destinationTitle) : "等待讀檔"
            };
            if (status == Picker) receipt["travel_options"] = travelOptions;
            Write("scene.json", receipt);
        }

        private void StartTravel(string target)
        {
            if (sceneStatus == "transitioning" && destination == target) return;
            if (currentLocation == ShopScene) shopBoundary = true;
            destination = target;
            destinationTitle = target == ShopScene ? "當鋪" : target == null ? null : AfterhourLocation.Get(target)?.GetTitle() ?? target;
            if (target != null)
                lastJourney = new JObject { ["from"] = currentLocation, ["to"] = target,
                    ["message"] = "前往" + destinationTitle };
            PublishScene("transitioning");
        }

        private void OnLeaveEarly(string id) { awaitingLateHook = true; StartTravel(ShopScene); }
        private void OnArriveEarly(string id) { awaitingLateHook = true; StartTravel(id); }
        private void OnTravelLate(string id) { awaitingLateHook = false; }
        private void OnLeaveAfterhoursLate(string id)
        {
            OnTravelLate(id);
            if (PlayerStore.HasInstance && PlayerStore.Instance.currentClientInstance == null && order == null)
                QueueCounterPapers();
        }

        // Run before any store observer, inventory read or command dispatch.
        private bool CanObserveStore()
        {
            string setup = NewGameDifficultyUI.IsOpen ? DifficultyPicker : NewGamePerksUI.IsOpen ? PerkPicker : NewGameSpecializationUI.IsOpen ? SpecializationPicker : null;
            if (setup != null)
            {
                if (sceneStatus != setup)
                {
                    currentLocation = currentTitle = destination = destinationTitle = null;
                    PublishScene(setup);
                }
                return false;
            }
            if (IsSetupScene(sceneStatus)) ResetScene();
            if (!PlayerStore.HasInstance)
            {
                if (sceneStatus != "waiting_for_load") ResetScene();
                return false;
            }
            if (!pending && generation == null) return false;
            var map = MapUIManager.Instance;
            if (map == null)
            {
                if (sceneStatus != "waiting_for_load") ResetScene();
                return false;
            }
            if (map.IsInTransit)
            {
                if (sceneStatus != "transitioning")
                {
                    string selected = sceneStatus == Picker ? (string)travelOptions?["selected_id"] : null;
                    StartTravel(selected ?? (map.Current == null ? null : ShopScene));
                }
                return false;
            }
            if (awaitingLateHook) return false;
            if (sceneStatus == "transitioning" || sceneStatus == "waiting_for_load")
            {
                var fade = StoreUIManager.Instance?.blackscreen;
                if (fade != null && fade.gameObject.activeInHierarchy) return false;
            }
            var outside = map.Current;
            if (TransitUI.IsOpen && sceneStatus != "transitioning")
            {
                var options = TravelOptions.Read(ref firstTravelCard, travelOptions);
                if (options != travelOptions)
                {
                    currentLocation = outside == null ? ShopScene : outside.Id;
                    currentTitle = outside == null ? "當鋪" : outside.GetTitle();
                    travelOptions = options;
                    PublishScene(Picker);
                }
                return false;
            }
            if (outside != null)
            {
                if (destination != null && destination != outside.Id) return false;
                if (sceneStatus != "ready" || currentLocation != outside.Id) MarkSceneReady(outside.Id, outside.GetTitle());
                return false;
            }
            return destination == null || destination == ShopScene;
        }

        private void MarkSceneReady(string id, string title)
        {
            if (sceneStatus == "ready" && currentLocation == id) return;
            currentLocation = id;
            currentTitle = title;
            destination = destinationTitle = null;
            PublishScene("ready");
        }

        private void InvalidatePreviousShopVisit()
        {
            if (shopBoundary)
            {
                // Invalidate commands based on the previous stay, even if stock was unchanged.
                Publish(new JObject { ["op"] = "delta", ["upsert"] = new JArray(), ["remove"] = new JArray() });
                nextView = 0;
                shopBoundary = false;
            }

        }
    }
}
