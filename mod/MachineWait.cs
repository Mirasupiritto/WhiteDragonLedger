using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace WhiteDragon.Ledger
{
    public sealed partial class InventoryObserver
    {
        private sealed class MachineWait
        {
            public JObject Receipt;
            public GameItem Owner, Vessel;
            public GameInventory Area;
            public PixelWindow Window;
            public MachineryProgressBar Meter;
            public string Control, Inbox;
            public double Threshold;
            public bool AtLeast;
            public float Started, Timeout;
        }

        private MachineWait machineWait;
        private float nextMachinePoll;

        private void FinishMachineWait(string status)
        {
            if (machineWait == null) return;
            var receipt = machineWait.Receipt;
            receipt["status"] = status;
            receipt["elapsed_ms"] = (long)((Time.realtimeSinceStartup - machineWait.Started) * 1000);
            receipt["revision"] = sequence;
            StoreCommandReceipt("watch", receipt);
            ModHelper.DeleteModDataFile(ModId, machineWait.Inbox);
            machineWait = null;
        }

        private string StartMachineWait(JObject request, string inbox)
        {
            var watch = new MachineWait { Started=Time.realtimeSinceStartup, Inbox=inbox,
                Receipt=new JObject { ["command_id"]=request["command_id"], ["generation"]=generation, ["slot"]=slot,
                    ["owner_id"]=request["owner_id"], ["control"]=request["control"] } };
            machineWait = watch;
            var timeout = request["timeout"];
            var threshold = request["threshold"];
            string direction = (string)request["direction"];
            if ((timeout?.Type != JTokenType.Integer && timeout?.Type != JTokenType.Float) ||
                (threshold?.Type != JTokenType.Integer && threshold?.Type != JTokenType.Float) ||
                !((double)timeout >= 0 && (double)timeout <= 60) ||
                double.IsNaN((double)threshold) || double.IsInfinity((double)threshold) ||
                (direction != "at_least" && direction != "at_most")) return "invalid_condition";
            if (request["expires_ms"]?.Type != JTokenType.Integer || (long)request["expires_ms"] < DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) return "expired";
            if ((string)request["generation"] != generation || (int?)request["revision"] != sequence) return "stale_observation";
            if (request["owner_id"]?.Type != JTokenType.Integer || !itemsById.TryGetValue((int)request["owner_id"], out watch.Owner))
                return "owner_unavailable";
            var shop = EmporiumEntry.Instance;
            watch.Area = watch.Owner.parentInventory;
            if ((watch.Area != shop.invElement && watch.Area != shop.showcaseElement) || !GeneralHelper.IsItemEffectivelyOwned(watch.Owner))
                return "owner_unavailable";
            watch.Window = watch.Owner.contentWindow;
            if (watch.Window == null || !watch.Window.IsVisible()) return "window_closed";
            watch.Control = (string)request["control"];
            watch.Timeout = (float)request["timeout"];
            watch.Threshold = (double)request["threshold"];
            watch.AtLeast = (string)request["direction"] == "at_least";
            if (watch.Control == "current_c")
            {
                if (watch.Owner.identifier != "heating_plate") return "reading_unavailable";
                watch.Vessel = (MachineHelper.GetBodyStorage(watch.Owner, 1) as GameSlotInventory)?.childItem;
                if (watch.Vessel == null) return "vessel_missing";
                watch.Receipt["vessel_id"] = LocalId(watch.Vessel);
            }
            return null;
        }

        private string SampleMachineWait()
        {
            var watch = machineWait;
            if (watch.Owner.contentWindow != watch.Window || !watch.Window.IsVisible()) return "window_closed";
            if (watch.Owner.parentInventory != watch.Area || !GeneralHelper.IsItemEffectivelyOwned(watch.Owner)) return "owner_changed";
            double value;
            if (watch.Vessel != null)
            {
                if ((MachineHelper.GetBodyStorage(watch.Owner, 1) as GameSlotInventory)?.childItem != watch.Vessel)
                    return "vessel_changed";
                value = watch.Vessel.GetTagReadonly("CONTAINER_TEMP_TAG").GetDouble();
            }
            else
            {
                MachineryProgressBar meter = null;
                WalkVisible(watch.Window.childElement, "content", null, null, (element, path, role, label) =>
                {
                    if (path == watch.Control) meter = element as MachineryProgressBar;
                });
                if (meter == null || (watch.Meter != null && !ReferenceEquals(meter, watch.Meter))) return "reading_unavailable";
                watch.Meter = meter;
                value = meter.value;
            }
            watch.Receipt["value"] = value;
            if (watch.AtLeast ? value >= watch.Threshold : value <= watch.Threshold) return "threshold_reached";
            return Time.realtimeSinceStartup - watch.Started >= watch.Timeout ? "timed_out" : null;
        }

        private void CheckMachineWait()
        {
            if (Time.realtimeSinceStartup < nextMachinePoll) return;
            nextMachinePoll = Time.realtimeSinceStartup + 0.1f;
            if (machineWait == null)
            {
                string inbox = "watch-" + generation + ".json";
                if (!ModHelper.ModDataFileExists(ModId, inbox)) return;
                var request = ReadInbox(inbox);
                if (request == null) return;
                string id = (string)request["command_id"];
                if (AlreadyHandled("watch", id, generation)) { ModHelper.DeleteModDataFile(ModId, inbox); return; }
                string blocked = StartMachineWait(request, inbox);
                if (blocked != null) { FinishMachineWait(blocked); return; }
            }
            string status = SampleMachineWait();
            if (status != null) FinishMachineWait(status);
        }
    }
}
