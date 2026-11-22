using System.Linq;
using Newtonsoft.Json.Linq;
using TMPro;

namespace WhiteDragon.Ledger
{
    public sealed partial class InventoryObserver
    {
        private string ReadDocument(GameItem item, JObject location, string since, JObject receipt)
        {
            JObject document;
            switch (item.identifier)
            {
                case "newspaper": document = ReadNewspaper(); break;
                case "exchange_directive": document = ReadExchangeList(); break;
                case "slip":
                    document = new JObject { ["title"]=item.GetTagReadonly("SLIP_TITLE").GetString(),
                        ["body"]=item.GetTagReadonly("SLIP_BODY").GetString() };
                    break;
                case "handnote":
                    document = new JObject { ["title"]=item.GetTagReadonly("HANDNOTE_TITLE").GetString(),
                        ["body"]=item.GetTagReadonly("HANDNOTE_BODY").GetString(),
                        ["signature"]=item.GetTagReadonly("HANDNOTE_SIGN").GetString() };
                    break;
                default: return "no_content_window";
            }
            if (document == null) return "document_unavailable";
            int id = LocalId(item);
            var read = new WindowRead();
            read.View = new JObject { ["owner_id"]=id, ["save_id"]=item.GetUniqueID(), ["name"]=item.GetDisplayName(true),
                ["kind"]=item.identifier, ["location"]=location, ["controls"]=new JArray(), ["document"]=document };
            workstations.TryGetValue(id, out var before);
            PublishWindow(id, read, receipt, before != null && before.Token == since ? before : null);
            return "observed";
        }

        private static JObject ReadNewspaper()
        {
            var news = NewsUIManager.Instance;
            if (news == null) return null;
            var articles = new JArray();
            if (news.uiGameObject.activeInHierarchy)
            {
                foreach (var entry in news.allElements.Where(e => e.gameObject.activeInHierarchy && e.savedStoreEvent != null))
                    articles.Add(new JObject { ["title"]=VisibleText(entry.title), ["body"]=VisibleText(entry.textContent) });
            }
            else
            {
                // PopulateUI activates AllQuiet in the game if no news exists. Read its text without activating the event.
                var events = StoreStation.Instance.storeEventManager.GetActiveEvents().Where(e => !e.isHiddenFromNewsPaper).ToList();
                if (events.Count == 0) events.Add(EventListCosmetic.AllQuiet());
                var layout = events.Count == 1 ? news.layout1Elements : events.Count == 2 ? news.layout2Elements :
                    events.Count == 3 ? news.layout3Elements : news.layout4Elements;
                foreach (var entry in events.Take(layout.Count))
                {
                    var row = new JObject { ["title"]=entry.newsName, ["body"]=entry.newsDescription };
                    if (!entry.isPermanent && entry.eventType != StoreEvent.EventType.COSMETIC &&
                        (entry.duration == 1 || Difficulty.Rules.eventEndCountdown))
                        row["days_remaining"] = entry.duration;
                    articles.Add(row);
                }
            }
            return new JObject { ["articles"]=articles };
        }

        private static JObject ReadExchangeList()
        {
            var manager = PlayerStore.Instance.exchangeManager;
            if (manager == null) return null;
            var offers = new JArray();
            foreach (var offer in manager.activeBarter.Concat(manager.promotionalBarter
                         .Where(p => manager.activePromotionalBarterIds.Contains(p.identifier))))
            {
                var requirements = new JArray();
                foreach (var group in offer.conditions.GroupBy(condition => new {
                    condition.requiredItemId, condition.requiredItemTag,
                    exclusions=string.Join("|",condition.blacklistedConditionId)
                }))
                {
                    var condition = group.First();
                    var need = new JObject { ["quantity"]=group.Count() };
                    if (!string.IsNullOrEmpty(condition.requiredItemId))
                    {
                        need["kind"] = condition.requiredItemId;
                        need["name"] = LocHelper.GetLocalizedOrDefault("Item", "item_" + condition.requiredItemId + "_name", condition.requiredItemId);
                    }
                    if (!string.IsNullOrEmpty(condition.requiredItemTag)) need["category"] = condition.requiredItemTag;
                    if (condition.blacklistedConditionId.Count > 0) need["exclude_conditions"] = new JArray(condition.blacklistedConditionId);
                    requirements.Add(need);
                }
                offers.Add(new JObject { ["id"]=offer.identifier, ["promotional"]=manager.activePromotionalBarterIds.Contains(offer.identifier),
                    ["requirements"]=requirements });
            }
            var doc = new JObject { ["offers"]=offers };
            var ui = ExchangeUIManager.Instance;
            if (ui != null && ui.panel.activeInHierarchy)
                doc["panel_text"] = new JArray(ui.panel.GetComponentsInChildren<TextMeshProUGUI>()
                    .Select(VisibleText).Where(s => !string.IsNullOrWhiteSpace(s)));
            return doc;
        }
    }
}
