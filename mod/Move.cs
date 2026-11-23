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
            var result = new JObject {
                ["command_id"] = (string)request["command_id"], ["status"] = status,
                ["generation"] = generation, ["slot"] = slot, ["revision"] = sequence,
                ["phase"] = phase, ["load_scans"] = loadScans, ["full_scans"] = fullScans,
                ["item_id"] = (int)request["item_id"], ["from"] = (string)request["source"],
                ["requested_area"] = (string)request["destination"],
                ["expected_revision"] = (int)request["revision"]
            };
            if (request["visit"] != null) result["visit"] = request["visit"];
            if (request["machine_id"] != null) result["machine_id"] = (int)request["machine_id"];
            return result;
        }

        private JObject ExecuteMove(JObject request)
        {
            if ((long)request["expires_ms"] < DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
                return Receipt(request, "expired");
            if ((string)request["generation"] != generation)
                return Receipt(request, "stale_observation");

            int id = (int)request["item_id"];
            string source = (string)request["source"], destination = (string)request["destination"];
            itemsById.TryGetValue(id, out var item);
            bool validArea = source == "inventory" || source == "showcase" || source == "front" || source == "back";
            bool allowAlreadyAtDest = destination == "inventory" || destination == "showcase" ||
                (destination == "back" && request["visit"] != null && (string)request["visit"] == visitId);
            if (validArea && allowAlreadyAtDest && source != destination && (int)request["revision"] <= sequence &&
                item?.parentInventory != null &&
                (GeneralHelper.IsItemOwned(item) || (source == "front" && destination == "back")) &&
                areas.TryGetValue(item.parentInventory, out var current) && current == destination)
            {
                var settled = Receipt(request, "already_in_destination");
                settled["actual_area"] = destination;
                settled["quantity"] = item.unitCount;
                return settled;
            }
            if ((int)request["revision"] != sequence) return Receipt(request, "stale_observation");
            if (item == null || item.parentInventory == null)
                return Receipt(request, "source_changed");
            if (source == "front" || source == "back" || destination == "front" || destination == "back")
                return MoveCounterStock(request,item,source,destination);
            if (phase != "CLOSED" && phase != "MORNING") return Receipt(request,"shop_open");
            GameSlotInventory container = null;
            GameItem machine = null;
            bool loading = destination == "purifier";
            if (source == "purifier" || loading)
            {
                if (request["machine_id"] == null || (loading ? source != "inventory" && source != "showcase" : destination != "inventory"))
                    return Receipt(request, "invalid_route");
                if (!itemsById.TryGetValue((int)request["machine_id"], out machine) || !purifiers.Contains(machine) ||
                    machine.parentInventory == null || !areas.ContainsKey(machine.parentInventory) ||
                    !GeneralHelper.IsItemOwned(machine)) return Receipt(request, "machine_changed");
                container = MachineHelper.GetBodyStorage(machine, 2) as GameSlotInventory;
                if (container == null) return Receipt(request, "machine_changed");
                if (loading)
                {
                    if (container.childItem == item && item.parentInventory == container)
                    {
                        var settled = Receipt(request, "already_in_destination");
                        settled["actual_area"] = "purifier";
                        settled["quantity"] = item.unitCount;
                        return settled;
                    }
                    if (container.childItem != null) return Receipt(request, "purifier_occupied");
                    if (!areas.TryGetValue(item.parentInventory, out var area) || area != source)
                        return Receipt(request, "source_changed");
                    if (!item.IsTag("LIQUID_CONTAINER_TAG") || item.IsTag("WINE_CONTAINER_TAG"))
                        return Receipt(request, "not_water_bottle");
                }
                else if (container.childItem != item || item.parentInventory != container)
                    return Receipt(request, "source_changed");
            }
            else
            {
                if (source == destination || (source != "inventory" && source != "showcase") ||
                    (destination != "inventory" && destination != "showcase"))
                    return Receipt(request, "invalid_route");
                if (!areas.TryGetValue(item.parentInventory, out var actual) || actual != source)
                    return Receipt(request, "source_changed");
            }
            if (!GeneralHelper.IsItemOwned(item)) return Receipt(request, "not_owned");
            var shop = EmporiumEntry.Instance;
            GameInventory target = loading ? (GameInventory)container :
                destination == "showcase" ? shop.showcaseElement : shop.invElement;
            if (item.parentInventory.IsRemoveLocked() || target.IsInsertLocked() || !item.MayRemove())
                return Receipt(request, "locked");

            if (destination == "showcase" && !GraphUtils.CanAccept(target, item))
            {
                var full = Receipt(request, "showcase_full");
                full["reason"] = "showcase_full";
                full["actual_area"] = source;
                AttachSpaceFailure(item, target, full);
                return full;
            }
            if (!loading && destination == "inventory" && shop.invElement != null && !GraphUtils.CanAccept(shop.invElement, item))
            {
                if (!IsSpaceFailure(shop.invElement, item)) return Receipt(request, "transfer_blocked");
                var full = Receipt(request, "no_space");
                full["reason"] = "inventory_full";
                full["actual_area"] = source;
                AttachSpaceFailure(item, shop.invElement, full);
                return full;
            }

            // The native placement routine enforces available space and slot rules.
            int quantityBefore = item.unitCount;
            if (loading)
            {
                if (!GraphUtils.CanAccept(container, item)) return Receipt(request, "slot_rejected");
                GraphUtils.TryAcceptOnce(container, item);
            }
            else if (destination == "showcase") ModHelper.MoveItemToDisplayCase(item);
            else ModHelper.MoveItemToInventory(item);
            changed.Add(item);
            ScanKnownIdentities();
            FlushChanges();
            string observed = null;
            if (item.parentInventory != null) areas.TryGetValue(item.parentInventory, out observed);
            if (container != null && item.parentInventory == container) observed = "purifier";
            bool sourceCleared = container == null || (loading ? container.childItem == item : container.childItem == null);
            string status = sourceCleared && observed == destination && item.unitCount == quantityBefore ? "moved" :
                observed == source && item.unitCount == quantityBefore ? "not_moved" : "outcome_unknown";
            var spaceTarget = !loading && destination == "showcase" ? target : !loading && destination == "inventory" ? shop.invElement : null;
            if (status == "not_moved" && spaceTarget != null && !GraphUtils.CanAccept(spaceTarget, item) && IsSpaceFailure(spaceTarget, item))
                status = destination == "showcase" ? "showcase_full" : "no_space";
            var result = Receipt(request, status);
            if (status == "showcase_full" || status == "no_space")
            {
                result["reason"] = destination == "showcase" ? "showcase_full" : "inventory_full";
                AttachSpaceFailure(item, spaceTarget, result);
            }
            if (container != null)
            {
                result["machine_key"] = machine.uniqueId;
                result["bottle_key"] = item.uniqueId;
                result["bottle_id"] = id;
                if (loading)
                {
                    result["slot_bottle_id"] = container.childItem == null ? JValue.CreateNull() : (JToken)LocalId(container.childItem);
                    result["machine"] = machine.GetDisplayName(true);
                }
                else result["source_slot_empty"] = container.childItem == null;
                result["available_strips"] = AvailableStrips();
                nextView = 0;
                RefreshDesk();
            }
            else
            {
                result["name"] = item.GetDisplayName(true);
                result["kind"] = item.identifier;
            }
            if (BottleLabel(item) is string label) result["bottle_label"] = label;
            result["actual_area"] = observed;
            result["quantity"] = item.unitCount;
            AttachReceiptIdentityChanges(result);
            return result;
        }
    }
}
