using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace WhiteDragon.Ledger
{
    public class MachineryRadioButton : PixelElement
    {
        public string text;
        public bool isOn,isDisabled;
        public bool Select(){isOn=true;return true;}
    }
    public class MachinerySlider : PixelElement
    {
        public float min,max=10,step=1,value;
        public bool isDisabled;
        public Action<float> onReleased;
        public void SetValue(float v,bool notify){value=(float)Math.Round(v);}
    }
    public class MachineryRangeSlider : PixelElement
    {
        public float min,max=10,step=1,low,high;
        public bool isDisabled;
        public Action<float,float> onReleased;
        public void SetValues(float l,float h,bool notify){low=l;high=h;}
    }

    internal static class NativeItemPanels
    {
        internal static ItemPanelView panel;
        internal static ItemPanelView second;
        internal static ItemPanelView Current(string requested=null)
        {
            if (requested==null && second==null) return panel;
            if (requested==panel?.Key) return panel;
            if (requested==second?.Key && requested!=null) return second;
            var keys=new[]{panel?.Key,second?.Key}.Where(k=>k!=null);
            return keys.Any() ? new ItemPanelView {Key=string.Join(",",keys),DelegateTo="select_panel"} : null;
        }
        internal static string ActiveKey() => panel?.Key;
        internal static JObject scan;
        internal static JObject ScannerReading() => scan;
        internal static bool ConfirmationOpen => false;
        internal static void CaptureConfirmation(bool previouslyOpen) {}
    }

    public sealed partial class InventoryObserver
    {
        private void ReplayItemPanel()
        {
            int calls=0;
            ItemPanelView MakePanel(string identity,int selection)
            {
                var p=new ItemPanelView { Key="feature_labels", Identity=identity };
                p.Controls["list"]=new JObject { ["type"]="choice", ["enabled"]=true, ["value"]=selection };
                p.Actions["list"]=v=>{
                    if (v?.Type!=JTokenType.Integer) return "invalid_choice";
                    calls++;
                    NativeItemPanels.panel=MakePanel(identity,(int)v);
                    return "applied";
                };
                return p;
            }
            NativeItemPanels.panel=MakePanel("first",0);
            JObject Request(string mode,JToken token=null)=>new JObject { ["action"]="item_panel", ["generation"]=generation,
                ["revision"]=sequence, ["expires_ms"]=long.MaxValue, ["mode"]=mode, ["panel_token"]=token, ["control"]="list", ["value"]=1 };
            var first=new JObject();
            Expect(ActAtCounter(Request("read"),first)=="observed" && first["view"]!=null,"panel baseline");
            var pick=Request("choose",first["panel_token"]);
            Expect(ActAtCounter(Request("read",first["panel_token"]),new JObject())=="unchanged","unchanged panel");
            var reply=new JObject();
            Expect(ActAtCounter(pick,reply)=="applied" && (int)reply["after"]["controls"]["list"]["value"]==1 &&
                reply["after"]["view"]==null && calls==1,"single native choice returns only visible delta");
            Expect(ActAtCounter(pick,new JObject())=="panel_changed" && calls==1,"cannot replay selection");
            NativeItemPanels.panel=MakePanel("reopened",1);
            Expect(ActAtCounter(Request("choose",reply["after"]["panel_token"]),new JObject())=="panel_changed","reopened window invalidates token");
            NativeItemPanels.panel=new ItemPanelView { Key="inspection", DelegateTo="player_visual_check" };
            Expect(ActAtCounter(Request("read"),reply)=="dedicated_panel" && (string)reply["next"]=="player_visual_check","visual judgement stays with player");
            NativeItemPanels.panel=null;
            Expect(ActAtCounter(Request("read"),reply)=="panel_closed","closed panel releases last observation");

            var scanner=new ItemPanelView {Key="chemical_scanner",Identity="scanner/1"};
            var finisher=new ItemPanelView {Key="chemical_finisher",Identity="finisher/1"};
            NativeItemPanels.panel=finisher; NativeItemPanels.second=scanner;
            bool focused=false;
            scanner.Focus=()=>focused=true;
            scanner.Close=()=>NativeItemPanels.second=null;
            reply=new JObject();
            Expect(ActAtCounter(Request("read"),reply)=="select_panel" && reply["panels"].Count()==2,"both panels named");
            var readScanner=Request("read"); readScanner["panel"]=scanner.Key;
            reply=new JObject(); ActAtCounter(readScanner,reply);
            var selected=reply["panel_token"];
            reply=new JObject();
            Expect(ActAtCounter(Request("focus",selected),reply)=="focused" && focused,"focus named scanner");
            var close=Request("close",reply["after"]["panel_token"]);
            reply=new JObject();
            Expect(ActAtCounter(close,reply)=="closed" && (string)reply["after"]["view"]["panel"]==finisher.Key,"close only scanner");
            JObject Reading(int extract)=>new JObject { ["total_ml"]=2.0, ["components"]=new JArray(
                new JObject { ["id"]="dream_cap_extract", ["ul"]=extract, ["percent"]=extract/20.0 },
                new JObject { ["id"]="physical_contaminant", ["ul"]=2000-extract, ["percent"]=(2000-extract)/20.0 }) };
            var scanned=new ItemPanelView {Key="chemical_scanner",Identity="scanner/2",Scan=Reading(1229)};
            NativeItemPanels.panel=scanned; NativeItemPanels.second=null;
            reply=new JObject();
            Expect(ActAtCounter(Request("read"),reply)=="observed" &&
                (string)reply["view"]["scan"]["components"][0]["id"]=="dream_cap_extract","scan reading is structured");
            var scanToken=reply["panel_token"];
            NativeItemPanels.panel=new ItemPanelView {Key="chemical_scanner",Identity="scanner/2",Scan=Reading(1300)};
            reply=new JObject();
            Expect(ActAtCounter(Request("read",scanToken),reply)=="observed" && reply["view"]==null &&
                (int)reply["scan"]["components"][0]["ul"]==1300,"live scan change arrives as delta");
            NativeItemPanels.panel=null;
            var machine=Item(7520,"cabinet",1);
            var tabs=new MachineryTabs();
            tabs.pages.Add(new GameGridInventory(1,1));
            tabs.pages.Add(new GameGridInventory(2,1));
            machine.contentWindow=new PixelWindow { visible=true, childElement=tabs };
            var inspect=new JObject { ["action"]="inspect_window", ["owner_id"]=LocalId(machine) };
            reply=new JObject();
            Expect(WorkstationAction(inspect,reply)=="observed" && reply["window"]["controls"][0]["pages"].Count()==2,"expose native tab strip");
            var change=new JObject { ["action"]="press_control", ["owner_id"]=LocalId(machine), ["window_token"]=reply["window_token"],
                ["control"]="content", ["label"]="1", ["tab_index"]=1 };
            reply=new JObject();
            Expect(WorkstationAction(change,reply)=="tab_selected" && tabs.selected==1 && reply["window_token"]!=null,"switch tab and return new controls");
            var slider=new MachinerySlider();
            int releases=0;
            slider.onReleased=v=>releases++;
            machine.contentWindow.childElement=slider;
            reply=new JObject();
            WorkstationAction(inspect,reply);
            change["action"]="adjust_control"; change["window_token"]=reply["window_token"]; change["values"]=new JArray(3.8);
            reply=new JObject();
            Expect(WorkstationAction(change,reply)=="adjusted" && (float)reply["value"]==4 && releases==1,"slider uses snapped value and release once");
            var range=new MachineryRangeSlider();
            machine.contentWindow.childElement=range;
            reply=new JObject();
            WorkstationAction(inspect,reply);
            change["window_token"]=reply["window_token"]; change["values"]=new JArray(8,3);
            Expect(WorkstationAction(change,new JObject())=="invalid_value" && range.high==0,"reject inverted range without touching machine");
            WorkstationAction(inspect,reply);
            change["window_token"]=reply["window_token"]; change["values"]=new JArray(3,8);
            Expect(WorkstationAction(change,new JObject())=="adjusted" && range.low==3 && range.high==8,"native range applies both endpoints");
            var radio=new MachineryRadioButton{text="材料"};
            machine.contentWindow.childElement=radio;
            WorkstationAction(inspect,reply);
            change=new JObject { ["action"]="press_control", ["owner_id"]=LocalId(machine), ["control"]="content",
                ["label"]=radio.text, ["window_token"]=reply["window_token"] };
            NativeItemPanels.panel=scanned;
            reply=new JObject();
            Expect(WorkstationAction(change,reply)=="pressed" && radio.isOn && reply["next_tool"]==null,
                "panel already open before the press is not re-announced");
            NativeItemPanels.panel=null;
            machine.To(null);
        }
    }
}
