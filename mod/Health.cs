using System;
using Newtonsoft.Json.Linq;

namespace WhiteDragon.Ledger
{
    public sealed partial class InventoryObserver
    {
        private static JObject Withdrawal(string kind, int days, bool active) => new JObject {
            ["kind"]=kind, ["days_since"]=days, ["debuff_active"]=active,
            ["debuff_after_days"]=HealthData.ADDICTION_DEBUFF_DAY, ["cure_after_days"]=HealthData.ADDICTION_CURE_DAY
        };

        private JObject HealthView(int day, JArray alerts)
        {
            var data=PlayerStore.Instance.healthData;
            if (data==null) return new JObject { ["status"]="unavailable" };
            int penalty=data.GetTotalAddictionPenalty();
            var health=new JObject { ["luck"]=HealthData.GetLuck(), ["can_read"]=HealthData.CanRead(),
                ["trade_penalty_percent"]=penalty };
            var addictions=new JArray();
            if (HealthData.IsNicotineAddicted()) addictions.Add(Withdrawal("nicotine",data.daysSinceNicotineConsumption,data.HasNicotineDebuff()));
            if (HealthData.IsAlcoholAddicted()) addictions.Add(Withdrawal("alcohol",data.daysSinceAlcoholConsumption,data.HasAlcoholDebuff()));
            if (HealthData.IsNarcoticAddicted())
            {
                var row=Withdrawal("narcotic",data.daysSinceNarcoticConsumption,data.HasNarcoticDebuff());
                row["days_since_relief"]=data.daysSinceNarcoticRelief;
                addictions.Add(row);
            }
            if (HealthData.IsGamblingAddicted()) addictions.Add(new JObject { ["kind"]="gambling", ["effect"]="automatic_lottery_purchase" });
            if (addictions.Count>0) health["addictions"]=addictions;
            if (penalty>0) alerts.Add(new JObject { ["type"]="addiction_withdrawal", ["trade_penalty_percent"]=penalty });

            var counts=new JObject { ["regular"]=0, ["expired"]=0, ["counterfeit"]=0, ["unusable"]=0, ["unidentified"]=0 };
            bool sentinel=HealthData.IsSentinel();
            if (sentinel)
            {
                int days=data.daysSinceSentinelInjection;
                string stage=days>=HealthData.SENTINEL_CRITICAL_DAY ? "critical" : days>=HealthData.SENTINEL_SEVERE_DAY ? "severe" :
                    days>=HealthData.SENTINEL_OVERDUE_DAY ? "overdue" : days>=HealthData.SENTINEL_WARNING_DAY ? "warning" : "ok";
                health["sentinel"]=new JObject { ["stage"]=stage, ["days_since_injection"]=days, ["pool"]=data.sentinelPool,
                    ["death_pool"]=HealthData.SENTINEL_DEATH_POOL, ["warning_day"]=HealthData.SENTINEL_WARNING_DAY,
                    ["overdue_day"]=HealthData.SENTINEL_OVERDUE_DAY, ["severe_day"]=HealthData.SENTINEL_SEVERE_DAY,
                    ["critical_day"]=HealthData.SENTINEL_CRITICAL_DAY, ["immunivax_owned"]=counts };
                if (stage!="ok") alerts.Add(new JObject { ["type"]="sentinel_injection_due", ["stage"]=stage,
                    ["days_left"]=Math.Max(0,HealthData.SENTINEL_OVERDUE_DAY-days) });
            }
            if (sentinel || addictions.Count>0) AddHealthItems(health,counts,sentinel);
            if (StartingPerk.IsBlackmail())
            {
                int due=day<=8 ? 8 : day+(8-day%7)%7;
                if (day==due && phase=="CLOSED") due+=7;
                int cost=StoreClientListSpec.GetBlackmailCost()+due-day;
                health["obligations"]=new JArray(new JObject { ["type"]="blackmail_payment", ["due_day"]=due,
                    ["amount"]=cost, ["payment"]="choose_dialogue", ["refusal"]="下層、革命、治安、上層及 NC 聲望各減 30，勒索特性移除。" });
                if (due==day+1) alerts.Add(new JObject { ["type"]="blackmail_due_tomorrow", ["due_day"]=due, ["amount"]=cost });
            }
            return health;
        }

        private void AttachHealthAlerts(int day,JObject receipt)
        {
            var alerts=new JArray();
            HealthView(day,alerts);
            if (alerts.Count>0) receipt["alerts"]=alerts;
        }

        private void AddHealthItems(JObject health,JObject counts,bool sentinel)
        {
            var available=new JArray();
            foreach (var pair in itemsById)
            {
                var item=pair.Value;
                if (item?.parentInventory==null) continue;
                var feature=sentinel ? item.FindItemFeatureByID("CATEGORY_GENUINE_INJECTOR") : null;
                string kind=feature!=null ? "immunivax" : item.IsTag("NICOTINE_VALUE") ? "nicotine" :
                    item.IsTag("ALCOHOL_VALUE") ? "alcohol" : item.IsTag("NARCOTIC_VALUE") ? "narcotic" : null;
                if (kind==null || !GeneralHelper.IsItemEffectivelyOwned(item) || GeneralHelper.IsDisplayOnly(item)) continue;
                var location=StockLocation(item);
                string area=(string)location?["area"];
                if (area!="inventory" && area!="showcase") continue;
                var row=new JObject { ["item_id"]=pair.Key, ["kind"]=kind, ["name"]=item.GetDisplayName(true) };
                if (feature!=null)
                {
                    string id=feature.IsExposed() ? feature.GetCurrentItemCondition()?.identifier : null;
                    string quality=id=="genuineInjector" ? "regular" : id=="expiredInjector" ? "expired" :
                        id=="counterfeitInjector" ? "counterfeit" : id=="unusableInjector" ? "unusable" : "unidentified";
                    counts[quality]=(int)counts[quality]+item.unitCount;
                    if (quality=="unusable") continue;
                    row["quality"]=quality;
                }
                if (item.MayActivateSlotItem() && item.CanActivateSlotItem()) available.Add(row);
            }
            if (available.Count==0) return;
            health["usable_items"]=available;
            health["next_tool"]="item_menu";
            health["verb"]="activate";
        }
    }
}
