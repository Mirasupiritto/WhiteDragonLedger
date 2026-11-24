using System;
using Newtonsoft.Json.Linq;
namespace WhiteDragon.Ledger
{
    public partial class ItemFeature { public int percent; public int GetCurrentValueMod()=>percent; }
    public static class ItemFeatureList
    {
        public static ItemFeature BargainDiscount(int p)=>new ItemFeature {percent=-p};
        public static ItemFeature BargainMarkup(int p)=>new ItemFeature {percent=p};
        public static ItemFeature BargainBuyingMarkup(int p)=>new ItemFeature {percent=p};
        public static ItemFeature BargainBuyingDiscount(int p)=>new ItemFeature {percent=-p};
        public static ItemFeature BlackmailMarkup(int p)=>new ItemFeature {percent=p};
        public static ItemFeature BlackmailDiscount(int p)=>new ItemFeature {percent=-p};
        public static ItemFeature ThreatenMarkup(int p)=>new ItemFeature {percent=p};
        public static ItemFeature ThreatenDiscount(int p)=>new ItemFeature {percent=-p};
    }
    public enum BargainType { Discount, Markup, BuyingMarkup, BuyingDiscount, Blackmail, Threaten }
    public partial class StoreClient { public string clientFaction="replay"; public double GetTradeRepMultiplier(bool sell)=>1; }
    public partial class PlayerStore { public bool isAugIntroduced; }
    public class BargainUIManager
    {
        public static BargainUIManager Instance=new BargainUIManager();
        private static readonly int[] PRICE_PERCENTS={13,27};
        private const int BLACKMAIL_PERCENT=19, THREATEN_PERCENT=22;
        private const float DISCOUNT_BUDGET_FACTOR=2f;
        public bool DiscountOffered=>false;
        public bool BuyingMarkupOffered=>false;
        private bool markupApplied;
        public bool speaking, offered;
        private static int GetMarkupBudgetCutPercent(int percent)=>37;
        private static bool IsClientMoving()=>false;
        private static bool IsDialoguePlaying()=>Instance.speaking;
        private bool CanOffer(BargainType type)=>type==BargainType.Blackmail || type==BargainType.Threaten ||
            (type==BargainType.Markup && !offered);
        private (double,double) ComputePreviewMultipliers(BargainType type,int p)=>(0.8,1);
        private static bool BargainUsesRoll(BargainType type)=>true;
        public int GetBargainSuccessChance(BargainType type,int p)=>80;
        public int GetBargainLeaveChance(BargainType type,int p)=>10;
        public void OfferMarkup(int p) { offered=true; speaking=true; }
        public void FinishDialogue(bool accept) { markupApplied=accept; speaking=false; }
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
            PlayerStore.Instance.isAugIntroduced=true;
            var original=PlayerStore.Instance.currentClientInstance;
            JObject receipt=null;
            string Read()
            {
                request["mode"]="read";
                receipt=new JObject();
                return ActAtCounter(request,receipt);
            }
            string Offer(int percent)
            {
                request["mode"]="offer"; request["type"]="Markup"; request["percent"]=percent;
                request["bargain_token"]=receipt["bargain_token"];
                receipt=new JObject();
                return ActAtCounter(request,receipt);
            }
            Expect(Read()=="observed" && (int)receipt["options"][0]["percent"]==13 &&
                (int)receipt["options"][1]["percent"]==27 && (int)receipt["options"][2]["percent"]==19 &&
                (int)receipt["options"][3]["percent"]==22,"native button percentages");
            var option=receipt["options"][0];
            Expect((int)option["budget_cut_percent"]==37 && (double)option["offer_price_factors"]["sell"]==1.13 &&
                (double)option["reputation_multipliers"]["sell"]==0.8,"native budget, price feature and reputation are separate");
            Expect(Offer(75)=="invalid_option","unlisted button rejected");
            Read();
            Expect(Offer(13)=="bargain_pending" && receipt["reaction"]==null,"pending submission");
            Expect(ActAtCounter(request,new JObject())=="bargain_unavailable","no second submission during dialogue");
            Expect(Read()=="bargain_pending" && receipt["reaction"]==null,"wait for native dialogue");
            BargainUIManager.Instance.FinishDialogue(true);
            Expect(Read()=="observed" && (string)receipt["reaction"]=="reluctantly_accepted","settled accepted");
            request["mode"]="offer";request["bargain_token"]="expired";
            Expect(ActAtCounter(request,new JObject())=="bargain_changed","stale token");
            BargainUIManager.Instance=new BargainUIManager();
            Read();Offer(13);
            merchant.clientIntent=StoreClient.ClientIntent.DIALOGUE;
            BargainUIManager.Instance.FinishDialogue(false);
            Expect(Read()=="observed" && (string)receipt["reaction"]=="rejected","dialogue intent alone is not departure");
            BargainUIManager.Instance=new BargainUIManager();
            Read();Offer(13);
            PlayerStore.Instance.currentClientInstance=null;
            Expect(Read()=="customer_left" && (string)receipt["reaction"]=="left","confirmed handoff after pending offer");
            PlayerStore.Instance.currentClientInstance=original;
            ObserveVisit();
            PlayerStore.Instance.isAugIntroduced=false;
            Console.WriteLine("Bargain replay passed.");
        }
    }
}
