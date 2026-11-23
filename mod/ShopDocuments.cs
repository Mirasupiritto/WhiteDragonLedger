using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using TMPro;

namespace WhiteDragon.Ledger
{
    public sealed partial class InventoryObserver
    {
        private static bool IsBookOrGuide(GameItem item)
        {
            if (item == null) return false;
            if (item.IsTag(BookHelper.BOOK_TAG) || item.IsTag(BookHelper.BOOK_ID) ||
                item.IsTag(MiscItemDirectory.GUIDE_PAPER_TAG) || item.IsTag(MiscItemDirectory.TUTORIAL_PAPER_TAG))
                return true;
            string id = item.identifier ?? "";
            return id.EndsWith("_guide", StringComparison.OrdinalIgnoreCase) ||
                   id.EndsWith("_book", StringComparison.OrdinalIgnoreCase);
        }

        private string ReadDocument(GameItem item, JObject location, string since, JObject receipt)
        {
            JObject document;
            switch (item.identifier)
            {
                case "newspaper": document = ReadNewspaper(); break;
                case "exchange_directive": document = ReadExchangeList(); break;
                case "slip":
                case "rev_codeword": document = TaggedPaper(item, "SLIP_TITLE", "SLIP_BODY"); break;
                case "handnote": document = TaggedPaper(item, "HANDNOTE_TITLE", "HANDNOTE_BODY", "HANDNOTE_SIGN"); break;
                case "sec_slip": document = TaggedPaper(item, "title", "body"); break;
                case "tutorial_page": document = TaggedPaper(item, "TUTORIAL_PAGE_TITLE", "TUTORIAL_PAGE_BODY", "TUTORIAL_PAGE_SIGNATURE"); break;
                case "procurement_slip":
                    document = TaggedPaper(item, "PROCUREMENT_HEADER", "PROCUREMENT_ORDER_BODY");
                    document["client"] = item.GetTagReadonly("PROCUREMENT_CLIENT_NAME").GetString();
                    document["reward"] = item.GetTagReadonly("PROCUREMENT_FINAL_REWARD").GetInt();
                    document["due_in_days"] = item.GetTagReadonly("PROCUREMENT_DAY").GetInt();
                    break;
                case "expedition_log":
                    var entries = new JArray((item.GetTagReadonly("LOG_CONTENT_TAG").GetString() ?? "").Split(new[] { '/' })
                        .Where(entry => !string.IsNullOrWhiteSpace(entry)));
                    document = new JObject { ["source"]="expedition_log", ["lines"]=entries, ["readable"]=entries.Count > 0 };
                    if (entries.Count == 0) document["reason"] = "empty_log";
                    break;
                case "loan_contract":
                    var loan = LoanPanelUIManager.Instance;
                    document = null;
                    if (loan?.panel != null)
                    {
                        bool open = loan.panel.activeSelf;
                        try
                        {
                            if (!open) loan.OpenUI();
                            string body = VisibleText(loan.body);
                            document = new JObject { ["source"]="loan_panel", ["body"]=body, ["readable"]=!string.IsNullOrWhiteSpace(body) };
                            if (!(bool)document["readable"]) document["reason"] = "no_document_text";
                        }
                        finally { if (!open) loan.CloseUI(); }
                    }
                    break;
                case "cert1": case "cert2": case "cert3": case "cert4":
                    int rank = AppraisalRankHelper.GetRank();
                    document = new JObject { ["source"]="appraisal_items", ["rank"]=rank, ["items"]=new JArray(
                        AppraisalItemRegistry.GetUnlocked(rank).Select(type => new JObject {
                            ["name"]=type.GetDisplayName(), ["allowed"]=!AppraisalItemRegistry.IsBlocked(type,rank)
                        })) };
                    break;
                case "intel_sec":
                    var record = PlayerStore.Instance.secData;
                    document = record == null ? null : new JObject { ["source"]="security_record",
                        ["note"]=record.note, ["officer"]=record.GetCaseOfficer(record.GetTotalCrimeValue()),
                        ["suspected_crime"]=record.GetSuspectedCrimeDisplay(), ["action"]=record.GetAction(),
                        ["operation"]=record.GetOperation(), ["evidence"]=record.GetEvidenceLevelDisplay(),
                        ["projected_sentence"]=record.GetProjectedSentence() };
                    break;
                default:
                    string reason = null;
                    if (item.IsTag(MiscItemDirectory.TUTORIAL_PAPER_TAG))
                        document = TaggedPaper(item, "TUTORIAL_PAGE_TITLE", "TUTORIAL_PAGE_BODY", "TUTORIAL_PAGE_SIGNATURE");
                    else if (item.IsTag("HANDNOTE_BODY"))
                        document = TaggedPaper(item, "HANDNOTE_TITLE", "HANDNOTE_BODY", "HANDNOTE_SIGN");
                    else if (item.IsTag("SLIP_BODY"))
                        document = TaggedPaper(item, "SLIP_TITLE", "SLIP_BODY");
                    else document = ReadGuideOrBook(item, out reason);
                    if (document == null)
                    {
                        receipt["item_id"] = LocalId(item);
                        receipt["name"] = item.GetDisplayName(true);
                        receipt["reason"] = reason ?? "document_unavailable";
                        if (item.IsTag(BookHelper.BOOK_ID)) receipt["book_id"] = item.GetTagReadonly(BookHelper.BOOK_ID)?.GetString();
                        return (string)receipt["reason"];
                    }
                    if (item.IsTag(BookHelper.BOOK_ID)) document["book_id"] = item.GetTagReadonly(BookHelper.BOOK_ID)?.GetString();
                    break;
            }
            if (document == null)
            {
                receipt["reason"] = "document_source_unavailable";
                receipt["kind"] = item.identifier;
                return "document_unavailable";
            }
            if (document["readable"] == null) document["readable"] = true;
            if (document["title"] == null) document["title"] = item.GetDisplayName(true);
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

        private static JObject ReadStoreBookData(string bookId)
        {
            if (string.IsNullOrEmpty(bookId)) return null;
            if (StoreBookData.bookDatas == null || !StoreBookData.bookDatas.TryGetValue(bookId, out var data)) return null;
            var pages = new JArray();
            int count = data.pageCount > 0 ? data.pageCount : Math.Max(data.subtitle?.Length ?? 0, data.body?.Length ?? 0);
            for (int i = 0; i < count; i++)
            {
                string subtitle = data.subtitle != null && i < data.subtitle.Length ? data.subtitle[i] : null;
                string body = data.body != null && i < data.body.Length ? data.body[i] : null;
                pages.Add(new JObject { ["page"] = i + 1, ["subtitle"] = subtitle, ["body"] = body });
            }
            bool readable = pages.Any(page => !string.IsNullOrWhiteSpace((string)page["body"]) || !string.IsNullOrWhiteSpace((string)page["subtitle"]));
            var document = new JObject { ["source"]="book_table", ["title"]=data.title, ["book_id"]=bookId,
                ["readable"]=readable, ["page_count"]=count, ["pages"]=pages };
            if (!readable) document["reason"] = "missing_book_text";
            return document;
        }

        private static JObject MissingBookText(GameItem item, string bookId) => new JObject {
            ["title"] = item.GetDisplayName(true), ["book_id"] = bookId, ["readable"] = false,
            ["reason"] = "missing_book_text", ["note"] = "找不到這本手冊的正文資料。", ["pages"] = new JArray()
        };

    }
}
