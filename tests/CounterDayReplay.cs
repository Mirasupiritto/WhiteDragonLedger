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
            option.TMP.text="[接受盘查。]";
            var exposure=CounterView()["inspection_exposure"];
            Expect(exposure["on_counter"]!=null && JToken.DeepEquals(exposure["inventory"],SecurityView()["contraband"]["inventory"]),
                "visible inspection choice shows current stock exposure");
            option.TMP.text="Trade";
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
            Expect((string)CounterView()["client_role"]=="buyer_seller","water buyer also offers stock");
            merchant.isClientBuying=false;
            Expect((string)CounterView()["client_role"]=="seller","selling client reports client_role seller");
            merchant.isClientBuying=true;
            merchant.isClientSelling=false;
            Expect((string)CounterView()["client_role"]=="buyer","buying client reports client_role buyer");
            merchant.isClientBuying=false;
            Expect((string)CounterView()["client_role"]=="dialogue","non-trading client reports client_role dialogue");
            merchant.clientIntent=StoreClient.ClientIntent.BARTER;
            Expect((string)CounterView()["client_role"]=="barter","barter client reports client_role barter");
            merchant.clientIntent=StoreClient.ClientIntent.GUNSMITH;
            Expect((string)CounterView()["client_role"]=="service","service client reports client_role service");
            merchant.clientIntent=StoreClient.ClientIntent.MERCHANT;
            merchant.isClientSelling=true;
            merchant.isClientBuying=true;
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
            store.storeState=PlayerStore.StoreState.OPEN;
            phase="OPEN";
            store.currentClientInstance=new StoreClientInstance();
            ObserveVisit();
            lastDemand=new JObject { ["status"]="observed", ["interests"]=new JArray("MATERIAL") };
            lastDemandVisit=visitId;
            barterSource=new BarterOffer();
            merchant.barterOffer=barterSource;
            lastBarterView=new JObject { ["selected_option"]=1, ["can_accept"]=false };
            barterToken="bt_speech";
            DialogUIManager.Instance=new DialogUIManager { current=new object() };
            var speechView=CounterView();
            Expect((string)speechView["stage"]=="speaking" && (bool)speechView["stale_during_speech"] &&
                speechView["demand"]!=null && (string)speechView["barter"]["barter_token"]=="bt_speech",
                "speaking stage retains last demand and barter with stale_during_speech");
            DialogUIManager.Instance=new DialogUIManager();
            store.currentClientInstance=null;
            ObserveVisit();

            // X4 / X'2 / X'0: protected_visitor only applies when interrupt=true (unless force=true or known)
            store.currentClientInstance = new StoreClientInstance { client = new StoreClient { identifier = "scavGeneral", displayName = "拾荒者" } };
            store.isClientArrived = true;
            ObserveVisit();
            var x4Normal = new JObject();
            Expect(ActAtCounter(new JObject {
                ["action"] = "dismiss", ["generation"] = generation, ["revision"] = sequence,
                ["visit"] = visitId, ["interrupt"] = true, ["expires_ms"] = long.MaxValue
            }, x4Normal) == "dismissed", "X4 generic visitor dismisses normally with interrupt=true");

            // Non-interrupt dismiss (finish_customer without interrupt) is never blocked by protected_visitor
            store.currentClientInstance = new StoreClientInstance { client = new StoreClient { identifier = "eleanorIntro", displayName = "埃莉諾" } };
            store.isClientArrived = true;
            ObserveVisit();
            Expect(ActAtCounter(new JObject {
                ["action"] = "dismiss", ["generation"] = generation, ["revision"] = sequence,
                ["visit"] = visitId, ["expires_ms"] = long.MaxValue
            }, new JObject()) == "dismissed", "X'0 story NPC normal dismiss (without interrupt) succeeds without force");

            store.currentClientInstance = new StoreClientInstance { client = new StoreClient { identifier = "eleanorIntro", displayName = "埃莉諾" } };
            store.isClientArrived = true;
            ObserveVisit();
            var x4StoryBlock = new JObject();
            Expect(ActAtCounter(new JObject {
                ["action"] = "dismiss", ["generation"] = generation, ["revision"] = sequence,
                ["visit"] = visitId, ["interrupt"] = true, ["expires_ms"] = long.MaxValue
            }, x4StoryBlock) == "protected_visitor" && (string)x4StoryBlock["reason"] == "story_npc",
                "X4 story NPC blocked on interrupt=true without force");
            var x4StoryForce = new JObject();
            Expect(ActAtCounter(new JObject {
                ["action"] = "dismiss", ["generation"] = generation, ["revision"] = sequence,
                ["visit"] = visitId, ["interrupt"] = true, ["force"] = true, ["expires_ms"] = long.MaxValue
            }, x4StoryForce) == "dismissed", "X4 story NPC dismissed when interrupt=true and force=true");

            store.currentClientInstance = new StoreClientInstance { client = new StoreClient { identifier = "seedVisitorFirst", displayName = "SeedFirst" } };
            store.isClientArrived = true;
            ObserveVisit();
            Expect(ActAtCounter(new JObject {
                ["action"] = "dismiss", ["generation"] = generation, ["revision"] = sequence,
                ["visit"] = visitId, ["expires_ms"] = long.MaxValue
            }, new JObject()) == "dismissed", "X'0 first-visit named visitor normal dismiss (without interrupt) succeeds without force");

            store.currentClientInstance = new StoreClientInstance { client = new StoreClient { identifier = "seedVisitor", displayName = "Seed" } };
            store.isClientArrived = true;
            ObserveVisit();
            var x4NamedBlock = new JObject();
            Expect(ActAtCounter(new JObject {
                ["action"] = "dismiss", ["generation"] = generation, ["revision"] = sequence,
                ["visit"] = visitId, ["interrupt"] = true, ["expires_ms"] = long.MaxValue
            }, x4NamedBlock) == "protected_visitor" && (string)x4NamedBlock["reason"] == "first_visit_named",
                "X4 first-visit named visitor blocked on interrupt=true without force");
            // Natural departure records visitor so a repeat visit is not blocked
            store.currentClientInstance = null;
            ObserveVisit();
            store.currentClientInstance = new StoreClientInstance { client = new StoreClient { identifier = "seedVisitor", displayName = "Seed" } };
            store.isClientArrived = true;
            ObserveVisit();
            Expect(ActAtCounter(new JObject {
                ["action"] = "dismiss", ["generation"] = generation, ["revision"] = sequence,
                ["visit"] = visitId, ["interrupt"] = true, ["expires_ms"] = long.MaxValue
            }, new JObject()) == "dismissed", "X'2 natural departure marks named visitor as seen");

            // After reload (seenNamedVisitors cleared), ledger visited_known=true allows dismiss
            seenNamedVisitors.Clear();
            store.currentClientInstance = new StoreClientInstance { client = new StoreClient { identifier = "seedVisitor", displayName = "Seed" } };
            store.isClientArrived = true;
            ObserveVisit();
            var x4AfterReset = new JObject();
            Expect(ActAtCounter(new JObject {
                ["action"] = "dismiss", ["generation"] = generation, ["revision"] = sequence,
                ["visit"] = visitId, ["interrupt"] = true, ["visited_known"] = true, ["expires_ms"] = long.MaxValue
            }, x4AfterReset) == "dismissed",
                "X'2 reloaded known visitor is not blocked when visited_known=true");

            // Visitor in phonebook / business card is never treated as first_visit_named
            seenNamedVisitors.Clear();
            store.PhoneClientDict[9991] = new StorePhoneClient { listed = true, dialogFuncId = "CardGuest" };
            store.currentClientInstance = new StoreClientInstance { client = new StoreClient { identifier = "cardGuest", displayName = "CardGuest" } };
            store.isClientArrived = true;
            ObserveVisit();
            Expect(ActAtCounter(new JObject {
                ["action"] = "dismiss", ["generation"] = generation, ["revision"] = sequence,
                ["visit"] = visitId, ["interrupt"] = true, ["expires_ms"] = long.MaxValue
            }, new JObject()) == "dismissed", "X'2 phonebook contact visitor is not blocked");
            store.PhoneClientDict.Remove(9991);
            store.currentClientInstance = null;
            ObserveVisit();

            // X8: demand_highlight_compat off -> omitted; on without mod -> compat_unavailable; on with mod -> highlighted_item_ids
            InventorySorting.Configure("replay");
            var hlItem = new GameItemElement {
                identifier = "apple", uniqueId = 9901,
                rectTransform = new UnityEngine.RectTransform { ReplayChildren = new[] { new UnityEngine.Transform { name = "CDH_Border" } } }
            };
            hlItem.To(EmporiumEntry.Instance.invElement);
            FlushChanges();
            ModHelper.sortConfig.highlightEntry.Value = false;
            var x8Off = CounterView();
            Expect(x8Off["highlighted_item_ids"] == null && x8Off["demand_highlight_status"] == null,
                "X8 demand_highlight_compat off omits highlight fields");
            ModHelper.sortConfig.highlightEntry.Value = true;
            ModLoader.Mods.Clear();
            var x8Missing = CounterView();
            Expect((string)x8Missing["demand_highlight_status"] == "compat_unavailable" && x8Missing["highlighted_item_ids"] == null,
                "X8 demand_highlight_compat on without mod returns compat_unavailable");
            ModLoader.Mods.Add(new LoadedMod {
                Manifest = new ModManifest { ID = "hollow.clientdemandhighlight", Name = "Client Demand Highlight", ModVersion = "1.3.10" },
                IsEnabled = true
            });
            var x8On = CounterView();
            Expect(x8On["highlighted_item_ids"] != null && ((JArray)x8On["highlighted_item_ids"]).Count == 1 && (int)x8On["highlighted_item_ids"][0] == LocalId(hlItem),
                "X8 demand_highlight_compat on with mod returns highlighted_item_ids");
            ModLoader.Mods.Clear();
            ModHelper.sortConfig.highlightEntry.Value = false;
            hlItem.To(null);

            Console.WriteLine("Counter and night bill replay passed: page identity, choices, response items, service changes without sleep.");
        }
    }
}
