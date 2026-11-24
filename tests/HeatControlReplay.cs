using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace WhiteDragon.Ledger
{
    public class MachineryRadioList : MachineryStack
    {
        public Action<int> OnSelect;
        public MachineryRadioList Select(int index, bool notify=false) { if (notify) OnSelect(index); return this; }
    }

    public sealed partial class InventoryObserver
    {
        private void ReplayHeatControl()
        {
            var plate=Item(8120,"heating_plate",0);
            var bottle=Item(8121,"bottle",0);
            var slot=new GameSlotInventory();
            bottle.To(slot);
            bottle.volume=250000;
            MachineHelper.chemistrySlots=new object[]{null,slot};
            var modeTag=new GameItem();
            var targetTag=new GameItem {associatedId=99};
            plate.scratchTagReader=key=>key=="HEATING_PLATE_STATE" ? modeTag : targetTag;
            var modes=new MachineryRadioList {OnSelect=index=>modeTag.associatedId=index};
            var minus=new MachineryButton {text=LocHelper.GetLocalizedMechanic("mech_heating_plate_btn_minus")};
            minus.onPress=()=>targetTag.associatedId=Math.Max(26,targetTag.associatedId-5);
            var row=new MachineryPanelRow();
            row.Panels.Add(new MachineryPanel {role="control", content=modes});
            row.Panels.Add(new MachineryPanel {role="target", content=minus});
            plate.contentWindow=new PixelWindow {visible=true,childElement=row};
            var request=new JObject { ["action"]="heat_control", ["mode"]="start", ["generation"]=generation,
                ["revision"]=sequence, ["expires_ms"]=long.MaxValue, ["owner_id"]=LocalId(plate), ["vessel_id"]=LocalId(bottle),
                ["command_id"]=Guid.NewGuid().ToString("N"), ["min_c"]=45, ["max_c"]=50, ["seconds"]=10 };
            void Tick(double temp) { Time.realtimeSinceStartup++; bottle.associatedId=(int)temp; TickHeat(); }
            bottle.associatedId=40;
            var receipt=new JObject();
            Expect(ActAtCounter(request,receipt)=="started" && modeTag.associatedId==1 &&
                (string)receipt["heat"]["strategy"]=="low_off","one request starts native low heat");
            plate.contentWindow.visible=false;
            Tick(49);
            Expect(modeTag.associatedId==0 && heatRun!=null,"regulates in background with window closed");
            Tick(44);
            Expect(modeTag.associatedId==1,"resumes when below range");
            int invRevision=sequence;
            Time.realtimeSinceStartup+=12;
            Tick(46);
            Expect(heatRun==null && (string)lastHeat["status"]=="finished" && modeTag.associatedId==0 &&
                sequence==invRevision,"timeout shuts down without inventory revision");
            plate.contentWindow.visible=true;
            Expect((bool)lastHeat["reached_range"] && (double)lastHeat["in_range_seconds"]>0 && lastHeat["warning"]==null,
                "finished run reports time in range");
            var hot=(JObject)request.DeepClone();
            hot["command_id"]=Guid.NewGuid().ToString("N"); hot["min_c"]=82; hot["max_c"]=99; hot["seconds"]=5;
            bottle.associatedId=54;
            receipt=new JObject();
            Expect(ActAtCounter(hot,receipt)=="started" && (string)receipt["heat"]["strategy"]=="native_target" &&
                modeTag.associatedId==2,"fixed plate below range uses native high");
            Tick(85);
            Expect(modeTag.associatedId==1,"inside range drops to low and lets the native target cap");
            Time.realtimeSinceStartup+=6;
            Tick(90);
            Expect((string)lastHeat["status"]=="finished" && (bool)lastHeat["reached_range"] && (double)lastHeat["peak_c"]==85,
                "native run records peak");
            hot["command_id"]=Guid.NewGuid().ToString("N");
            bottle.associatedId=60;
            ActAtCounter(hot,new JObject());
            Tick(70);
            Time.realtimeSinceStartup+=6;
            Tick(75);
            Expect((string)lastHeat["status"]=="finished" && !(bool)lastHeat["reached_range"] &&
                (string)lastHeat["warning"]=="range_not_reached","timeout below range is flagged");
            var cold=(JObject)request.DeepClone();
            cold["command_id"]=Guid.NewGuid().ToString("N");
            bottle.associatedId=26;
            Expect(ActAtCounter(cold,new JObject())=="started" && modeTag.associatedId==2,"far below a low/off range starts on high");
            Tick(40);
            Expect(modeTag.associatedId==1,"near the range eases to low to avoid overshoot");
            var halt=(JObject)cold.DeepClone(); halt["mode"]="stop"; halt["run_id"]=cold["command_id"];
            ActAtCounter(halt,new JObject());
            plate.associatedId=2;
            bottle.associatedId=46;
            request["command_id"]=Guid.NewGuid().ToString("N");
            receipt=new JObject();
            Expect(ActAtCounter(request,receipt)=="started" && targetTag.associatedId==49 && minus.presses==10 &&
                (string)receipt["heat"]["strategy"]=="native_target","upgraded plate uses actual native buttons once");
            var newer=(string)receipt["heat"]["run_id"];
            var stop=(JObject)request.DeepClone(); stop["mode"]="stop"; stop["run_id"]="earlier";
            Expect(ActAtCounter(stop,new JObject())=="run_changed" && heatRun!=null,"old stop cannot affect new run");
            modeTag.associatedId=2;
            Tick(50);
            Expect(heatRun==null && (string)lastHeat["status"]=="manual_override" && modeTag.associatedId==2,
                "manual high setting preserved");
            request["command_id"]=Guid.NewGuid().ToString("N");
            Expect(ActAtCounter(request,new JObject())=="started","start after manual takeover");
            bottle.To(null);
            Tick(50);
            Expect(heatRun==null && (string)lastHeat["status"]=="vessel_changed" && modeTag.associatedId==0,
                "removing bottle shuts original plate off");
            Expect(CounterView(consumeHeat: true)["heat"]!=null && CounterView(consumeHeat: true)["heat"]==null,
                "finished heat appears only once in counter views");
            plate.To(null); MachineHelper.chemistrySlots=null; lastHeat=null;
        }
    }
}
