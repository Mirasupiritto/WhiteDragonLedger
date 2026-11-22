using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine.UI;

namespace WhiteDragon.Ledger
{
    internal static class TravelOptions
    {
        // ShowPage rebuilds this list immediately. The previous page's Unity objects
        // stay in the hierarchy until the frame ends, so use the menu's active list.
        private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
        private static readonly FieldInfo Cards = typeof(TransitUI).GetField("cards", PrivateStatic);
        private static readonly FieldInfo Selected = typeof(TransitUI).GetField("selected", PrivateStatic);
        private static readonly FieldInfo Page = typeof(TransitUI).GetField("page", PrivateStatic);

        internal static string Act(string action, string id, JObject receipt)
        {
            var frame = TransitUI.Root.GetComponent<TransitFrame>();
            var map = MapUIManager.Instance;
            if (action == "close") { map.CloseUI(); return "close_requested"; }
            var cards = (List<TransitCardView>)Cards.GetValue(null);
            if (action == "select" || action == "depart")
            {
                var card = cards.Find(c => c.location.Id == id && c.available && c.location.IsAvailable());
                if (card == null) return "destination_unavailable";
                if (action == "select") { TransitUI.Select(card.location); return "selection_submitted"; }
                if (Selected.GetValue(null) != card.location || !NativeButton.Enabled(frame.travelButton)) return "selection_changed";
                string title = card.location.GetTitle();
                frame.travelButton.onClick.Invoke();
                receipt["destination"] = id;
                receipt["destination_title"] = title;
                return "departure_requested";
            }
            var button = action == "next" ? frame.nextButton : action == "previous" ? frame.prevButton : null;
            if (button == null || !NativeButton.Enabled(button)) return "page_unavailable";
            button.onClick.Invoke();
            return "page_requested";
        }

        internal static JObject Read(ref TransitCardView firstCard, JObject previous)
        {
            if (Cards == null || Selected == null || Page == null)
                return previous ?? new JObject { ["status"] = "unsupported_layout" };
            var frame = TransitUI.Root.GetComponent<TransitFrame>();
            var cards = (List<TransitCardView>)Cards.GetValue(null);
            var selected = (AfterhourLocation)Selected.GetValue(null);
            var first = cards.Count == 0 ? null : cards[0];
            var page = (int)Page.GetValue(null) + 1;
            bool depart = NativeButton.Enabled(frame.travelButton);
            bool back = NativeButton.Enabled(frame.prevButton);
            bool next = NativeButton.Enabled(frame.nextButton);
            if (previous != null && firstCard == first && (int)previous["page"] == page &&
                (string)previous["selected_id"] == selected?.Id && (bool)previous["can_depart"] == depart &&
                (bool)previous["has_previous_page"] == back && (bool)previous["has_next_page"] == next)
                return previous;

            firstCard = first;
            var places = new JArray();
            foreach (var card in cards)
            {
                var events = new JArray();
                if (card.scroll != null)
                    foreach (var label in card.scroll.content.GetComponentsInChildren<TextMeshProUGUI>())
                        if (label.isActiveAndEnabled && !string.IsNullOrWhiteSpace(label.text)) events.Add(label.text);
                places.Add(new JObject { ["id"] = card.location.Id, ["title"] = card.location.GetTitle(),
                    ["available"] = card.available, ["event_text"] = events });
            }
            return new JObject { ["status"] = "visible", ["page"] = page, ["destinations"] = places,
                ["selected_id"] = selected?.Id, ["can_depart"] = depart,
                ["has_previous_page"] = back, ["has_next_page"] = next };
        }
    }
}
