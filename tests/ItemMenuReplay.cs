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
            Expect(ActAtCounter(Request(smoke,"choose",token,"activate"),new JObject())=="menu_changed","stale read cannot consume");
            string kitToken=(string)receipt["menu_token"];
            receipt=new JObject();
            Expect(ActAtCounter(Request(kit,"choose",kitToken,"open"),receipt)=="observed" &&
                (int)receipt["window"]["controls"][0]["items"][0]["id"]==LocalId(strip),"open kit returns selectable strip");
            receipt=new JObject();
            ActAtCounter(Request(smoke),receipt);
            token=(string)receipt["menu_token"];
            Expect(ActAtCounter(Request(smoke,"choose",token,"activate"),new JObject())=="invoked" &&
                smoke.responseClicks==1,"activate calls native action once");
            Expect(ActAtCounter(Request(smoke,"choose",token,"activate"),new JObject())=="menu_changed" &&
                smoke.responseClicks==1,"spent menu cannot consume twice");
            var portable=Item(7504,"portable_water_purifier",1);
            var bottle=Item(7505,"bottle",1);
            var move=new JObject { ["command_id"]=System.Guid.NewGuid().ToString("N"), ["generation"]=generation,
                ["revision"]=sequence, ["source"]="inventory", ["destination"]="purifier", ["expires_ms"]=long.MaxValue,
                ["machine_id"]=LocalId(portable), ["item_id"]=LocalId(bottle) };
            var redirect=ExecuteMove(move);
            Expect((string)redirect["status"]=="wrong_operation" && (int)redirect["arguments"]["source_id"]==LocalId(portable) &&
                (int)redirect["arguments"]["target_id"]==LocalId(bottle) && bottle.parentInventory==EmporiumEntry.Instance.invElement,
                "portable purifier redirects without moving or using the bottle");
            smoke.To(null); kit.To(null); portable.To(null); bottle.To(null);
        }
    }
}
