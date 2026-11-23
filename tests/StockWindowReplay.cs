using System;
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
            Expect((string)Move("front","back")["status"]=="visit_changed","wrong customer");
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
            card.To(shop.showcaseElement);
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
            Console.WriteLine("Stock replay passed: counter staging, nested ownership, furnace roles, power, fermentation and notices.");
        }
    }
}
