using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace WhiteDragon.Ledger
{
    public sealed partial class InventoryObserver
    {
        private JObject HeatingView(GameItem machine)
        {
            var vessel=(MachineHelper.GetBodyStorage(machine,1) as GameSlotInventory)?.childItem;
            int upgrades=MachineBlender.GetUpgradeCount(machine);
            return new JObject { ["mode_index"]=machine.GetTagReadonly("HEATING_PLATE_STATE").GetInt(),
                ["target_c"]=machine.GetTagReadonly("CURRENT_TARGET_TEMP").GetInt(),
                ["current_c"]=vessel==null ? JValue.CreateNull() : new JValue(vessel.GetTagReadonly("CONTAINER_TEMP_TAG").GetDouble()),
                ["vessel_id"]=vessel==null ? JValue.CreateNull() : new JValue(LocalId(vessel)),
                ["upgrades"]=upgrades, ["target_adjustable"]=upgrades>=2,
                ["target_hint"]=upgrades>=2 ? "Use the native minus/plus buttons (5 C per press)." :
                    "Fixed target at this upgrade level. Target buttons unlock at upgrade 2." };
        }

        private static JObject LiquidSummary(GameItem item)
        {
            if (item==null) return null;
            var parts=LiquidParts.Read(item);
            long total=parts.Sum(p=>(long)p.Value);
            var summary=new JObject { ["total_ul"]=total, ["total_ml"]=Math.Round(total/1000.0,3) };
            if (parts.Count>0)
            {
                var main=parts[0];
                summary["primary_id"]=main.Key.identifier;
                summary["primary_name"]=main.Key.displayName;
                summary["primary_ul"]=main.Value;
                summary["primary_ml"]=Math.Round(main.Value/1000.0,3);
                summary["primary_percent"]=total==0 ? 0 : Math.Round(main.Value*100.0/total,2);
            }
            return summary;
        }

        private void DescribeEvaporator(GameItem owner, WindowRead read, JArray rows)
        {
            var batterySlot=MachineHelper.GetBodyStorage(owner,0) as GameSlotInventory;
            var containerSlot=MachineHelper.GetBodyStorage(owner,2) as GameSlotInventory;
            var distillateSlot=MachineHelper.GetBodyStorage(owner,3) as GameSlotInventory;
            foreach (JObject row in rows)
            {
                var element=read.Controls[(string)row["control"]];
                if (batterySlot!=null && ReferenceEquals(element,batterySlot) && row["energy_kj"]==null)
                    AddMachinePower(owner,batterySlot,row);
                if (containerSlot!=null && ReferenceEquals(element,containerSlot) && containerSlot.childItem!=null &&
                    row["items"] is JArray items && items.Count>0 && items[0] is JObject entry)
                    entry["liquid"]=LiquidSummary(containerSlot.childItem);
            }
            int qualityBonus=MachineryHelper.GetCurrentQualityBonus(owner);
            read.View["quality_bonus"]=qualityBonus;
            if (qualityBonus<=0)
            {
                read.View["distillate_capture"]=false;
                read.View["distillate_reason"]="no_quality_bonus";
            }
            else if (distillateSlot?.childItem==null)
            {
                read.View["distillate_capture"]=false;
                read.View["distillate_reason"]="no_receiving_container";
            }
            else read.View["distillate_capture"]=true;
        }

        private JObject FilterView(GameItem vessel)
        {
            var filter=WaterHelper.GetFilterItem(vessel);
            return new JObject { ["filter_id"]=filter==null ? JValue.CreateNull() : new JValue(LocalId(filter)),
                ["direction"]="pour_into_this_container", ["next_tool"]=filter==null ? "transfer_slot_item" : "water_use" };
        }

        private void DescribeFilter(GameItem item,JObject receipt)
        {
            if (!item.IsTag("WATER_FILTER")) return;
            var choices=new JArray();
            foreach (var area in areas)
                foreach (var vessel in area.Key.childItems)
                    if (vessel.IsTag("FILTER_CONTAINER_TAG") && GeneralHelper.IsItemEffectivelyOwned(vessel))
                        choices.Add(new JObject { ["id"]=LocalId(vessel), ["name"]=vessel.GetDisplayName(true), ["area"]=area.Value });
            receipt["filter_route"]=new JObject { ["receivers"]=choices, ["receiver_tag"]="FILTER_CONTAINER_TAG",
                ["hint"]="Open a compatible receiving vessel (e.g. water_pitcher), insert this filter in its slot, then pour the source liquid into the receiver using water_use preview/apply." };
        }

        private static void AddChemicalPurityFields(GameItem item, JObject row)
        {
            if (item==null || row==null || !ChemicalProductHelper.HasChemicalPurity(item)) return;
            row["purity"]=Math.Round(ChemicalProductHelper.GetPurity(item), 2);
            string grade=ChemicalProductHelper.GetCurrentChemicalConditionId(item);
            if (!string.IsNullOrEmpty(grade)) row["grade"]=grade;
        }

        private static void DescribeChemFinisher(WindowRead read, JArray rows)
        {
            string outputText=LocHelper.GetLocalizedMechanic("mech_chem_finisher_btn_output");
            foreach (JObject row in rows)
            {
                if (!read.Controls.TryGetValue((string)row["control"], out var element) || !(element is MachineryButton button)) continue;
                row["action"]=button.text==outputText ? "output" : "configure";
            }
        }

        private JArray SmugglerBayRows()
        {
            var result=new JArray();
            foreach (var area in areas)
                foreach (var item in area.Key.childItems)
                    if (ContrabandHelper.IsSmugglerBay(item) && GeneralHelper.IsItemEffectivelyOwned(item))
                        result.Add(new JObject { ["id"]=LocalId(item), ["name"]=item.GetDisplayName(true),
                            ["kind"]=item.identifier, ["area"]=area.Value });
            return result;
        }

        private string ProduceBatch(GameItem machine, MachineryButton button, int quantity, JObject receipt)
        {
            if (quantity<1 || quantity>64 || machine.identifier!="chem_finisher" ||
                button.text!=LocHelper.GetLocalizedMechanic("mech_chem_finisher_btn_output")) return "batch_unavailable";
            var input=MachineHelper.GetBodyStorage(machine,1) as GameSlotInventory;
            var output=MachineHelper.GetBodyStorage(machine,2) as GameGridInventory;
            if (input==null || output==null) return "machine_changed";
            string kind=machine.GetTagReadonly("SELECTED_CHEM_ID_TAG").GetString();
            receipt["requested"]=quantity;
            receipt["produced"]=0;
            for (int i=0;i<quantity;i++)
            {
                if (!MachineChemFinisher.CanOutput(machine,input)) { receipt["reason"]="recipe_not_ready"; break; }
                if (output.IsInsertLocked() || button.isDisabled) { receipt["reason"]="output_locked"; break; }
                // With a partly filled grid, use a produced unit's footprint to prove there is room
                // before the native callback consumes any more reagent.
                if (output.childItems.Count>0)
                {
                    var sample=output.childItems.FirstOrDefault(x=>x.identifier==kind);
                    var free=InventoryCapacity(output)["largest_free"];
                    if (sample==null || (int)free["w"]<sample.modifiedShape.globalWidth || (int)free["h"]<sample.modifiedShape.globalHeight)
                    { receipt["reason"]="clear_output_space"; break; }
                }
                int before=KindQuantity(output,kind);
                if (!button.Select()) { receipt["reason"]="button_rejected"; break; }
                int count=KindQuantity(output,kind)-before;
                if (count!=1) { receipt["reason"]="output_unconfirmed"; break; }
                receipt["produced"]=i+1;
            }
            if ((int)receipt["produced"]>0 && output.childItems.FirstOrDefault(x=>x.identifier==kind) is GameItem producedSample)
                AddChemicalPurityFields(producedSample,receipt);
            return (int)receipt["produced"]==quantity ? "produced" : "batch_incomplete";
        }

        private string RequestSupplierQuote(JObject request,JObject receipt)
        {
            if (order!=null || staged!=null || stagedBundle.Count!=0 || CounterStage()!="ready" ||
                !merchant.IsClientSelling() || merchant.isOfferWholesale || merchant.isRegularSellDisable)
                return "quote_unavailable";
            var shop=EmporiumEntry.Instance;
            var front=shop.frontInvinvElement;
            var table=shop.backInvinvElement;
            int id=(int)request["item_id"];
            if (!itemsById.TryGetValue(id,out var item) || GeneralHelper.IsItemOwned(item) || item.parentInventory!=front ||
                !shop.GetAllNonOwnedItem().Contains(item)) return "offer_changed";
            if (GeneralHelper.IsItemLocked(item) || table.childItems.Count!=0 || table.IsInsertLocked() || front.IsRemoveLocked() || !item.MayRemove())
                return "counter_unavailable";
            int units=item.unitCount;
            ModHelper.MoveItemToBackCounter(item);
            if (item.parentInventory!=table) return "placement_unconfirmed";
            try
            {
                if (table.childItems.Count==1 && table.childItems[0]==item && item.unitCount==units)
                    receipt["quote"]=new JObject { ["price"]=item.GetNegociatedValue(), ["side"]="buy", ["item_id"]=id, ["price_source"]="table_quote" };
            }
            finally { ModHelper.MoveItemToFrontCounter(item); }
            receipt["item_id"]=id;
            receipt["returned_to_front"]=item.parentInventory==front && item.unitCount==units;
            if (!(bool)receipt["returned_to_front"]) return "return_unconfirmed";
            return receipt["quote"] is JObject ? "quoted" : "quote_unavailable";
        }
    }
}
