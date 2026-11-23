using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace WhiteDragon.Ledger
{
    public sealed partial class InventoryObserver
    {
        private BarterOffer barterSource;
        private JArray barterBook;
        private JObject lastBarterView;
        private string barterToken, submittedBarterVisit;

        // These are the two native clients that use CreateBarterFoodMultiOffer.
        private bool IsFoodBarter() => merchant.clientIntent == StoreClient.ClientIntent.BARTER &&
            (merchant.identifier == "storyFoodBarter" || merchant.identifier == "normalFoodBarter");

        private static JArray BarterGoods() => new JArray(EmporiumEntry.Instance.GetAllNonOwnedItem()
            .GroupBy(item => new { kind=item.identifier, name=item.GetDisplayName(true) })
            .OrderBy(group => group.Key.kind).ThenBy(group => group.Key.name)
            .Select(group => new JObject { ["kind"]=group.Key.kind, ["name"]=group.Key.name,
                ["quantity"]=group.Sum(item => item.unitCount) }));

        private static JObject CurrentBundle(BarterOffer offer)
        {
            var give = new JArray(offer.conditions.Select(condition => new JObject {
                ["category"]=condition.requiredItemTag, ["kind"]=condition.requiredItemId,
                ["exclude_conditions"]=new JArray(condition.blacklistedConditionId), ["quantity"]=1
            }));
            return new JObject { ["option"]=offer.currentSetIndex+1, ["requirement_text"]=offer.barterTextBody,
                ["give"]=give, ["receive"]=BarterGoods() };
        }

        private static bool TurnBarterPage(BarterOffer offer, int index)
        {
            if (offer.currentSetIndex == index) return true;
            int count = offer.OfferSetCount();
            for (int i=0; i<count; i++)
            {
                int next = (offer.currentSetIndex+1) % count;
                NegociationUIManager.Instance.OnRightArrowButtonClick();
                if (offer.currentSetIndex != next) return false;
                if (next == index) return true;
            }
            return false;
        }

        private bool BookMatches(BarterOffer offer) => barterSource == offer && barterBook != null &&
            barterBook.Count == offer.OfferSetCount() && JToken.DeepEquals(barterBook[offer.currentSetIndex], CurrentBundle(offer));

        private bool ReadBarterBook(BarterOffer offer)
        {
            if (BookMatches(offer)) return true;
            barterSource = offer;
            barterBook = null;
            barterToken = null;
            int start = offer.currentSetIndex, count = offer.OfferSetCount();
            var rows = new JObject[count];
            // The native arrows materialize reward objects. Never invoke the reward factories separately.
            try
            {
                for (int offset=0; offset<count; offset++)
                {
                    int index = (start+offset) % count;
                    if (!TurnBarterPage(offer,index)) return false;
                    rows[index] = CurrentBundle(offer);
                    if (!((JArray)rows[index]["receive"]).HasValues) return false;
                }
            }
            finally
            {
                TurnBarterPage(offer,start);
            }
            if (offer.currentSetIndex != start || !JToken.DeepEquals(rows[start],CurrentBundle(offer))) return false;
            barterBook = new JArray(rows);
            return true;
        }

        private JObject BarterView(BarterOffer offer)
        {
            var back = EmporiumEntry.Instance.backInvinvElement.childItems;
            var payment = new JArray(back.Select(i => new JObject { ["id"]=LocalId(i), ["kind"]=i.identifier,
                ["name"]=i.GetDisplayName(true), ["quantity"]=i.unitCount }));
            // AcceptBarter processes the whole counter. The scavenger asks for one food item.
            bool exactPayment = back.Count == 1 && back[0].unitCount == 1 && GeneralHelper.IsItemOwned(back[0]) &&
                offer.IsBarterAcceptable();
            var view = new JObject { ["options"]=barterBook.DeepClone(), ["selected_option"]=offer.currentSetIndex+1,
                ["payment_on_counter"]=payment, ["can_accept"]=exactPayment && submittedBarterVisit != visitId };
            if (barterToken == null || !JToken.DeepEquals(lastBarterView,view))
            {
                lastBarterView = (JObject)view.DeepClone();
                barterToken = Guid.NewGuid().ToString("N");
            }
            view["barter_token"] = barterToken;
            return view;
        }

        private string ActOnBarter(JObject request, JObject receipt)
        {
            ObserveVisit();
            if (visitId == null || visitId != (string)request["visit"]) return VisitMismatchStatus(receipt);
            string stage = CounterStage();
            if (stage != "barter") return stage;
            var offer = merchant.barterOffer;
            if (offer == null || !offer.UseOfferSets() || Interactable.isPaused) return "barter_unavailable";
            if (EmporiumEntry.Instance.backInvinvElement.childItems.Any(i => !GeneralHelper.IsItemOwned(i)))
                return "clear_trade_table";
            string mode = (string)request["mode"];
            if (mode == "read")
            {
                if (!ReadBarterBook(offer)) return "barter_unavailable";
                receipt["barter"] = BarterView(offer);
                return "observed";
            }
            if (!BookMatches(offer) || (string)request["barter_token"] != (string)BarterView(offer)["barter_token"])
                return "barter_changed";
            int index = (int)request["option"]-1;
            if (index < 0 || index >= barterBook.Count) return "unknown_option";
            if (mode == "select")
            {
                if (!TurnBarterPage(offer,index) || !BookMatches(offer)) return "barter_changed";
                receipt["barter"] = BarterView(offer);
                return "barter_selected";
            }
            if (mode != "accept") return "invalid_action";
            if (index != offer.currentSetIndex || !(bool)lastBarterView["can_accept"]) return "exchange_unavailable";
            if ((int?)request["payment_item_id"] != (int)lastBarterView["payment_on_counter"][0]["id"]) return "payment_changed";
            var rewards = EmporiumEntry.Instance.GetAllNonOwnedItem().ToArray();
            receipt["selected"] = barterBook[index].DeepClone();
            receipt["payment"] = lastBarterView["payment_on_counter"].DeepClone();
            submittedBarterVisit = visitId;
            NegociationUIManager.Instance.OnOfferAcceptClick();
            receipt["visit"] = visitId;
            receipt["reception_token"] = ReceptionView()["token"];
            return merchant.isBusinessComplete && rewards.All(GeneralHelper.IsItemOwned) ? "exchanged" : "exchange_unconfirmed";
        }
    }
}
