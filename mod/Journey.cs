using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace WhiteDragon.Ledger
{
    public sealed partial class InventoryObserver
    {
        private readonly CommandMailbox journeyMailbox = new CommandMailbox();
        private JObject journeyView;
        private string journeyToken;
        private float nextJourneyPoll;

        private bool CheckJourney()
        {
            if (sceneSession == null || Time.realtimeSinceStartup < nextJourneyPoll) return false;
            nextJourneyPoll = Time.realtimeSinceStartup + .15f;
            return journeyMailbox.Process("journey", sceneSession, PlayerStore.HasInstance ? PlayerStore.Instance.saveSlotId : slot, ActOnJourney);
        }

        private string ActOnJourney(JObject request, JObject receipt)
        {
            if ((long)request["expires_ms"] < DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) return "expired";
            receipt["session"] = sceneSession;
            receipt["revision"] = sceneRevision;
            if (IsSetupScene(sceneStatus)) receipt["slot"] = JValue.CreateNull();
            if ((string)request["generation"] != sceneSession || (int)request["revision"] != sceneRevision) return "scene_changed";
            string tool = (string)request["tool"];
            string action = (string)request["action"];
            if (tool == "feedback_box" && action == "read" && sceneStatus == "ready" && currentLocation == ShopScene)
                return ReadFeedbackBox((bool?)request["full"] == true, receipt);
            JObject view;
            NightInventory night = null;
            bool choosing = tool == "specializations" && sceneStatus == SpecializationPicker && NewGameSpecializationUI.IsOpen;
            bool perks = tool == "starting_perks" && sceneStatus == PerkPicker && NewGamePerksUI.IsOpen;
            bool difficulty = tool == "starting_difficulty" && sceneStatus == DifficultyPicker && NewGameDifficultyUI.IsOpen;
            if (difficulty)
            {
                if (!StartingDifficulty.Supported) return "unsupported_layout";
                view = StartingDifficulty.View();
            }
            else if (perks)
            {
                if (!StartingPerks.Supported) return "unsupported_layout";
                view = StartingPerks.View();
            }
            else if (choosing)
                view = Specializations.View();
            else if (tool == "shop_activity" && sceneStatus == "ready" && currentLocation == ShopScene)
                view = ShopActivityView();
            else if (tool == "travel_menu" && sceneStatus == Picker && TransitUI.IsOpen)
            {
                TransitCardView first = null;
                view = TravelOptions.Read(ref first, null);
                if ((string)view["status"] != "visible") return "unsupported_layout";
            }
            else if (tool == "explore" && sceneStatus == "ready" && MapUIManager.Instance.Current?.Id == currentLocation)
            {
                night = new NightInventory();
                view = night.View;
            }
            else return "unavailable_in_scene";

            if (action == "read")
            {
                // Each observation authorizes at most one action, even if the native UI changes asynchronously.
                if (journeyToken == null || !JToken.DeepEquals(view, journeyView)) journeyToken = Guid.NewGuid().ToString("N");
                journeyView = view;
                receipt["view_token"] = journeyToken;
                if ((string)request["view_token"] == journeyToken) return "unchanged";
                receipt["view"] = view.DeepClone();
                return "observed";
            }
            if (journeyToken == null || (string)request["view_token"] != journeyToken || !JToken.DeepEquals(view, journeyView))
                return "view_changed";
            journeyToken = null;
            if (difficulty) return StartingDifficulty.Select(action, (string)request["difficulty_id"], receipt);
            if (perks)
            {
                string status = StartingPerks.Act(action, (string)request["perk_id"], view, receipt);
                if (NewGamePerksUI.IsOpen)
                {
                    journeyView = StartingPerks.View();
                    journeyToken = Guid.NewGuid().ToString("N");
                    receipt["view_token"] = journeyToken;
                    receipt["state"] = journeyView["state"].DeepClone();
                }
                return status;
            }
            if (choosing) return Specializations.Choose(action, (string)request["scenario_id"], receipt);
            if (Interactable.isPaused || Time.timeScale <= 0f) return "paused";
            if (night != null) return ActOutside(night, request, receipt);
            if (tool == "travel_menu")
            {
                string result = TravelOptions.Act(action, (string)request["location_id"], receipt);
                if (result == "departure_requested" && sceneStatus == Picker && MapUIManager.Instance.IsInTransit)
                    StartTravel((string)receipt["destination"]);
                return result;
            }
            return ActInShop(action, view, request);
        }

    }
}
