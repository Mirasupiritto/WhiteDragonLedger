using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine.UI;

namespace UnityEngine.UI
{
    public partial class Button
    {
        public bool isActiveAndEnabled = true, interactable = true;
        public bool IsInteractable() => interactable;
    }
}

namespace WhiteDragon.Ledger
{
    public class TravelContent
    {
        public TextMeshProUGUI[] labels;
        public T[] GetComponentsInChildren<T>() => labels as T[];
    }
    public class TravelScroll { public TravelContent content; }
    public class TransitCardView { public AfterhourLocation location; public bool available; public TravelScroll scroll; }
    public class TransitFrame { public Button travelButton = new Button(), prevButton = new Button(), nextButton = new Button(); }
    public class TravelRoot
    {
        public TransitFrame frame = new TransitFrame();
        public T GetComponent<T>() where T : class => frame as T;
    }
    public static class TransitUI
    {
        public static bool IsOpen;
        public static TravelRoot Root = new TravelRoot();
        private static List<TransitCardView> cards = new List<TransitCardView>();
        private static AfterhourLocation selected;
        private static int page;
        public static void Select(AfterhourLocation location){selected=location;Root.frame.travelButton.interactable=true;}
        public static void Show(int index, bool choose, params TransitCardView[] items)
        {
            IsOpen = true;
            cards.Clear();
            cards.AddRange(items);
            page = index;
            selected = choose ? cards[0].location : null;
            Root.frame.travelButton.interactable = selected != null;
            Root.frame.prevButton.interactable = page > 0;
            Root.frame.nextButton.interactable = page == 0;
        }
    }

    public sealed partial class InventoryObserver
    {
        private void ReplayTravelOptions()
        {
            var forest = new TransitCardView { location = AfterhourLocation.Get("forest"), available = true,
                scroll = new TravelScroll { content = new TravelContent { labels = new[] {
                    new TextMeshProUGUI { text = "當晚事件" }, new TextMeshProUGUI { text = "hidden", isActiveAndEnabled = false } } } } };
            var ruins = new TransitCardView { location = AfterhourLocation.Get("ruins") };
            TransitUI.Show(0, false, forest, ruins);
            Expect(!CanObserveStore() && sceneStatus == Picker && currentLocation == ShopScene, "picker suspends the shop");
            var places = (JArray)travelOptions["destinations"];
            Expect(places.Count == 2 && (string)places[0]["id"] == "forest" && !(bool)places[1]["available"] &&
                ((JArray)places[0]["event_text"]).Count == 1 && !(bool)travelOptions["can_depart"], "visible map cards");
            int revision = sceneRevision;
            Expect(!CanObserveStore() && sceneRevision == revision, "unchanged selection does not publish");
            TransitUI.Show(0, true, forest, ruins);
            Expect(!CanObserveStore() && (string)travelOptions["selected_id"] == "forest" && (bool)travelOptions["can_depart"], "selection");
            TransitUI.Show(1, false, ruins);
            Expect(!CanObserveStore() && (int)travelOptions["page"] == 2 && (bool)travelOptions["has_previous_page"] &&
                ((JArray)travelOptions["destinations"]).Count == 1, "new page replaces prior cards");
            TransitUI.IsOpen = false;
            Expect(CanObserveStore(), "cancel resumes shop");
            MarkSceneReady(ShopScene, "當鋪");
            Expect(travelOptions == null && lastJourney == null, "cancel clears cards without recording a trip");
            TransitUI.Show(0, true, forest);
            Expect(!CanObserveStore(), "reopen before departure");
        }
    }
}
