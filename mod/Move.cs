using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace WhiteDragon.Ledger
{
    public sealed partial class InventoryObserver
    {
        private float nextCheck;
        private string processedCommand;
        private JObject processedReceipt;

        // Run on the game thread after manual changes have reached the journal.
        private void CheckMove()
        {
            if (Time.realtimeSinceStartup < nextCheck) return;
            nextCheck = Time.realtimeSinceStartup + 0.15f;
            var inbox = "move-" + generation + ".json";
            if (!ModHelper.ModDataFileExists(ModId, inbox)) return;
            var request = JObject.Parse(ModHelper.ReadModDataFileText(ModId, inbox));
            string command = (string)request["command_id"];
            if (!Guid.TryParseExact(command, "N", out var parsed) || parsed.ToString("N") != command)
            {
                ModHelper.DeleteModDataFile(ModId, inbox);
                return;
            }
            var reply = "receipt-" + command + ".json";
            if (!ModHelper.ModDataFileExists(ModId, reply))
            {
                if (processedCommand != command)
                {
                    processedCommand = command;
                    // Keep an explicit unknown outcome if the native transfer throws.
                    processedReceipt = Receipt(request, "outcome_unknown");
                    try { processedReceipt = ExecuteMove(request); }
                    catch (Exception error) { Debug.LogException(error); }
                }
                Write(reply, processedReceipt);
            }
            ModHelper.DeleteModDataFile(ModId, inbox);
        }

        private JObject Receipt(JObject request, string status)
        {
            return new JObject {
                ["command_id"] = (string)request["command_id"], ["status"] = status,
                ["generation"] = generation, ["slot"] = slot, ["revision"] = sequence,
                ["phase"] = phase, ["load_scans"] = loadScans, ["full_scans"] = fullScans,
                ["item_id"] = (int)request["item_id"], ["from"] = (string)request["source"],
                ["requested_area"] = (string)request["destination"],
                ["expected_revision"] = (int)request["revision"]
            };
        }

        private JObject ExecuteMove(JObject request)
        {
            if ((long)request["expires_ms"] < DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
                return Receipt(request, "expired");
            if ((string)request["generation"] != generation || (int)request["revision"] != sequence)
                return Receipt(request, "stale_observation");
            if (phase != "CLOSED" && phase != "MORNING") return Receipt(request, "shop_open");

            int id = (int)request["item_id"];
            string source = (string)request["source"], destination = (string)request["destination"];
            if (source == destination || (source != "inventory" && source != "showcase") ||
                (destination != "inventory" && destination != "showcase"))
                return Receipt(request, "invalid_route");
            if (!itemsById.TryGetValue(id, out var item) || item.parentInventory == null ||
                !areas.TryGetValue(item.parentInventory, out var actual) || actual != source)
                return Receipt(request, "source_changed");
            if (!GeneralHelper.IsItemOwned(item)) return Receipt(request, "not_owned");
            var shop = EmporiumEntry.Instance;
            var target = destination == "showcase" ? shop.showcaseElement : shop.invElement;
            if (item.parentInventory.IsRemoveLocked() || target.IsInsertLocked() || !item.MayRemove())
                return Receipt(request, "locked");

            // The native placement routine enforces available space and slot rules.
            int quantityBefore = item.unitCount;
            if (destination == "showcase") ModHelper.MoveItemToDisplayCase(item);
            else ModHelper.MoveItemToInventory(item);
            changed.Add(item);
            FlushChanges();
            string observed = null;
            if (item.parentInventory != null) areas.TryGetValue(item.parentInventory, out observed);
            string status = observed == destination && item.unitCount == quantityBefore ? "moved" :
                observed == source && item.unitCount == quantityBefore ? "not_moved" : "outcome_unknown";
            var result = Receipt(request, status);
            result["name"] = item.GetDisplayName(true);
            result["kind"] = item.identifier;
            result["actual_area"] = observed;
            result["quantity"] = item.unitCount;
            return result;
        }
    }
}
