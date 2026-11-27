using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace WhiteDragon.Ledger
{
    public sealed partial class InventoryObserver
    {
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
                    items.Add(new JObject
                    {
                        ["item_id"] = LocalId(item),
                        ["name"] = item.GetDisplayName(true),
                        ["kind"] = item.identifier,
                        ["area"] = area,
                        ["default_estimate"] = item.GetTagReadonly("APPRAISAL_DEFAULT_VALUE").GetInt()
                    });
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

        private static string CurrentAppraisalDialogueLine(JArray fallbackLines)
        {
            var dialog = DialogUIManager.Instance;
            string bubbleText = dialog?.clientBubble?.textfield?.text;
            if (!string.IsNullOrWhiteSpace(bubbleText)) return bubbleText;
            var current = dialog?.GetCurrentDialog();
            if (current != null)
            {
                var lineProp = current.GetType().GetProperty("text") ?? current.GetType().GetProperty("line");
                if (lineProp?.GetValue(current) is string s && !string.IsNullOrWhiteSpace(s)) return s;
                var lineField = current.GetType().GetField("text") ?? current.GetType().GetField("line");
                if (lineField?.GetValue(current) is string sf && !string.IsNullOrWhiteSpace(sf)) return sf;
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
            string line = CurrentAppraisalDialogueLine(openingLines);
            if (!string.IsNullOrEmpty(line)) receipt["line"] = line;
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
    }
}
