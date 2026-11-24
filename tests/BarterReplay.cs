using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace WhiteDragon.Ledger
{
    public class BarterCondition
    {
        public string requiredItemTag="FOOD", requiredItemId="";
        public List<string> blacklistedConditionId=new List<string>();
    }

    public class BarterOffer
    {
        public List<BarterCondition> conditions=new List<BarterCondition> {new BarterCondition()};
        public string barterTextBody="一件食品";
        public int currentSetIndex;
        public readonly string[][] pages={new[]{"blood_bag","blood_bag"},new[]{"scrap_metal","scrap_metal","scrap_metal"},
            new[]{"scratcher","scratcher"}};
        public bool UseOfferSets()=>true;
        public int OfferSetCount()=>pages.Length;
        public bool IsBarterAcceptable()=>EmporiumEntry.Instance.backInvinvElement.childItems.Any(i=>i.owned && i.IsTag(conditions[0].requiredItemTag));
        public void Show()
        {
            var front=EmporiumEntry.Instance.frontInvinvElement;
            foreach (var item in front.childItems.Where(i=>!i.owned).ToArray()) item.To(null);
            foreach (var kind in pages[currentSetIndex]) new GameItem {identifier=kind,owned=false}.To(front);
        }
    }

    public partial class NegociationUIManager
    {
        public int barterClicks, barterTurns;
        public void OnRightArrowButtonClick()
        {
            var offer=PlayerStore.Instance.currentClientInstance.client.barterOffer;
            offer.currentSetIndex=(offer.currentSetIndex+1)%offer.OfferSetCount();
            barterTurns++;
            offer.Show();
        }
        private void AcceptReplayBarter(StoreClient client)
        {
            if (!client.barterOffer.IsBarterAcceptable() || client.isBusinessComplete) return;
            barterClicks++;
            client.isBusinessComplete=true;
            foreach (var reward in EmporiumEntry.Instance.GetAllNonOwnedItem())
            {
                reward.owned=true;
                reward.To(EmporiumEntry.Instance.invElement);
            }
        }
    }

    public sealed partial class InventoryObserver
    {
        private void ReplayBarter(bool narcotic = false)
        {
            var store=PlayerStore.Instance;
            var shop=EmporiumEntry.Instance;
            var ui=NegociationUIManager.Instance;
            var front=shop.frontInvinvElement.childItems.ToArray();
            var back=shop.backInvinvElement.childItems.ToArray();
            foreach (var item in front.Concat(back)) item.To(shop.invElement);
            store.currentClientInstance=new StoreClientInstance();
            store.isClientArrived=true;
            ObserveVisit();
            merchant.identifier=narcotic ? "addictGeneralBarter" : "normalFoodBarter";
            merchant.clientIntent=StoreClient.ClientIntent.BARTER;
            merchant.barterOffer=new BarterOffer {currentSetIndex=1};
            var offer=merchant.barterOffer;
            string category=narcotic ? "NARCOTIC" : "FOOD";
            offer.conditions[0].requiredItemTag=category;
            int initialTurns=ui.barterTurns, initialClicks=ui.barterClicks;
            var req=new JObject { ["action"]="barter",["mode"]="read",["visit"]=visitId,["generation"]=generation,
                ["revision"]=sequence,["expires_ms"]=DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds() };
            var reply=new JObject();
            int originalCash=store.playerCash;
            Expect(ActAtCounter(req,reply)=="awaiting_opening" && ui.barterTurns-initialTurns==0,"no quotes during entry");
            merchant.isIntroduced=true;offer.Show();
            var dialog=DialogUIManager.Instance;
            dialog.current=new object();dialog.typing=true;
            Expect(ActAtCounter(req,reply)=="speaking" && ui.barterTurns-initialTurns==0,"no quotes before final line ends");
            dialog.current=null;dialog.typing=false;
            ui.IsOpen=false;
            Expect(ActAtCounter(req,reply)=="tablet_closed","closed tablet");
            ui.IsOpen=true;
            Expect(ActAtCounter(req,reply)=="observed","three quotes in one receipt");
            var quotes=(JArray)reply["barter"]["options"];
            string[] kinds={"blood_bag","scrap_metal","scratcher"};
            int[] counts={2,3,2};
            Expect(quotes.Count==3 && offer.currentSetIndex==1 && ui.barterTurns-initialTurns==3 && ui.barterClicks-initialClicks==0,"restore original page without settling");
            for(int i=0;i<3;i++)
                Expect((int)quotes[i]["option"]==i+1 && (string)quotes[i]["give"][0]["category"]==category &&
                    (int)quotes[i]["give"][0]["quantity"]==1 && (int)quotes[i]["receive"][0]["quantity"]==counts[i] &&
                    (string)quotes[i]["receive"][0]["kind"]==kinds[i],"payment and reward by page");
            string old=(string)reply["barter"]["barter_token"];
            Expect(ActAtCounter(req,reply)=="observed" && ui.barterTurns-initialTurns==3 && old==(string)reply["barter"]["barter_token"],"unchanged visit does not flip again");
            req["mode"]="select";req["option"]=1;req["barter_token"]=old;
            var altered=shop.frontInvinvElement.childItems.First(i=>!i.owned);
            altered.unitCount++;
            Expect(ActAtCounter(req,reply)=="barter_changed" && ui.barterTurns-initialTurns==3 && ui.barterClicks-initialClicks==0,"changed quote stops before selecting");
            altered.unitCount--;
            altered.To(shop.backInvinvElement);
            Expect(ActAtCounter(req,reply)=="clear_trade_table" && ui.barterTurns-initialTurns==3,"do not clear customer goods staged on back counter");
            altered.To(shop.frontInvinvElement);
            Expect(ActAtCounter(req,reply)=="barter_selected" && offer.currentSetIndex==0 && ui.barterClicks-initialClicks==0,"pick blood bags separately");
            req["barter_token"]=reply["barter"]["barter_token"];req["mode"]="accept";
            Expect(ActAtCounter(req,reply)=="exchange_unavailable","empty counter never trades");
            var food=Item(narcotic ? 8062 : 8061,narcotic ? "injection" : "ration",5);food.waterTags.Add(category);
            shop.backInvWindow.visible=true;
            var put=new JObject { ["item_id"]=LocalId(food), ["source"]="inventory", ["destination"]="back", ["generation"]=generation,
                ["revision"]=sequence,["visit"]=visitId,["expires_ms"]=req["expires_ms"],["command_id"]="staging" };
            Expect((string)ExecuteMove(put)["status"]=="moved","stage food through move_item");
            req["revision"]=sequence;
            Expect(ActAtCounter(req,reply)=="barter_changed","payment changed since read");
            req["mode"]="read";Expect(ActAtCounter(req,reply)=="observed","recheck staged item");
            req["barter_token"]=reply["barter"]["barter_token"];
            req["mode"]="accept";req["payment_item_id"]=LocalId(food)+1;
            Expect(ActAtCounter(req,reply)=="payment_changed","explicit payment ID");
            req["payment_item_id"]=LocalId(food);req["option"]=2;
            Expect(ActAtCounter(req,reply)=="exchange_unavailable","only the selected page can settle");
            req["option"]=1;
            Expect(ActAtCounter(req,reply)=="exchanged" && ui.barterClicks-initialClicks==1 && store.playerCash==originalCash,"exact chosen barter");
            Expect(ActAtCounter(req,reply)=="business_complete" && ui.barterClicks-initialClicks==1,"duplicate cannot accept again");
            store.currentClientInstance=new StoreClientInstance();ObserveVisit();
            Expect(ActAtCounter(req,reply)=="customer_replaced","new visit rejects old choice");
            food.To(shop.invElement);
            foreach(var item in front) item.To(shop.frontInvinvElement);
            foreach(var item in back) item.To(shop.backInvinvElement);
            Console.WriteLine("Barter replay passed: opening, all three native pages, restoration, cached read, choice, staged food and one acceptance.");
        }
    }
}
