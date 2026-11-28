using Newtonsoft.Json.Linq;

namespace WhiteDragon.Ledger
{
    public static class ItemPinHelper
    {
        public static bool BlocksClicks => false;
        public static bool MayPin(GameItem item) => true;
        public static bool IsPinned(GameItem item) => item.waterTags.Contains("pinned");
        public static void TogglePinned(GameItem item)
        {
            if (!item.waterTags.Remove("pinned")) item.waterTags.Add("pinned");
        }
    }
    public class ItemMouseDragHandler
    {
        public static ItemMouseDragHandler current;
        public bool IsDraggingItem;
    }
    public class ItemSelectHandler
    {
        public static ItemSelectHandler current = new ItemSelectHandler();
        private GameItem equipped;
        public bool IsEquipped => equipped != null;
        public GameItem GetEquippedItem() => equipped;
        public void TryEquipItem(GameItem item) { equipped=item; }
        public void EndSelect() { equipped=null; }
    }
    public partial class GameItem
    {
        public bool MayConfigureSlotItem() => false;
        public bool CanConfigureSlotItem() => false;
        public bool MayToggleSlotItem() => false;
        public bool CanToggleSlotItem() => false;
        public void ConfigureSlotItem() {}
        public void ToggleSlotItem() {}
        public void TryUnloadAll(bool skipPinned) {}
    }
    public sealed partial class InventoryObserver
    {
        private void ReplayItemMenu()
        {
            var smoke=Item(7501,"cigarette",1);
            smoke.waterTags.Add("SUBSTANCE");
            var kit=Item(7502,"water_test_container",1);
            kit.contentWindow=new PixelWindow {childElement=new GameGridInventory(3,3)};
            var strip=Item(7503,"water_test_strip",1);
            strip.waterTags.Add("WATER_TESTING_STRIP_TAG");
            strip.To((GameInventory)kit.contentWindow.childElement);
            JObject Request(GameItem item,string mode="read",string token=null,string verb=null) =>
                new JObject { ["action"]="item_menu", ["revision"]=sequence, ["generation"]=generation, ["expires_ms"]=long.MaxValue,
                    ["item_id"]=LocalId(item), ["mode"]=mode, ["menu_token"]=token, ["verb"]=verb };
            JObject receipt=new JObject();
            Expect(ActAtCounter(Request(smoke),receipt)=="observed" && receipt["base_actions"]!=null &&
                (string)receipt["labels"]["activate"]=="handler_btn_consume" && smoke.responseClicks==0,"read native consume without consuming");
            string token=(string)receipt["menu_token"];
            var needsTarget=new JObject();
            Expect(ActAtCounter(Request(smoke,"choose",token,"use"),needsTarget)=="target_required" &&
                (string)needsTarget["menu_token"]==token,"asking for targets preserves the same menu");
            var check=Request(smoke,"targets",token);
            check["target_ids"]=new JArray(LocalId(kit),LocalId(smoke),999999);
            var candidates=new JObject();
            Expect(ActAtCounter(check,candidates)=="observed" && (string)candidates["targets"][0]["status"]=="available" &&
                (string)candidates["targets"][1]["status"]=="target_unavailable" && (string)candidates["targets"][2]["status"]=="target_unavailable" &&
                smoke.waterClicks==0,"candidate preview uses native predicate without applying");
            Expect(ActAtCounter(Request(smoke,"read",token),new JObject())=="unchanged","same menu read");
            receipt=new JObject();
            Expect(ActAtCounter(Request(kit,"read",token),receipt)=="observed" && receipt["base_actions"]==null &&
                receipt["added"].ToString().Contains("open") && receipt["label_changes"]["activate"].Type==JTokenType.Null,"cross-item delta resets consume label");
            string kitToken=(string)receipt["menu_token"];
            receipt=new JObject();
            Expect(ActAtCounter(Request(kit,"choose",kitToken,"open"),receipt)=="observed" &&
                (int)receipt["window"]["controls"][0]["items"][0]["id"]==LocalId(strip),"open kit returns selectable strip");
            var staleReply=new JObject();
            Expect(ActAtCounter(Request(smoke,"choose",token,"activate"),staleReply)=="stale_menu_token" &&
                (string)staleReply["reason"]=="token_expired" &&
                staleReply["menu_token"]!=null && staleReply["enabled"]!=null,"stale read returns stale_menu_token with fresh token");
            receipt=new JObject();
            ActAtCounter(Request(smoke),receipt);
            token=(string)receipt["menu_token"];
            Expect(ActAtCounter(Request(smoke,"choose",token,"activate"),new JObject())=="invoked" &&
                smoke.responseClicks==1,"activate calls native action once");
            var spentReply=new JObject();
            Expect(ActAtCounter(Request(smoke,"choose",token,"activate"),spentReply)=="stale_menu_token" &&
                (string)spentReply["reason"]=="token_already_used" &&
                spentReply["menu_token"]!=null && smoke.responseClicks==1,"spent menu cannot consume twice and returns fresh token");
            Expect(ActAtCounter(Request(smoke,"choose",(string)spentReply["menu_token"],"activate"),new JObject())=="invoked" &&
                smoke.responseClicks==2,"retry with returned fresh menu_token succeeds in one step");
            var portable=Item(7504,"portable_water_purifier",1);
            var bottle=Item(7505,"bottle",1);
            var move=new JObject { ["command_id"]=System.Guid.NewGuid().ToString("N"), ["generation"]=generation,
                ["revision"]=sequence, ["source"]="inventory", ["destination"]="purifier", ["expires_ms"]=long.MaxValue,
                ["machine_id"]=LocalId(portable), ["item_id"]=LocalId(bottle) };
            var redirect=ExecuteMove(move);
            Expect((string)redirect["status"]=="wrong_operation" && (int)redirect["arguments"]["source_id"]==LocalId(portable) &&
                (int)redirect["arguments"]["target_id"]==LocalId(bottle) && bottle.parentInventory==EmporiumEntry.Instance.invElement,
                "portable purifier redirects without moving or using the bottle");
            var slot=(GameInventory)kit.contentWindow.childElement;
            slot.owner=kit;
            var card=Item(7506,"system_module_ruined",1);
            card.To(slot);
            card.waterTags.Add("MODULE_STUCK_TAG");
            card.mayRemove=false;
            var extractor=Item(7507,"module_extractor",1);
            var inspect=new JObject { ["action"]="inspect_window", ["owner_id"]=LocalId(kit) };
            var window=new JObject();
            WorkstationAction(inspect,window);
            var slotRows=(JArray)window["window"]["controls"][0]["items"];
            Expect(slotRows[0]["removal"]==null && (string)slotRows[1]["removal"]["reason"]=="module_stuck",
                "only stuck items report removal instructions");
            var take=new JObject { ["action"]="transfer_slot", ["owner_id"]=LocalId(kit), ["item_id"]=LocalId(card),
                ["source"]="slot", ["control"]="content", ["window_token"]=window["window_token"] };
            var blocked=new JObject();
            Expect(WorkstationAction(take,blocked)=="transfer_blocked" && (string)blocked["reason"]=="module_stuck" &&
                (int)blocked["removal"]["target_id"]==LocalId(card),"failed removal identifies the card and extractor route");
            receipt=new JObject();
            Expect(ActAtCounter(Request(card),receipt)=="observed" && (string)receipt["removal"]["next_tool"]=="item_menu",
                "right click on installed card explains the native tool");
            receipt=new JObject();
            ActAtCounter(Request(extractor),receipt);
            var use=Request(extractor,"choose",(string)receipt["menu_token"],"use");
            use["target_id"]=LocalId(card);
            receipt=new JObject();
            Expect(ActAtCounter(use,receipt)=="invoked" && (bool?)receipt["module_stuck"]==false &&
                (int)receipt["owner_id"]==LocalId(kit) && card.parentInventory==slot,"extractor loosens card in place");
            window=new JObject();
            WorkstationAction(inspect,window);
            take["window_token"]=window["window_token"];
            Expect(WorkstationAction(take,new JObject())=="moved" && card.parentInventory==EmporiumEntry.Instance.invElement,
                "loosened card can be taken normally");

            // S6: interaction_blocked always includes specific reason
            receipt = new JObject();
            ActAtCounter(Request(smoke), receipt);
            string s6Token = (string)receipt["menu_token"];
            Interactable.isPaused = true;
            var s6Paused = new JObject();
            Expect(ActAtCounter(Request(smoke, "choose", s6Token, "activate"), s6Paused) == "interaction_blocked" &&
                (string)s6Paused["reason"] == "paused", "S6 paused attaches reason=paused");
            Interactable.isPaused = false;
            PlayerStore.Instance.lockAllInventoryFlag = 1;
            var s6InvLock = new JObject();
            Expect(ActAtCounter(Request(smoke, "choose", (string)s6Paused["menu_token"] ?? s6Token, "activate"), s6InvLock) == "interaction_blocked" &&
                (string)s6InvLock["reason"] == "inventory_locked", "S6 lockAllInventoryFlag attaches reason=inventory_locked");
            PlayerStore.Instance.lockAllInventoryFlag = 0;
            staged = smoke;
            var s6Trade = new JObject();
            Expect(ActAtCounter(Request(smoke, "choose", (string)s6InvLock["menu_token"] ?? s6Token, "activate"), s6Trade) == "interaction_blocked" &&
                (string)s6Trade["reason"] == "trade_in_progress", "S6 staged order attaches reason=trade_in_progress");
            staged = null;
            var prevSelect = ItemSelectHandler.current;
            ItemSelectHandler.current = null;
            var s6Select = new JObject();
            Expect(ActAtCounter(Request(smoke, "choose", (string)s6Trade["menu_token"] ?? s6Token, "equip"), s6Select) == "interaction_blocked" &&
                (string)s6Select["reason"] == "select_handler_missing", "S6 missing ItemSelectHandler attaches reason=select_handler_missing");
            ItemSelectHandler.current = prevSelect;

            card.To(null); extractor.To(null);
            smoke.To(null); kit.To(null); portable.To(null); bottle.To(null);
        }
    }
}
