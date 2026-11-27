using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace WhiteDragon.Ledger
{
    public sealed partial class InventoryObserver
    {
        private JObject Row(GameItem item, string area)
        {
            if (CardNumber(item.identifier) != 0) phoneCards.Add(item);
            if (item.identifier == "water_purifier") purifiers.Add(item);
            if (item.identifier == "portable_water_purifier") portablePurifiers.Add(item);
            if (item.identifier == "water_test_strip" || item.identifier == "water_test_container") stripSupplies.Add(item);
            var row = StockRow(item, area);
            if (item.IsTag("LIQUID_CONTAINER_TAG")) bottles.Add(item);
            return row;
        }

        private JObject StockRow(GameItem item, string area)
        {
            var row = new JObject { ["id"] = LocalId(item), ["save_id"] = item.uniqueId, ["name"] = item.GetDisplayName(true),
                ["kind"] = item.identifier, ["area"] = area, ["owned"] = GeneralHelper.IsItemOwned(item),
                ["quantity"] = item.unitCount, ["categories"] = new JArray(item.GetGameItemType()),
                ["contraband"] = item.IsTag(TypeHelper.CONTRABAND),
                ["trade_contraband"] = ContrabandHelper.IsContraband(item) };
            if (BottleLabel(item) is string label)
                row["bottle_label"] = label;
            if (StolenHelper.IsStolenItem(item))
            {
                row["stolen"] = true;
                row["stolen_heat"] = StolenHelper.GetFoundChance(item);
            }
            EnrichStockRow(item, row);
            return row;
        }

        private void PopulateStockBaseline(JArray rows)
        {
            var roots = new HashSet<GameItem>();
            ++fullScans;
            ++loadScans;
            foreach (var area in areas)
                foreach (var item in area.Key.childItems)
                {
                    if (BagGrid(item) != null) roots.Add(item);
                    var row = Row(item, area.Value);
                    known.Add(ids[item], row);
                    rows.Add(row);
                }
            ExpandBagChanges(roots);
            foreach (var item in roots)
            {
                if (areas.ContainsKey(item.parentInventory)) continue;
                var row = BagStockRow(item);
                if (row == null) continue;
                known[LocalId(item)] = row;
                rows.Add(row);
            }
        }

        private void ForgetRootItem(GameItem item)
        {
            bottles.Remove(item);
            purifiers.Remove(item);
            portablePurifiers.Remove(item);
            stripSupplies.Remove(item);
            phoneCards.Remove(item);
        }

        private void ApplyStockChanges(JArray upserts, JArray removed)
        {
            ExpandBagChanges(changed);
            foreach (var item in changed)
            {
                JObject row;
                if (item.parentInventory != null && areas.TryGetValue(item.parentInventory, out var area))
                    row = Row(item, area);
                else
                {
                    ForgetRootItem(item);
                    row = BagStockRow(item);
                }
                if (row != null)
                {
                    int id = LocalId(item);
                    if (known.TryGetValue(id, out var old) && JToken.DeepEquals(old, row)) continue;
                    known[id] = row;
                    upserts.Add(row);
                }
                else if (ids.TryGetValue(item, out var id) && known.Remove(id))
                {
                    observedIdentities.Remove(id);
                    removed.Add(id);
                }
            }
            changed.Clear();
        }
    }
}
