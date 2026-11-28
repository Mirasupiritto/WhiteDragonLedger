using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine.UI;

namespace UnityEngine.UI
{
    public partial class Button
    {
        public Action onClick;
    }
}

namespace WhiteDragon.Ledger
{
    public class NetworkUpgrade
    {
        public int cost=12, state=1, cooldownCurrent;
        public string tab = "services";
        public int GetCost() => cost;
        public string GetLocalizedTitle() => "First referral";
        public static string GetTabLabel(string tab) => tab;
        public bool IsUnlocked() => state==2;
        public bool IsReady() => state==1;
    }

    public partial class PlayerStore
    {
        public int wildFavor;
        public Dictionary<string,NetworkUpgrade> networkUpgrade=new Dictionary<string,NetworkUpgrade>();
    }

    public class WildNetworkSlot
    {
        public string id;
        public Action<string> onClick;
        public Action<string,bool> onHover;
        public Button button;
        public TextMeshProUGUI label=new TextMeshProUGUI();
        public T GetComponent<T>() where T:class => button as T;
        public T GetComponentInChildren<T>() where T:class => label as T;
    }
    public class NetworkRoot
    {
        public WildNetworkSlot[] children=new WildNetworkSlot[0];
        public T[] GetComponentsInChildren<T>() => children as T[];
    }
    public class WildNetworkFrame
    {
        public NetworkRoot tabBar=new NetworkRoot(),gridContent=new NetworkRoot();
        public TextMeshProUGUI infoTitle,infoSubtitle,infoDescription,infoMessage,infoCooldown,usePrice,useLabel;
        public Button useButton=new Button();
    }
    public static class WildNetworkUI
    {
        private static WildNetworkFrame frame;
        private static int currentTab;
        private static string selectedId;
        public static bool IsOpen => frame!=null;
        public static void Close() { frame=null; }
        public static WildNetworkFrame Fixture()
        {
            frame=new WildNetworkFrame();
            currentTab=0;
            selectedId=null;
            var tab = new WildNetworkSlot { id="1", button=new Button() };
            tab.label.text="Services";
            tab.button.onClick=()=>{currentTab=1; tab.button.interactable=currentTab!=1;};
            frame.tabBar.children=new[]{tab};
            frame.gridContent.children=new[]{new WildNetworkSlot { id="referral", onClick=id=>selectedId=id }};
            return frame;
        }
    }

    public sealed partial class InventoryObserver
    {
        private void ReplayNetwork()
        {
            var store=PlayerStore.Instance;
            int originalCash=store.playerCash;
            store.playerCash=100;
            store.wildFavor=5;
            var upgrade=new NetworkUpgrade();
            store.networkUpgrade["referral"]=upgrade;
            var frame=WildNetworkUI.Fixture();
            var catalog = new List<WildNetworkSlot>(frame.gridContent.children);
            for (int i = 1; i < 55; i++)
            {
                var slot = new WildNetworkSlot { id = "fixture_service_" + i };
                store.networkUpgrade.Add(slot.id, new NetworkUpgrade());
                catalog.Add(slot);
            }
            catalog.Add(catalog[0]);
            frame.gridContent.children = catalog.ToArray();
            frame.useButton.onClick=()=>{store.playerCash-=7; store.wildFavor-=5; upgrade.state=2;};
            JObject receipt=null;
            string token=null;
            string Send(string mode,string id=null,int? cost=null,string since=null)
            {
                receipt=new JObject();
                return ActAtCounter(new JObject { ["action"]="wild_network",["generation"]=generation,["revision"]=sequence,
                    ["expires_ms"]=DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds(), ["mode"]=mode,["target"]=id,
                    ["cost"]=cost, ["panel_token"]=token, ["since_token"]=since },receipt);
            }
            void Read()
            {
                Expect(Send("read")=="observed","native network read");
                token=(string)receipt["panel"]["panel_token"];
            }
            JObject AcceptSelection()
            {
                var detail = (JObject)receipt["selected"];
                Expect(receipt["panel"] == null && detail["tabs"] == null && detail["services"] == null,
                       "action receipt contains no catalog");
                var fresh = (string)receipt["panel_token"];
                Expect(!string.IsNullOrEmpty(fresh) && fresh != token, "action issues a fresh token");
                token = fresh;
                return detail;
            }
            Expect(CounterStage()=="wild_network" && !CanDismissCustomer(store),"network suppresses negotiation and dismissal");
            Read();
            Expect((string)receipt["panel"]["tabs"][0]["label"]=="Services" && (int)receipt["panel"]["favor"]==5,
                   "visible tabs and favor");
            Expect(((JArray)receipt["panel"]["services"]).Count == 55, "full catalog on first read");
            Expect(Send("read",since:token)=="unchanged","repeat read compact");
            Expect(Send("tab","1")=="tab_selected","select native tab");
            Read();
            Expect(Send("select","referral")=="service_selected","select without spending");
            var detail = AcceptSelection();
            Expect((string)detail["selected_id"] == "referral" && (bool)detail["can_buy"] && (bool)detail["ready"] &&
                   (int)detail["cost"] == 12 && (int)detail["cash"] == 100 && (int)detail["favor"] == 5 &&
                   (int)detail["favor_to_spend"] == 5 && (int)detail["cash_to_pay"] == 7,
                   "selected quote with favor/cash split");
            Expect(Send("buy","referral",13)=="quote_changed" && store.playerCash==100,"wrong price no payment");
            upgrade.cost=14;
            Expect(Send("buy","referral",12)=="panel_changed","stale panel no payment");
            upgrade.cost=12;
            Read();
            Expect(Send("select","referral") == "service_selected", "select the current quote");
            detail = AcceptSelection();
            string quoteToken = token;
            Expect(Send("buy",(string)detail["selected_id"],(int)detail["cost"])=="purchased" && (int)receipt["cash_paid"]==7 &&
                   (int)receipt["favor_spent"]==5 && (int)receipt["cash_after"]==93 && (bool)receipt["unlocked"],
                   "native purchase spends both balances");
            detail = AcceptSelection();
            Expect((int)detail["cash"] == 93 && (int)detail["favor"] == 0 && (bool)detail["unlocked"] &&
                   (int)detail["favor_to_spend"] == 0 && (int)detail["cash_to_pay"] == 12 &&
                   !(bool)detail["can_buy"], "post-purchase selection reflects applied cost and state");
            string purchasedToken = token;
            token = quoteToken;
            Expect(Send("buy","referral",12)=="panel_changed" && store.playerCash==93,"second click cannot charge again");
            token = purchasedToken;
            Expect(Send("read",since:token) == "unchanged" && receipt["panel"] == null, "token matches full post-purchase view");
            // X5: adjacent services select exact target ID on first call
            var pharma = new NetworkUpgrade { cost = 5 };
            var crim = new NetworkUpgrade { cost = 8 };
            store.networkUpgrade["PHARMA"] = pharma;
            store.networkUpgrade["CRIMINEL_NETWORK"] = crim;
            catalog[0].id = "PHARMA";
            catalog[1].id = "CRIMINEL_NETWORK";
            catalog[1].onClick = catalog[0].onClick;
            Read();
            string staleToken = token;
            Expect(Send("select", "CRIMINEL_NETWORK") == "service_selected" &&
                   (string)receipt["selected"]["selected_id"] == "CRIMINEL_NETWORK",
                   "X5 select picks exact adjacent service ID on first attempt");
            token = staleToken;
            Expect(Send("select", "PHARMA") == "panel_changed" &&
                   (string)receipt["selected_id"] == "CRIMINEL_NETWORK",
                   "X'3 stale token select returns panel_changed without changing selection");
            token = (string)receipt["panel_token"];
            Expect(Send("close")=="closed" && Send("read")=="panel_closed","explicit close");
            store.networkUpgrade.Remove("PHARMA");
            store.networkUpgrade.Remove("CRIMINEL_NETWORK");
            catalog[0].id = "referral";
            catalog[1].id = "fixture_service_1";
            catalog[1].onClick = null;
            foreach (var slot in catalog.GetRange(1, 54)) store.networkUpgrade.Remove(slot.id);
            store.playerCash=originalCash;
            Console.WriteLine("Wild network replay passed: modal, tabs, selection, changed quote, favor/cash and close.");
        }
    }
}
