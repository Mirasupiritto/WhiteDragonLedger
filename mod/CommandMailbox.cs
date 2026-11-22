using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace WhiteDragon.Ledger
{
    public sealed partial class InventoryObserver
    {
        private sealed class CommandMailbox
        {
            private JObject lastReceipt;

            public bool Process(string channel, string generation, int slot, Func<JObject,JObject,string> act)
            {
                string inbox = channel + "-" + generation + ".json";
                if (!ModHelper.ModDataFileExists(ModId, inbox)) return false;
                var request = JObject.Parse(ModHelper.ReadModDataFileText(ModId, inbox));
                string id = (string)request["command_id"];
                string file = "receipt-" + id + ".json";
                if (Guid.TryParseExact(id, "N", out var key) && id == key.ToString("N") && !ModHelper.ModDataFileExists(ModId, file))
                {
                    if (id != (string)lastReceipt?["command_id"])
                    {
                        lastReceipt = new JObject { ["generation"]=generation, ["slot"]=slot, ["command_id"]=id, ["status"]="outcome_unknown" };
                        try { lastReceipt["status"] = act(request, lastReceipt); }
                        catch (Exception error) { Debug.LogException(error); }
                    }
                    Write(file, lastReceipt);
                }
                ModHelper.DeleteModDataFile(ModId, inbox);
                return true;
            }
        }
    }
}
