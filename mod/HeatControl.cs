using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace WhiteDragon.Ledger
{
    public sealed partial class InventoryObserver
    {
        private sealed class HeatRun
        {
            public GameItem Plate, Bottle;
            public GameSlotInventory Slot;
            public MachineryRadioList Modes;
            public int Low, High, Target, ExpectedMode;
            public float Started;
            public int Seconds;
            public bool NativeTarget;
            public JObject Summary;
            public double Temperature => Bottle.GetTagReadonly("CONTAINER_TEMP_TAG").GetDouble();
            public int Mode => Plate.GetTagReadonly("HEATING_PLATE_STATE").GetInt();
            public int SetPoint => Plate.GetTagReadonly("CURRENT_TARGET_TEMP").GetInt();

            public bool SetMode(int mode)
            {
                if (Mode != mode)
                {
                    Modes.Select(mode, notify:true);
                    if (Mode != mode) return false;
                }
                ExpectedMode = mode;
                return true;
            }

            // First check reachability on the native five-degree grid before pressing either button.
            public bool ConfigureTarget()
            {
                Target = SetPoint;
                if (Target >= Low && Target <= High) { NativeTarget = true; return true; }
                if (MachineBlender.GetUpgradeCount(Plate) < 2) return Target >= Low;
                int step = Target < Low ? 5 : -5;
                int goal = Target;
                while (step > 0 ? goal < Low : goal > High)
                    goal = Math.Max(26, Math.Min(160, goal + step));
                if (goal < Low) goal = Target; // Use the existing higher setting with low/off control.
                string caption = LocHelper.GetLocalizedMechanic(step > 0 ? "mech_heating_plate_btn_plus" : "mech_heating_plate_btn_minus");
                MachineryButton button = null;
                WalkVisible(Plate.contentWindow.childElement,"content",null,null,(element,path,role,label) =>
                {
                    if (role == "target" && element is MachineryButton b && b.text == caption) button = b;
                });
                while (Target != goal)
                {
                    int next = Math.Max(26, Math.Min(160, Target + step));
                    if (button == null || button.isDisabled || !button.Select() || SetPoint != next) return false;
                    Target = next;
                }
                NativeTarget = Target >= Low && Target <= High;
                return true;
            }
        }

        private HeatRun heatRun;
        private JObject lastHeat;
        private float nextHeatCheck;

        private void EndHeat(string reason)
        {
            var run = heatRun;
            if (run == null) return;
            if (run.Mode != run.ExpectedMode) reason = "manual_override";
            bool owned = GeneralHelper.IsItemEffectivelyOwned(run.Plate);
            if (owned && reason != "manual_override" && !run.SetMode(0)) reason = "off_unconfirmed";
            run.Summary["status"] = reason;
            run.Summary["mode_index"] = run.Mode;
            run.Summary["elapsed_seconds"] = (int)(Time.realtimeSinceStartup-run.Started);
            if (run.Slot.childItem == run.Bottle) run.Summary["current_c"] = run.Temperature;
            lastHeat = run.Summary;
            heatRun = null;
            nextView = 0;
        }

        private void TickHeat()
        {
            var run = heatRun;
            if (run == null) return;
            if (Time.realtimeSinceStartup < nextHeatCheck) return;
            nextHeatCheck = Time.realtimeSinceStartup + 0.1f;
            if (run.Mode != run.ExpectedMode || run.SetPoint != run.Target) { EndHeat("manual_override"); return; }
            if (!GeneralHelper.IsItemEffectivelyOwned(run.Plate) ||
                (run.Plate.parentInventory != EmporiumEntry.Instance.invElement && run.Plate.parentInventory != EmporiumEntry.Instance.showcaseElement))
            { EndHeat("owner_changed"); return; }
            if (run.Slot.childItem != run.Bottle || WaterHelper.IsEmpty(run.Bottle)) { EndHeat("vessel_changed"); return; }
            if (Time.realtimeSinceStartup-run.Started >= run.Seconds) { EndHeat("finished"); return; }
            double temp = run.Temperature;
            int mode = run.ExpectedMode == 1 ? 1 : 0;
            if (run.NativeTarget || temp < run.Low) mode = 1;
            else if (temp >= (run.Low + run.High) / 2.0) mode = 0;
            if (!run.SetMode(mode)) EndHeat("control_unavailable");
        }

        private string HeatAction(JObject request, JObject receipt)
        {
            string mode = (string)request["mode"];
            if (mode == "stop" && heatRun != null && (string)heatRun.Summary["run_id"] == (string)request["run_id"])
                EndHeat("stopped");
            if (mode == "status" || mode == "stop")
            {
                var summary = heatRun?.Summary ?? lastHeat;
                if (summary == null) return "no_heat_run";
                if (mode == "stop" && (string)summary["run_id"] != (string)request["run_id"]) return "run_changed";
                receipt["heat"] = summary.DeepClone();
                if (heatRun != null && heatRun.Slot.childItem == heatRun.Bottle)
                    receipt["heat"]["current_c"] = heatRun.Temperature;
                return "observed";
            }
            if (mode != "start") return "invalid_action";
            if (request["run_id"]?.Type == JTokenType.String && (string)request["run_id"] != (string)lastHeat?["run_id"])
                return "run_changed";
            if (heatRun != null) { receipt["heat"] = heatRun.Summary.DeepClone(); return "already_running"; }
            int low = (int)request["min_c"], high = (int)request["max_c"], seconds = (int)request["seconds"];
            if (low < 26 || high > 160 || high < low || seconds < 1) return "invalid_condition";
            if (!itemsById.TryGetValue((int)request["owner_id"],out var plate) || plate.identifier != "heating_plate" ||
                !GeneralHelper.IsItemEffectivelyOwned(plate) || (plate.parentInventory != EmporiumEntry.Instance.invElement &&
                plate.parentInventory != EmporiumEntry.Instance.showcaseElement)) return "owner_unavailable";
            if (plate.contentWindow == null || !plate.contentWindow.IsVisible()) return "window_closed";
            if (plate.contentWindow.isLocked || GeneralHelper.IsItemLocked(plate) || Interactable.isPaused ||
                (phase == "OPEN" && !CanInteractWithStock(plate))) return "locked";
            var slot = MachineHelper.GetBodyStorage(plate,1) as GameSlotInventory;
            var bottle = slot?.childItem;
            if (bottle == null || LocalId(bottle) != (int)request["vessel_id"] || WaterHelper.IsEmpty(bottle)) return "vessel_changed";
            var panels = plate.contentWindow.childElement as MachineryPanelRow;
            var modes = panels?.Panels.FirstOrDefault(p=>p.role=="control")?.content as MachineryRadioList;
            if (modes == null) return "control_unavailable";
            var run = new HeatRun { Plate=plate, Bottle=bottle, Slot=slot, Modes=modes, Low=low, High=high,
                Started=Time.realtimeSinceStartup, Seconds=seconds };
            run.ExpectedMode = run.Mode;
            if (!run.ConfigureTarget()) { receipt["target_c"]=run.SetPoint; return "target_unavailable"; }
            run.Summary = new JObject { ["status"]="running", ["run_id"]=request["command_id"], ["owner_id"]=LocalId(plate),
                ["vessel_id"]=LocalId(bottle), ["min_c"]=low, ["max_c"]=high, ["seconds"]=seconds,
                ["target_c"]=run.Target, ["strategy"]=run.NativeTarget ? "native_target" : "low_off" };
            heatRun = run;
            nextHeatCheck = 0;
            TickHeat();
            receipt["heat"] = (heatRun?.Summary ?? lastHeat).DeepClone();
            return heatRun == null ? (string)lastHeat["status"] : "started";
        }
    }
}
