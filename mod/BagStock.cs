using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace WhiteDragon.Ledger
{
    public sealed partial class InventoryObserver
    {
        // Only actual storage grids of portable bags. Machine output and inspection props have other semantics.
        private readonly Dictionary<GameItem, (GameInventory grid, bool locked)> bagGrids =
            new Dictionary<GameItem, (GameInventory, bool)>();

        private static GameInventory BagGrid(GameItem bag) =>
            IsBackpackOrPouch(bag) ? bag.contentWindow?.childElement as GameGridInventory : null;

        private static bool BagLocked(GameItem bag, GameInventory grid) =>
            GeneralHelper.IsItemLocked(bag) || bag.contentWindow.isLocked || grid.IsRemoveLocked();

        private void OnBagItemChanged(GameItem item, GameInventory grid, SlotMarker marker)
        {
            changed.Add(item);
            // The native estimate of the enclosing bag also includes its contents.
            var seen = new HashSet<GameItem>();
            var parent = grid.GetParentItem();
            while (parent != null && bagGrids.ContainsKey(parent) && seen.Add(parent))
            {
                changed.Add(parent);
                var outer = parent.parentInventory;
                if (outer == null || areas.ContainsKey(outer)) break;
                parent = outer.GetParentItem();
            }
        }

        private void BindBagGrid(GameInventory grid, bool bind)
        {
            if (bind)
            {
                grid.onSlotAddItemLateFunc += OnBagItemChanged;
                grid.onSlotRemoveItemLateFunc += OnBagItemChanged;
                grid.onSlotChangeAmountItemFunc += OnBagItemChanged;
            }
            else
            {
                grid.onSlotAddItemLateFunc -= OnBagItemChanged;
                grid.onSlotRemoveItemLateFunc -= OnBagItemChanged;
                grid.onSlotChangeAmountItemFunc -= OnBagItemChanged;
            }
        }

        private void DetachBagGrids()
        {
            foreach (var entry in bagGrids.Values) BindBagGrid(entry.grid, false);
            bagGrids.Clear();
        }

        private void PollBagAccess()
        {
            foreach (var entry in bagGrids)
                if (BagLocked(entry.Key, entry.Value.grid) != entry.Value.locked ||
                    !GeneralHelper.IsItemEffectivelyOwned(entry.Key))
                    changed.Add(entry.Key);
        }

        private string BagRoot(GameItem item, JArray path, out bool locked)
        {
            locked = false;
            var slot = item.parentInventory;
            if (slot == null) return null;
            if (areas.TryGetValue(slot, out var root)) return root;
            var seen = new HashSet<GameItem>();
            while (slot != null)
            {
                var bag = slot.GetParentItem();
                if (bag == null || BagGrid(bag) != slot || !GeneralHelper.IsItemEffectivelyOwned(bag) ||
                    !seen.Add(bag)) return null;
                locked |= BagLocked(bag,slot);
                path?.AddFirst(new JObject { ["id"]=LocalId(bag), ["name"]=bag.GetDisplayName(true) });
                slot = bag.parentInventory;
                if (slot != null && areas.TryGetValue(slot,out root)) return root;
            }
            return null;
        }

        private JObject BagStockRow(GameItem item)
        {
            if (item.parentInventory == null || GeneralHelper.IsDisplayOnly(item)) return null;
            var path = new JArray();
            string area = BagRoot(item,path,out bool locked);
            if (area == null || path.Count == 0) return null;
            var row = StockRow(item,area);
            row["owned"] = GeneralHelper.IsItemEffectivelyOwned(item);
            row["container_path"] = path;
            row["parent_id"] = path.Last["id"];
            row["access"] = locked || GeneralHelper.IsItemLocked(item) ? "container_locked" : "unpack_first";
            return row;
        }

        // Moving a bag changes the root area of its entire subtree. Ordinary item changes only touch that item.
        private void ExpandBagChanges(HashSet<GameItem> items)
        {
            var pending = new List<GameItem>(items);
            var seen = new HashSet<GameItem>();
            for (int index=0; index<pending.Count; index++)
            {
                var bag = pending[index];
                var grid = BagGrid(bag);
                if (grid == null || !seen.Add(bag)) continue;
                bool present = GeneralHelper.IsItemEffectivelyOwned(bag) && BagRoot(bag,null,out _) != null;
                bool tracked = bagGrids.ContainsKey(bag);
                if (!present && !tracked) continue;
                if (present != tracked) BindBagGrid(grid,present);
                if (present) bagGrids[bag] = (grid,BagLocked(bag,grid));
                else bagGrids.Remove(bag);
                foreach (var child in grid.childItems)
                {
                    if (child.parentInventory != grid || GeneralHelper.IsDisplayOnly(child)) continue;
                    items.Add(child);
                    if (BagGrid(child) != null) pending.Add(child);
                }
            }
        }
    }
}
