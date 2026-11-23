using System.Linq;
using Newtonsoft.Json.Linq;
namespace WhiteDragon.Ledger
{
    public static class MachineBlender { public static int GetUpgradeCount(GameItem item)=>item.associatedId; }
    public static class MachineChemFinisher { public static bool CanOutput(GameItem machine,GameSlotInventory slot)=>slot.childItem!=null && slot.childItem.volume>=2000; }
    public static class Liquid { public static string GetCurrentPartFromId(string id)=>id+"_part"; }
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
            machine.bookTagValue="test_product";
            var button=new MachineryButton { text=LocHelper.GetLocalizedMechanic("mech_chem_finisher_btn_output") };
            button.onPress=()=>{source.volume-=2000;new GameItem{identifier="test_product"}.To(output);};
            var receipt=new JObject();
            Expect(ProduceBatch(machine,button,3,receipt)=="batch_incomplete" && (int)receipt["produced"]==2 &&
                source.volume==500 && (string)receipt["reason"]=="recipe_not_ready","production stops at the real input limit");
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
            fabric.To(null); pitcher.To(null); plate.To(null);
            machine.To(null); MachineHelper.chemistrySlots=null;
        }
    }
}
