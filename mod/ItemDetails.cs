using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace WhiteDragon.Ledger
{
    public sealed partial class InventoryObserver
    {
        private readonly HashSet<GameItem> portablePurifiers = new HashSet<GameItem>();
        private readonly Dictionary<int, (string name, string kind)> observedIdentities = new Dictionary<int, (string, string)>();
        private readonly List<JObject> journalIdentityChanges = new List<JObject>();
        private readonly List<JObject> receiptIdentityChanges = new List<JObject>();

        private void ResetItemDetails()
        {
            portablePurifiers.Clear();
            observedIdentities.Clear();
            journalIdentityChanges.Clear();
            receiptIdentityChanges.Clear();
        }

        private static JObject ItemSize(GameItem item)
        {
            var shape = item?.modifiedShape ?? item?.shape;
            int w = shape != null ? Math.Max(1, shape.globalWidth) : 1;
            int h = shape != null ? Math.Max(1, shape.globalHeight) : 1;
            return new JObject { ["w"] = w, ["h"] = h };
        }

        private static JObject ItemSlot(GameItem item)
        {
            var shape = item?.modifiedShape;
            return new JObject
            {
                ["x"] = shape != null ? shape.minX : 0,
                ["y"] = shape != null ? shape.minY : 0
            };
        }

        private static bool IsItemRotatable(GameItem item)
        {
            if (item == null || item.parentInventory is GameSlotInventory) return false;
            var shape = item.modifiedShape ?? item.shape;
            if (shape == null) return false;
            if (shape.width != shape.height) return true;
            if (shape.width <= 1) return false;
            int n = shape.width;
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    bool a = shape.GetLocal(x, y) != 0;
                    bool b = shape.GetLocal(n - 1 - y, x) != 0;
                    if (a != b) return true;
                }
            }
            return false;
        }

        private static JObject InventoryCapacity(GameInventory inv)
        {
            if (inv == null) return null;
            if (inv is GameSlotInventory slotInv)
            {
                int free = slotInv.childItem == null ? 1 : 0;
                return new JObject
                {
                    ["w"] = 1,
                    ["h"] = 1,
                    ["free_cells"] = free,
                    ["largest_free"] = new JObject { ["w"] = free, ["h"] = free }
                };
            }
            if (!(inv is GameGridInventory grid) || grid.inventoryShape == null) return null;
            var shape = grid.inventoryShape;
            int w = Math.Max(0, shape.width);
            int h = Math.Max(0, shape.height);
            if (w == 0 || h == 0)
            {
                return new JObject
                {
                    ["w"] = w,
                    ["h"] = h,
                    ["free_cells"] = 0,
                    ["largest_free"] = new JObject { ["w"] = 0, ["h"] = 0 }
                };
            }
            bool[,] freeGrid = new bool[w, h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    freeGrid[x, y] = shape.Get(x, y) == 0;
            foreach (var child in grid.childItems)
            {
                if (child == null || GeneralHelper.IsDisplayOnly(child)) continue;
                var cs = child.modifiedShape;
                if (cs == null) continue;
                int x0 = Math.Max(0, cs.minX), x1 = Math.Min(w - 1, cs.maxX);
                int y0 = Math.Max(0, cs.minY), y1 = Math.Min(h - 1, cs.maxY);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                        if (cs.Get(x, y) > 0) freeGrid[x, y] = false;
            }
            int freeCells = 0, bestArea = 0, bestW = 0, bestH = 0;
            int[] heights = new int[w];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (freeGrid[x, y]) { freeCells++; heights[x]++; }
                    else heights[x] = 0;
                }
                for (int left = 0; left < w; left++)
                {
                    int minH = heights[left];
                    if (minH == 0) continue;
                    for (int right = left; right < w && heights[right] > 0; right++)
                    {
                        if (heights[right] < minH) minH = heights[right];
                        int rw = right - left + 1;
                        int area = rw * minH;
                        if (area > bestArea || (area == bestArea && Math.Max(rw, minH) > Math.Max(bestW, bestH)))
                        {
                            bestArea = area;
                            bestW = rw;
                            bestH = minH;
                        }
                    }
                }
            }
            return new JObject
            {
                ["w"] = w,
                ["h"] = h,
                ["free_cells"] = freeCells,
                ["largest_free"] = new JObject { ["w"] = bestW, ["h"] = bestH }
            };
        }

        private static JObject ShopCapacities()
        {
            var shop = EmporiumEntry.Instance;
            var caps = new JObject();
            if (InventoryCapacity(shop?.invElement) is JObject invCap) caps["inventory"] = invCap;
            if (InventoryCapacity(shop?.showcaseElement) is JObject showCap) caps["showcase"] = showCap;
            return caps;
        }

        private static bool EvaluateAddPredicate(Func<GameItem, GameInventory, bool> predicate, GameItem item, GameInventory target)
        {
            if (predicate == null) return true;
            foreach (var d in predicate.GetInvocationList())
            {
                if ((bool)d.DynamicInvoke(item, target)) return true;
            }
            return false;
        }

        private static bool IsSpaceFailure(GameInventory target, GameItem item)
        {
            if (target == null || item == null) return false;
            if (target is GameSlotInventory slotInv)
                return slotInv.childItem != null && slotInv.childItem != item;
            if (!EvaluateAddPredicate(item.mayInventoryAddItemFunc, item, target)) return false;
            if (!EvaluateAddPredicate(target.mayInventoryAddItemFunc, item, target)) return false;
            return true;
        }

        private static void AttachSpaceFailure(GameItem item, GameInventory target, JObject receipt)
        {
            receipt["item_size"] = ItemSize(item);
            if (InventoryCapacity(target) is JObject cap) receipt["target_capacity"] = cap;
        }

        private void AttachBagChoiceCapacity(GameInventory inventory, GameItem choiceItem, int index, JObject row)
        {
            if (PlayerStore.Instance?.currentClientInstance != null) return;
            if (index == 0 && choiceItem?.identifier == "plastic_bag")
            {
                row["has_backpack"] = false;
                row["capacity"] = JValue.CreateNull();
                return;
            }
            var bags = EmporiumEntry.Instance?.GetAllOwnedItemsByTag("BACKPACK_TAG");
            if (bags != null && index >= 1 && index - 1 < bags.Count)
            {
                var bag = bags[index - 1];
                string choiceText = choiceItem?.GetDisplayName(true) ?? "";
                string bagName = bag.GetDisplayName(true) ?? "";
                if (string.IsNullOrEmpty(choiceItem?.identifier) ||
                    (bagName.Length > 0 && choiceText.IndexOf(bagName, StringComparison.Ordinal) >= 0))
                {
                    row["has_backpack"] = true;
                    row["backpack_id"] = LocalId(bag);
                    if (bag.contentWindow?.childElement is GameInventory bagInv &&
                        InventoryCapacity(bagInv) is JObject cap)
                        row["capacity"] = cap;
                    return;
                }
            }
            if (choiceItem?.contentWindow?.childElement is GameInventory directInv &&
                InventoryCapacity(directInv) is JObject directCap)
            {
                row["has_backpack"] = true;
                row["capacity"] = directCap;
            }
        }

        private static bool IsWaterContainerItem(GameItem item) =>
            item != null && item.IsTag("LIQUID_CONTAINER_TAG") && !item.IsTag("WINE_CONTAINER_TAG");

        private static void AddPortablePurifierFields(GameItem item, JObject row)
        {
            if (item == null || item.identifier != "portable_water_purifier") return;
            int charge = Math.Max(0, DurabilityHelper.GetRealDurability(item));
            int maxCharge = Math.Max(0, DurabilityHelper.GetMaxDurability(item));
            row["charge_remaining"] = charge;
            row["charge_source"] = "durability";
            row["ul_per_charge"] = 100000;
            row["charge_hint"] = "Portable purifier uses durability, rounded up per bottle. No battery slot.";
            if (maxCharge > 0) row["charge_max"] = maxCharge;
            row["liters_remaining"] = Math.Round(charge * 0.1, 1);
        }

        private static void AddWaterContainerFields(GameItem item, JObject row)
        {
            if (!IsWaterContainerItem(item)) return;
            int ul = Math.Max(0, WaterHelper.GetCurrentCapacityUL(item));
            bool empty = WaterHelper.IsEmpty(item);
            double volumeMl = empty ? 0 : ul / 1000.0;
            int capMl = Math.Max(0, WaterHelper.GetTotalCapacityML(item));
            row["water_container"] = true;
            row["liquid_ml"] = volumeMl;
            bool containsWater=HasWater(item);
            row["water_ml"] = item.GetTagReadonly(Liquid.GetCurrentPartFromId("water")).GetInt() / 1000.0;
            if (!empty)
            {
                var main = LiquidParts.Read(item).FirstOrDefault();
                if (main.Key != null)
                    row["liquid_main"] = new JObject { ["id"]=main.Key.identifier, ["name"]=main.Key.displayName,
                        ["percent"]=Math.Round(100.0 * main.Value / ul, 2) };
                if (!containsWater && row["categories"] is JArray tags)
                    foreach (var tag in tags.Where(t => (string)t == "WATER").ToList()) tag.Remove();
            }
            if (capMl > 0) row["capacity_ml"] = capMl;
            row["empty"] = empty;
            row["contains_water"] = containsWater;
        }

        private JArray PortablePurifierRows()
        {
            foreach (var entry in areas)
                foreach (var child in entry.Key.childItems)
                    if (child != null && child.identifier == "portable_water_purifier")
                        portablePurifiers.Add(child);
            var result = new JArray();
            foreach (var item in portablePurifiers.ToList())
            {
                if (item == null || item.parentInventory == null || !areas.TryGetValue(item.parentInventory, out var area) ||
                    !GeneralHelper.IsItemOwned(item))
                {
                    portablePurifiers.Remove(item);
                    continue;
                }
                var entry = new JObject
                {
                    ["id"] = LocalId(item),
                    ["save_id"] = item.GetUniqueID(),
                    ["name"] = item.GetDisplayName(true),
                    ["area"] = area
                };
                AddPortablePurifierFields(item, entry);
                result.Add(entry);
            }
            return result;
        }

        private static long SafeEstimatedValue(GameItem item)
        {
            if (item == null) return 0;
            try
            {
                return StoreStation.Instance?.storeEventManager != null
                    ? item.GetCurrentValue(false, true, true, false, 0L)
                    : item.GetBaseValue();
            }
            catch { return item.GetBaseValue(); }
        }

        private static void AddGeometryFields(GameItem item, JObject row)
        {
            row["size"] = ItemSize(item);
            row["slot"] = ItemSlot(item);
            row["rotatable"] = IsItemRotatable(item);
        }

        private void EnrichStockRow(GameItem item, JObject row)
        {
            AddGeometryFields(item, row);
            row["estimated_value"] = SafeEstimatedValue(item);
            AddPortablePurifierFields(item, row);
            AddWaterContainerFields(item, row);
            AddChemicalPurityFields(item, row);
            int id = (int)row["id"];
            observedIdentities[id] = ((string)row["name"] ?? "", (string)row["kind"] ?? "");
        }

        private void QueueIdentityChange(List<JObject> target, int id, string fromName, string toName, string fromKind, string toKind)
        {
            foreach (JObject existing in target)
            {
                if ((int)existing["id"] == id && (string)existing["to"] == toName && (string)existing["to_kind"] == toKind)
                    return;
            }
            var change = new JObject { ["id"] = id, ["from"] = fromName, ["to"] = toName };
            if (!string.Equals(fromKind, toKind, StringComparison.Ordinal))
            {
                change["from_kind"] = fromKind;
                change["to_kind"] = toKind;
            }
            target.Add(change);
        }

        private void ScanKnownIdentities()
        {
            foreach (var area in areas)
            {
                foreach (var item in area.Key.childItems)
                {
                    if (item == null || GeneralHelper.IsDisplayOnly(item)) continue;
                    if (!ids.TryGetValue(item, out int id)) continue;
                    string curName = item.GetDisplayName(true) ?? "";
                    string curKind = item.identifier ?? "";
                    if (observedIdentities.TryGetValue(id, out var prev) &&
                        (!string.Equals(prev.name, curName, StringComparison.Ordinal) ||
                         !string.Equals(prev.kind, curKind, StringComparison.Ordinal)))
                    {
                        changed.Add(item);
                        QueueIdentityChange(journalIdentityChanges, id, prev.name, curName, prev.kind, curKind);
                        QueueIdentityChange(receiptIdentityChanges, id, prev.name, curName, prev.kind, curKind);
                    }
                    observedIdentities[id] = (curName, curKind);
                }
            }
        }

        private static JArray CloneChanges(List<JObject> list) =>
            new JArray(list.Select(c => c.DeepClone()));

        private static string ReadDisplayedTooltipText(GameItem item)
        {
            if (item is GameItemElement element)
            {
                try
                {
                    string tooltip = element.GetTooltip()?.nakedText;
                    if (!string.IsNullOrEmpty(tooltip)) return tooltip;
                }
                catch { }
            }
            var builder = new RichTextBuilder(int.MaxValue);
            ModInternal.CreateTooltipLate(builder, item);
            return builder.Build(int.MaxValue)?.nakedText ?? "";
        }

        private string ReadItemDetailsAction(JObject request, JObject receipt)
        {
            if (!InventorySorting.ItemDetailsCompatEnabled)
            {
                receipt["reason"] = "item_details_compat_off";
                return "assist_disabled";
            }
            var mod = FindItemDetailsMod();
            if (mod == null)
            {
                receipt["reason"] = "item_details_mod_not_loaded";
                return "item_details_mod_not_loaded";
            }
            var requestedIds = request["item_ids"] as JArray;
            if (requestedIds == null || requestedIds.Count == 0)
            {
                receipt["reason"] = "item_ids_required";
                return "invalid_request";
            }
            var rows = new JArray();
            foreach (var token in requestedIds)
            {
                int id = (int)token;
                if (!itemsById.TryGetValue(id, out var item) || item == null || item.parentInventory == null)
                {
                    rows.Add(new JObject { ["item_id"] = id, ["status"] = "not_found" });
                    continue;
                }
                rows.Add(new JObject {
                    ["item_id"] = id,
                    ["name"] = item.GetDisplayName(true),
                    ["kind"] = item.identifier,
                    ["status"] = "observed",
                    ["details_text"] = ReadDisplayedTooltipText(item)
                });
            }
            receipt["source"] = "item_details_mod";
            receipt["mod_version"] = mod.Manifest?.ModVersion;
            receipt["items"] = rows;
            return "observed";
        }

        private void AttachReceiptIdentityChanges(JObject receipt)
        {
            if (receipt == null || receiptIdentityChanges.Count == 0) return;
            receipt["identity_changed"] = CloneChanges(receiptIdentityChanges);
            receiptIdentityChanges.Clear();
        }
    }
}
