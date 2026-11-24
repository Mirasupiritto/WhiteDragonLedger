using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace WhiteDragon.Ledger
{
    public class ReplayPortrait { public object sprite; public bool enabled=true; }
    public class SpriteDict { public static SpriteDict Instance=new SpriteDict(); public Dictionary<string,object> spriteDictionary=new Dictionary<string,object>(); }
    public partial class ReplayClientAvatar { public ReplayPortrait image=new ReplayPortrait(); }
    public partial class DialogUIManager { public Bubble secBubble; public bool isSecMonologuePlaying; }
    public partial class StoreUIManager { public ReplayClientAvatar secGameObject=new ReplayClientAvatar(); public bool isReportedAug; }
    public partial class StoreClient { public bool reported; public Func<bool> tryInterceptReport; }
    public partial class PlayerStore { public Action onSecAlarmLeftAction; }
    public class StoreSecButton
    {
        public static StoreSecButton Instance=new StoreSecButton();
        public bool IsClientArrestable()=>true;
        public bool ShouldRequireHold()=>true;
        public string GetTooltip()=>string.Empty;
        public void OnClick()=>PlayerStore.Instance.currentClientInstance.client.reported=true;
    }

    public sealed partial class InventoryObserver
    {
        private void ReplayReport()
        {
            var store=PlayerStore.Instance;
            var avatar=StoreUIManager.Instance.clientGameObject;
            var previous=merchant;
            var oldClient=store.currentClientInstance;
            var oldVisit=visitId;
            var oldPhase=phase;
            var arrived=store.isClientArrived;
            var wasVisible=avatar.gameObject.activeInHierarchy;
            store.currentClientInstance=new StoreClientInstance();
            merchant=store.currentClientInstance.client;
            phase="OPEN";
            visitId=generation;
            store.isClientArrived=true;
            avatar.gameObject.activeInHierarchy=true;
            // Odin was identified by name on the slot 13 run. Vary only the rendered art.
            var art=new object();
            SpriteDict.Instance.spriteDictionary["wanted4"]=art;
            foreach (var shown in new[]{null,new object(),art})
            {
                avatar.image.sprite=shown;
                var view=new JObject();
                AttachPosterMatch(view);
                Expect((view["poster_match"]!=null)==(shown==art),"only the pictured appearance matches");
            }
            Expect(ReportLight(true,true,false,false,false) && !ReportLight(true,true,false,true,false) &&
                !ReportLight(false,false,false,false,false),"ready, reported and empty alarm");
            var req=new JObject { ["visit"]=visitId, ["mode"]="read", ["expires_ms"]=long.MaxValue,
                ["generation"]=generation, ["revision"]=sequence };
            var receipt=new JObject();
            Expect(ReportAction(req,receipt)=="observed","read alarm");
            req["report_token"]=receipt["report_token"];
            req["mode"]="report";
            Expect(ReportAction(req,new JObject())=="reported","native alarm submission");
            var guard=StoreUIManager.Instance.secGameObject.gameObject;
            guard.activeInHierarchy=true;
            ObserveReport();
            req["mode"]="read";
            Expect(ReportAction(req,new JObject())=="reported","guard still present");
            store.currentClientInstance=null;
            guard.activeInHierarchy=false;
            ObserveReport();
            receipt=new JObject();
            Expect(ReportAction(req,receipt)=="arrest_finished" && (bool)receipt["customer_removed"] &&
                (int)receipt["cash_delta"]==0,"guard left without a cash change");
            avatar.image.sprite=null;
            avatar.gameObject.activeInHierarchy=wasVisible;
            store.currentClientInstance=oldClient;
            store.isClientArrived=arrived;
            merchant=previous;
            visitId=oldVisit;
            phase=oldPhase;
            Console.WriteLine("Report replay passed.");
        }
    }
}
