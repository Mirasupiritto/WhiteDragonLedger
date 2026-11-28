using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace WhiteDragon.Ledger
{
    public class ReplaySprite { public string name; }
    public class ReplayPortrait { public ReplaySprite sprite; public bool enabled=true; }
    public class SpriteDict { public static SpriteDict Instance=new SpriteDict(); public Dictionary<string,ReplaySprite> spriteDictionary=new Dictionary<string,ReplaySprite>(); }
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
        public void OnClick()
        {
            var client=PlayerStore.Instance.currentClientInstance.client;
            var intercept=client.tryInterceptReport;
            client.tryInterceptReport=null;
            if (intercept?.Invoke()!=true) client.reported=true;
        }
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
            var art=new ReplaySprite { name="wanted4" };
            SpriteDict.Instance.spriteDictionary["wanted4"]=art;
            foreach (var shown in new[]{null,new ReplaySprite { name="regular" },art})
            {
                avatar.image.sprite=shown;
                var view=new JObject();
                AttachPosterMatch(view);
                Expect((view["poster_match"]!=null)==(shown==art),"only the pictured appearance matches");
            }
            // S8: same-name Sprite instance or MaidensPawnshopHD cp_hd_portrait_<id> matches poster_match
            StoreClientListWanted.POSTER_IDS.Add("wanted1");
            SpriteDict.Instance.spriteDictionary["wanted1"]=new ReplaySprite { name="wanted1" };
            avatar.image.sprite=new ReplaySprite { name="wanted1" };
            var s8View1=new JObject();
            AttachPosterMatch(s8View1);
            Expect((string)s8View1["poster_match"]?["poster_id"]=="wanted1","S8 same-name Sprite clone matches wanted1");
            avatar.image.sprite=new ReplaySprite { name="cp_hd_portrait_wanted1" };
            var s8View2=new JObject();
            AttachPosterMatch(s8View2);
            Expect((string)s8View2["poster_match"]?["poster_id"]=="wanted1","S8 MaidensPawnshopHD cp_hd_portrait_wanted1 matches wanted1");
            avatar.image.sprite=new ReplaySprite { name="cp_hd_portrait_regular" };
            var s8View3=new JObject();
            AttachPosterMatch(s8View3);
            Expect(s8View3["poster_match"]==null,"S8 regular portrait does not false-match");
            StoreClientListWanted.POSTER_IDS.Remove("wanted1");
            Expect(ReportLight(true,true,false,false,false) && !ReportLight(true,true,false,true,false) &&
                !ReportLight(false,false,false,false,false),"ready, reported and empty alarm");
            var req=new JObject { ["visit"]=visitId, ["mode"]="read", ["expires_ms"]=long.MaxValue,
                ["generation"]=generation, ["revision"]=sequence };
            var receipt=new JObject();
            Expect(ReportAction(req,receipt)=="observed","read alarm");
            req["report_token"]=receipt["report_token"];
            var firstToken=(string)receipt["report_token"];
            req["mode"]="report";
            merchant.tryInterceptReport=()=>true;
            receipt=new JObject();
            Expect(ReportAction(req,receipt)=="report_intercepted" && (string)receipt["next_tool"]=="get_trade_context",
                "intercept ends report polling");
            ObserveReport();
            var dialog=DialogUIManager.Instance;
            DialogUIManager.Instance=new DialogUIManager { current=new object(), typing=true };
            req["mode"]="read";
            receipt=new JObject();
            Expect(ReportAction(req,receipt)=="report_intercepted" && (string)receipt["next_tool"]=="get_trade_context",
                "same visit retains interception while speaking");
            DialogUIManager.Instance=dialog;
            req["mode"]="read";
            receipt=new JObject();
            Expect(ReportAction(req,receipt)=="observed" && (bool)receipt["light"] &&
                (bool)receipt["intercepted_before"] && receipt["report_token"]!=null &&
                receipt["choice_hint"]!=null,
                "after interception speech ends, read returns fresh light and token");
            // The published revision did not move (day 14, Arthur Lafleur stayed at revision 53), so the
            // token itself must change or the MCP's deterministic command ID replays the intercepted receipt.
            Expect((int)receipt["revision"]==(int)req["revision"] && (string)receipt["report_token"]!=firstToken,
                "token changes after an interception at the same revision");
            req["mode"]="report";
            req["report_token"]=firstToken;
            Expect(ReportAction(req,new JObject())=="stale_observation","pre-interception token cannot press again");
            req["report_token"]=receipt["report_token"];
            Expect(ReportAction(req,new JObject())=="reported","second report after interception succeeds");
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
