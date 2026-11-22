using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace WhiteDragon.Ledger
{
    public sealed partial class InventoryObserver
    {
        private sealed class FactionReading
        {
            public string Score, Status;
            public readonly Dictionary<string, bool> Effects = new Dictionary<string, bool>();
        }

        private string feedbackLoad;
        private Dictionary<string, FactionReading> previousFeedback;

        private static JObject FeedbackEffect(StoreReputationPerk perk, bool active)
        {
            return new JObject { ["id"] = perk.perkId, ["name"] = perk.GetLocalizedPerkName(),
                ["description"] = perk.GetLocalizedDescription(), ["threshold"] = perk.perkThreshold,
                ["always_active"] = perk.isAlwaysActive, ["active"] = active };
        }

        private string ReadFeedbackBox(bool full, JObject receipt)
        {
            var factions = PlayerStore.Instance.storeReputations;
            if (factions == null || generation == null) return "feedback_unavailable";
            // The feedback panel refreshes these flags before rendering each faction.
            StoreReputation.UpdateReputation();
            bool baseline = full || previousFeedback == null || feedbackLoad != generation;
            var latest = new Dictionary<string, FactionReading>();
            var rows = new JArray();
            foreach (var faction in factions)
            {
                var current = new FactionReading {
                    Score = faction.GetReputationExact().ToString("F2", CultureInfo.InvariantCulture),
                    Status = StoreReputation.GetReputationStatus(faction.GetReputation())
                };
                latest.Add(faction.factionId, current);
                FactionReading before = null;
                if (!baseline) previousFeedback.TryGetValue(faction.factionId, out before);
                var row = new JObject { ["id"] = faction.factionId, ["name"] = faction.factionDisplay };
                if (before == null || before.Score != current.Score || before.Status != current.Status)
                {
                    row["reputation"] = current.Score;
                    row["standing"] = current.Status;
                    if (before != null) row["previous_reputation"] = before.Score;
                }
                var effects = new JArray();
                foreach (var perk in faction.perks)
                {
                    bool active = perk.isAlwaysActive || perk.isUnlocked;
                    current.Effects.Add(perk.perkId, active);
                    if (before == null || !before.Effects.TryGetValue(perk.perkId, out bool wasActive))
                        effects.Add(FeedbackEffect(perk, active));
                    else if (active != wasActive)
                        effects.Add(new JObject { ["id"] = perk.perkId, ["name"] = perk.GetLocalizedPerkName(), ["active"] = active });
                }
                if (before != null)
                    foreach (var old in before.Effects)
                        if (!current.Effects.ContainsKey(old.Key))
                            effects.Add(new JObject { ["id"] = old.Key, ["removed"] = true });
                if (before == null || effects.Count > 0) row["effects"] = effects;
                if (row["reputation"] != null || effects.Count > 0) rows.Add(row);
            }
            if (!baseline)
                foreach (var id in previousFeedback.Keys)
                    if (!latest.ContainsKey(id)) rows.Add(new JObject { ["id"] = id, ["removed"] = true });
            previousFeedback = latest;
            feedbackLoad = generation;
            if (!baseline && rows.Count == 0) return "unchanged";
            if (baseline) receipt["reputation_scale"] = 200;
            receipt[baseline ? "factions" : "changes"] = rows;
            return baseline ? "baseline" : "delta";
        }
    }
}
