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
            if ((string)request["visit"] != visitId) return "visit_changed";
            bool response = (string)request["action"] == "response_choice";
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
            return "choice_submitted";
        }
    }
}
