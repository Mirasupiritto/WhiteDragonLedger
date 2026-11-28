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
            var request = ReadInbox(inbox);
            if (request == null) return;
            string command = (string)request["command_id"];
            if (!AlreadyHandled("move", command, generation))
            {
                if (processedCommand != command)
                {
                    processedCommand = command;
                    // Keep an explicit unknown outcome if the native transfer throws.
                    processedReceipt = Receipt(request, "outcome_unknown");
                    try { processedReceipt = ExecuteMove(request); }
                    catch (Exception error) { Debug.LogException(error); }
                }
                StoreCommandReceipt("move", processedReceipt);
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

        private bool TryParseBayArea(string area, out GameItem bay, out GameInventory grid)
        {
            bay = null;
            grid = null;
            if (string.IsNullOrEmpty(area) || !area.StartsWith("bay:") ||
                !int.TryParse(area.Substring(4), out int ownerId) || ownerId <= 0 ||
                !itemsById.TryGetValue(ownerId, out var candidate) ||
                !GeneralHelper.IsItemEffectivelyOwned(candidate) ||
                !IsSmugglerBayContainer(candidate))
                return false;
            var bagGrid = candidate.contentWindow?.childElement as GameGridInventory;
            if (bagGrid == null) return false;
            bay = candidate;
            grid = bagGrid;
            return true;
        }

        private JObject ExecuteMove(JObject request)
        {
            if ((long)request["expires_ms"] < DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
                return Receipt(request, "expired");
            if ((string)request["generation"] != generation)
                return Receipt(request, "stale_observation");

            int id = (int)request["item_id"];
            string source = (string)request["source"], destination = (string)request["destination"];
            TryParseBayArea(source, out var sourceBay, out var sourceBayGrid);
            TryParseBayArea(destination, out var destBay, out var destBayGrid);
            bool bayMove = sourceBay != null || destBay != null;
            bool trashMove = destination == "trash" || source == "trash";
            bool stockMove=(source=="inventory" && destination=="showcase") || (source=="showcase" && destination=="inventory");
            bool hiddenMove = source == "hidden" || destination == "hidden";
            if ((stockMove || bayMove) && request["visit"]!=null && (string)request["visit"]!=visitId)
                return Receipt(request, visitId==null ? "customer_left" : "customer_replaced");
            itemsById.TryGetValue(id, out var item);
            var shop = EmporiumEntry.Instance;
            bool validArea = source == "inventory" || source == "showcase" || source == "front" || source == "back" || source == "hidden" || source == "trash" || sourceBay != null;
            bool allowAlreadyAtDest = destination == "inventory" || destination == "showcase" || destination == "hidden" || destination == "trash" || destBay != null ||
                (destination == "back" && request["visit"] != null && (string)request["visit"] == visitId);
            if (validArea && allowAlreadyAtDest && source != destination && (int)request["revision"] <= sequence &&
                item?.parentInventory != null &&
                (GeneralHelper.IsItemOwned(item) || (source == "front" && destination == "back")) &&
                ((destBayGrid != null && item.parentInventory == destBayGrid) ||
                 (destination == "trash" && shop.trashcanInvElement != null && item.parentInventory == shop.trashcanInvElement) ||
                 (areas.TryGetValue(item.parentInventory, out var current) && current == destination)))
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
            if (destination == "purifier" && request["machine_id"] != null &&
                itemsById.TryGetValue((int)request["machine_id"], out var portable) && portable?.identifier == "portable_water_purifier" &&
                GeneralHelper.IsItemEffectivelyOwned(portable) && StockLocation(portable) != null)
            {
                var redirect = Receipt(request, "wrong_operation");
                redirect["next_tool"] = "water_use";
                redirect["arguments"] = new JObject { ["source_id"]=LocalId(portable), ["target_id"]=id, ["mode"]="preview" };
                return redirect;
            }
            if (phase != "CLOSED" && phase != "MORNING")
            {
                bool allowedOpenMove = stockMove || (bayMove && !hiddenMove) || (trashMove && !hiddenMove) ||
                    (hiddenMove && phase == "OPEN" && !PlayerStore.Instance.IsClientPresent());
                if (!allowedOpenMove || phase != "OPEN")
                {
                    if (hiddenMove)
                    {
                        var blocked = Receipt(request, "transfer_blocked");
                        blocked["reason"] = phase == "OPEN" && PlayerStore.Instance.IsClientPresent()
                            ? "customer_present" : "hidden_grid_closed_only";
                        return blocked;
                    }
                    return Receipt(request, "shop_open");
                }
                var entry=EmporiumEntry.Instance;
                bool needInv = stockMove || source == "inventory" || destination == "inventory";
                bool needShowcase = stockMove || source == "showcase" || destination == "showcase";
                if ((needShowcase && !entry.showcaseWindow.IsVisible()) || (needInv && !entry.invWindow.IsVisible()) || Interactable.isPaused)
                {
                    var blocked=Receipt(request,"transfer_blocked");
                    blocked["reason"]=Interactable.isPaused ? "paused" : "stock_window_hidden";
                    return blocked;
                }
            }
            if (hiddenMove)
            {
                if (StorePoster.Instance == null || !StorePoster.Instance.isPulled)
                {
                    var blocked = Receipt(request, "transfer_blocked");
                    blocked["reason"] = "poster_down";
                    blocked["next_tool"] = "toggle_poster";
                    return blocked;
                }
            }
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
                bool validTrashRoute = trashMove && source != destination &&
                    (destination == "trash" ? (source == "inventory" || source == "showcase" || source == "hidden" || sourceBay != null) :
                     destination == "inventory");
                bool validHiddenRoute = hiddenMove && source != destination &&
                    (source == "hidden"
                        ? (destination == "inventory" || destination == "showcase" || destination == "trash" || destBay != null)
                        : (source == "inventory" || source == "showcase" || sourceBay != null));
                bool validBayRoute = bayMove && source != destination &&
                    (sourceBay != null ? (destination == "inventory" || destination == "showcase" || destination == "hidden" || destination == "trash" || destBay != null) :
                     (source == "inventory" || source == "showcase" || source == "hidden"));
                if (source == destination ||
                    (!validHiddenRoute && !validBayRoute && !validTrashRoute && ((source != "inventory" && source != "showcase") ||
                    (destination != "inventory" && destination != "showcase"))))
                    return Receipt(request, "invalid_route");
                if (sourceBayGrid != null)
                {
                    if (item.parentInventory != sourceBayGrid)
                        return Receipt(request, "source_changed");
                }
                else if (source == "trash")
                {
                    if (shop.trashcanInvElement == null || item.parentInventory != shop.trashcanInvElement)
                        return Receipt(request, "source_changed");
                }
                else if (!areas.TryGetValue(item.parentInventory, out var actual) || actual != source)
                    return Receipt(request, "source_changed");
            }
            if (!GeneralHelper.IsItemOwned(item)) return Receipt(request, "not_owned");
            if (destination == "trash")
            {
                if (item.IsTag(GeneralHelper.IMPORTANT_TAG))
                {
                    var blocked = Receipt(request, "transfer_blocked");
                    blocked["reason"] = "important_item";
                    return blocked;
                }
                if (item.IsTag(GeneralHelper.HAZARDOUS_WASTE_TAG) || IsHazardousWasteItem(item))
                {
                    var blocked = Receipt(request, "transfer_blocked");
                    blocked["reason"] = "hazardous_waste_cannot_trash";
                    return blocked;
                }
                if (shop.trashcanInvElement == null)
                    return Receipt(request, "transfer_blocked");
            }
            GameInventory target = loading ? (GameInventory)container :
                destBayGrid != null ? destBayGrid :
                destination == "trash" ? shop.trashcanInvElement :
                destination == "hidden" ? shop.hiddenElement :
                destination == "showcase" ? shop.showcaseElement : shop.invElement;
            if (item.parentInventory.IsRemoveLocked() || target.IsInsertLocked() || !item.MayRemove())
                return Receipt(request, "locked");

            if ((destination == "showcase" || destination == "hidden" || destination == "trash" || destBayGrid != null) && !GraphUtils.CanAccept(target, item))
            {
                string fullCode = destination == "showcase" ? "showcase_full" : "no_space";
                var full = Receipt(request, fullCode);
                full["reason"] = destination == "showcase" ? "showcase_full" : destination == "trash" ? "trash_full" : destBayGrid != null ? "bay_full" : "hidden_full";
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
            var sourceInv = item.parentInventory;
            if (loading || destination == "hidden" || destination == "trash" || destBayGrid != null)
            {
                if (!GraphUtils.CanAccept(container ?? target, item)) return Receipt(request, loading ? "slot_rejected" : "no_space");
                GraphUtils.TryAcceptOnce(container ?? target, item);
            }
            else if (destination == "showcase") ModHelper.MoveItemToDisplayCase(item);
            else ModHelper.MoveItemToInventory(item);
            changed.Add(item);
            ScanKnownIdentities();
            FlushChanges();
            string observed = null;
            bool consumedIntoTrash = destination == "trash" && item.parentInventory == null &&
                sourceInv != null && !sourceInv.childItems.Contains(item);
            if (destBayGrid != null && item.parentInventory == destBayGrid) observed = destination;
            else if (destination == "trash" && ((shop.trashcanInvElement != null && item.parentInventory == shop.trashcanInvElement) || consumedIntoTrash)) observed = "trash";
            else if (sourceBayGrid != null && item.parentInventory == sourceBayGrid) observed = source;
            else if (source == "trash" && shop.trashcanInvElement != null && item.parentInventory == shop.trashcanInvElement) observed = "trash";
            else if (item.parentInventory != null) areas.TryGetValue(item.parentInventory, out observed);
            if (container != null && item.parentInventory == container) observed = "purifier";
            bool sourceCleared = container == null || (loading ? container.childItem == item : container.childItem == null);
            bool quantityMatches = item.unitCount == quantityBefore || consumedIntoTrash;
            string status = sourceCleared && observed == destination && quantityMatches ? "moved" :
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
            if (sourceBay != null) result["from_container"] = FromContainerJson(sourceBay);
            result["actual_area"] = observed;
            result["quantity"] = consumedIntoTrash ? quantityBefore : item.unitCount;
            AttachReceiptIdentityChanges(result);
            return result;
        }
    }
}
