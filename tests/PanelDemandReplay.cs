using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using TMPro;

namespace WhiteDragon.Ledger
{
    public class PanelNode
    {
        public string name;
        public Window gameObject = new Window { activeInHierarchy = true };
        public TextMeshProUGUI label;
        public List<PanelNode> children = new List<PanelNode>();
        public int childCount => children.Count;
        public PanelNode GetChild(int i) => children[i];
        public PanelNode Find(string key) => children.Find(c => c.name == key);
        public T GetComponent<T>() where T:class => label as T;
    }

    public sealed partial class InventoryObserver
    {
        private static PanelNode PanelText(string name, string text) =>
            new PanelNode { name=name, label=new TextMeshProUGUI { text=text } };

        private void ReplayPanelDemand()
        {
            var ui = NegociationUIManager.Instance;
            var host = new PanelNode();
            var prior = ui.uiObject.elements;
            ui.uiObject.elements = new object[] { new NegotiationTabletFrame { infoHost=host } };
            host.children.Add(PanelText("InfoLabel","ui_nego_restrictions"));
            host.children.Add(new PanelNode { name="Tag", children={ PanelText("Text","ui_nego_tag_no_contraband") } });
            host.children.Add(PanelText("InfoLabel","ui_nego_interests"));
            host.children.Add(new PanelNode { name="Tag", children={ PanelText("Text","Water") } });
            var visible = CounterView()["demand"];
            Expect((bool)visible["exclude_contraband"] && (string)visible["restrictions"][0]=="ui_nego_tag_no_contraband" &&
                (string)visible["interests"][0]=="Water" && visible["buy_tags"]==null,"panel overrides profile");
            host.children.Insert(2, new PanelNode { name="Tag", children={ PanelText("Text","ui_nego_tag_variety") } });
            var variety = CounterView()["demand"];
            Expect((int)variety["max_per_kind"]==1 && ((JArray)variety["sold_kinds"]).Count==0,
                   "visible native variety before the sale");
            merchant.varietyTrackingList.Add("scrap_metal");
            DialogUIManager.Instance.HasPausedConversation=true;
            var paused = CounterView();
            Expect(paused["demand"]==null && (string)paused["visit_variety"]["visit"]==visitId &&
                   (string)paused["visit_variety"]["sold_kinds"][0]=="scrap_metal",
                   "same visit retains the observed rule with fresh native sales during dialogue");
            DialogUIManager.Instance.HasPausedConversation=false;
            host.children.Add(new PanelNode { name="Probe" });
            Expect((string)CounterView()["demand"]["status"]=="panel_updating","ignore layout probes");
            host.children.Clear();
            Expect(CounterView()["demand"]["max_per_kind"]==null && ((JArray)CounterView()["demand"]["restrictions"]).Count==0,"do not retain old restrictions");
            ui.uiObject.activeInHierarchy=false;
            Expect((string)CounterView()["demand"]["status"]=="panel_closed","hidden UI is not an empty restrictions row");
            ui.uiObject.activeInHierarchy=true;
            host.children.Add(PanelText("InfoLabel","ui_nego_restrictions"));
            Expect((string)CounterView()["visit_variety"]["status"]=="last_visible_for_visit",
                   "a restriction heading without tags preserves the previous observation");
            host.children.Add(new PanelNode { name="Tag", children={ PanelText("Text","ui_nego_tag_no_contraband") } });
            Expect(CounterView()["visit_variety"]==null,"a populated replacement row supersedes the old rule");
            merchant.varietyTrackingList.Clear();
            ui.uiObject.elements=prior;
        }
    }
}
