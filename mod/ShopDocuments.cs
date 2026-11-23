using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using TMPro;

namespace WhiteDragon.Ledger
{
    public sealed partial class InventoryObserver
    {
        private static readonly FieldInfo MultiBookIndexField = typeof(MultiPageUIManager).GetField("currentBookIndex",
            BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo MultiPageField = typeof(MultiPageUIManager).GetField("currentPage",
            BindingFlags.Instance | BindingFlags.NonPublic);

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
                    document = new JObject { ["title"]=item.GetTagReadonly("SLIP_TITLE").GetString(),
                        ["body"]=item.GetTagReadonly("SLIP_BODY").GetString() };
                    break;
                case "handnote":
                    document = new JObject { ["title"]=item.GetTagReadonly("HANDNOTE_TITLE").GetString(),
                        ["body"]=item.GetTagReadonly("HANDNOTE_BODY").GetString(),
                        ["signature"]=item.GetTagReadonly("HANDNOTE_SIGN").GetString() };
                    break;
                default:
                    if (!IsBookOrGuide(item)) return "no_content_window";
                    document = ReadGuideOrBook(item, out var reason);
                    if (document == null) return reason ?? "document_unavailable";
                    break;
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

        private static JObject ReadStoreBookData(string bookId)
        {
            if (string.IsNullOrEmpty(bookId)) return null;
            var data = StoreBookData.GetBookById(bookId);
            if (data == null) return null;
            var pages = new JArray();
            int count = data.pageCount > 0 ? data.pageCount : Math.Max(data.subtitle?.Length ?? 0, data.body?.Length ?? 0);
            for (int i = 0; i < count; i++)
            {
                string subtitle = data.subtitle != null && i < data.subtitle.Length ? data.subtitle[i] : null;
                string body = data.body != null && i < data.body.Length ? data.body[i] : null;
                pages.Add(new JObject { ["page"] = i + 1, ["subtitle"] = subtitle, ["body"] = body });
            }
            return new JObject { ["title"] = data.title, ["book_id"] = bookId, ["readable"] = true, ["pages"] = pages };
        }

        private static JArray ExtractTextLines(IEnumerable<TextMeshProUGUI> labels)
        {
            var lines = new JArray();
            if (labels == null) return lines;
            foreach (var label in labels)
                if (label != null && !string.IsNullOrWhiteSpace(label.text))
                    lines.Add(label.text.Trim());
            return lines;
        }

        private static JObject UnreadableGuide(GameItem item, int pageCount) => new JObject {
            ["title"] = item.GetDisplayName(true),
            ["readable"] = false,
            ["page_count"] = pageCount,
            ["pages"] = new JArray(),
            ["reason"] = "image_only_guide",
            ["note"] = "此手冊頁面由遊戲內插圖構成，無可讀取的 TextMeshPro 文字元件。"
        };

        private static JObject ReadGuideOrBook(GameItem item, out string unavailableReason)
        {
            unavailableReason = null;
            if (item.IsTag(BookHelper.BOOK_ID))
            {
                string bookId = item.GetTagReadonly(BookHelper.BOOK_ID)?.GetString();
                var bookDoc = ReadStoreBookData(bookId);
                if (bookDoc != null) return bookDoc;
            }
            var multi = MultiPageUIManager.Instance;
            var paper = PaperUIManager.Instance;
            bool wasMultiOpen = multi != null && multi.panel != null && (multi.panel.activeInHierarchy || multi.panel.activeSelf);
            bool wasPaperBookOpen = paper != null && paper.bookPanel != null && (paper.bookPanel.activeInHierarchy || paper.bookPanel.activeSelf);
            var paperGuidePanels = paper == null ? null : new[] {
                paper.foodStampGuidePanel, paper.augIdGuide, paper.cigaretteGuidePanel,
                paper.cigaretteColorPanel, paper.injectorGuidePanel, paper.codeBookPanel
            };
            bool wasPaperGuideOpen = paperGuidePanels != null &&
                paperGuidePanels.Any(p => p != null && (p.activeInHierarchy || p.activeSelf));
            bool openedByInspect = false;
            try
            {
                if (!wasMultiOpen && !wasPaperBookOpen && !wasPaperGuideOpen)
                {
                    if (!item.MayActivateSlotItem() || !item.CanActivateSlotItem())
                    {
                        unavailableReason = "document_locked";
                        return null;
                    }
                    item.ActivateSlotItem();
                    openedByInspect = true;
                }
                if (multi != null && multi.panel != null && (multi.panel.activeInHierarchy || multi.panel.activeSelf))
                {
                    int bookIndex = MultiBookIndexField != null ? (int)MultiBookIndexField.GetValue(multi) : 0;
                    if (multi.books != null && bookIndex >= 0 && bookIndex < multi.books.Count && multi.books[bookIndex] != null)
                    {
                        var bookRoot = multi.books[bookIndex];
                        var pages = new JArray();
                        if (!wasMultiOpen)
                        {
                            int lastPage = MultiPageField != null ? (int)MultiPageField.GetValue(multi) : 0;
                            for (int guard = 0; guard < 32; guard++)
                            {
                                var lines = ExtractTextLines(bookRoot.GetComponentsInChildren<TextMeshProUGUI>(false));
                                if (lines.Count > 0)
                                    pages.Add(new JObject { ["page"] = lastPage + 1, ["lines"] = lines });
                                multi.FlipRightClicked();
                                int nextPage = MultiPageField != null ? (int)MultiPageField.GetValue(multi) : lastPage;
                                if (nextPage == lastPage) break;
                                lastPage = nextPage;
                            }
                        }
                        if (pages.Count == 0)
                        {
                            var allLines = ExtractTextLines(bookRoot.GetComponentsInChildren<TextMeshProUGUI>(true));
                            if (allLines.Count > 0)
                                pages.Add(new JObject { ["page"] = 1, ["lines"] = allLines });
                        }
                        return pages.Count > 0
                            ? new JObject { ["title"] = item.GetDisplayName(true), ["readable"] = true, ["pages"] = pages }
                            : UnreadableGuide(item, 0);
                    }
                }
                if (paper != null && paper.bookPanel != null && (paper.bookPanel.activeInHierarchy || paper.bookPanel.activeSelf))
                {
                    if (PaperUIManager.currentBook != null && PaperUIManager.currentBook.IsTag(BookHelper.BOOK_ID))
                    {
                        var bookDoc = ReadStoreBookData(PaperUIManager.currentBook.GetTagReadonly(BookHelper.BOOK_ID)?.GetString());
                        if (bookDoc != null) return bookDoc;
                    }
                    var page = new JObject {
                        ["page"] = Math.Max(1, paper.bookCurrentPage),
                        ["subtitle"] = paper.bookSubtitle?.text,
                        ["body"] = paper.bookBody?.text
                    };
                    bool hasText = !string.IsNullOrWhiteSpace((string)page["subtitle"]) || !string.IsNullOrWhiteSpace((string)page["body"]);
                    return hasText
                        ? new JObject { ["title"] = !string.IsNullOrWhiteSpace(paper.bookTitle?.text) ? paper.bookTitle.text : item.GetDisplayName(true),
                            ["readable"] = true, ["pages"] = new JArray { page } }
                        : UnreadableGuide(item, 1);
                }
                if (paperGuidePanels != null)
                {
                    foreach (var panel in paperGuidePanels)
                    {
                        if (panel == null || (!panel.activeInHierarchy && !panel.activeSelf)) continue;
                        var lines = ExtractTextLines(panel.GetComponentsInChildren<TextMeshProUGUI>(true));
                        return lines.Count > 0
                            ? new JObject { ["title"] = item.GetDisplayName(true), ["readable"] = true,
                                ["pages"] = new JArray { new JObject { ["page"] = 1, ["lines"] = lines } } }
                            : UnreadableGuide(item, 1);
                    }
                }
                unavailableReason = "document_unavailable";
                return null;
            }
            finally
            {
                if (openedByInspect)
                {
                    if (!wasMultiOpen && multi != null && multi.panel != null && (multi.panel.activeInHierarchy || multi.panel.activeSelf))
                        multi.CloseUI();
                    if (!wasPaperBookOpen && paper != null && paper.bookPanel != null && (paper.bookPanel.activeInHierarchy || paper.bookPanel.activeSelf))
                        paper.CloseBook();
                    if (!wasPaperGuideOpen && paper != null) paper.CloseAllPaperUI();
                }
            }
        }
    }
}
