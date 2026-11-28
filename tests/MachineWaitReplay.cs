using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace WhiteDragon.Ledger
{
    public sealed partial class InventoryObserver
    {
        private void ReplayMachineWaitAndInbox()
        {
            var plate = Item(8110,"heating_plate",0);
            var vessel = Item(8111,"bottle",0);
            var container = new GameSlotInventory();
            vessel.To(container);
            MachineHelper.chemistrySlots = new object[] {null, container};
            var meter = new MachineryProgressBar();
            plate.contentWindow = new PixelWindow { visible=true, childElement=meter };
            string inbox = "watch-" + generation + ".json";

            void Tick() { Time.realtimeSinceStartup += 1; CheckMachineWait(); }
            string Start(string control, int threshold, int timeout=30)
            {
                string id = Guid.NewGuid().ToString("N");
                Write(inbox,new JObject { ["command_id"]=id, ["generation"]=generation, ["revision"]=sequence,
                    ["owner_id"]=LocalId(plate), ["expires_ms"]=long.MaxValue, ["control"]=control,
                    ["direction"]="at_least", ["threshold"]=threshold, ["timeout"]=timeout });
                Tick();
                return "receipt-"+id+".json";
            }
            JObject Result(string file) => JObject.Parse(ModHelper.ReadModDataFileText(ModId,file));

            ModHelper.WriteModDataFileText(ModId,inbox,"{");
            Tick();
            Expect(!ModHelper.ModDataFileExists(ModId,inbox),"bad inbox releases channel");
            vessel.associatedId=40;
            var file=Start("current_c",45);
            Expect(!ModHelper.ModDataFileExists(ModId,file),"no intermediate receipts");
            vessel.associatedId=46;
            Tick();
            var result=Result(file);
            Expect((string)result["status"]=="threshold_reached" && (double)result["value"]==46 &&
                (int)result["vessel_id"]==LocalId(vessel) && (int)result["elapsed_ms"]>=1000,"one final thermometer reading");
            file=Start("current_c",55);
            vessel.To(null);
            Tick();
            Expect((string)Result(file)["status"]=="vessel_changed","never follow a different vessel");
            file=Start("content",1,0);
            Expect((string)Result(file)["status"]=="timed_out" && (double)Result(file)["value"]==0,"timeout includes last progress");
            file=Start("content",1);
            plate.contentWindow.visible=false;
            Tick();
            Expect((string)Result(file)["status"]=="window_closed","window closed during wait");
            plate.To(null);
            MachineHelper.chemistrySlots=null;
        }
    }
}
