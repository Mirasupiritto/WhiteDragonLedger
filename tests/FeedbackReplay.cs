using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace WhiteDragon.Ledger
{
    public partial class PlayerStore
    {
        public List<StoreReputation> storeReputations = new List<StoreReputation>();
    }

    public class StoreReputationPerk
    {
        public string perkId;
        public int perkThreshold;
        public bool isAlwaysActive, isUnlocked;
        public string GetLocalizedPerkName() => perkId;
        public string GetLocalizedDescription() => "effect " + perkId;
    }

    public class StoreReputation
    {
        public string factionId, factionDisplay;
        public double amount;
        public List<StoreReputationPerk> perks = new List<StoreReputationPerk>();
        public int GetReputation() => (int)amount;
        public double GetReputationExact() => amount;
        public static string GetReputationStatus(int value) => value >= 0 ? "neutral" : "disliked";
        public static void UpdateReputation()
        {
            foreach (var faction in PlayerStore.Instance.storeReputations)
                foreach (var perk in faction.perks)
                    perk.isUnlocked = perk.perkThreshold < 0 ? faction.amount <= perk.perkThreshold : faction.amount >= perk.perkThreshold;
        }
    }

    public sealed partial class InventoryObserver
    {
        private void ReplayFeedback()
        {
            var original = generation;
            generation = "feedback-load";
            var store = PlayerStore.Instance;
            var faction = new StoreReputation { factionId="lower", factionDisplay="Lower", amount=2.25 };
            faction.perks.Add(new StoreReputationPerk { perkId="bonus", perkThreshold=2 });
            faction.perks.Add(new StoreReputationPerk { perkId="penalty", perkThreshold=-2 });
            faction.perks.Add(new StoreReputationPerk { perkId="permanent", perkThreshold=10, isAlwaysActive=true });
            store.storeReputations.Add(faction);
            MarkSceneReady(ShopScene,"當鋪");
            var request = JourneyRequest("feedback_box");
            var receipt = new JObject();
            string Read()
            {
                receipt = new JObject();
                return ActOnJourney(request, receipt);
            }
            Expect(Read()=="baseline" && (string)receipt["factions"][0]["reputation"]=="2.25" &&
                (bool)receipt["factions"][0]["effects"][2]["active"], "feedback baseline");
            Expect(Read()=="unchanged" && receipt["factions"]==null, "no repeated catalog");
            faction.amount = -2.75;
            Expect(Read()=="delta" && (string)receipt["changes"][0]["previous_reputation"]=="2.25" &&
                ((JArray)receipt["changes"][0]["effects"]).Count==2 &&
                !(bool)receipt["changes"][0]["effects"][0]["active"] &&
                (bool)receipt["changes"][0]["effects"][1]["active"], "negative threshold activated, positive disabled");
            faction.amount = -2.85;
            Expect(Read()=="delta" && receipt["changes"][0]["effects"]==null, "fractional score only");
            generation = "different-load";
            Expect(Read()=="baseline", "load starts independent baseline");
            request["full"] = true;
            Expect(Read()=="baseline", "manual full read");
            generation = original;
            store.storeReputations.Remove(faction);
            Console.WriteLine("Feedback box replay passed.");
        }
    }
}
