using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace WhiteDragon.Ledger
{
    internal sealed class ItemPanelView
    {
        public string Key, Identity, DelegateTo;
        public Action Focus, Close;
        public readonly JObject Controls = new JObject();
        public readonly JArray Text = new JArray();
        public readonly Dictionary<string, Func<JToken,string>> Actions = new Dictionary<string, Func<JToken,string>>();
        // Structured reading of the scanned item (chemical scanner only), taken from the item itself.
        public JObject Scan;

        public JObject Data()
        {
            var data = new JObject { ["panel"]=Key, ["can_close"]=Close!=null, ["can_focus"]=Focus!=null, ["text"]=Text.DeepClone(), ["controls"]=Controls.DeepClone() };
            if (Scan != null) data["scan"] = Scan.DeepClone();
            return data;
        }
    }

    public sealed partial class InventoryObserver
    {
        private ItemPanelView lastItemPanel;
        private string itemPanelToken;

        private string PublishItemPanel(ItemPanelView panel, string since, JObject receipt)
        {
            if (panel == null || panel.DelegateTo != null)
            {
                lastItemPanel=null;
                itemPanelToken=null;
                if (panel == null) return "panel_closed";
                receipt["panel"]=panel.Key;
                receipt["next"]=panel.DelegateTo;
                if (panel.DelegateTo=="select_panel")
                {
                    receipt["panels"]=new JArray(panel.Key.Split(new[]{','}));
                    return "select_panel";
                }
                return "dedicated_panel";
            }
            bool samePanel = lastItemPanel != null && lastItemPanel.Identity == panel.Identity;
            bool delta = samePanel && since != null && since == itemPanelToken;
            var view = panel.Data();
            bool identical = samePanel && JToken.DeepEquals(lastItemPanel.Data(),view);
            if (!identical) itemPanelToken=Guid.NewGuid().ToString("N");
            receipt["panel_token"]=itemPanelToken;
            if (!delta) receipt["view"]=view;
            else if (!identical)
            {
                if (!JToken.DeepEquals(lastItemPanel.Text,panel.Text))
                {
                    var lines=new JObject();
                    for (int i=0;i<Math.Max(lastItemPanel.Text.Count,panel.Text.Count);i++)
                    {
                        var before=i<lastItemPanel.Text.Count ? lastItemPanel.Text[i] : null;
                        var after=i<panel.Text.Count ? panel.Text[i] : null;
                        if (!JToken.DeepEquals(before,after)) lines[i.ToString()]=after?.DeepClone() ?? JValue.CreateNull();
                    }
                    receipt["text_changes"]=lines;
                }
                var changes=new JObject();
                foreach (var field in panel.Controls.Properties())
                    if (!JToken.DeepEquals(lastItemPanel.Controls[field.Name],field.Value)) changes[field.Name]=field.Value.DeepClone();
                foreach (var field in lastItemPanel.Controls.Properties())
                    if (panel.Controls[field.Name]==null) changes[field.Name]=JValue.CreateNull();
                if (changes.HasValues) receipt["controls"]=changes;
                if (!JToken.DeepEquals(lastItemPanel.Scan,panel.Scan)) receipt["scan"]=panel.Scan?.DeepClone() ?? JValue.CreateNull();
            }
            lastItemPanel=panel;
            return delta && identical ? "unchanged" : "observed";
        }

        private string ItemPanelAction(JObject request,JObject receipt)
        {
            string mode=(string)request["mode"], token=(string)request["panel_token"];
            string requested=(string)request["panel"];
            if (requested==null && token!=null && token==itemPanelToken) requested=lastItemPanel?.Key;
            var panel=NativeItemPanels.Current(requested);
            if (mode=="read") return PublishItemPanel(panel,token,receipt);
            if (mode!="choose" && mode!="focus" && mode!="close") return "invalid_action";
            if (panel==null || panel.DelegateTo!=null || lastItemPanel==null || token==null || token!=itemPanelToken ||
                panel.Identity!=lastItemPanel.Identity || (mode=="choose" && !JToken.DeepEquals(panel.Data(),lastItemPanel.Data())))
            {
                PublishItemPanel(panel,null,receipt);
                return "panel_changed";
            }
            if (mode=="close" || mode=="focus")
            {
                var action=mode=="close" ? panel.Close : panel.Focus;
                if (action==null) return "control_unavailable";
                action();
                var updated=NativeItemPanels.Current(mode=="focus" ? panel.Key : null);
                var after=new JObject();
                after["status"]=PublishItemPanel(updated,null,after);
                receipt["after"]=after;
                if (mode=="close" && NativeItemPanels.Current(panel.Key)?.Identity==panel.Identity) return "close_unconfirmed";
                return mode=="close" ? "closed" : "focused";
            }
            string key=(string)request["control"];
            if (key==null || !panel.Actions.TryGetValue(key,out var invoke)) return "control_unavailable";
            bool confirmationWasOpen=NativeItemPanels.ConfirmationOpen;
            string result=invoke(request["value"]);
            if (result!="applied") return result;
            NativeItemPanels.CaptureConfirmation(confirmationWasOpen);
            // One decision per token, even when a button leaves every visible field unchanged.
            string prior=itemPanelToken;
            itemPanelToken=Guid.NewGuid().ToString("N");
            receipt["control"]=key;
            var next=NativeItemPanels.Current();
            if (next?.DelegateTo=="select_panel") next=NativeItemPanels.Current(panel.Key);
            var nextReceipt=new JObject();
            nextReceipt["status"]=PublishItemPanel(next,next!=null && next.Identity==panel.Identity ? itemPanelToken : prior,nextReceipt);
            receipt["after"]=nextReceipt;
            return result;
        }
    }
}
