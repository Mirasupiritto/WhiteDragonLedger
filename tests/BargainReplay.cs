using System;
using Newtonsoft.Json.Linq;
namespace WhiteDragon.Ledger
{
    public enum BargainType { Discount, Markup, BuyingMarkup, BuyingDiscount, Blackmail, Threaten }
    public partial class StoreClient { public string clientFaction="replay"; }
    public partial class PlayerStore { public bool isAugIntroduced; }
    public class BargainUIManager
    {
        public static BargainUIManager Instance=new BargainUIManager();
        private bool discountOffered=false, markupApplied, buyingMarkupOffered=false, buyingDiscountApplied=false,
            blackmailApplied=false, threatenApplied=false;
        private int markupPercent;
        public bool depart;
        private static bool IsClientMoving()=>false;
        private static bool IsDialoguePlaying()=>false;
        private bool CanOffer(BargainType type)=>type==BargainType.Markup && !markupApplied;
        private (double,double) ComputePreviewMultipliers(BargainType type,int p)=>(0.8,1);
        private static bool BargainUsesRoll(BargainType type)=>true;
        public int GetBargainSuccessChance(BargainType type,int p)=>80;
        public int GetBargainLeaveChance(BargainType type,int p)=>10;
        public void OfferMarkup(int p) {
            if (depart) PlayerStore.Instance.currentClientInstance.client.clientIntent=StoreClient.ClientIntent.DIALOGUE;
            else { markupApplied=true; markupPercent=p; }
        }
        public void OfferDiscount(int p)=>throw new NotImplementedException();
        public void OfferBuyingMarkup(int p)=>throw new NotImplementedException();
        public void DemandBuyingDiscount(int p)=>throw new NotImplementedException();
        public void Blackmail()=>throw new NotImplementedException();
        public void ThreatenAug()=>throw new NotImplementedException();
    }
    public sealed partial class InventoryObserver
    {
        private void ReplayBargain()
        {
            var request=new JObject { ["action"]="bargain", ["mode"]="read", ["generation"]=generation,
                ["visit"]=visitId, ["revision"]=sequence, ["expires_ms"]=DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds() };
            var receipt=new JObject();
            Expect(ActAtCounter(request,receipt)=="observed" && (int)receipt["options"][0]["leave_chance"]==10,"native leave chance");
            request["mode"]="offer"; request["type"]="Markup"; request["percent"]=10; request["bargain_token"]=receipt["bargain_token"];
            Expect(ActAtCounter(request,receipt)=="bargain_offered" && (double)receipt["price_multipliers"]["sell"]==1.1,"accepted markup");
            Expect(ActAtCounter(request,receipt)=="bargain_changed","consumed bargain token");
            BargainUIManager.Instance=new BargainUIManager {depart=true};
            request["mode"]="read";ActAtCounter(request,receipt);
            request["mode"]="offer";request["bargain_token"]=receipt["bargain_token"];
            Expect(ActAtCounter(request,receipt)=="customer_left","native departure dialogue");
            Console.WriteLine("Bargain replay passed.");
        }
    }
}
