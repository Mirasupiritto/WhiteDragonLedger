using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;

namespace WhiteDragon.Ledger
{
    public sealed partial class InventoryObserver
    {
        // Indices passed to MultiPageUIManager.OpenUI by the original item factories.
        private static readonly Dictionary<string, int> GuideBooks = new Dictionary<string, int> {
            ["fuel_guide"] = 0,
            ["water_guide"] = 1,
            ["hydroponic_guide"] = 2,
            ["chemistry_guide"] = 3,
            ["ribwich_guide"] = 4,
            ["ingot_book"] = 5,
            ["hand_guide"] = 6,
            ["wine_book"] = 7,
        };

        private static bool TryPaperPanel(string kind, out GameObject panel)
        {
            var paper = PaperUIManager.Instance;
            switch (kind)
            {
                case "mentor_contract": panel = paper?.contractPanel; return true;
                case "deed": panel = paper?.deedPanel; return true;
                case "lottery_info": panel = paper?.lotteryInfo; return true;
                case "cigarette_guide": panel = paper?.cigaretteGuidePanel; return true;
                case "stamp_guide": panel = paper?.foodStampGuidePanel; return true;
                case "injector_guide": panel = paper?.injectorGuidePanel; return true;
                case "cigarette_color": panel = paper?.cigaretteColorPanel; return true;
                case "aug_id_guide": panel = paper?.augIdGuide; return true;
                case "wanted_paper": panel = paper?.wantedPanel; return true;
                case "joe_card": panel = paper?.JoeWildBusinessCard; return true;
                case "card_brewer": panel = paper?.cardBrewer; return true;
                case "card_mining": panel = paper?.cardMining; return true;
                case "card_pharma": panel = paper?.cardPharma; return true;
                case "card_chem": panel = paper?.cardChem; return true;
                case "card_rev": panel = paper?.cardRev; return true;
                case "sec_card": panel = paper?.secCardPanel; return true;
                case "codebook": panel = paper?.codeBookPanel; return true;
                case "logo_checker": panel = RepublicLogoUIManager.Instance?.panel; return true;
                default: panel = null; return false;
            }
        }

        private static void ReadPageLines(GameObject root, JArray lines)
        {
            foreach (var label in root.GetComponents<TextMeshProUGUI>())
            {
                if (!label.enabled) continue;
                string text = ReadLocalizedLabel(label);
                if (text.Length > 0) lines.Add(text);
            }
            foreach (Transform child in root.transform)
                if (child.gameObject.activeSelf) ReadPageLines(child.gameObject, lines);
        }

        private static JObject Page(GameObject root, int number)
        {
            var lines = new JArray();
            ReadPageLines(root, lines);
            var page = new JObject { ["page"] = number };
            if (lines.Count > 0) page["lines"] = lines;
            else page["reason"] = "image_only_guide";
            return page;
        }

        private static JObject PagedDocument(GameItem item, JArray pages, string source)
        {
            bool readable = pages.Any(p => p["lines"] != null);
            var document = new JObject { ["title"] = item.GetDisplayName(true), ["source"] = source,
                ["readable"] = readable, ["page_count"] = pages.Count, ["pages"] = pages };
            if (!readable) document["reason"] = pages.Count > 0 ? "image_only_guide" : "missing_pages";
            return document;
        }

        private static JObject ReadItemText(GameItem item)
        {
            var document = new JObject { ["title"] = item.GetDisplayName(true), ["source"] = "item_description" };
            if (!string.IsNullOrWhiteSpace(item.shortDescription)) document["description"] = item.shortDescription;
            if (!string.IsNullOrWhiteSpace(item.longDescription)) document["body"] = item.longDescription;
            if (!string.IsNullOrWhiteSpace(item.flavorText)) document["flavor_text"] = item.flavorText;
            bool readable = document.Count > 2;
            document["readable"] = readable;
            if (!readable) document["reason"] = "no_document_text";
            return document;
        }

        private static JObject ReadGuideOrBook(GameItem item, out string reason)
        {
            reason = null;
            string bookId = item.IsTag(BookHelper.BOOK_ID) ? item.GetTagReadonly(BookHelper.BOOK_ID)?.GetString() : null;
            bool paperPage = TryPaperPanel(item.identifier, out var panel);
            if (GuideBooks.TryGetValue(item.identifier ?? "", out int index) || bookId == StoreBookData.INGOT_BOOK ||
                paperPage)
            {
                if (!item.MayActivateSlotItem() || !item.CanActivateSlotItem())
                {
                    reason = "document_locked";
                    return null;
                }
                if (paperPage && panel == null) { reason = "reader_unavailable"; return null; }
                if (paperPage) return PagedDocument(item, new JArray(Page(panel, 1)), "paper_panel");
                var books = MultiPageUIManager.Instance?.books;
                if (bookId == StoreBookData.INGOT_BOOK) index = GuideBooks["ingot_book"];
                if (books == null || index >= books.Count || books[index] == null)
                {
                    reason = "reader_unavailable";
                    return null;
                }
                var pages = new JArray();
                foreach (Transform page in books[index].transform)
                    pages.Add(Page(page.gameObject, pages.Count + 1));
                return PagedDocument(item, pages, "multi_page_reader");
            }
            if (bookId != null) return ReadStoreBookData(bookId) ?? MissingBookText(item, bookId);
            if (item.IsTag("PERMIT_ITEM_TAG"))
            {
                var types = new JArray();
                int count = item.GetTagReadonly("PERMIT_ITEM_COUNT").GetInt();
                for (int i = 1; i <= count; i++) types.Add(item.GetTagReadonly("PERMIT_ITEM_TAG" + i).GetString());
                return new JObject { ["source"]="permit", ["level"]=item.GetTagReadonly("PERMIT_ITEM_LEVEL").GetInt(), ["types"]=types };
            }
            if (item.GetGameItemType().Contains("DOCUMENT") || IsBookOrGuide(item)) return ReadItemText(item);
            reason = "no_content_window";
            return null;
        }

        private static JObject TaggedPaper(GameItem item, string title, string body, string signature = null)
        {
            var document = new JObject { ["title"] = item.GetTagReadonly(title).GetString(),
                ["body"] = item.GetTagReadonly(body).GetString(), ["source"] = "item_tags" };
            if (signature != null) document["signature"] = item.GetTagReadonly(signature).GetString();
            bool readable = !string.IsNullOrWhiteSpace((string)document["body"]) || !string.IsNullOrWhiteSpace((string)document["signature"]);
            document["readable"] = readable;
            if (!readable) document["reason"] = "no_document_text";
            return document;
        }
    }
}
