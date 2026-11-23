using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace WhiteDragon.Ledger
{
    public sealed partial class InventoryObserver
    {
        // Follow only slots exposed on the parent's currently visible page.
        private JObject StockLocation(GameItem item)
        {
            var path = new JArray();
            var seen = new HashSet<GameItem>();
            while (item != null && item.parentInventory != null && seen.Add(item))
            {
                var slot = item.parentInventory;
                if (areas.TryGetValue(slot,out var area))
                    return new JObject { ["area"]=area, ["containers"]=path };
                var parent = slot.GetParentItem();
                if (parent == null || !GeneralHelper.IsItemEffectivelyOwned(parent) || GeneralHelper.IsItemLocked(parent) ||
                    parent.contentWindow == null || !parent.contentWindow.IsVisible() || parent.contentWindow.isLocked)
                    return null;
                var visible = ReadWindow(parent).Controls.FirstOrDefault(entry => ReferenceEquals(entry.Value,slot));
                if (visible.Key == null) return null;
                path.AddFirst(new JObject { ["owner_id"]=LocalId(parent), ["control"]=visible.Key });
                item = parent;
            }
            return null;
        }

        private static JObject ModuleRemoval(GameItem item, int id)
        {
            if (!item.IsTag("MODULE_STUCK_TAG")) return null;
            return new JObject {
                ["reason"]="module_stuck", ["message"]=LocHelper.GetLocalizedMechanic("mech_module_stuck"),
                ["tool_kinds"]=new JArray("module_extractor","module_extractor_advanced"),
                ["next_tool"]="item_menu", ["verb"]="use", ["target_id"]=id,
                ["hint"]="Use an owned extractor on this card, then take the loosened card out of the slot."
            };
        }

        private void AddMachinePower(GameItem machine, GameSlotInventory slot, JObject row)
        {
            var battery = slot.childItem;
            row["battery_present"] = battery != null;
            row["can_power"] = MachineHelper.CanPower(machine,slot);
            row["cycle_power_usage"] = MachineryHelper.GetMachinePowerUsage(machine);
            if (battery == null) return;
            row["battery_id"] = LocalId(battery);
            if (battery.IsTag(PowerHelper.POWER_SOURCE_ITEM_ENERGY))
                row["energy_kj"] = battery.GetTagReadonly(PowerHelper.POWER_SOURCE_ITEM_ENERGY).GetInt();
            if (battery.IsTag(PowerHelper.POWER_SOURCE_ITEM_MAX_ENERGY))
                row["max_energy_kj"] = battery.GetTagReadonly(PowerHelper.POWER_SOURCE_ITEM_MAX_ENERGY).GetInt();
        }

        private static readonly string[] FurnaceSlots = { "power", "module", "input", "output", "note" };

        private void DescribeMachine(GameItem owner, WindowRead read, JArray rows)
        {
            bool furnace = owner.identifier == "furnace";
            bool powered = furnace || owner.IsTag(MachineHelper.STANDARD_MACHINE_TAG);
            var batterySlot = powered ? MachineHelper.GetBatterySlot(owner) : null;
            string ferment = owner.IsTag("WINE_CONTAINER_TAG") ? LocHelper.GetLocalizedMechanic("mech_wine_start_fermenting") : null;
            foreach (JObject row in rows)
            {
                var element = read.Controls[(string)row["control"]];
                if (element is MachineryButton button && ferment != null && button.text == ferment)
                    row["action"] = "start_fermentation";
                if (!(element is GameInventory slot)) continue;
                if (batterySlot != null && ReferenceEquals(slot,batterySlot))
                    AddMachinePower(owner,batterySlot,row);
                if (furnace)
                    for (int i=0;i<FurnaceSlots.Length;i++)
                        if (ReferenceEquals(slot,MachineHelper.GetBodyStorage(owner,i)))
                        {
                            row["role"] = FurnaceSlots[i];
                            if (FurnaceSlots[i]=="output") row["insert_allowed"] = false;
                            break;
                        }
            }
        }
    }
}
