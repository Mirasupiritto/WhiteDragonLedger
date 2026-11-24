using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine.UI;

namespace WhiteDragon.Ledger
{
    public partial class GameItem
    {
        public int responseClicks;
        public void ActivateSlotItem() { responseClicks++; onTestActivate?.Invoke(); }
    }
    public class TabletCheckbox
    {
        public Window gameObject = new Window { activeInHierarchy=true };
        public Button button = new Button();
        public StoreService service;
        public bool IsOn => service.isUsing;
        public void Toggle() { service.isUsing = !service.isUsing; }
    }
    public partial class EndOfDayUIManager
    {
        private List<(StoreService, TabletCheckbox)> rows = new List<(StoreService, TabletCheckbox)>();
        public void Add(StoreService service) => rows.Add((service, new TabletCheckbox { service=service }));
    }
    public sealed partial class InventoryObserver
    {
        private void ReplayCounterDay()
        {
            var store = PlayerStore.Instance;
            store.storeState = PlayerStore.StoreState.OPEN;
            phase = "OPEN";
            store.currentClientInstance = new StoreClientInstance();
            ObserveVisit();
            var talk = new DialogUIManager {current=new object(),choice=true};
            DialogUIManager.Instance = talk;
            var option = new DialogChoiceBubble {index=2, enabled=true};
            option.gameObject.activeInHierarchy=true;
            option.TMP.text="Trade";
            talk.dialogChoiceBubbles.Add(option);
            var view = CounterView();
            Expect(view["wanted_name_match"]==null && view["consequence"]==null && view["story_branch"]==null,"ordinary choice omits story hints");
            merchant.identifier="cartelHandlerWeekly";
            Expect((string)CounterView()["consequence"]=="unknown","unresolved cartel choice marked for player");
            merchant.displayName="name_odin"; merchant.identifier="cartelSec";
            view=CounterView();
            Expect((bool)view["story_branch"] && (string)view["consequence"]=="unknown" && (string)view["next_action"]=="player_visual_check" &&
                ((JObject)view["wanted_name_match"]).Count==2,"cartel branch and exact poster name without appearance");
            merchant.displayName="unmatched"; merchant.identifier="regular";
            var request = new JObject { ["generation"]=generation,["revision"]=sequence,["visit"]=visitId,
                ["action"]="choose_dialogue",["choice_id"]=2,["page"]=view["page"],
                ["expires_ms"]=DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds() };
            var reply = new JObject();
            Expect((string)view["dialog_choices"][0]["text"]=="Trade","visible counter choice");
            option.TMP.text="Leave";
            Expect(ActAtCounter(request,reply)=="page_changed" && option.clicks==0,"changed option text");
            option.enabled=false;
            request["page"]=CounterView()["page"];
            Expect(ActAtCounter(request,reply)=="choice_unavailable","disabled option");
            option.enabled=true;
            request["page"]=CounterView()["page"];
            Expect(ActAtCounter(request,reply)=="choice_submitted" && option.clicks==1,"click selected visible option");
            talk.choice=false;
            talk.isWaitingForManualAdvance=true;
            request["action"]="advance";
            request["page"]=CounterView()["page"];
            talk.current=new object();
            Expect(ActAtCounter(request,reply)=="page_changed","old manual page");
            request["page"]=CounterView()["page"];
            Expect(ActAtCounter(request,reply)=="advanced" && !talk.isWaitingForManualAdvance,"one manual page");

            var shop = EmporiumEntry.Instance;
            shop.responseInventory=new GameInventory();
            shop.responseWindow=new PixelWindow {visible=true};
            var paper=Item(8781,"response",0);
            paper.To(shop.responseInventory);
            request["action"]="response_choice";
            var responses=CounterView();
            request["page"]=responses["page"];
            request["choice_id"]=responses["response_choices"][0]["id"];
            store.currentClientInstance=new StoreClientInstance();
            Expect(ActAtCounter(request,reply)=="customer_replaced" && paper.responseClicks==0,"changed visit reports customer_replaced");
            request["visit"]=visitId;
            request["page"]=CounterView()["page"];
            shop.responseWindow.visible=false;
            Expect(ActAtCounter(request,reply)=="page_changed","closed response window");
            shop.responseWindow.visible=true;
            request["page"]=CounterView()["page"];
            Expect(ActAtCounter(request,reply)=="choice_submitted" && paper.responseClicks==1,"activate displayed response item");
            shop.responseWindow.visible=false;

            var summary=EndOfDayUIManager.Instance;
            int submissions=summary.submissions;
            StoreStation.Instance.dayCounter++;
            store.storeState=PlayerStore.StoreState.CLOSED;
            phase="CLOSED";
            store.storeService.Clear();
            var cleaning=new StoreService {id="clean",displayName="Cleaning",cost=7,isUsing=false};
            var power=new StoreService {id="power",displayName="Power",cost=10,isMandatory=true};
            store.storeService.Add(cleaning);
            store.storeService.Add(power);
            summary.Add(cleaning);
            summary.Add(power);
            summary.IsOpen=false;
            request["action"]="prepare_sleep";
            request["day"]=StoreStation.Instance.dayCounter;
            request["bill_token"]=NightBill()["bill_token"];
            Expect(ActAtCounter(request,reply)=="bill_open" && (int)reply["bill"]["service_cost"]==10 &&
                ((JArray)reply["bill"]["services"]).Count==2 && summary.submissions==submissions,"open bill without sleeping");
            request["action"]="night_service";
            request["service_id"]="clean";
            request["cost"]=7;
            request["selected"]=true;
            request["bill_token"]=reply["bill"]["bill_token"];
            Expect(ActAtCounter(request,reply)=="service_set" && (int)reply["bill"]["service_cost"]==17,"set service and total");
            Expect(ActAtCounter(request,reply)=="bill_changed" && cleaning.isUsing,"old bill cannot toggle again");
            request["bill_token"]=NightBill()["bill_token"];
            Expect(ActAtCounter(request,reply)=="already_set","target state is idempotent");
            request["selected"]=false;
            cleaning.cost=9;
            Expect(ActAtCounter(request,reply)=="bill_changed" && cleaning.isUsing,"price changed");
            request["bill_token"]=NightBill()["bill_token"];
            Expect(ActAtCounter(request,reply)=="service_cost_changed","wrong exact price");
            request["cost"]=9;
            cleaning.currentCooldown=1;
            request["bill_token"]=NightBill()["bill_token"];
            Expect(ActAtCounter(request,reply)=="service_unavailable","cooldown service");
            request["service_id"]="power";
            request["cost"]=10;
            Expect(ActAtCounter(request,reply)=="service_unavailable" && power.isUsing,"mandatory service");
            request["service_id"]="clean";
            request["cost"]=9;
            cleaning.currentCooldown=0;
            request["bill_token"]=NightBill()["bill_token"];
            Expect(ActAtCounter(request,reply)=="service_set" && (int)reply["bill"]["service_cost"]==10 &&
                summary.submissions==submissions,"cancel only the selected service");
            request["action"]="sleep";
            request["service_cost"]=10;
            request["bill_token"]="earlier-selection";
            store.playerCash=50;
            Expect(ActAtCounter(request,reply)=="bill_changed" && summary.submissions==submissions,"equal total and stale bill");
            request["bill_token"]=NightBill()["bill_token"];
            Expect(ActAtCounter(request,reply)=="sleep_submitted" && summary.submissions==submissions+1,"explicit confirmation only");
            Console.WriteLine("Counter and night bill replay passed: page identity, choices, response items, service changes without sleep.");
        }
    }
}
