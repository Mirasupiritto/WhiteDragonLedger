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
