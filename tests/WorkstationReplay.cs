using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace UnityEngine {
    public struct Vector2 { }
    public static class Input { public static Vector2 mousePosition; }
}
namespace WhiteDragon.Ledger {
    public interface PixelElement {}
    public partial class EmporiumEntry {
        public GameItem dossier;
        public PixelWindow docInvWindow = new PixelWindow();
        public GameSlotInventory docInvElement = new GameSlotInventory();
    }
    public class MachineryLabel : PixelElement { public string GetText() => "label"; }
    public class PixelWindow {
        public PixelElement childElement;
        public string titleString="Fixture";
        public bool isLocked, visible;
        public bool IsVisible() => visible;
        public void Hide() { visible=false; }
    }
    public class ItemMouseDoubleClickHandler {
        public static ItemMouseDoubleClickHandler current=new ItemMouseDoubleClickHandler();
        public void OpenContentAction(GameItem item,UnityEngine.Vector2 position) {item.contentWindow.visible=true;}
    }
    public class MachineryPanel : PixelElement {
        public string role;
        public MachineryLabel label;
        public PixelElement content;
    }
    public class MachineryTabs : PixelElement {
        public int selected;
        public List<PixelElement> pages=new List<PixelElement>();
        public int count => pages.Count;
        public void Select(int index,bool notify=false) { selected=index; }
        public PixelElement GetPage(int index) => pages[index];
        public string GetCaption(int index) => index.ToString();
    }
    public class MachineryPanelRow : PixelElement { public List<MachineryPanel> Panels=new List<MachineryPanel>(); }
    public class MachineryIntake : PixelElement { public PixelElement content; }
    public class MachineryStack : PixelElement { public List<PixelElement> elements=new List<PixelElement>(); }
    public class MachineryText : PixelElement { public string GetText() => "visible"; }
    public class MachineryProgressBar : PixelElement { public float value; }
    public class MachineryButton : PixelElement {
        public string text,description;
        public bool isDisabled;
        public int presses;
        public System.Action onPress;
        public bool Select() { presses++; onPress?.Invoke(); return true; }
    }
    public static class MachineryContainerUtils {
        public static bool IsHidden(PixelElement element) => false;
    }
    public sealed partial class InventoryObserver {
        private void ReplayWorkstation()
        {
            phase="MORNING";
            var owner=Item(821,"furnace",0);
            var material=Item(822,"ore",0);
            material.To(EmporiumEntry.Instance.showcaseElement);
            var input=new GameGridInventory(4, 3);
            var output=new GameGridInventory(2, 2);
            var button=new MachineryButton { text="Start" };
            var layout=new MachineryPanelRow();
            layout.Panels.Add(new MachineryPanel { role="input",content=input });
            layout.Panels.Add(new MachineryPanel { role="output",content=output });
            layout.Panels.Add(new MachineryPanel { role="control",content=button });
            var meter=new MachineryProgressBar();
            layout.Panels.Add(new MachineryPanel { role="meter",content=meter });
            owner.contentWindow=new PixelWindow {childElement=layout};
            var request=new JObject { ["owner_id"]=LocalId(owner), ["action"]="inspect_window" };
            var reply=new JObject();
            Expect(WorkstationAction(request,reply)=="window_closed","query never opens implicitly");
            request["open_if_closed"]=true;
            Expect(WorkstationAction(request,reply)=="observed" && reply["window"]["controls"].Count()==4 &&
                (int)reply["window"]["controls"][0]["capacity"]["w"]==4 &&
                (int)reply["window"]["controls"][0]["capacity"]["free_cells"]==12,
                "native open and full controls with slot capacity");
            var slotOwner = Item(823, "evaporator", 0);
            slotOwner.To(EmporiumEntry.Instance.showcaseElement);
            var singleSlot = new GameSlotInventory();
            slotOwner.contentWindow = new PixelWindow { visible=true, childElement=new MachineryPanel { role="input", content=singleSlot } };
            var slotReply = new JObject();
            Expect(WorkstationAction(new JObject { ["owner_id"]=LocalId(slotOwner), ["action"]="inspect_window" }, slotReply)=="observed" &&
                (string)slotReply["window"]["controls"][0]["accepts"]=="one_container" &&
                slotReply["window"]["controls"][0]["capacity"]==null,
                "single-item slot reports accepts one_container instead of 1x1 capacity");
            slotOwner.To(null);
            input.replayFull = true;
            var blockedTransfer = new JObject();
            Expect(WorkstationAction(new JObject { ["action"]="transfer_slot", ["owner_id"]=LocalId(owner),
                ["item_id"]=LocalId(material), ["source"]="showcase", ["control"]="content/0/body",
                ["window_token"]=reply["window_token"] }, blockedTransfer) == "no_space" &&
                (int)blockedTransfer["item_size"]["w"]==1 &&
                (int)blockedTransfer["target_capacity"]["free_cells"]==12,
                "full workstation slot returns no_space");
            input.replayFull = false;
            reply = new JObject();
            WorkstationAction(request, reply);
            request["since_token"]=reply["window_token"];
            reply=new JObject();
            Expect(WorkstationAction(request,reply)=="observed" && reply["window"]==null && reply["window_delta"]==null,"no unchanged data");
            request["action"]="transfer_slot";
            request["item_id"]=LocalId(material);
            request["source"]="showcase";
            request["window_token"]=reply["window_token"];
            request["control"]="content/1/body";
            Expect(WorkstationAction(request,new JObject())=="insert_blocked" && output.childItems.Count==0,"output only");
            request["action"]="inspect_window";
            request.Remove("since_token");
            WorkstationAction(request,reply);
            request["action"]="transfer_slot";
            request["control"]="content/0/body";
            request["window_token"]=reply["window_token"];
            reply=new JObject();
            Expect(WorkstationAction(request,reply)=="moved" && material.parentInventory==input &&
                reply["window"]==null && (int)reply["window_delta"]["controls_updated"][0]["items_updated"][0]["id"]==LocalId(material),
                "direct generic store reports only slot delta");
            request["source"]="slot";
            request["window_token"]=reply["window_token"];
            reply=new JObject();
            Expect(WorkstationAction(request,reply)=="moved" && material.parentInventory==EmporiumEntry.Instance.invElement &&
                (int)reply["window_delta"]["controls_updated"][0]["items_removed"][0]==LocalId(material),"recover to inventory");
            request["action"]="transfer_slots";
            request["source"]="inventory";
            var more = Item(19079,"ore",0);
            var batch = new JArray(LocalId(material),LocalId(more));
            for (int i=0;i<4;i++) batch.Add(LocalId(Item(19110+i,"supply",0)));
            request["item_ids"]=batch;
            request["window_token"]=reply["window_token"];
            reply=new JObject();
            Expect(WorkstationAction(request,reply)=="moved" && input.childItems.Contains(material) &&
                input.childItems.Contains(more) && ((JArray)reply["items"]).Count==6,"six-item batch insert");
            request["source"]="slot";
            request["window_token"]=reply["window_token"];
            reply=new JObject();
            Expect(WorkstationAction(request,reply)=="moved" && ((JArray)reply["items"]).Count==6 &&
                input.childItems.Count==0,"six-item batch retrieval");
            request["source"]="inventory";
            request["window_token"]=reply["window_token"];
            request["item_ids"]=new JArray(LocalId(material),LocalId(more));
            reply=new JObject();
            Expect(WorkstationAction(request,reply)=="moved","restage for partial batch");
            request["source"]="slot";
            request["window_token"]=reply["window_token"];
            request["item_ids"]=new JArray(LocalId(material),999999,LocalId(more));
            reply=new JObject();
            Expect(WorkstationAction(request,reply)=="batch_incomplete" && (string)reply["items"][0]["status"]=="moved" &&
                (string)reply["items"][1]["status"]=="item_unavailable" && (string)reply["items"][2]["status"]=="skipped" &&
                input.childItems.Contains(more), "batch stops with exact outcomes");
            request["action"]="press_control";
            request["control"]="content/2/body";
            request["label"]="Start";
            request["window_token"]=reply["window_token"];
            button.isDisabled=true;
            Expect(WorkstationAction(request,new JObject())=="window_changed" && button.presses==0,"no stale activation");
            button.isDisabled=false;
            var refreshed=new JObject();
            WorkstationAction(new JObject { ["action"]="inspect_window",["owner_id"]=LocalId(owner) },refreshed);
            request["window_token"]=refreshed["window_token"];
            meter.value=0.5f;
            reply=new JObject();
            Expect(WorkstationAction(request,reply)=="pressed" && button.presses==1 && (float)reply["window_delta"]["controls_updated"][0]["value"]==0.5f,"one native press despite meter tick");
            Expect(WorkstationAction(request,new JObject())=="window_changed" && button.presses==1,"no duplicate press");
            ReplayArchive();
        }

        private void ReplayArchive()
        {
            var shop=EmporiumEntry.Instance;
            var box=new GameItem {identifier="dossier",owned=false,contentWindow=new PixelWindow { childElement=new GameInventory() }};
            shop.dossier=box;
            box.To(shop.docInvElement);
            var paper=Item(825,"slip",0);
            var request=new JObject { ["action"]="inspect_archive" };
            var receipt=new JObject();
            Expect(WorkstationAction(request,receipt)=="archive_unavailable","fixture must be accessible");
            shop.docInvWindow.visible=true;
            Expect(WorkstationAction(request,receipt)=="window_closed","read does not open filing box");
            request["open_if_closed"]=true;
            Expect(WorkstationAction(request,receipt)=="observed","open real fixture");
            request=new JObject { ["action"]="transfer_slot", ["owner_id"]=receipt["owner_id"],
                ["control"]="content", ["source"]="inventory", ["item_id"]=LocalId(paper), ["window_token"]=receipt["window_token"] };
            receipt=new JObject();
            Expect(WorkstationAction(request,receipt)=="moved" && (string)receipt["actual_area"]=="archive" &&
                (int)receipt["window_delta"]["controls_updated"][0]["items_updated"][0]["id"]==LocalId(paper),"filed document delta");
            var archived = new JObject();
            Expect(WorkstationAction(new JObject { ["action"]="inspect_window", ["owner_id"]=LocalId(paper) }, archived)=="observed" &&
                (string)archived["window"]["location"]["area"]=="archive" && archived["window"]["document"]!=null, "read filed paper without moving it");
            request["source"]="slot";
            request["window_token"]=receipt["window_token"];
            receipt=new JObject();
            Expect(WorkstationAction(request,receipt)=="moved" && (string)receipt["actual_area"]=="inventory" &&
                (int)receipt["window_delta"]["controls_updated"][0]["items_removed"][0]==LocalId(paper),"same document returned");
            request=new JObject { ["action"]="close_archive", ["window_token"]=receipt["window_token"] };
            Expect(WorkstationAction(request,new JObject())=="window_closed" && !box.contentWindow.visible,"close filing box");
        }
    }
}
