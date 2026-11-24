using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace WhiteDragon.Ledger
{
    public sealed partial class InventoryObserver
    {
        private JObject lastDecision;
        private static GameInventory ResponseInventory()
        {
            var shop = EmporiumEntry.Instance;
            if (shop.responseWindowClosable != null && shop.responseWindowClosable.IsVisible())
                return shop.responseInventoryClosable;
            return shop.responseWindow != null && shop.responseWindow.IsVisible() ? shop.responseInventory : null;
        }

        private static bool VisibleChoice(DialogChoiceBubble button, int type = 0) =>
            button != null && button.type == type && button.gameObject.activeInHierarchy && button.TMP != null;

        private static JArray DialogChoices(DialogUIManager talk)
        {
            if (talk == null || !talk.IsChoice()) return new JArray();
            return new JArray(talk.dialogChoiceBubbles.Where(button => VisibleChoice(button)).Select(button =>
                new JObject { ["id"]=button.index, ["text"]=button.TMP.text, ["enabled"]=button.IsInteractable() }));
        }

        private JArray ResponseChoices(GameInventory inventory)
        {
            if (inventory == null) return new JArray();
            var rows = new JArray();
            int index = 0;
            foreach (var item in inventory.childItems)
            {
                var row = new JObject { ["id"]=LocalId(item),
                    ["text"]=item.GetDisplayName(true), ["enabled"]=item.MayActivateSlotItem() && item.CanActivateSlotItem() };
                AttachBagChoiceCapacity(inventory, item, index++, row);
                rows.Add(row);
            }
            return rows;
        }

        private static bool LinesRequestCounter(JArray lines)
        {
            if (lines == null) return false;
            foreach (var token in lines)
            {
                string line = (string)token;
                if (string.IsNullOrEmpty(line)) continue;
                if (line.IndexOf("柜台", StringComparison.Ordinal) >= 0 ||
                    line.IndexOf("櫃檯", StringComparison.Ordinal) >= 0 ||
                    line.IndexOf("counter", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private JObject BuildCounterRequest()
        {
            var tableItems = new JArray();
            foreach (var item in EmporiumEntry.Instance.backInvinvElement.childItems)
            {
                if (item == null || GeneralHelper.IsDisplayOnly(item) || !GeneralHelper.IsItemOwned(item)) continue;
                var entry = new JObject
                {
                    ["id"] = LocalId(item),
                    ["name"] = item.GetDisplayName(true),
                    ["kind"] = item.identifier,
                    ["quantity"] = item.unitCount
                };
                if (BottleLabel(item) is string bottleLabel) entry["bottle_label"] = bottleLabel;
                AddChemicalPurityFields(item, entry);
                tableItems.Add(entry);
            }
            return new JObject
            {
                ["from_lines"] = openingLines.DeepClone(),
                ["table_items"] = tableItems
            };
        }

        private void ObserveDecision(string stage, JObject view)
        {
            if (stage != "advance_required" && stage != "choice_required" && stage != "response_required")
            {
                lastPage = null;
                pageToken = null;
                lastDecision = null;
                return;
            }
            var talk = DialogUIManager.Instance;
            var decision = new JObject { ["stage"]=stage, ["visit"]=visitId };
            object page = talk?.GetCurrentDialog();
            if (stage == "response_required")
            {
                var inventory = ResponseInventory();
                page = inventory;
                decision["response_choices"] = ResponseChoices(inventory);
            }
            else if (stage == "choice_required")
                decision["dialog_choices"] = DialogChoices(talk);

            if (page != lastPage || !JToken.DeepEquals(lastDecision, decision))
                pageToken = Guid.NewGuid().ToString("N");
            lastPage = page;
            lastDecision = decision;
            view["page"] = pageToken;
            foreach (string name in new[] { "dialog_choices", "response_choices" })
                if (decision[name] != null) view[name] = decision[name].DeepClone();
            // SecurityDialogInspectionLastLine binds this localized label to the inspection choice.
            if (stage=="choice_required" && decision["dialog_choices"]!=null && decision["dialog_choices"].Any(choice =>
                (string)choice["text"]==LocHelper.GetLocalizedOrDefault("Dialogue","dialog_sec_inspect_choice_accept","[接受盘查。]")))
                view["inspection_exposure"]=ContrabandCounts(true);
            // StoreClientListCartel builds faction choices for these clients with Dialogue.AddChoice.
            // Faction membership alone is shared by ordinary paying and trading customers.
            if (stage == "choice_required" && (merchant?.identifier == "cartelSec" ||
                merchant?.identifier == "cartelHandlerCheckin" || merchant?.identifier == "cartelHandlerWeekly"))
            {
                view["story_branch"]=true;
                view["consequence"]="unknown";
                view["consequence_hint"]=merchant.identifier=="cartelSec" ?
                    "警官詢問是否舉報卡特爾，請玩家決定。" : "這是卡特爾的對話選擇，請玩家決定。";
            }
            if (stage == "choice_required" && merchant?.clientIntent == StoreClient.ClientIntent.DIALOGUE &&
                (merchant.identifier == "cartelHandlerWeekly" || LinesRequestCounter(openingLines)))
                view["counter_request"] = BuildCounterRequest();
            if (stage == "choice_required" && merchant?.identifier == "radioactiveWasteCustomer")
            {
                view["forced_choice"] = true;
                view["forced_choice_reason"] =
                    "此企業廢料主管強制要求收下 1／3／5 桶核廢料（每桶補貼 $100），沒有拒絕選項；建議選最少數量（1 桶）。核廢料無法被垃圾桶清除，留在櫃檯過夜會於隔日早晨重回 front，帶到垃圾場拋棄達標會觸發喬治奧歸還一次。";
            }
        }

        private string SubmitDecision(JObject request, JObject receipt)
        {
            ObserveVisit();
            bool response = (string)request["action"] == "response_choice";
            bool customerlessResponse = response && PlayerStore.Instance.currentClientInstance == null;
            if ((string)request["visit"] != visitId || (visitId == null && !customerlessResponse))
                return VisitMismatchStatus(receipt);
            string stage = CounterStage();
            if (stage != (response ? "response_required" : "choice_required")) return "page_changed";
            var view = new JObject();
            ObserveDecision(stage, view);
            if ((string)request["page"] != pageToken) return "page_changed";
            int id = (int)request["choice_id"];
            var row = ((JArray)view[response ? "response_choices" : "dialog_choices"])
                .FirstOrDefault(x => (int)x["id"] == id);
            if (row == null || !(bool)row["enabled"]) return "choice_unavailable";
            receipt["selected"] = row.DeepClone();
            receipt["visit"] = visitId;
            if (response)
                ResponseInventory().childItems.First(item => LocalId(item) == id).ActivateSlotItem();
            else
                DialogUIManager.Instance.dialogChoiceBubbles.First(button => VisibleChoice(button) && button.index == id)
                    .OnDialogChoiceClick();
            ObserveVisit();
            receipt["visit"] = visitId;
            receipt["reception_token"] = ReceptionView()["token"];
            return "choice_submitted";
        }
    }
}
