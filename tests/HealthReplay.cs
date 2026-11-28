using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace WhiteDragon.Ledger
{
    public partial class HealthData
    {
        public const int ADDICTION_DEBUFF_DAY=3, ADDICTION_CURE_DAY=20, SENTINEL_WARNING_DAY=6,
            SENTINEL_OVERDUE_DAY=7, SENTINEL_SEVERE_DAY=10, SENTINEL_CRITICAL_DAY=14, SENTINEL_DEATH_POOL=20;
        public static bool sentinel,nicotine,narcotic;
        public int daysSinceNicotineConsumption,daysSinceAlcoholConsumption,daysSinceNarcoticConsumption,
            daysSinceNarcoticRelief,daysSinceSentinelInjection,sentinelPool;
        public static bool IsSentinel()=>sentinel;
        public static bool IsNicotineAddicted()=>nicotine;
        public static bool IsAlcoholAddicted()=>false;
        public static bool IsNarcoticAddicted()=>narcotic;
        public static bool IsGamblingAddicted()=>false;
        public bool HasAlcoholDebuff()=>false;
        public bool HasNicotineDebuff()=>nicotine && daysSinceNicotineConsumption>=3;
        public bool HasNarcoticDebuff()=>narcotic && Math.Min(daysSinceNarcoticConsumption,daysSinceNarcoticRelief)>=3;
        public int GetTotalAddictionPenalty()=>(HasNicotineDebuff()?8:0)+(HasNarcoticDebuff()?8:0);
        public static int GetLuck()=>0;
        public static bool CanRead()=>true;
    }
    public partial class StartingPerk { public static bool blackmail; public static bool IsBlackmail()=>blackmail; }
    public class StoreClientListSpec { public static int GetBlackmailCost()=>40+StoreStation.Instance.dayCounter; }
    public partial class GameItem {
        public ItemFeature injector;
        public ItemFeature FindItemFeatureByID(string id)=>id=="CATEGORY_GENUINE_INJECTOR" ? injector : null;
    }
    public partial class ItemFeature { public bool exposed; public bool IsExposed()=>exposed; }
    public class GameOverUIManager {
        public static GameOverUIManager Instance;
        public Window uiPanel=new Window();
        public TMPro.TextMeshProUGUI reason=new TMPro.TextMeshProUGUI();
    }
    public sealed partial class InventoryObserver
    {
        private void ReplayHealth()
        {
            var store=PlayerStore.Instance;
            var data=store.healthData;
            var alerts=new JArray();
            var health=HealthView(1,alerts);
            Expect(health["addictions"]==null && health["sentinel"]==null && alerts.Count==0,"healthy compact receipt");
            HealthData.nicotine=true;
            data.daysSinceNicotineConsumption=3;
            var tobacco=Item(88301,"tobacco",1);
            tobacco.waterTags.Add("NICOTINE_VALUE");
            health=HealthView(1,alerts);
            Expect((bool)health["addictions"][0]["debuff_active"] && (int)health["trade_penalty_percent"]==8 &&
                (string)alerts[0]["type"]=="addiction_withdrawal" && health["usable_items"].Any(i=>(int)i["item_id"]==LocalId(tobacco)),
                "withdrawal offers a usable item without consuming it");
            HealthData.nicotine=false;
            HealthData.sentinel=true;
            var injector=Item(88302,"injector",1);
            injector.injector=new ItemFeature {label=new ItemCondition {identifier="counterfeitInjector"}};
            data.daysSinceSentinelInjection=6;
            var savedDay=StoreStation.Instance.dayCounter;
            StoreStation.Instance.dayCounter=7;
            StartingPerk.blackmail=true;
            alerts=new JArray();
            health=HealthView(7,alerts);
            Expect((string)health["sentinel"]["stage"]=="warning" && (int)health["sentinel"]["immunivax_owned"]["unidentified"]==1 &&
                (int)health["obligations"][0]["amount"]==48 && alerts.Any(a=>(string)a["type"]=="blackmail_due_tomorrow"),
                "warning day, hidden injector quality and first collection");
            injector.injector.exposed=true;
            foreach (var sample in new[]{(7,"overdue"),(10,"severe"),(14,"critical")})
            {
                data.daysSinceSentinelInjection=sample.Item1;
                health=HealthView(7,new JArray());
                Expect((string)health["sentinel"]["stage"]==sample.Item2 &&
                    (int)health["sentinel"]["immunivax_owned"]["counterfeit"]==1,"native stage and revealed injector quality");
            }
            injector.injector.label.identifier="unusableInjector";
            health=HealthView(7,new JArray());
            var owned=health["sentinel"]["immunivax_owned"];
            Expect((int)owned["unusable"]==1 && (int)owned["unidentified"]==0 &&
                !(health["usable_items"]?.Any(i=>(int)i["item_id"]==LocalId(injector)) ?? false),"identified unusable injector excluded from activation");
            var priorPhase=phase;
            var priorCustomer=store.currentClientInstance;
            int priorCash=store.playerCash;
            phase="OPEN";
            StoreStation.Instance.dayCounter=8;
            store.currentClientInstance=new StoreClientInstance();
            store.currentClientInstance.client.identifier="blackmailer";
            ObserveBlackmailPayment(8);
            alerts=new JArray();
            HealthView(8,alerts);
            Expect(alerts.Any(a=>(string)a["type"]=="blackmail_due_today" && (int)a["amount"]==48),"day 8 collection");
            store.playerCash-=48;
            store.currentClientInstance=null;
            ObserveBlackmailPayment(8);
            var next=HealthView(8,new JArray())["obligations"][0];
            Expect((int)next["due_day"]==15 && (int)next["amount"]==55,"advance as soon as collector leaves paid");
            // V2: usable_items excludes items in trashcanInvElement
            tobacco.To(EmporiumEntry.Instance.trashcanInvElement);
            var trashHealth = HealthView(8, new JArray());
            Expect(!(trashHealth["usable_items"]?.Any(i => (int)i["item_id"] == LocalId(tobacco)) ?? false),
                "V2 usable_items excludes items in trash");

            store.playerCash=priorCash;
            store.currentClientInstance=priorCustomer;
            phase=priorPhase;
            StoreStation.Instance.dayCounter=savedDay;
            StartingPerk.blackmail=false;
            HealthData.sentinel=false;
            tobacco.To(null);injector.To(null);
            Console.WriteLine("Health replay passed.");
        }
    }
}
