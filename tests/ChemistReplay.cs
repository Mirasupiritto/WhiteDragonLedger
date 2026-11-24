using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
namespace WhiteDragon.Ledger
{
    public static class MachineBlender { public static int GetUpgradeCount(GameItem item)=>item.associatedId; }
    public static class MachineChemFinisher { public static bool CanOutput(GameItem machine,GameSlotInventory slot)=>slot.childItem!=null && slot.childItem.volume>=2000; }
    public class Liquid {
        public string identifier, displayName;
        public string currentPart => GetCurrentPartFromId(identifier);
        public int particleSize, filterVolumeRemoved, filterDurabilityUsage;
        public double filterMinimumPurity;
        public static List<Liquid> Liquids = new List<Liquid> {
            new Liquid { identifier="water", displayName="水" },
            new Liquid { identifier="alcohol", displayName="酒精", particleSize=3, filterVolumeRemoved=500, filterDurabilityUsage=1 }
        };
        public static string GetCurrentPartFromId(string id)=>id+"_part";
    }
    public partial class GameItem { public double GetDouble()=>associatedId; }
    public sealed partial class InventoryObserver
    {
        private void ReplayChemist()
        {
            var machine=Item(8101,"chem_finisher",1);
            var source=Item(8102,"bottle",1);
            var slot=new GameSlotInventory();
            source.To(slot);
            source.volume=4500;
            var output=new GameGridInventory(6,4);
            MachineHelper.chemistrySlots=new object[]{null,slot,output};
            machine.waterTags.Add("SELECTED_CHEM_ID_TAG");
            machine.bookTagValue="test_product";
            var button=new MachineryButton { text=LocHelper.GetLocalizedMechanic("mech_chem_finisher_btn_output") };
            button.onPress=()=>{source.volume-=2000;new GameItem{identifier="test_product"}.To(output);};
            var receipt=new JObject();
            Expect(ProduceBatch(machine,button,3,receipt)=="batch_incomplete" && (int)receipt["produced"]==2 &&
                source.volume==500 && (string)receipt["reason"]=="recipe_not_ready","production stops at the real input limit");
            machine.To(EmporiumEntry.Instance.invElement);
            machine.contentWindow=new PixelWindow { visible=true, childElement=slot };
            var finisherPanel=new ItemPanelView { Key="chemical_finisher", Identity="finisher/1" };
            finisherPanel.Controls["-57964"]=new JObject { ["type"]="button", ["label"]="Select", ["recipe_id"]="dream_dust", ["enabled"]=true };
            finisherPanel.Actions["-57964"]=_=>{ machine.bookTagValue="dream_dust"; return "applied"; };
            NativeItemPanels.panel=finisherPanel;
            var pickRecipe=new JObject();
            ActAtCounter(new JObject { ["action"]="item_panel", ["generation"]=generation, ["revision"]=sequence,
                ["expires_ms"]=long.MaxValue, ["mode"]="read" },pickRecipe);
            var pickResult=new JObject();
            Expect(ActAtCounter(new JObject { ["action"]="item_panel", ["generation"]=generation, ["revision"]=sequence,
                ["expires_ms"]=long.MaxValue, ["mode"]="choose", ["panel_token"]=pickRecipe["panel_token"],
                ["control"]="-57964" },pickResult)=="applied","select recipe by recipe_id button");
            var inspectFinisher=new JObject();
            Expect(WorkstationAction(new JObject { ["action"]="inspect_window", ["owner_id"]=LocalId(machine) },inspectFinisher)=="observed" &&
                (string)inspectFinisher["window"]["recipe_id"]=="dream_dust","chem_finisher window reports selected recipe_id");
            NativeItemPanels.panel=null;
            Expect(BottleLabel(source)!=null,"water has label");
            source.waterTags.Add("replay_solvent");
            Expect(BottleLabel(source)==null && !HasWater(source),"pure solvent has no water label");
            source.volume=0;source.waterTags.Remove("replay_solvent");
            Expect(BottleLabel(source)==null,"empty bottle has no water label");
            var fabric=Item(8103,"kotton_fabric",1);
            fabric.waterTags.Add("WATER_FILTER");
            var pitcher=Item(8104,"water_pitcher",1);
            pitcher.waterTags.Add("FILTER_CONTAINER_TAG");
            var route=new JObject();
            DescribeFilter(fabric,route);
            Expect(route["filter_route"]["receivers"].Any(r=>(int)r["id"]==LocalId(pitcher)) &&
                (string)FilterView(pitcher)["direction"]=="pour_into_this_container","filter belongs in the receiving vessel");
            var plate=Item(8105,"heating_plate",1);
            plate.associatedId=1;
            Expect(!(bool)HeatingView(plate)["target_adjustable"],"basic plate fixed");
            plate.associatedId=2;
            Expect((bool)HeatingView(plate)["target_adjustable"],"upgraded plate adjustable");
            var evap=Item(8106,"evaporator",1);
            evap.To(EmporiumEntry.Instance.invElement);
            var evapBattery=new GameSlotInventory();
            var evapModule=new GameGridInventory(2,2);
            var evapContainer=new GameSlotInventory();
            var evapDistillate=new GameSlotInventory();
            var cell=Item(8107,"power_cell",1);
            cell.waterTags.Add(PowerHelper.POWER_SOURCE_ITEM_ENERGY);
            cell.associatedId=120;
            cell.To(evapBattery);
            var beaker=Item(8108,"bottle",1);
            beaker.volume=5000;
            beaker.scratchTagReader=tag=>new GameItem { associatedId=tag=="caffeine_part" ? 15000 : 0 };
            var caffeine=new Liquid { identifier="caffeine", displayName="Caffeine" };
            Liquid.Liquids.Add(caffeine);
            beaker.To(evapContainer);
            MachineHelper.chemistrySlots=new object[]{evapBattery,evapModule,evapContainer,evapDistillate};
            var evapLayout=new MachineryPanelRow();
            evapLayout.Panels.Add(new MachineryPanel { role="battery", content=evapBattery });
            evapLayout.Panels.Add(new MachineryPanel { role="input", content=evapContainer });
            evapLayout.Panels.Add(new MachineryPanel { role="output", content=evapDistillate });
            evap.contentWindow=new PixelWindow { visible=true, childElement=evapLayout };
            var evapView=new JObject();
            Expect(WorkstationAction(new JObject { ["action"]="inspect_window", ["owner_id"]=LocalId(evap) },evapView)=="observed" &&
                (int)evapView["window"]["controls"][0]["energy_kj"]==120 &&
                (double)evapView["window"]["controls"][1]["items"][0]["liquid"]["total_ml"]==20.0 &&
                (string)evapView["window"]["controls"][1]["items"][0]["liquid"]["primary_id"]=="caffeine" &&
                !(bool)evapView["window"]["distillate_capture"] &&
                (string)evapView["window"]["distillate_reason"]=="no_quality_bonus",
                "evaporator reports battery energy, container liquid summary, and distillate status");
            Liquid.Liquids.Remove(caffeine);
            evap.To(null); cell.To(null); beaker.To(null);
            fabric.To(null); pitcher.To(null); plate.To(null);
            machine.To(null); MachineHelper.chemistrySlots=null;
        }
    }
}
