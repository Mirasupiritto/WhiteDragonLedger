using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace WhiteDragon.Ledger
{
    public sealed partial class InventoryObserver
    {
        private readonly HashSet<GameItem> phoneCards = new HashSet<GameItem>();
        private JObject previousPhone;
        private object previousPhonePage;
        private string phoneToken;

        private static long CardNumber(string kind)
        {
            switch (kind)
            {
                case "joe_card": return StorePhoneClient.WILD_CLIENT_NUMBER;
                case "card_brewer": return StorePhoneClient.RED_IMP_REP_NUMBER;
                case "card_mining": return StorePhoneClient.MINING_NUMBER;
                case "card_pharma": return StorePhoneClient.PHARMA_NUMBER;
                case "card_chem": return StorePhoneClient.CHEM_NUMBER;
                case "card_rev": return StorePhoneClient.REV_MERCHANT_NUMBER;
                default: return 0;
            }
        }

        private static string ServiceSummary(StorePhoneClient person)
        {
            switch (person.dialogFuncId)
            {
                case "Wild": return LocHelper.GetLocalizedDialoguePhone("browse_wild_service");
                case "RedImp": return "酒類供貨與預訂";
                case "Mining": return "礦物供貨與預訂";
                case "Pharma": return "醫藥品供貨與預訂";
                case "Chem": return "化學品供貨與預訂";
                case "RevMerchant": return "聯絡革命商人，安排來店";
                case "Sec": return "向治安部通報通緝對象";
                case "Commissary": return "聯絡供應處自動電話服務";
                default: return "詳見通話選單";
            }
        }

        private JObject PhoneView()
        {
            var phone = PhoneUIManager.Instance;
            var talk = DialoguePhoneUIManager.Instance;
            bool panelOpen = phone != null && phone.phonePanel.activeInHierarchy;
            var page = panelOpen && talk != null ? talk.GetCurrentDialog() : null;
            var contacts = new JArray();
            var choices = new JArray();
            var view = new JObject { ["panel_open"] = panelOpen, ["contacts"] = contacts, ["choices"] = choices };
            foreach (var entry in PlayerStore.Instance.PhoneClientDict)
            {
                var person = entry.Value;
                var card = JValue.CreateNull();
                foreach (var item in phoneCards)
                    if (item != null && item.parentInventory != null && areas.ContainsKey(item.parentInventory) &&
                        GeneralHelper.IsItemOwned(item) && CardNumber(item.identifier) == entry.Key)
                    { card = new JValue(LocalId(item)); break; }
                bool listed = person.ShownInPhoneBook();
                if (!listed && card.Type == JTokenType.Null) continue;
                contacts.Add(new JObject { ["number"] = entry.Key, ["name"] = person.GetLocalizedDisplayName(),
                    ["in_phonebook"] = listed, ["card_id"] = card, ["service_summary"] = ServiceSummary(person),
                    ["will_answer"] = PhoneUIManager.WillAnswerCall(entry.Key), ["state"] = person.phoneState.ToString(),
                    ["cooldown_days"] = person.currentCooldown });
            }
            if (panelOpen && phone.currentPhoneClient != null)
                view["connected_to"] = phone.currentPhoneClient.GetLocalizedDisplayName();
            if (page != null)
            {
                var bubble = talk.clientBubble;
                if (bubble != null && bubble.gameObject.activeInHierarchy) view["line"] = bubble.textfield.text;
                view["can_advance"] = talk.isWaitingForManualAdvance && !talk.IsChoice();
                if (talk.IsChoice())
                    foreach (var button in talk.dialogChoiceBubbles)
                        if (button != null && button.gameObject.activeInHierarchy)
                            choices.Add(new JObject { ["id"] = button.index, ["label"] = button.TMP.text,
                                ["enabled"] = button.IsInteractable() });
            }
            if (previousPhonePage != page || !JToken.DeepEquals(previousPhone, view))
            {
                previousPhonePage = page;
                previousPhone = (JObject)view.DeepClone();
                phoneToken = Guid.NewGuid().ToString("N");
            }
            view["phone_token"] = phoneToken;
            return view;
        }

        private string ActOnPhone(JObject request, JObject receipt)
        {
            var phone = PhoneUIManager.Instance;
            var view = PhoneView();
            if ((string)request["phone_token"] != (string)view["phone_token"]) return "phone_changed";
            string action = (string)request["action"];
            if (action == "dial")
            {
                if (phone == null || (bool)view["panel_open"]) return "phone_unavailable";
                long number = (long)request["number"];
                foreach (var row in (JArray)view["contacts"])
                {
                    if ((long)row["number"] != number) continue;
                    if (!(bool)row["will_answer"]) return "unavailable";
                    phone.OpenUI();
                    phone.AutoCall(number);
                    receipt["number"] = number;
                    return phone.currentPhoneClient == PlayerStore.Instance.PhoneClientDict[number] ? "connected" : "dial_unconfirmed";
                }
                return "unknown_contact";
            }
            var talk = DialoguePhoneUIManager.Instance;
            if (talk == null || talk.GetCurrentDialog() == null || !(bool)view["panel_open"]) return "no_call";
            if (action == "phone_advance")
            {
                if ((bool?)view["can_advance"] != true) return "not_waiting";
                talk.OnAdvancePressed();
                return "advanced";
            }
            foreach (var button in talk.dialogChoiceBubbles)
            {
                if (!talk.IsChoice() || button == null || !button.gameObject.activeInHierarchy ||
                    button.index != (int)request["choice_id"] || !button.IsInteractable()) continue;
                receipt["selected_label"] = button.TMP.text;
                button.OnDialogChoiceClick();
                return "choice_submitted";
            }
            return "choice_unavailable";
        }
    }
}
