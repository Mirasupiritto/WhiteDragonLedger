using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace WhiteDragon.Ledger
{
    public sealed partial class InventoryObserver
    {
        private readonly HashSet<GameItem> phoneCards = new HashSet<GameItem>();
        private JObject previousPhone;
        private object previousPhonePage;
        private string phoneToken;

        // PhoneDialogList queues these suppliers. Their dialogue puts these specific
        // goods directly on the front counter; free samples are already player-owned.
        private readonly Dictionary<GameItem, (string source, string contact)> phoneArrivals =
            new Dictionary<GameItem, (string source, string contact)>();

        private bool IsTradeSettledItem(GameItem item) =>
            order != null || completedOrder != null || staged != null || stagedBundle.Count > 0 ||
            item == observedSale || received.Contains(item);

        private void ObservePhoneArrival(GameItem item, GameInventory inventory, SlotMarker marker)
        {
            if (item == null || ids.ContainsKey(item)) return;
            var store = PlayerStore.Instance;
            var shop = EmporiumEntry.Instance;
            var client = store.currentClientInstance?.GetClientBlueprint();
            if (inventory == shop?.frontInvinvElement)
            {
                long number = 0;
                switch (client?.identifier)
                {
                    case "courrierChem": case "courrierChemFree":
                        if (item.identifier == "dream_dust") number = StorePhoneClient.CHEM_NUMBER;
                        break;
                    case "courrierMining": case "courrierMiningFree":
                        if (item.identifier == "common_ore") number = StorePhoneClient.MINING_NUMBER;
                        break;
                    case "courrierPharma": case "courrierPharmaFree":
                        if (item.identifier == "pink_injector") number = StorePhoneClient.PHARMA_NUMBER;
                        break;
                    case "courrierRedImp": case "courrierRedImpFree":
                        if (item.identifier == "beerCase") number = StorePhoneClient.RED_IMP_REP_NUMBER;
                        break;
                }
                if (number != 0)
                {
                    phoneArrivals[item] = ("phone_delivery", store.PhoneClientDict.TryGetValue(number, out var contact)
                        ? contact.GetLocalizedDisplayName() : null);
                    return;
                }
            }
            if ((inventory == shop?.invElement || inventory == shop?.frontInvinvElement) &&
                store.IsClientPresent() && client != null && GeneralHelper.IsItemOwned(item) && !IsTradeSettledItem(item))
            {
                phoneArrivals[item] = ("visitor_gift", string.IsNullOrEmpty(client.displayName) ? null : client.displayName);
            }
        }

        private void AttachPhoneArrivals(JObject record, JArray upsert)
        {
            if (phoneArrivals.Count == 0) return;
            var groups = phoneArrivals.ToLookup(entry => entry.Value, entry => entry.Key);
            int outside = 0;
            var arrivals = new JArray();
            foreach (var group in groups)
            {
                var delivered = new JArray();
                foreach (var item in group)
                {
                    if (!ids.TryGetValue(item, out var id)) continue;
                    var row = upsert.FirstOrDefault(r => (int)r["id"] == id);
                    if (row == null) continue;
                    delivered.Add(id);
                    if ((bool)row["owned"] && ContrabandHelper.IsContraband(item) &&
                        !((JArray)row["container_path"] ?? new JArray()).Any(bag =>
                            itemsById.TryGetValue((int)bag["id"], out var parent) && ContrabandHelper.IsSmugglerBay(parent)))
                        outside += item.unitCount;
                }
                if (delivered.Count == 0) continue;
                var arrival = new JObject { ["ids"] = delivered, ["source"] = group.Key.source };
                if (group.Key.contact != null) arrival["contact"] = group.Key.contact;
                arrivals.Add(arrival);
            }
            phoneArrivals.Clear();
            if (arrivals.Count > 0) record["arrivals"] = arrivals;
            if (outside > 0) record["contraband_outside_bay"] = outside;
        }

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
            view["keypad_protocol"] = 1;
            view["can_open"] = phone != null && phase != "OPEN" && !panelOpen && !Interactable.isPaused &&
                PlayerStore.Instance.lockStorePhysicalFlag == 0 && !PlayerStore.Instance.IsClientPresent();
            if (panelOpen)
            {
                view["display"] = phone.numberDisplay.text;
                view["status_text"] = phone.statusDisplay.text;
                view["can_enter_number"] = page == null && string.IsNullOrEmpty(phone.numberDisplay.text) &&
                    phone.statusDisplay.text == LocHelper.GetLocalizedUI("ui_phone_ready");
            }
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
                        if (VisibleChoice(button, 1))
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

        private void AttachPhoneState(JObject receipt, bool includeTalkState)
        {
            var after = PhoneView();
            var keys = includeTalkState
                ? new[] { "phone_token", "panel_open", "display", "status_text", "connected_to", "line", "can_advance", "choices" }
                : new[] { "phone_token", "panel_open", "display", "status_text", "connected_to" };
            foreach (var key in keys)
                if (after[key] != null) receipt[key] = after[key].DeepClone();
        }

        private string ActOnPhone(JObject request, JObject receipt)
        {
            var phone = PhoneUIManager.Instance;
            var view = PhoneView();
            if ((string)request["phone_token"] != (string)view["phone_token"]) return "phone_changed";
            string action = (string)request["action"];
            if ((action == "dial" || (action == "phone_keypad" && (string)request["mode"] == "open")) && !(bool)view["can_open"])
            {
                receipt["reason"] = phase == "OPEN" ? "shop_open" : (bool)view["panel_open"] ? "phone_already_open" :
                    PlayerStore.Instance.IsClientPresent() ? "customer_present" : "phone_locked";
                return "phone_unavailable";
            }
            if (action == "phone_keypad")
            {
                switch ((string)request["mode"])
                {
                    case "open":
                        phone.OpenUI();
                        break;
                    case "close":
                        if (!(bool)view["panel_open"]) return "phone_not_open";
                        phone.CloseUI();
                        break;
                    case "dial":
                        if ((bool?)view["can_enter_number"] != true) return "phone_not_ready";
                        string digits = (string)request["digits"];
                        if (string.IsNullOrEmpty(digits) || digits.Length > 9 || digits.Any(c => c < '0' || c > '9'))
                            return "invalid_number";
                        foreach (char digit in digits) phone.PressNumber(digit - '0');
                        break;
                    default: return "invalid_mode";
                }
                AttachPhoneState(receipt, false);
                return "submitted";
            }
            if (action == "dial")
            {
                long number = (long)request["number"];
                foreach (var row in (JArray)view["contacts"])
                {
                    if ((long)row["number"] != number) continue;
                    if (!(bool)row["will_answer"]) return "unavailable";
                    phone.OpenUI();
                    phone.AutoCall(number);
                    receipt["number"] = number;
                    AttachPhoneState(receipt, true);
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
                AttachPhoneState(receipt, true);
                return "advanced";
            }
            foreach (var button in talk.dialogChoiceBubbles)
            {
                if (!talk.IsChoice() || !VisibleChoice(button, 1) ||
                    button.index != (int)request["choice_id"] || !button.IsInteractable()) continue;
                receipt["selected_label"] = button.TMP.text;
                int cashBefore = PlayerStore.Instance.playerCash;
                var activeClient = phone.currentPhoneClient;
                button.OnDialogChoiceClick();
                receipt["cash_delta"] = PlayerStore.Instance.playerCash - cashBefore;
                if (activeClient != null)
                {
                    receipt["contact_state"] = activeClient.phoneState.ToString();
                    receipt["cooldown_days"] = activeClient.currentCooldown;
                }
                AttachPhoneState(receipt, true);
                return "choice_submitted";
            }
            return "choice_unavailable";
        }
    }
}
