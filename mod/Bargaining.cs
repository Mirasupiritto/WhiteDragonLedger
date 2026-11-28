using System;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace WhiteDragon.Ledger
{
    public sealed partial class InventoryObserver
    {
        private string bargainToken;
        private JObject lastBargain;
        private const BindingFlags BargainMembers = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;

        // 保留反射：無等效公開 API（W0 調查：BargainUsesRoll、ComputePreviewMultipliers、GetMarkupBudgetCutPercent 為 private）
        private static object NativeBargain(string name, params object[] args) =>
            typeof(BargainUIManager).GetMethod(name,BargainMembers).Invoke(BargainUIManager.Instance,args);

        private string pendingBargainVisit;
        private BargainType pendingBargainType;

        // 保留反射：無等效公開 API（W0 調查：PRICE_PERCENTS、BLACKMAIL_PERCENT、THREATEN_PERCENT、DISCOUNT_BUDGET_FACTOR 與 *Applied 為 private）
        private static object BargainField(string name) =>
            typeof(BargainUIManager).GetField(name,BargainMembers).GetValue(BargainUIManager.Instance);

        private static int[] ButtonPercents(BargainType type)
        {
            if (type==BargainType.Blackmail) return new[]{(int)BargainField("BLACKMAIL_PERCENT")};
            if (type==BargainType.Threaten) return new[]{(int)BargainField("THREATEN_PERCENT")};
            return (int[])BargainField("PRICE_PERCENTS");
        }

        private static bool WasApplied(BargainType type)
        {
            // Gifts always succeed. For rolled offers the Offered getters also include rejections.
            switch (type)
            {
                case BargainType.Discount: return BargainUIManager.Instance.DiscountOffered;
                case BargainType.BuyingMarkup: return BargainUIManager.Instance.BuyingMarkupOffered;
                case BargainType.Markup: return (bool)BargainField("markupApplied");
                case BargainType.BuyingDiscount: return (bool)BargainField("buyingDiscountApplied");
                case BargainType.Blackmail: return (bool)BargainField("blackmailApplied");
                case BargainType.Threaten: return (bool)BargainField("threatenApplied");
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        // Preview the price feature this button would add. Existing modifiers and rounding belong to the table quote.
        private JObject OfferPriceFactors(BargainType type,int percent)
        {
            ItemFeature sell=null, buy=null;
            switch (type)
            {
                case BargainType.Discount: sell=ItemFeatureList.BargainDiscount(percent); break;
                case BargainType.Markup: sell=ItemFeatureList.BargainMarkup(percent); break;
                case BargainType.BuyingDiscount: buy=ItemFeatureList.BargainBuyingDiscount(percent); break;
                case BargainType.BuyingMarkup: buy=ItemFeatureList.BargainBuyingMarkup(percent); break;
                case BargainType.Blackmail:
                    if (merchant.IsClientBuying()) sell=ItemFeatureList.BlackmailMarkup(percent);
                    if (merchant.IsClientSelling()) buy=ItemFeatureList.BlackmailDiscount(percent);
                    break;
                case BargainType.Threaten:
                    if (merchant.IsClientBuying()) sell=ItemFeatureList.ThreatenMarkup(percent);
                    if (merchant.IsClientSelling()) buy=ItemFeatureList.ThreatenDiscount(percent);
                    break;
            }
            return new JObject { ["sell"]=1+(sell?.GetCurrentValueMod() ?? 0)/100.0,
                ["buy"]=1+(buy?.GetCurrentValueMod() ?? 0)/100.0 };
        }

        private static bool CanOfferType(BargainUIManager ui, BargainType type)
        {
            switch (type)
            {
                case BargainType.Discount: return ui.CanOfferDiscount();
                case BargainType.Markup: return ui.CanOfferMarkup();
                case BargainType.BuyingMarkup: return ui.CanOfferBuyingMarkup();
                case BargainType.BuyingDiscount: return ui.CanDemandBuyingDiscount();
                case BargainType.Blackmail: return ui.CanBlackmail();
                case BargainType.Threaten: return ui.CanThreatenAug();
                default: return false;
            }
        }

        private JObject BargainView()
        {
            var ui = BargainUIManager.Instance;
            var options = new JArray();
            foreach (BargainType type in Enum.GetValues(typeof(BargainType)))
            {
                if (!CanOfferType(ui, type) ||
                    (type == BargainType.Threaten && !PlayerStore.Instance.isAugIntroduced)) continue;
                bool crime = type == BargainType.Threaten || type == BargainType.Blackmail;
                foreach (int percent in ButtonPercents(type))
                {
                    var rep = ((double sell,double buy))NativeBargain("ComputePreviewMultipliers",type,percent);
                    var option = new JObject { ["type"]=type.ToString(), ["percent"]=percent,
                        ["uses_roll"]=(bool)NativeBargain("BargainUsesRoll",type),
                        ["leave_chance"]=ui.GetBargainLeaveChance(type,percent), ["success_chance"]=ui.GetBargainSuccessChance(type,percent),
                        ["offer_price_factors"]=OfferPriceFactors(type,percent),
                        ["reputation_multipliers"]=new JObject { ["sell"]=rep.sell, ["buy"]=rep.buy }, ["crime"]=crime,
                        ["budget_cut_percent"]=merchant.useClientBudget && type==BargainType.Markup ?
                            (int)NativeBargain("GetMarkupBudgetCutPercent",percent) : 0,
                        ["reputation_faction"]=merchant.clientFaction,
                        ["reputation_hint"]=$"{merchant.clientFaction} ×{rep.sell:0.##}（賣）／×{rep.buy:0.##}（買）" };
                    if (merchant.useClientBudget && type==BargainType.Discount)
                        option["budget_increase_percent"]=percent*(float)BargainField("DISCOUNT_BUDGET_FACTOR");
                    options.Add(option);
                }
            }
            return new JObject { ["visit"]=visitId, ["revision"]=sequence, ["options"]=options,
                ["table_quote"]=TableQuote(), ["remaining_budget"]=merchant.useClientBudget ? (JToken)merchant.GetBudget() : null,
                ["reputation_multipliers"]=new JObject { ["sell"]=merchant.GetTradeRepMultiplier(true), ["buy"]=merchant.GetTradeRepMultiplier(false) } };
        }

        private string BargainAction(JObject request, JObject receipt)
        {
            ObserveVisit();
            string requested=(string)request["visit"];
            bool read=(string)request["mode"]=="read";
            bool settling=read && pendingBargainVisit!=null && requested==pendingBargainVisit;
            if (visitId == null || visitId != requested)
            {
                if (settling)
                {
                    receipt["reaction"]="left";
                    receipt["customer_present"]=false;
                    pendingBargainVisit=null;
                }
                return VisitMismatchStatus(receipt);
            }
            string stage = CounterStage(true);
            bool clientMoving = StoreClientMono.Instance != null && StoreClientMono.Instance.IsMoving();
            bool dialoguePlaying = DialogUIManager.Instance != null && DialogUIManager.Instance.GetCurrentDialog() != null;
            bool busy=BargainUIManager.Instance==null || Interactable.isPaused ||
                clientMoving || dialoguePlaying ||
                StoreUIManager.Instance.isAboutToStartMainDialog;
            if (settling)
            {
                if (busy || stage=="speaking" || stage=="advance_required" || stage=="conversation_paused")
                {
                    receipt["stage"]=stage;
                    receipt["next_tool"]=stage=="advance_required" ? "get_trade_context" : "bargain";
                    return "bargain_pending";
                }
                receipt["customer_present"]=true;
                receipt["reaction"]=!WasApplied(pendingBargainType) ? "rejected" :
                    pendingBargainType==BargainType.Discount || pendingBargainType==BargainType.BuyingMarkup ?
                    "accepted" : "reluctantly_accepted";
                pendingBargainVisit=null;
                if (stage!="ready") { receipt["stage"]=stage; return "bargain_settled"; }
            }
            if (stage != "ready" || busy)
            {
                receipt["reason"]=stage == "ready" ? "panel_unavailable" : stage;
                return "bargain_unavailable";
            }
            var view = BargainView();
            if ((string)request["mode"] == "read")
            {
                lastBargain=view;
                receipt["bargain_token"]=bargainToken=Guid.NewGuid().ToString("N");
                receipt.Merge(view);
                return "observed";
            }
            if ((string)request["mode"] != "offer") return "invalid_action";
            if (bargainToken == null || (string)request["bargain_token"] != bargainToken || !JToken.DeepEquals(view,lastBargain))
            { receipt["reason"]="read_current_options"; return "bargain_changed"; }
            var choice=view["options"].FirstOrDefault(o=>(string)o["type"]==(string)request["type"] &&
                (int?)o["percent"]==(int?)request["percent"]);
            if (choice == null) { receipt["reason"]="option_not_listed"; return "invalid_option"; }
            bargainToken=null;
            var type=(BargainType)Enum.Parse(typeof(BargainType),(string)choice["type"]);
            int percent=(int)choice["percent"];
            var ui=BargainUIManager.Instance;
            switch (type)
            {
                case BargainType.Discount: ui.OfferDiscount(percent); break;
                case BargainType.Markup: ui.OfferMarkup(percent); break;
                case BargainType.BuyingMarkup: ui.OfferBuyingMarkup(percent); break;
                case BargainType.BuyingDiscount: ui.DemandBuyingDiscount(percent); break;
                case BargainType.Blackmail: ui.Blackmail(); break;
                case BargainType.Threaten: ui.ThreatenAug(); break;
            }
            pendingBargainVisit=visitId;
            pendingBargainType=type;
            receipt["visit"]=visitId;
            receipt["crime"]=choice["crime"];
            receipt["reputation_faction"]=merchant.clientFaction;
            receipt["next_tool"]="bargain";
            return "bargain_pending";
        }
    }
}
