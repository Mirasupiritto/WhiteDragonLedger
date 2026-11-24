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

        // These private members drive the native button state, reputation preview and outcome dialogue.
        private static object NativeBargain(string name, params object[] args) =>
            typeof(BargainUIManager).GetMethod(name,BargainMembers).Invoke(BargainUIManager.Instance,args);

        private static int AppliedPercent(BargainType type)
        {
            string prefix = char.ToLowerInvariant(type.ToString()[0]) + type.ToString().Substring(1);
            bool gift = type == BargainType.Discount || type == BargainType.BuyingMarkup;
            var manager = typeof(BargainUIManager);
            if (!(bool)manager.GetField(prefix + (gift ? "Offered" : "Applied"),BargainMembers).GetValue(BargainUIManager.Instance)) return 0;
            return type == BargainType.Blackmail || type == BargainType.Threaten ? 20 :
                (int)manager.GetField(prefix+"Percent",BargainMembers).GetValue(BargainUIManager.Instance);
        }

        private JObject PriceMultipliers(BargainType? preview = null, int percent = 0)
        {
            double sell = 1, buy = 1;
            foreach (BargainType type in Enum.GetValues(typeof(BargainType)))
            {
                double p = (preview == type ? percent : AppliedPercent(type)) / 100.0;
                switch (type)
                {
                    case BargainType.Discount: sell *= 1-p; break;
                    case BargainType.Markup: sell *= 1+p; break;
                    case BargainType.BuyingMarkup: buy *= 1+p; break;
                    case BargainType.BuyingDiscount: buy *= 1-p; break;
                    default:
                        if (merchant.IsClientBuying()) sell *= 1+p;
                        if (merchant.IsClientSelling()) buy *= 1-p;
                        break;
                }
            }
            return new JObject { ["sell"]=sell, ["buy"]=buy };
        }

        private JObject BargainView()
        {
            var ui = BargainUIManager.Instance;
            var options = new JArray();
            foreach (BargainType type in Enum.GetValues(typeof(BargainType)))
            {
                if (!(bool)NativeBargain("CanOffer",type) ||
                    (type == BargainType.Threaten && !PlayerStore.Instance.isAugIntroduced)) continue;
                bool crime = type == BargainType.Threaten || type == BargainType.Blackmail;
                foreach (int percent in crime ? new[]{20} : new[]{10,25,50,75})
                {
                    var rep = ((double sell,double buy))NativeBargain("ComputePreviewMultipliers",type,percent);
                    var option = new JObject { ["type"]=type.ToString(), ["percent"]=percent,
                        ["uses_roll"]=(bool)NativeBargain("BargainUsesRoll",type),
                        ["leave_chance"]=ui.GetBargainLeaveChance(type,percent), ["success_chance"]=ui.GetBargainSuccessChance(type,percent),
                        ["price_multipliers"]=PriceMultipliers(type,percent), ["crime"]=crime,
                        ["reputation_faction"]=merchant.clientFaction,
                        ["reputation_hint"]=$"{merchant.clientFaction} ×{rep.sell:0.##}（賣）／×{rep.buy:0.##}（買）" };
                    options.Add(option);
                }
            }
            return new JObject { ["visit"]=visitId, ["revision"]=sequence, ["options"]=options,
                ["price_multipliers"]=PriceMultipliers() };
        }

        private string BargainAction(JObject request, JObject receipt)
        {
            ObserveVisit();
            if (visitId == null || visitId != (string)request["visit"]) return VisitMismatchStatus(receipt);
            string stage = CounterStage(true);
            if (stage != "ready" || BargainUIManager.Instance == null || Interactable.isPaused ||
                (bool)NativeBargain("IsClientMoving") || (bool)NativeBargain("IsDialoguePlaying") || StoreUIManager.Instance.isAboutToStartMainDialog)
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
            bool leaving=merchant.clientIntent == StoreClient.ClientIntent.DIALOGUE;
            receipt["customer_present"]=PlayerStore.Instance.currentClientInstance?.GetClientBlueprint()==merchant;
            receipt["crime"]=choice["crime"];
            receipt["reputation_faction"]=merchant.clientFaction;
            receipt["reaction"]=leaving ? "leaving" : AppliedPercent(type)==0 ? "rejected" :
                type==BargainType.Discount || type==BargainType.BuyingMarkup ? "accepted" : "reluctantly_accepted";
            receipt["price_multipliers"]=PriceMultipliers();
            return leaving || !(bool)receipt["customer_present"] ? "customer_left" : "bargain_offered";
        }
    }
}
