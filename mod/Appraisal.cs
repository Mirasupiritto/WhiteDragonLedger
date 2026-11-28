using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace WhiteDragon.Ledger
{
    public sealed partial class InventoryObserver
    {
        private static JObject VisualFeatures(GameItem item)
        {
            if (item == null) return null;
            if (item.IsTag("HAND_TAG"))
            {
                return new JObject
                {
                    ["type"] = "hand",
                    ["material"] = item.GetTagReadonly("HAND_MATERIAL").GetString(),
                    ["interface"] = item.GetTagReadonly("HAND_INTERFACE").GetString(),
                    ["interface_marking"] = item.GetTagReadonly("HAND_INTERFACE_MARKING").GetString(),
                    ["condition"] = item.GetTagReadonly("HAND_CONDITION").GetString(),
                    ["addon_armored"] = string.Equals(item.GetTagReadonly("HAND_ADDON_ARMORED").GetString(), "ARMORED", StringComparison.Ordinal),
                    ["addon_bladed"] = string.Equals(item.GetTagReadonly("HAND_ADDON_BLADED").GetString(), "BLADED", StringComparison.Ordinal)
                };
            }
            if (item.IsTag("RIBWICH_TAG"))
            {
                int wrap = item.GetTagReadonly("RIBWICH_WRAP_TAG").GetInt();
                int pickles = item.GetTagReadonly("RIBWICH_PICKLES_TAG").GetInt();
                int meat = item.GetTagReadonly("RIBWICH_MEAT_TAG").GetInt();
                int bun = item.GetTagReadonly("RIBWICH_BUN_TAG").GetInt();
                int davreLogo = item.GetTagReadonly("RIBWICH_DAVRE_LOGO_TAG").GetInt();
                int ribwichLogo = item.GetTagReadonly("RIBWICH_RIBWICH_LOGO_TAG").GetInt();
                return new JObject
                {
                    ["type"] = "ribwich",
                    ["wrap"] = wrap == -1 ? "none" : wrap == 0 ? "sealed" : wrap == 1 ? "open_left" : wrap == 2 ? "open_right" : wrap.ToString(),
                    ["pickles"] = pickles == -1 ? "none" : pickles == 0 ? "normal" : pickles == 1 ? "extra" : pickles.ToString(),
                    ["meat"] = meat == 0 ? "normal" : meat == 1 ? "double" : meat.ToString(),
                    ["bun"] = bun == 0 ? "normal" : bun == 1 ? "deluxe" : bun.ToString(),
                    ["davre_logo_valid"] = davreLogo == 0,
                    ["davre_logo_raw"] = davreLogo,
                    ["ribwich_logo_valid"] = ribwichLogo == 0,
                    ["ribwich_logo_raw"] = ribwichLogo,
                    ["production_year"] = item.GetTagReadonly("RIBWICH_PRODUCTION_YEAR_TAG").GetInt(),
                    ["production_month"] = item.GetTagReadonly("RIBWICH_PRODUCTION_MONTH_TAG").GetInt(),
                    ["facility_number"] = item.GetTagReadonly("RIBWICH_FACILITY_NUMBER_TAG").GetInt()
                };
            }
            if (item.IsTag("PLASMA_CELL_TAG"))
            {
                return new JObject
                {
                    ["type"] = "plasma_fuel",
                    ["casing"] = item.GetTagReadonly("PLASMA_CASING").GetString(),
                    ["crack"] = item.GetTagReadonly("PLASMA_CASING_CONDITION_CRACK").GetString(),
                    ["wear"] = item.GetTagReadonly("PLASMA_CASING_CONDITION_WEAR").GetString(),
                    ["uncovered"] = item.GetTagReadonly("PLASMA_CASING_CONDITION_UNCOVERED").GetString(),
                    ["reinforced"] = item.GetTagReadonly("PLASMA_CASING_CONDITION_REINFORCED").GetString(),
                    ["serial"] = item.GetTagReadonly("PLASMA_SERIAL").GetString(),
                    ["color"] = item.GetTagReadonly("PLASMA_COLOR").GetString(),
                    ["seal"] = item.GetTagReadonly("PLASMA_SEAL").GetString()
                };
            }
            return null;
        }

        private JArray AppraisalItems()
        {
            var items = new JArray();
            var shop = EmporiumEntry.Instance;
            if (shop == null) return items;
            void Scan(GameInventory inv, string area)
            {
                if (inv == null) return;
                foreach (var item in inv.childItems)
                {
                    if (item == null || GeneralHelper.IsItemOwned(item) || !item.IsTag("APPRAISAL_ITEM")) continue;
                    var row = new JObject
                    {
                        ["item_id"] = LocalId(item),
                        ["name"] = item.GetDisplayName(true),
                        ["kind"] = item.identifier,
                        ["area"] = area,
                        ["default_estimate"] = item.GetTagReadonly("APPRAISAL_DEFAULT_VALUE").GetInt()
                    };
                    if (VisualFeatures(item) is JObject features)
                        row["visual_features"] = features;
                    items.Add(row);
                }
            }
            Scan(shop.frontInvinvElement, "front");
            Scan(shop.backInvinvElement, "back");
            return items;
        }

        private JObject AppraisalView()
        {
            var items = AppraisalItems();
            return new JObject
            {
                ["items"] = items,
                ["remaining_count"] = items.Count,
                ["impatience"] = merchant?.clientImpatience ?? 0,
                ["business_completed"] = merchant?.isBusinessComplete ?? false
            };
        }

        private static JObject AppraisalGuildView()
        {
            var store = PlayerStore.Instance;
            int rank = AppraisalRankHelper.GetRank();
            var unlocked = new JArray();
            var list = AppraisalItemRegistry.GetUnlocked(rank);
            if (list != null)
            {
                foreach (var type in list)
                {
                    if (type == null) continue;
                    unlocked.Add(new JObject
                    {
                        ["id"] = type.id,
                        ["name"] = type.GetDisplayName(),
                        ["blocked"] = AppraisalItemRegistry.IsBlocked(type, rank)
                    });
                }
            }
            return new JObject
            {
                ["rank"] = rank,
                ["rank_name"] = AppraisalRankHelper.GetRankName(rank),
                ["base_fee"] = AppraisalRankHelper.GetBaseFee(),
                ["progress"] = store.appraisalProgress,
                ["progress_to_promote"] = AppraisalRankHelper.Current?.progressToPromote ?? 0f,
                ["can_progress"] = AppraisalRankHelper.CanProgress(),
                ["unlocked_types"] = unlocked
            };
        }

        private static string DialogueNodeText(Dialogue node)
        {
            if (node == null) return "";
            if (!string.IsNullOrWhiteSpace(node.plainText)) return node.plainText;
            return node.text?.Build(int.MaxValue)?.nakedText ?? "";
        }

        private static string DialogueChainText(Dialogue root)
        {
            if (root == null) return "";
            string first = DialogueNodeText(root);
            string second = DialogueNodeText(root.nextDialogue);
            return second.Length > 0 ? first + "\n" + second : first;
        }

        private static bool MatchesDialogueTemplate(Dialogue current, Dialogue template)
        {
            if (current == null || template == null) return false;
            string curChain = DialogueChainText(current);
            string tplChain = DialogueChainText(template);
            if (tplChain.Length > 0 && string.Equals(curChain, tplChain, StringComparison.Ordinal))
                return true;
            string tplSecond = DialogueNodeText(template.nextDialogue);
            return tplSecond.Length > 0 &&
                string.Equals(DialogueNodeText(current), tplSecond, StringComparison.Ordinal);
        }

        private static string CurrentAppraisalDialogueLine(JArray fallbackLines)
        {
            var dialog = DialogUIManager.Instance;
            string bubbleText = dialog?.clientBubble?.textfield?.text;
            if (!string.IsNullOrWhiteSpace(bubbleText)) return bubbleText;
            if (dialog?.GetCurrentDialog() is Dialogue current)
            {
                if (!string.IsNullOrWhiteSpace(current.plainText)) return current.plainText;
                string built = current.text?.Build(int.MaxValue)?.nakedText;
                if (!string.IsNullOrWhiteSpace(built)) return built;
            }
            return fallbackLines != null && fallbackLines.Count > 0 ? (string)fallbackLines.Last : null;
        }

        private string AppraisalAction(JObject request, JObject receipt)
        {
            if (phase != "OPEN") return "shop_closed";
            ObserveVisit();
            if (visitId == null || (string)request["visit"] != visitId) return VisitMismatchStatus(receipt);
            receipt["visit"] = visitId;
            if (merchant == null || merchant.clientIntent != StoreClient.ClientIntent.APPRAISAL_SERVICE)
            {
                receipt["reason"] = "not_appraisal_service";
                return "wrong_client_intent";
            }
            int estimate = (int?)request["estimate"] ?? 0;
            if (estimate <= 0)
            {
                receipt["reason"] = "invalid_estimate";
                return "invalid_request";
            }
            if (Interactable.isPaused)
            {
                receipt["reason"] = "paused";
                return "interaction_blocked";
            }
            if (GenericConfirmationUIManager.Instance?.panel?.activeInHierarchy == true)
            {
                receipt["reason"] = "confirmation_open";
                return "interaction_blocked";
            }
            string stage = CounterStage();
            if (stage == "speaking" || stage == "awaiting_opening" || stage == "advance_required" || stage == "choice_required")
            {
                receipt["reason"] = stage;
                return "interaction_blocked";
            }
            int itemId = (int?)request["item_id"] ?? 0;
            var shop = EmporiumEntry.Instance;
            if (itemId <= 0 || !itemsById.TryGetValue(itemId, out var item) || item == null)
            {
                receipt["reason"] = "item_not_found";
                return "item_unavailable";
            }
            if (GeneralHelper.IsItemOwned(item))
            {
                receipt["reason"] = "item_owned";
                return "item_unavailable";
            }
            if (item.parentInventory != shop.frontInvinvElement && item.parentInventory != shop.backInvinvElement)
            {
                receipt["reason"] = "not_on_counter";
                return "item_unavailable";
            }
            if (!item.IsTag("APPRAISAL_ITEM"))
            {
                receipt["reason"] = "not_appraisal_item";
                return "item_unavailable";
            }

            var store = PlayerStore.Instance;
            int cashBefore = store.playerCash;
            int impatienceBefore = merchant.clientImpatience;
            string prevLine = CurrentAppraisalDialogueLine(null);
            var existingSlips = new HashSet<GameItem>();
            foreach (var inv in new[] { shop.frontInvinvElement, shop.backInvinvElement })
            {
                if (inv == null) continue;
                foreach (var child in inv.childItems)
                    if (child != null && child.identifier == "slip") existingSlips.Add(child);
            }

            AppraisalServiceHelper.OnEstimate(item, estimate);
            var inspector = InspectionUIManager.Instance;
            if (inspector != null && inspector.currentInspectingItem == item)
                inspector.CloseUI();

            FlushChanges();
            ObserveVisit();
            receipt["item_id"] = itemId;
            receipt["estimate"] = estimate;

            bool accepted = item.parentInventory != shop.frontInvinvElement && item.parentInventory != shop.backInvinvElement;
            if (accepted)
            {
                int remaining = AppraisalItems().Count;
                receipt["cash_delta"] = store.playerCash - cashBefore;
                receipt["remaining_count"] = remaining;
                receipt["all_done"] = remaining == 0;
                foreach (var inv in new[] { shop.frontInvinvElement, shop.backInvinvElement })
                {
                    if (inv == null) continue;
                    foreach (var child in inv.childItems)
                    {
                        if (child == null || child.identifier != "slip" || existingSlips.Contains(child)) continue;
                        string slipText = child.GetTagReadonly("SLIP_BODY").GetString();
                        if (!string.IsNullOrEmpty(slipText)) receipt["slip_text"] = slipText;
                        break;
                    }
                    if (receipt["slip_text"] != null) break;
                }
                return "accepted";
            }

            receipt["cash_delta"] = 0;
            string line = CurrentAppraisalDialogueLine(null);
            if (!string.IsNullOrEmpty(line) && !string.Equals(line, prevLine, StringComparison.Ordinal))
                receipt["line"] = line;
            else
            {
                receipt["line_pending"] = true;
                receipt["next_action"] = "wait_for_dialogue_and_check_get_trade_context";
            }
            if (DialogUIManager.Instance?.GetCurrentDialog() is Dialogue dlg)
            {
                if (MatchesDialogueTemplate(dlg, AppraisalServiceHelper.GetTooHighDialog()))
                    receipt["direction"] = "too_high";
                else if (MatchesDialogueTemplate(dlg, AppraisalServiceHelper.GetTooLowDialog()))
                    receipt["direction"] = "too_low";
            }
            if (merchant.clientImpatience >= 100)
            {
                receipt["reason"] = "patience_exhausted";
                return "walked_out";
            }
            receipt["remaining_count"] = AppraisalItems().Count;
            receipt["impatience"] = merchant.clientImpatience;
            receipt["impatience_delta"] = merchant.clientImpatience - impatienceBefore;
            return "rejected";
        }

        private string AppraisalAssistAction(JObject request, JObject receipt)
        {
            if (!InventorySorting.AppraisalAssistEnabled)
            {
                receipt["reason"] = "appraisal_assist_off";
                return "assist_disabled";
            }
            if (phase != "OPEN") return "shop_closed";
            ObserveVisit();
            if (visitId == null || (string)request["visit"] != visitId) return VisitMismatchStatus(receipt);
            receipt["visit"] = visitId;
            if (merchant == null || merchant.clientIntent != StoreClient.ClientIntent.APPRAISAL_SERVICE)
            {
                receipt["reason"] = "not_appraisal_service";
                return "wrong_client_intent";
            }
            var shop = EmporiumEntry.Instance;
            var rows = new JArray();
            void Scan(GameInventory inv)
            {
                if (inv == null) return;
                foreach (var item in inv.childItems)
                {
                    if (item == null || GeneralHelper.IsItemOwned(item) || !item.IsTag("APPRAISAL_ITEM")) continue;
                    rows.Add(new JObject
                    {
                        ["item_id"] = LocalId(item),
                        ["name"] = item.GetDisplayName(true),
                        ["kind"] = item.identifier,
                        ["recommended_estimate"] = AppraisalHelper.GetCorrectBaseValue(item)
                    });
                }
            }
            Scan(shop?.frontInvinvElement);
            Scan(shop?.backInvinvElement);
            receipt["source"] = "appraisal_assist";
            receipt["items"] = rows;
            return "observed";
        }
    }
}
