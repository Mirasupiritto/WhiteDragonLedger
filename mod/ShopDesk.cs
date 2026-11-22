using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace WhiteDragon.Ledger
{
    public sealed partial class InventoryObserver
    {
        private StoreClientInstance visit;
        private StoreClient merchant;
        private string visitId;
        private JArray lots = new JArray(), night = new JArray();
        private readonly Dictionary<int,JObject> unsold = new Dictionary<int,JObject>();
        private readonly HashSet<GameItem> purifiers = new HashSet<GameItem>();
        private readonly HashSet<GameItem> stripSupplies = new HashSet<GameItem>();
        private readonly Dictionary<GameItem,int> payments = new Dictionary<GameItem,int>();
        private readonly List<GameItem> received = new List<GameItem>();
        private int previousCash, paidAtCallback, wholesaleQuote;
        private string supplier, deskFile, previousView;
        private float nextView;
        private bool lotsChanged;

        private void ObserveVisit()
        {
            var current = PlayerStore.Instance.currentClientInstance;
            if (current == visit) return;
            DisconnectVisit();
            visit = current;
            if (visit == null) return;
            merchant = visit.GetClientBlueprint();
            if (merchant == null) return;
            visitId = Guid.NewGuid().ToString("N");
            merchant.onItemSold += BoughtFromMerchant;
            merchant.onItemBought += SoldToMerchant;
        }

        private void DisconnectVisit()
        {
            if (merchant != null)
            {
                merchant.onItemSold -= BoughtFromMerchant;
                merchant.onItemBought -= SoldToMerchant;
            }
            visit = null;
            merchant = null;
            visitId = null;
            observedVariety = false;
            informationToken = null;
        }

        private void BoughtFromMerchant(GameItem item, int unitQuote)
        {
            if (!merchant.isOfferWholesale) return;
            if (received.Count == 0)
            {
                supplier = merchant.displayName;
                // The native wholesale button has already debited cash, but none of
                // the offered items have changed owner at the first callback.
                paidAtCallback = previousCash - PlayerStore.Instance.playerCash;
                wholesaleQuote = PlayerStore.Instance.GetWholesalePrice();
            }
            received.Add(item);
        }

        private void SoldToMerchant(GameItem item, int actualPayment)
        {
            observedSale = item;
            observedRevenue = actualPayment;
            if (unsold.ContainsKey(item.uniqueId)) payments[item] = actualPayment;
        }

        private void LoadDesk()
        {
            lots = JArray.Parse(ModHelper.GetModData(ModId, "purchase_lots", "[]"));
            night = JArray.Parse(ModHelper.GetModData(ModId, "last_night", "[]"));
            unsold.Clear();
            foreach (JObject lot in lots)
                foreach (JObject member in (JArray)lot["members"])
                    if (member["sale"] == null) unsold.Add((int)member["key"], member);
            previousCash = PlayerStore.Instance.playerCash;
            deskFile = "desk-" + generation + ".json";
            previousView = null;
            nextView = 0;
            lotsChanged = true;
            ObserveVisit();
        }

        private void PublishLots()
        {
            Write("lots-" + generation + ".json", new JObject { ["slot"] = slot,
                ["generation"] = generation, ["lots"] = lots.DeepClone() });
        }

        private void CloseTransactions()
        {
            bool dirty = false;
            // Buying individually calls the same per-item hook. Only the native
            // wholesale completion consumes the wholesale offer in this frame.
            if (received.Count > 0 && merchant != null && !merchant.isOfferWholesale &&
                received.TrueForAll(GeneralHelper.IsItemOwned))
            {
                var members = new JArray();
                foreach (var item in received)
                {
                    int key = item.GetUniqueID();
                    changed.Add(item);
                    var member = new JObject { ["key"] = key, ["name"] = item.GetDisplayName(true),
                        ["kind"] = item.identifier };
                    members.Add(member);
                    unsold[key] = member;
                }
                lots.Add(new JObject { ["lot_id"] = Guid.NewGuid().ToString("N"), ["supplier"] = supplier,
                    ["supplier_id"] = merchant.identifier, ["day"] = StoreStation.Instance.dayCounter,
                    ["paid"] = paidAtCallback == wholesaleQuote && paidAtCallback >= 0 ? (JToken)paidAtCallback : JValue.CreateNull(),
                    ["members"] = members });
                dirty = true;
            }
            received.Clear();
            foreach (var payment in payments)
            {
                int key = payment.Key.uniqueId;
                if (GeneralHelper.IsItemOwned(payment.Key) || !unsold.TryGetValue(key, out var member)) continue;
                member["sale"] = payment.Value;
                changed.Add(payment.Key);
                unsold.Remove(key);
                dirty = true;
            }
            payments.Clear();
            if (dirty)
            {
                ModHelper.SetModData(ModId, "purchase_lots", lots.ToString(Formatting.None));
                lotsChanged = true;
                nextView = 0;
            }
            previousCash = PlayerStore.Instance.playerCash;
        }

        private int LocalId(GameItem item)
        {
            if (!ids.TryGetValue(item, out var id))
            {
                ids[item] = id = ++nextId;
                itemsById.Add(id, item);
            }
            return id;
        }

        private static string BottleLabel(GameItem item)
        {
            if (!item.IsTag("LIQUID_CONTAINER_TAG")) return null;
            // Match the label shown by the scanner, without creating a feature on the item.
            var feature = item.IsTag("WINE_CONTAINER_TAG") ? null : item.FindItemFeatureByCategory("CATEGORY_WATER_PURITY");
            return (feature == null ? ItemConditionList.CreateBaseWater() : feature.GetCurrentItemCondition()).display;
        }

        private JArray MachineRows()
        {
            var result = new JArray();
            foreach (var machine in purifiers)
            {
                if (machine.parentInventory == null || !areas.ContainsKey(machine.parentInventory) ||
                    !GeneralHelper.IsItemOwned(machine)) continue;
                var container = MachineHelper.GetBodyStorage(machine, 2) as GameSlotInventory;
                var power = MachineHelper.GetBatterySlot(machine);
                if (container == null || power == null) continue;
                var bottle = container.childItem;
                var battery = power.childItem;
                var row = new JObject { ["machine_id"] = LocalId(machine), ["machine_key"] = machine.GetUniqueID(),
                    ["machine"] = machine.GetDisplayName(true), ["day"] = StoreStation.Instance.dayCounter, ["battery_present"] = battery != null,
                    ["can_power"] = MachineHelper.CanPower(machine, power),
                    ["can_remove_bottle"] = bottle != null && bottle.MayRemove(),
                    ["inventory_has_room"] = bottle != null && !EmporiumEntry.Instance.invElement.IsInsertLocked() &&
                        EmporiumEntry.Instance.invElement.TryFindOneValidInventorySlot(bottle) != null,
                    ["bottle_id"] = bottle == null ? JValue.CreateNull() : (JToken)LocalId(bottle),
                    ["bottle_key"] = bottle == null ? JValue.CreateNull() : (JToken)bottle.GetUniqueID() };
                if (bottle != null && BottleLabel(bottle) is string label) row["bottle_label"] = label;
                if (battery != null && battery.IsTag(PowerHelper.POWER_SOURCE_ITEM_ENERGY))
                    row["energy_kj"] = battery.GetTagReadonly(PowerHelper.POWER_SOURCE_ITEM_ENERGY).GetInt();
                if (battery != null && battery.IsTag(PowerHelper.POWER_SOURCE_ITEM_MAX_ENERGY))
                    row["max_energy_kj"] = battery.GetTagReadonly(PowerHelper.POWER_SOURCE_ITEM_MAX_ENERGY).GetInt();
                result.Add(row);
            }
            return result;
        }

        private void BeforeSleep()
        {
            if (generation == null || !CanObserveStore()) return;
            night = MachineRows();
            ModHelper.SetModData(ModId, "last_night", night.ToString(Formatting.None));
            nextView = 0;
        }

        private int AvailableStrips()
        {
            int strips = 0;
            foreach (var supply in stripSupplies)
            {
                if (!GeneralHelper.IsItemOwned(supply) || supply.parentInventory == null || !areas.ContainsKey(supply.parentInventory)) continue;
                if (supply.identifier == "water_test_strip") { if (!supply.IsTag(WaterTestHelper.IS_SPENT)) ++strips; }
                else foreach (var strip in supply.FindAllChildItems())
                    if (strip.identifier == "water_test_strip" && !strip.IsTag(WaterTestHelper.IS_SPENT)) ++strips;
            }
            return strips;
        }

        private void RefreshDesk()
        {
            if (lotsChanged)
            {
                PublishLots();
                lotsChanged = false;
            }
            ObserveVisit();
            ObserveOpening();
            if (Time.realtimeSinceStartup < nextView) return;
            nextView = Time.realtimeSinceStartup + 1f;
            var machines = MachineRows();
            var customer = merchant == null ? null : new JObject { ["visit"] = visitId,
                ["name"] = merchant.displayName, ["remaining"] = merchant.useClientBudget ? (JToken)merchant.GetBudget() : JValue.CreateNull() };
            var doc = new JObject { ["generation"] = generation, ["slot"] = slot, ["revision"] = sequence,
                ["phase"] = phase, ["day"] = StoreStation.Instance.dayCounter, ["customer"] = customer,
                ["day_info"] = DayView(), ["phone"] = PhoneView(), ["counter_protocol"] = 2, ["counter"] = CounterView(), ["available_strips"] = AvailableStrips(), ["machines"] = machines, ["last_night"] = night.DeepClone() };
            var text = doc.ToString(Formatting.None);
            if (text == previousView) return;
            ModHelper.WriteModDataFileText(ModId, deskFile, text);
            previousView = text;
        }
    }
}
