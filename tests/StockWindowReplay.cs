using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace WhiteDragon.Ledger
{
    public sealed partial class InventoryObserver
    {
        private void ReplayStockSection()
        {
            var shop=EmporiumEntry.Instance;
            var store=PlayerStore.Instance;
            phase="OPEN";
            store.storeState=PlayerStore.StoreState.OPEN;
            store.currentClientInstance=new StoreClientInstance();
            var sample=new GameItem {identifier="dream_dust",owned=true,unitCount=1};
            sample.waterTags.Add(TypeHelper.CONTRABAND);
            store.currentClientInstance.client.identifier="courrierChemFree";
            ObservePhoneArrival(sample,shop.frontInvinvElement,null);
            store.currentClientInstance=new StoreClientInstance();
            var delivery=new JObject();
            AttachPhoneArrivals(delivery,new JArray(new JObject { ["id"]=LocalId(sample), ["owned"]=true }));
            Expect((string)delivery["arrivals"][0]["source"]=="phone_delivery" &&
                (int)delivery["arrivals"][0]["ids"][0]==LocalId(sample) && (int)delivery["contraband_outside_bay"]==1,
                "courier receipt survives departure and counts the owned sample");
            store.isClientArrived=true;
            ObserveVisit();
            DialogUIManager.Instance=new DialogUIManager();
            NegociationUIManager.Instance.IsOpen=true;
            store.currentClientInstance.client.isIntroduced=true;
            shop.backInvinvElement.childItems.Clear();
            shop.backInvWindow.visible=true;
            shop.frontInvWindow.visible=true;
            shop.invWindow.visible=true;
            shop.showcaseWindow.visible=true;
            var item=Item(63001,"ore",6);
            var req=new JObject { ["generation"]=generation, ["expires_ms"]=DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds(),
                ["item_id"]=LocalId(item), ["visit"]=visitId, ["command_id"]="stock" };
            JObject Move(string source,string destination)
            {
                req["revision"]=sequence;
                req["source"]=source;
                req["destination"]=destination;
                return ExecuteMove(req);
            }
            int cash=store.playerCash;
            var staged=Move("inventory","back");
            Expect((string)staged["status"]=="moved" && (bool)staged["same_item_at_destination"] &&
                (string)staged["actual_area"]=="back" && store.playerCash==cash,"stage only, same item");
            var secondOwned=Item(63008,"soap",2);
            req["item_id"]=LocalId(secondOwned);
            Expect((string)Move("inventory","back")["status"]=="moved" && shop.backInvinvElement.childItems.Count==2,
                "stage multiple owned items onto trade table");
            Expect((string)Move("back","inventory")["status"]=="moved" && secondOwned.parentInventory==shop.invElement,
                "return second staged owned item");
            req["item_id"]=LocalId(item);
            Expect((string)Move("back","front")["status"]=="invalid_route","owned stays on owner side");
            Expect((string)Move("back","showcase")["status"]=="moved","return to display");
            item.To(shop.frontInvinvElement); item.owned=false;
            shop.backInvWindow.visible=false;
            Expect((string)Move("front","back")["status"]=="transfer_blocked","hidden destination");
            shop.backInvWindow.visible=true;
            var other=Item(63002,"soap",2);
            other.To(shop.backInvinvElement);
            Expect((string)Move("front","back")["status"]=="table_occupied","occupied table");
            other.To(shop.invElement);
            req["visit"]="previous";
            Expect((string)Move("front","back")["status"]=="customer_replaced","wrong customer reports customer_replaced");
            var savedBeforeNull = store.currentClientInstance;
            store.currentClientInstance = null;
            ObserveVisit();
            Expect((string)Move("front","back")["status"]=="customer_left","missing customer reports customer_left");
            store.currentClientInstance = savedBeforeNull;
            ObserveVisit();
            req["visit"]=visitId;
            Expect((string)Move("front","back")["status"]=="moved" && (string)Move("back","inventory")["status"]=="invalid_route" &&
                (string)Move("back","front")["status"]=="moved" && !item.owned && store.playerCash==cash,"guest goods returned");

            var stray=Item(63007,"newspaper",1);
            stray.To(shop.backInvinvElement);
            var idleReq=new JObject { ["generation"]=generation, ["expires_ms"]=DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds(),
                ["item_id"]=LocalId(stray), ["command_id"]="idle_clear", ["source"]="back", ["destination"]="inventory" };
            idleReq["revision"]=sequence;
            Expect((string)ExecuteMove(idleReq)["status"]=="visit_changed","idle clear blocked while customer present");
            var paper=Item(63401,"newspaper",1);
            var card=Item(63402,"card_rev",1);
            var guestPaper=Item(63403,"newspaper",1);
            paper.To(shop.frontInvinvElement);
            card.To(shop.frontInvinvElement);
            guestPaper.To(shop.frontInvinvElement);
            guestPaper.owned=false;
            QueueCounterPapers();
            shop.invElement.replayFull = true;
            FileCounterPapers();
            Expect((string)paperCleanup[0]["status"] == "inventory_full" && paper.parentInventory == shop.frontInvinvElement, "full inventory leaves paper intact");
            shop.invElement.replayFull = false;
            papersDirty = true;
            FileCounterPapers();
            Expect(paper.parentInventory==shop.invElement && card.parentInventory==shop.invElement && guestPaper.parentInventory==shop.frontInvinvElement
                && stray.parentInventory==shop.backInvinvElement, "file only owned deliveries, preserve manually staged and guest items");
            card.To(shop.frontInvinvElement);
            QueueFrontPaper(card,shop.frontInvinvElement,null);
            card.To(shop.backInvinvElement);
            FileCounterPapers();
            Expect(card.parentInventory==shop.invElement && paperCleanup.Count==1, "track a delivered card even when swept to back");
            var collect = (JObject)idleReq.DeepClone();
            collect["source"]="front";
            collect["visit"]=visitId;
            collect["revision"]=sequence;
            collect["item_id"]=LocalId(guestPaper);
            Expect((string)ExecuteMove(collect)["status"]=="invalid_route", "manual front collection rejects guest goods");
            card.To(shop.frontInvinvElement);
            collect["item_id"]=LocalId(card);
            Expect((string)ExecuteMove(collect)["status"]=="moved" && card.parentInventory==shop.invElement, "manual collection for the same guest");
            // P0-1: slip, postit, and backpacks auto-clean; move_items front/back -> inventory works when stage is not_selling/information/ready.
            var infoSlip = Item(63404, "slip", 1);
            var pack = Item(63405, "fanny_pack", 50);
            infoSlip.To(shop.frontInvinvElement);
            pack.To(shop.frontInvinvElement);
            QueueFrontPaper(infoSlip, infoSlip.parentInventory, null);
            QueueFrontPaper(pack, pack.parentInventory, null);
            FileCounterPapers();
            Expect(infoSlip.parentInventory == shop.invElement && pack.parentInventory == shop.invElement,
                "auto-cleanup handles info slips and returned pouches/backpacks");
            var buyerOnly = new StoreClient { clientIntent = StoreClient.ClientIntent.INFORMATION_DEALER, isIntroduced = true };
            var prevClient = store.currentClientInstance;
            store.currentClientInstance = new StoreClientInstance { client = buyerOnly };
            ObserveVisit();
            DialogUIManager.Instance = new DialogUIManager();
            pack.To(shop.backInvinvElement);
            collect["source"] = "back";
            collect["visit"] = visitId;
            collect["revision"] = sequence;
            collect["item_id"] = LocalId(pack);
            Expect((string)ExecuteMove(collect)["status"] == "moved" && pack.parentInventory == shop.invElement,
                "move_items collects owned back items during non-sell customer stages when no order is active");
            store.currentClientInstance = prevClient;
            ObserveVisit();
            var waste = Item(63406, "nuclear_waste", 0);
            waste.To(shop.backInvinvElement);
            var blockedToBack = (JObject)idleReq.DeepClone();
            blockedToBack["source"] = "inventory";
            blockedToBack["destination"] = "back";
            blockedToBack["visit"] = visitId;
            blockedToBack["revision"] = sequence;
            blockedToBack["item_id"] = LocalId(card);
            var blockedReply = ExecuteMove(blockedToBack);
            Expect((string)blockedReply["status"] == "table_occupied" &&
                (string)blockedReply["reason"] == "nuclear_waste_on_counter" &&
                !string.IsNullOrEmpty((string)blockedReply["guidance"]),
                "table_occupied reports nuclear_waste_on_counter and guidance");
            waste.To(null);
            guestPaper.To(null);
            phase="MORNING";
            var shelve = (JObject)idleReq.DeepClone();
            shelve["source"]="inventory";
            shelve["destination"]="showcase";
            shelve["item_id"]=LocalId(card);
            shelve["revision"]=sequence;
            shop.showcaseElement.replayFull = true;
            var showcaseFull = ExecuteMove(shelve);
            Expect((string)showcaseFull["status"] == "showcase_full" && card.parentInventory == shop.invElement &&
                (int)showcaseFull["item_size"]["w"] == 1 && (int)showcaseFull["target_capacity"]["w"] == 7,
                "showcase capacity refusal includes item_size and target_capacity");
            shop.showcaseElement.replayFull = false;
            phase="OPEN";
            shelve["visit"]=visitId;
            Expect((string)ExecuteMove(shelve)["status"]=="moved" && card.parentInventory==shop.showcaseElement,
                "open shop allows owned inventory onto a visible showcase");
            shelve.Remove("visit");
            phase="MORNING";
            var returnToInv = (JObject)shelve.DeepClone();
            returnToInv["source"] = "showcase"; returnToInv["destination"] = "inventory"; returnToInv["revision"] = sequence;
            shop.invElement.replayFull = true;
            var invNoSpace = ExecuteMove(returnToInv);
            Expect((string)invNoSpace["status"] == "no_space" && (string)invNoSpace["reason"] == "inventory_full" &&
                (int)invNoSpace["item_size"]["w"] == 1 &&
                (int)invNoSpace["target_capacity"]["w"] == 24 &&
                (int)invNoSpace["target_capacity"]["h"] == 10 &&
                invNoSpace["target_capacity"]["largest_free"] != null,
                "full inventory returns no_space with item_size and target_capacity");
            shop.invElement.replayFull = false;
            card.To(shop.invElement);
            shelve["revision"]=sequence-1;
            Expect((string)ExecuteMove(shelve)["status"]=="stale_observation" && card.parentInventory==shop.invElement,"stale shelf request cannot move");
            card.To(shop.showcaseElement);
            Expect((string)ExecuteMove(shelve)["status"]=="already_in_destination","stale shelf request can confirm a location");
            card.To(shop.invElement);
            var frontCollect = new JObject { ["generation"]=generation, ["expires_ms"]=DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds(),
                ["item_id"]=LocalId(card), ["command_id"]="auto_filed_card", ["source"]="front", ["destination"]="inventory", ["revision"]=sequence-1 };
            Expect((string)ExecuteMove(frontCollect)["status"]=="already_in_destination",
                "auto-filed front item already in inventory confirms already_in_destination");
            phase="OPEN";

            var savedClient=store.currentClientInstance;
            store.currentClientInstance=null;
            ObserveVisit();
            stray.owned=false;
            idleReq["revision"]=sequence;
            Expect((string)ExecuteMove(idleReq)["status"]=="invalid_route","idle clear rejects unowned item");
            stray.owned=true;
            idleReq["revision"]=sequence;
            other.To(shop.backInvinvElement);
            var cleared=ExecuteMove(idleReq);
            Expect((string)cleared["status"]=="moved" && (string)cleared["actual_area"]=="inventory" &&
                stray.parentInventory==shop.invElement && other.parentInventory==shop.backInvinvElement,"first owned item cleared");
            idleReq["item_id"]=LocalId(other);
            idleReq["revision"]=sequence;
            store.currentClientInstance=savedClient;
            Expect((string)ExecuteMove(idleReq)["status"]=="visit_changed" && other.parentInventory==shop.backInvinvElement,
                "customer arriving between clear commands blocks second move");
            store.currentClientInstance=null;
            ObserveVisit();
            idleReq["revision"]=sequence;
            Expect((string)ExecuteMove(idleReq)["actual_area"]=="inventory" && other.parentInventory==shop.invElement,
                "remaining item can be cleared when idle again");
            idleReq["item_id"]=LocalId(stray);
            phase="CLOSED";
            store.storeState=PlayerStore.StoreState.CLOSED;
            stray.To(shop.backInvinvElement);
            idleReq["destination"]="showcase";
            idleReq["revision"]=sequence;
            var clearedClosed=ExecuteMove(idleReq);
            Expect((string)clearedClosed["status"]=="moved" && (string)clearedClosed["actual_area"]=="showcase" &&
                shop.backInvinvElement.childItems.Count==0,"idle clear moves owned back item while CLOSED");
            // Afterhours return while CLOSED triggers QueueCounterPapers -> FileCounterPapers clears returned backpack, never waste.
            var returnedPack = Item(63407, "satchel", 150);
            var leftWaste = Item(63408, "nuclear_waste", 0);
            returnedPack.To(shop.backInvinvElement);
            leftWaste.To(shop.backInvinvElement);
            OnLeaveAfterhoursLate("dumping_ground");
            FileCounterPapers();
            Expect(returnedPack.parentInventory == shop.invElement && leftWaste.parentInventory == shop.backInvinvElement,
                "afterhours return while CLOSED auto-collects backpack and leaves nuclear_waste on counter");
            leftWaste.To(null);
            phase="OPEN";
            store.storeState=PlayerStore.StoreState.OPEN;
            store.currentClientInstance=savedClient;
            ObserveVisit();

            phase="CLOSED";
            var bag=Item(63003,"bag",2);
            var inner=Item(63004,"bottle_case",2);
            var bagSlot=new GameInventory {owner=bag};
            bag.contentWindow=new PixelWindow {visible=true, childElement=bagSlot};
            bag.To(shop.backInvinvElement);
            bag.contentWindow.Hide();
            var part=Item(650,"gun_part",1);
            part.To(bagSlot);
            var inspectBag=new JObject { ["action"]="inspect_window", ["owner_id"]=LocalId(bag), ["open_if_closed"]=true };
            var bagReply=new JObject();
            Expect(WorkstationAction(inspectBag,bagReply)=="observed" && (string)bagReply["window"]["location"]["area"]=="back" &&
                (int)bagReply["window"]["controls"][0]["items"][0]["save_id"]==650 && part.parentInventory==bagSlot,
                "inspect owned back-counter bag in place, including original loot ID");
            var backTransfer=new JObject { ["action"]="transfer_slot", ["owner_id"]=LocalId(bag), ["item_id"]=LocalId(part),
                ["source"]="slot", ["control"]="content", ["window_token"]=bagReply["window_token"] };
            Expect(WorkstationAction(backTransfer,new JObject())=="owner_unavailable","back-counter inspection does not unlock transfers");
            bag.owned=false;
            Expect(WorkstationAction(inspectBag,new JObject())=="owner_unavailable","guest back-counter container excluded");
            bag.owned=true;
            phase="OPEN";
            Expect(WorkstationAction(inspectBag,new JObject())=="owner_unavailable","trading counter excluded during business");
            phase="CLOSED";
            bag.To(shop.invElement);
            inner.To(bagSlot);
            var tray=new GameInventory {owner=inner};
            inner.contentWindow=new PixelWindow {visible=true,childElement=tray};
            var read=new JObject { ["action"]="inspect_window",["owner_id"]=LocalId(inner) };
            var reply=new JObject();
            Expect(WorkstationAction(read,reply)=="observed" &&
                (int)reply["window"]["location"]["containers"][0]["owner_id"]==LocalId(bag),"nested visible owner path");
            var put=new JObject { ["action"]="transfer_slot",["owner_id"]=LocalId(inner),["item_id"]=LocalId(other),
                ["source"]="inventory",["control"]="content",["window_token"]=reply["window_token"] };
            bag.contentWindow.visible=false;
            Expect(WorkstationAction(put,new JObject())=="owner_unavailable","closed outer bag");
            bag.contentWindow.visible=true;
            bag.To(shop.showcaseElement);
            Expect(WorkstationAction(put,new JObject())=="window_changed","ancestor relocated");
            WorkstationAction(read,reply);
            put["window_token"]=reply["window_token"];
            Expect(WorkstationAction(put,new JObject())=="moved" && other.parentInventory==tray,"use native nested slot");

            var furnace=Item(63005,"furnace",0);
            var layout=new MachineryStack();
            var power=MachineHelper.battery;
            MachineHelper.furnaceSlots=new object[] {power,new GameInventory(),new GameInventory(),new GameInventory(),new GameInventory()};
            foreach (var slot in MachineHelper.furnaceSlots) layout.elements.Add((PixelElement)slot);
            furnace.contentWindow=new PixelWindow {visible=true,childElement=layout};
            var controls=(JArray)ReadWindow(furnace).View["controls"];
            Expect((int)controls[0]["cycle_power_usage"]==4 && controls[0]["can_power"]!=null &&
                controls.Select(row=>(string)row["role"]).SequenceEqual(FurnaceSlots) &&
                !(bool)controls[3]["insert_allowed"],"visible furnace slots and native power usage");
            MachineHelper.furnaceSlots=null;
            var wine=Item(63006,"wine",0);
            wine.waterTags.Add("WINE_CONTAINER_TAG");
            var start=new MachineryButton {text="mech_wine_start_fermenting"};
            wine.contentWindow=new PixelWindow {visible=true,childElement=start};
            read["owner_id"]=LocalId(wine);
            WorkstationAction(read,reply);
            Expect((string)reply["window"]["controls"][0]["action"]=="start_fermentation","localized ferment control");
            var press=new JObject { ["action"]="press_control",["owner_id"]=LocalId(wine),["window_token"]=reply["window_token"],
                ["label"]=start.text,["control"]="content" };
            Expect(WorkstationAction(press,new JObject())=="pressed" && start.presses==1,"normal button path starts wine");

            var notice=GenericConfirmationUIManager.Instance;
            notice.panel.activeInHierarchy=true;
            notice.bodyTMP.text="mech_portable_purifier_already_clean";
            var panel=new JObject {["mode"]="read"};
            Expect(WaterPanelAction(panel,reply)=="purifier_notice","native notice read");
            panel["mode"]="notice_close"; panel["panel_token"]=reply["panel_token"];
            int clicks=notice.clicks;
            notice.cancelButton.activeInHierarchy=true;
            Expect(WaterPanelAction(panel,reply)=="panel_changed" && notice.clicks==clicks,"never confirm a two-button decision");
            notice.cancelButton.activeInHierarchy=false;
            notice.bodyTMP.text="Another prompt";
            Expect(WaterPanelAction(panel,reply)=="panel_changed" && notice.clicks==clicks,"ignore unrelated notice");
            notice.bodyTMP.text="mech_portable_purifier_not_enough_charge";
            panel["mode"]="read";
            WaterPanelAction(panel,reply);
            panel["mode"]="notice_close"; panel["panel_token"]=reply["panel_token"];
            Expect(WaterPanelAction(panel,reply)=="notice_closed" && notice.clicks==clicks+1 &&
                WaterPanelAction(panel,reply)=="panel_changed","one notice acknowledgement");

            // O5: visitor_gift contraband arrival vs trade purchase
            phase = "OPEN";
            store.storeState = PlayerStore.StoreState.OPEN;
            store.isClientArrived = true;
            store.currentClientInstance = new StoreClientInstance();
            store.currentClientInstance.client.identifier = "cartelHandlerWeekly";
            store.currentClientInstance.client.displayName = "卡特爾接頭人";
            var gift1 = new GameItem { identifier = "dreamshroom", owned = true, unitCount = 3 };
            gift1.waterTags.Add(TypeHelper.CONTRABAND);
            gift1.To(shop.invElement);
            ObservePhoneArrival(gift1, shop.invElement, null);
            var giftUpsert = new JArray(new JObject { ["id"] = LocalId(gift1), ["owned"] = true });
            var giftRec = new JObject();
            AttachPhoneArrivals(giftRec, giftUpsert);
            Expect(giftRec["arrivals"] != null &&
                (string)giftRec["arrivals"][0]["source"] == "visitor_gift" &&
                (string)giftRec["arrivals"][0]["contact"] == "卡特爾接頭人" &&
                (int)giftRec["contraband_outside_bay"] == 3,
                "visitor_gift contraband arrival attaches arrivals and contraband_outside_bay");
            var tradeItem = new GameItem { identifier = "dreamshroom", owned = true, unitCount = 1 };
            received.Add(tradeItem);
            ObservePhoneArrival(tradeItem, shop.invElement, null);
            var tradeRec = new JObject();
            AttachPhoneArrivals(tradeRec, new JArray(new JObject { ["id"] = LocalId(tradeItem), ["owned"] = true }));
            Expect(tradeRec["arrivals"] == null, "trade-received item is not tagged as visitor_gift");
            received.Remove(tradeItem);

            // O6: hidden grid move & inspection_exposure
            phase = "CLOSED";
            store.storeState = PlayerStore.StoreState.CLOSED;
            if (!areas.ContainsKey(shop.hiddenElement)) areas[shop.hiddenElement] = "hidden";
            var hiddenItem = Item(63501, "dreamshroom", 5);
            hiddenItem.unitCount = 2;
            hiddenItem.waterTags.Add(TypeHelper.CONTRABAND);
            hiddenItem.To(shop.invElement);
            StorePoster.Instance.isPulled = false;
            var hiddenReq = new JObject {
                ["generation"] = generation,
                ["expires_ms"] = DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds(),
                ["item_id"] = LocalId(hiddenItem),
                ["command_id"] = "hidden_move",
                ["source"] = "inventory",
                ["destination"] = "hidden",
                ["revision"] = sequence,
            };
            var downRes = ExecuteMove(hiddenReq);
            Expect((string)downRes["status"] == "transfer_blocked" && (string)downRes["reason"] == "poster_down" &&
                (string)downRes["next_tool"] == "toggle_poster",
                "hidden move blocked when poster is down");
            var posterUpReq = new JObject { ["action"] = "toggle_poster", ["mode"] = "up",
                ["generation"] = generation, ["revision"] = sequence, ["expires_ms"] = long.MaxValue };
            var posterUpRes = new JObject();
            Expect(ActAtCounter(posterUpReq, posterUpRes) == "poster_up" && (bool)posterUpRes["poster"]["pulled"],
                "toggle_poster pulls poster up");
            hiddenReq["revision"] = sequence;
            var upRes = ExecuteMove(hiddenReq);
            Expect((string)upRes["status"] == "moved" && (string)upRes["actual_area"] == "hidden",
                "hidden move succeeds when poster is pulled");
            Expect((int)ContrabandCounts(true, true)["hidden_grid_exposed"] == 2,
                "inspection_exposure counts hidden_grid_exposed when poster is pulled");
            var posterDownReq = new JObject { ["action"] = "toggle_poster", ["mode"] = "down",
                ["generation"] = generation, ["revision"] = sequence, ["expires_ms"] = long.MaxValue };
            var posterDownRes = new JObject();
            Expect(ActAtCounter(posterDownReq, posterDownRes) == "poster_down" && !(bool)posterDownRes["poster"]["pulled"] &&
                (int)posterDownRes["hidden_grid"] == 2,
                "toggle_poster pulls poster down and reports hidden_grid count");
            Expect((int)ContrabandCounts(true, true)["hidden_grid_exposed"] == 0,
                "inspection_exposure excludes hidden_grid_exposed when poster is down");
            Expect((bool)SecurityView()["poster"]["pulled"] == false, "security.poster.pulled reflects poster state");
            phase = "OPEN";
            store.storeState = PlayerStore.StoreState.OPEN;
            store.currentClientInstance = new StoreClientInstance();
            posterUpReq["revision"] = sequence;
            var openPosterRes = new JObject();
            Expect(ActAtCounter(posterUpReq, openPosterRes) == "poster_unavailable" &&
                (string)openPosterRes["reason"] == "customer_present",
                "toggle_poster refuses when open with customer present");
            store.currentClientInstance = null;

            // P6: sell_order / trade_batch from smuggler bay returns to original bay on decline, falls back to inventory when full, and move_items supports bay:<owner_id> -> back.
            shop.invElement.childItems.Clear();
            shop.showcaseElement.childItems.Clear();
            shop.backInvinvElement.childItems.Clear();
            var bay1 = Item(32, "smuggler_bay", 1);
            var bay1Grid = new GameGridInventory(4, 4) { owner = bay1 };
            bay1.contentWindow = new PixelWindow { childElement = bay1Grid };
            bay1.To(shop.invElement);
            var dustItems = new List<GameItem>();
            for (int i = 0; i < 8; i++)
            {
                var d = Item(83 + i, "dream_dust", 8);
                d.waterTags.Add(TypeHelper.CONTRABAND);
                d.To(bay1Grid);
                dustItems.Add(d);
            }
            var bay2 = Item(33, "smuggler_bay_mini", 1);
            var bay2Grid = new GameGridInventory(4, 4) { owner = bay2 };
            bay2.contentWindow = new PixelWindow { childElement = bay2Grid };
            bay2.To(shop.invElement);
            var bay2Items = new List<GameItem>();
            for (int i = 0; i < 7; i++)
            {
                var d = Item(100 + i, "dream_dust", 8);
                d.waterTags.Add(TypeHelper.CONTRABAND);
                d.To(bay2Grid);
                bay2Items.Add(d);
            }
            FlushChanges();
            int bay1Id = LocalId(bay1);
            int bay2Id = LocalId(bay2);

            phase = "OPEN";
            store.storeState = PlayerStore.StoreState.OPEN;
            store.currentClientInstance = new StoreClientInstance();
            ObserveVisit();
            DialogUIManager.Instance = new DialogUIManager();
            NegociationUIManager.Instance.IsOpen = true;
            merchant.budget = 500;

            JObject RunP6Sale(JArray lines)
            {
                string cmd = Guid.NewGuid().ToString("N");
                Write("sale-" + generation + ".json", new JObject {
                    ["command_id"] = cmd, ["generation"] = generation, ["revision"] = sequence,
                    ["visit"] = visitId, ["lines"] = lines,
                    ["expires_ms"] = DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds()
                });
                for (int n = 0; n < 32; n++) { UnityEngine.Time.realtimeSinceStartup++; CheckOrder(); CloseTransactions(); }
                return JObject.Parse(ModHelper.ReadModDataFileText(ModId, "receipt-" + cmd + ".json"));
            }

            var declineLines = new JArray();
            foreach (var d in dustItems)
                declineLines.Add(new JObject { ["item_id"] = LocalId(d), ["min_price"] = 12 });
            var declineReply = RunP6Sale(declineLines);
            Expect((string)declineReply["status"] == "finished", "P6 8-bottle bay decline finishes");
            var declinedRows = (JArray)declineReply["items"];
            Expect(declinedRows.Count == 8, "P6 8-bottle bay decline has 8 lines");
            foreach (JObject r in declinedRows)
            {
                Expect((string)r["status"] == "returned_below_minimum", "P6 bay item returned_below_minimum");
                Expect((int)r["from_container"]["owner_id"] == bay1Id && (string)r["from_container"]["kind"] == "smuggler_bay",
                    "P6 bay item records from_container");
                Expect((string)r["actual_area"] == "bay:" + bay1Id, "P6 bay item returns to original bay");
            }
            Expect(dustItems.All(d => d.parentInventory == bay1Grid), "P6 all 8 bottles back in bay1");
            Expect((int)ContrabandCounts()["inventory"] == 0 && (int)ContrabandCounts()["smuggler_bay"] == 15,
                "P6 contraband inventory count stays 0 after bay return");

            // P6 fallback when bay is full
            bay1Grid.replayFull = true;
            var fbReply = RunP6Sale(new JArray(new JObject { ["item_id"] = LocalId(dustItems[0]), ["min_price"] = 12 }));
            bay1Grid.replayFull = false;
            Expect((string)fbReply["status"] == "finished", "P6 full bay fallback finishes");
            var fbLine = (JObject)fbReply["items"][0];
            Expect((string)fbLine["returned_to"] == "inventory" && (bool)fbLine["contraband_outside_bay"] &&
                (string)fbLine["actual_area"] == "inventory" && dustItems[0].parentInventory == shop.invElement,
                "P6 full bay fallback returns to inventory with contraband_outside_bay");
            // Move bottle back to bay1 via move_item("inventory" -> "bay:<id>")
            var backToBayRes = ExecuteMove(new JObject
            {
                ["command_id"] = "f1000000000000000000000000000001",
                ["item_id"] = LocalId(dustItems[0]),
                ["source"] = "inventory",
                ["destination"] = "bay:" + bay1Id,
                ["generation"] = generation,
                ["revision"] = sequence,
                ["visit"] = visitId,
                ["expires_ms"] = long.MaxValue
            });
            Expect((string)backToBayRes["status"] == "moved" && dustItems[0].parentInventory == bay1Grid,
                "P6 move_item moves from inventory into bay: " + backToBayRes);

            // P6 Cartel delivery: 15 bottles from 2 bays moved directly to back
            int movedToBack = 0;
            foreach (var d in dustItems)
            {
                var mv = ExecuteMove(new JObject
                {
                    ["command_id"] = "f20000000000000000000000000000" + movedToBack.ToString("D2"),
                    ["item_id"] = LocalId(d),
                    ["source"] = "bay:" + bay1Id,
                    ["destination"] = "back",
                    ["generation"] = generation,
                    ["revision"] = sequence,
                    ["visit"] = visitId,
                    ["expires_ms"] = long.MaxValue
                });
                Expect((string)mv["status"] == "moved" && (string)mv["actual_area"] == "back" &&
                    (int)mv["from_container"]["owner_id"] == bay1Id, "P6 bay1 bottle moved to back");
                movedToBack++;
            }
            foreach (var d in bay2Items)
            {
                var mv = ExecuteMove(new JObject
                {
                    ["command_id"] = "f20000000000000000000000000000" + movedToBack.ToString("D2"),
                    ["item_id"] = LocalId(d),
                    ["source"] = "bay:" + bay2Id,
                    ["destination"] = "back",
                    ["generation"] = generation,
                    ["revision"] = sequence,
                    ["visit"] = visitId,
                    ["expires_ms"] = long.MaxValue
                });
                Expect((string)mv["status"] == "moved" && (string)mv["actual_area"] == "back" &&
                    (int)mv["from_container"]["owner_id"] == bay2Id, "P6 bay2 bottle moved to back");
                movedToBack++;
            }
            Expect(movedToBack == 15 && shop.backInvinvElement.childItems.Count == 15, "P6 all 15 bottles from 2 bays staged on back");
            store.currentClientInstance = null;

            // S1: Stolen item marking & security/inspection exposure
            shop.invElement.childItems.Clear();
            shop.showcaseElement.childItems.Clear();
            shop.hiddenElement.childItems.Clear();
            shop.backInvinvElement.childItems.Clear();
            var stolenCig = Item(2001, "hemingway_cigarettes", 100);
            stolenCig.waterTags.Add(StolenHelper.STOLEN_TAG);
            stolenCig.scratchTagReader = key => key == "STOLEN_VALUE_INT" ? new GameItem { associatedId = 45 } : stolenCig;
            var legalSoap = Item(2002, "soap", 5);
            var stolenRow = StockRow(stolenCig, "inventory");
            var legalRow = StockRow(legalSoap, "inventory");
            Expect((bool?)stolenRow["stolen"] == true && (int?)stolenRow["stolen_heat"] == 45,
                "S1 stolen item row includes stolen and stolen_heat");
            Expect(legalRow["stolen"] == null && legalRow["stolen_heat"] == null,
                "S1 normal legal item omits stolen fields");
            var secBeforeBay = ContrabandCounts(true, true);
            Expect((int)secBeforeBay["stolen"]["inventory"] == 1 && (int)secBeforeBay["stolen_outside_bay"] == 1,
                "S1 stolen item in inventory counts toward stolen_outside_bay");
            var s1Bay = Item(2003, "smuggler_bay", 1);
            var s1BayGrid = new GameGridInventory(4, 4) { owner = s1Bay };
            s1Bay.contentWindow = new PixelWindow { childElement = s1BayGrid };
            s1Bay.To(shop.invElement);
            stolenCig.To(s1BayGrid);
            var secInBay = ContrabandCounts(true, true);
            Expect((int)secInBay["stolen"]["smuggler_bay"] == 1 && (int)secInBay["stolen_outside_bay"] == 0,
                "S1 stolen item moved into smuggler_bay clears stolen_outside_bay");

            // S2 & S4: trash destination + MORNING hidden & bay <-> hidden
            phase = "MORNING";
            store.storeState = PlayerStore.StoreState.MORNING;
            shop.hiddenElement = new GameGridInventory(2, 2);
            areas[shop.hiddenElement] = "hidden";
            StorePoster.Instance.isPulled = true;
            FlushChanges();
            var junkDust = Item(2004, "dream_dust", 1);
            junkDust.To(shop.invElement);
            var importantItem = Item(2005, "key_card", 1);
            importantItem.waterTags.Add(GeneralHelper.IMPORTANT_TAG);
            var wasteItem = Item(2006, "nuclear_waste", 1);
            wasteItem.waterTags.Add(GeneralHelper.HAZARDOUS_WASTE_TAG);
            FlushChanges();

            var impTrash = ExecuteMove(new JObject
            {
                ["command_id"] = "a2000000000000000000000000000001",
                ["item_id"] = LocalId(importantItem), ["source"] = "inventory", ["destination"] = "trash",
                ["generation"] = generation, ["revision"] = sequence, ["expires_ms"] = long.MaxValue
            });
            Expect((string)impTrash["status"] == "transfer_blocked" && (string)impTrash["reason"] == "important_item",
                "S2 trash blocks IMPORTANT_TAG");
            var wasteTrash = ExecuteMove(new JObject
            {
                ["command_id"] = "a2000000000000000000000000000002",
                ["item_id"] = LocalId(wasteItem), ["source"] = "inventory", ["destination"] = "trash",
                ["generation"] = generation, ["revision"] = sequence, ["expires_ms"] = long.MaxValue
            });
            Expect((string)wasteTrash["status"] == "transfer_blocked" && (string)wasteTrash["reason"] == "hazardous_waste_cannot_trash",
                "S2 trash blocks nuclear waste");
            shop.trashcanInvElement.replayFull = true;
            var fullTrash = ExecuteMove(new JObject
            {
                ["command_id"] = "a2000000000000000000000000000003",
                ["item_id"] = LocalId(junkDust), ["source"] = "inventory", ["destination"] = "trash",
                ["generation"] = generation, ["revision"] = sequence, ["expires_ms"] = long.MaxValue
            });
            Expect((string)fullTrash["status"] == "no_space" && (string)fullTrash["reason"] == "trash_full",
                "S2 trash full returns no_space with reason=trash_full");
            shop.trashcanInvElement.replayFull = false;
            var toTrash = ExecuteMove(new JObject
            {
                ["command_id"] = "a2000000000000000000000000000004",
                ["item_id"] = LocalId(junkDust), ["source"] = "inventory", ["destination"] = "trash",
                ["generation"] = generation, ["revision"] = sequence, ["expires_ms"] = long.MaxValue
            });
            Expect((string)toTrash["status"] == "moved" && (string)toTrash["actual_area"] == "trash" &&
                junkDust.parentInventory == shop.trashcanInvElement, "S2 move to trash succeeds");
            var fromTrash = ExecuteMove(new JObject
            {
                ["command_id"] = "a2000000000000000000000000000005",
                ["item_id"] = LocalId(junkDust), ["source"] = "trash", ["destination"] = "inventory",
                ["generation"] = generation, ["revision"] = sequence, ["expires_ms"] = long.MaxValue
            });
            Expect((string)fromTrash["status"] == "moved" && (string)fromTrash["actual_area"] == "inventory" &&
                junkDust.parentInventory == shop.invElement, "S2 recover from trash to inventory succeeds");
            // X1: two consecutive trash moves both report moved + actual_area=trash
            var junkDust1 = Item(1902, "dust", 10);
            var junkDust2 = Item(1903, "dust2", 10);
            junkDust1.To(shop.invElement);
            junkDust2.To(shop.invElement);
            FlushChanges();
            var toTrash1 = ExecuteMove(new JObject
            {
                ["command_id"] = "a2000000000000000000000000000006",
                ["item_id"] = LocalId(junkDust1), ["source"] = "inventory", ["destination"] = "trash",
                ["generation"] = generation, ["revision"] = sequence, ["expires_ms"] = long.MaxValue
            });
            Expect((string)toTrash1["status"] == "moved" && (string)toTrash1["actual_area"] == "trash", "X1 first trash move succeeds");
            var toTrash2 = ExecuteMove(new JObject
            {
                ["command_id"] = "a2000000000000000000000000000007",
                ["item_id"] = LocalId(junkDust2), ["source"] = "inventory", ["destination"] = "trash",
                ["generation"] = generation, ["revision"] = sequence, ["expires_ms"] = long.MaxValue
            });
            Expect((string)toTrash2["status"] == "moved" && (string)toTrash2["actual_area"] == "trash", "X1 second trash move succeeds");
            junkDust1.To(null);
            junkDust2.To(null);

            // X2: bay:<id> supports storage_bay_chem, returns rejected_by_container on filter rejection, and keeps contraband outside smuggler_bay
            var chemBay = Item(1910, "storage_bay_chem", 1);
            chemBay.waterTags.Add("CONTAINER_TAG");
            var chemBayGrid = new GameGridInventory(8, 8);
            chemBayGrid.mayInventoryAddItemFunc = (it, inv) => it.identifier != "tough_cotton_fabric";
            chemBay.contentWindow = new Window { childElement = chemBayGrid };
            chemBay.To(shop.invElement);
            var chemReagent = Item(1911, "acid_reagent", 1);
            var clothItem = Item(1912, "tough_cotton_fabric", 1);
            var chemContraband = Item(1913, "banned_chem", 1);
            chemContraband.waterTags.Add("CONTRABAND");
            chemReagent.To(shop.invElement);
            clothItem.To(shop.invElement);
            chemContraband.To(chemBayGrid);
            FlushChanges();
            var toChemBay = ExecuteMove(new JObject
            {
                ["command_id"] = "a2000000000000000000000000000008",
                ["item_id"] = LocalId(chemReagent), ["source"] = "inventory", ["destination"] = "bay:" + LocalId(chemBay),
                ["generation"] = generation, ["revision"] = sequence, ["expires_ms"] = long.MaxValue
            });
            Expect((string)toChemBay["status"] == "moved" && (string)toChemBay["actual_area"] == "bay:" + LocalId(chemBay),
                "X2 bay:<chem> accepts chemical reagent");
            var clothToChem = ExecuteMove(new JObject
            {
                ["command_id"] = "a2000000000000000000000000000009",
                ["item_id"] = LocalId(clothItem), ["source"] = "inventory", ["destination"] = "bay:" + LocalId(chemBay),
                ["generation"] = generation, ["revision"] = sequence, ["expires_ms"] = long.MaxValue
            });
            Expect((string)clothToChem["status"] == "rejected_by_container" && (string)clothToChem["reason"] == "rejected_by_container",
                "X2 disallowed item returns rejected_by_container");
            var secChem = SecurityView();
            Expect((int)secChem["contraband"]["smuggler_bay"] == 0 && (int)secChem["contraband"]["inventory"] >= 1,
                "X2 contraband in storage_bay_chem counts outside smuggler_bay");
            chemReagent.To(null); clothItem.To(null); chemContraband.To(null); chemBay.To(null);

            // S4: MORNING hidden & bay:<id> <-> hidden
            var invToHidden = ExecuteMove(new JObject
            {
                ["command_id"] = "a4000000000000000000000000000001",
                ["item_id"] = LocalId(junkDust), ["source"] = "inventory", ["destination"] = "hidden",
                ["generation"] = generation, ["revision"] = sequence, ["expires_ms"] = long.MaxValue
            });
            Expect((string)invToHidden["status"] == "moved" && (string)invToHidden["actual_area"] == "hidden",
                "S4 MORNING inventory -> hidden succeeds");
            var bayToHidden = ExecuteMove(new JObject
            {
                ["command_id"] = "a4000000000000000000000000000002",
                ["item_id"] = LocalId(stolenCig), ["source"] = "bay:" + LocalId(s1Bay), ["destination"] = "hidden",
                ["generation"] = generation, ["revision"] = sequence, ["expires_ms"] = long.MaxValue
            });
            Expect((string)bayToHidden["status"] == "moved" && (string)bayToHidden["actual_area"] == "hidden" &&
                (int)bayToHidden["from_container"]["owner_id"] == LocalId(s1Bay), "S4 MORNING bay -> hidden succeeds with from_container");
            var hiddenToBay = ExecuteMove(new JObject
            {
                ["command_id"] = "a4000000000000000000000000000003",
                ["item_id"] = LocalId(stolenCig), ["source"] = "hidden", ["destination"] = "bay:" + LocalId(s1Bay),
                ["generation"] = generation, ["revision"] = sequence, ["expires_ms"] = long.MaxValue
            });
            Expect((string)hiddenToBay["status"] == "moved" && stolenCig.parentInventory == s1BayGrid,
                "S4 hidden -> bay succeeds");
            phase = "OPEN";
            store.storeState = PlayerStore.StoreState.OPEN;
            store.currentClientInstance = new StoreClientInstance();
            var openHidden = ExecuteMove(new JObject
            {
                ["command_id"] = "a4000000000000000000000000000004",
                ["item_id"] = LocalId(junkDust), ["source"] = "hidden", ["destination"] = "inventory",
                ["generation"] = generation, ["revision"] = sequence, ["expires_ms"] = long.MaxValue
            });
            Expect((string)openHidden["status"] == "transfer_blocked" && (string)openHidden["reason"] == "customer_present",
                "S4 OPEN with customer blocks hidden move with transfer_blocked/customer_present");
            store.currentClientInstance = null;

            // R1 & R2: read_item_details requires item_details_compat toggle and item_details mod loaded
            InventorySorting.Configure("replay");
            ModHelper.sortConfig.detailsEntry.Value = true;
            var r2Item = Item(2101, "hemingway_cigarettes", 100);
            r2Item.To(shop.invElement);
            FlushChanges();
            ModLoader.Mods.Clear();
            var r2MissingReq = new JObject {
                ["action"] = "read_item_details", ["generation"] = generation, ["revision"] = sequence,
                ["item_ids"] = new JArray(LocalId(r2Item)), ["expires_ms"] = long.MaxValue
            };
            var r2MissingRes = new JObject();
            Expect(ActAtCounter(r2MissingReq, r2MissingRes) == "item_details_mod_not_loaded" &&
                (string)r2MissingRes["reason"] == "item_details_mod_not_loaded",
                "R2 read_item_details blocks when item_details mod is not loaded");
            ModLoader.Mods.Add(new LoadedMod {
                Manifest = new ModManifest { ID = "item_details", Name = "Item Details / 物品详情", ModVersion = "0.9.7" },
                IsEnabled = true, Instance = new object(), LoadError = ""
            });
            ModInternal.ReplayTooltipLate = (b, it) => {
                b.Buffer.Append("（未拥有）一包海明威香烟\n[标签] 正品\n× 赝品（防伪标问题、商标问题）\n真实：赝品 -50%（伪装为 正品）");
            };
            // V1: read_item_details requires in-game item_details_compat toggle enabled
            ModHelper.sortConfig.detailsEntry.Value = false;
            var r2ToggleOff = new JObject();
            Expect(ActAtCounter(r2MissingReq, r2ToggleOff) == "assist_disabled" &&
                (string)r2ToggleOff["reason"] == "item_details_compat_off" && r2ToggleOff["items"] == null,
                "V1 read_item_details returns assist_disabled when in-game toggle is off");
            ModHelper.sortConfig.detailsEntry.Value = true;
            var r2OkRes = new JObject();
            Expect(ActAtCounter(r2MissingReq, r2OkRes) == "observed" &&
                (string)r2OkRes["source"] == "item_details_mod" &&
                (string)r2OkRes["mod_version"] == "0.9.7" &&
                ((string)r2OkRes["items"][0]["details_text"]).Contains("真实：赝品 -50%（伪装为 正品）"),
                "R2 read_item_details returns displayed tooltip text and mod version");
            // V6: a GameItemElement is read through the public GetTooltip(), which includes the basic tooltip lines
            var v6Element = new GameItemElement { uniqueId = 2102, identifier = "hemingway_cigarettes", price = 100 };
            v6Element.ReplayTooltipBasic = () => {
                var basic = new RichTextBuilder(int.MaxValue);
                basic.Buffer.Append("售价：100\n");
                return basic;
            };
            v6Element.To(shop.invElement);
            LocalId(v6Element);
            FlushChanges();
            var v6Res = new JObject();
            Expect(ActAtCounter(new JObject {
                ["action"] = "read_item_details", ["generation"] = generation, ["revision"] = sequence,
                ["item_ids"] = new JArray(LocalId(v6Element)), ["expires_ms"] = long.MaxValue
            }, v6Res) == "observed" &&
                ((string)v6Res["items"][0]["details_text"]).StartsWith("售价：100") &&
                ((string)v6Res["items"][0]["details_text"]).Contains("真实：赝品 -50%（伪装为 正品）"),
                "V6 GameItemElement tooltip comes from GetTooltip() with basic and mod lines");
            ModInternal.ReplayTooltipLate = null;
            ModLoader.Mods.Clear();
            ModHelper.sortConfig.detailsEntry.Value = false;

            Console.WriteLine("Stock replay passed: counter staging, nested ownership, furnace roles, power, fermentation and notices.");
        }
    }
}
