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
        public int GetCost() => cost;
        public string GetLocalizedTitle() => "First referral";
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
            Expect(CounterStage()=="wild_network" && !CanDismissCustomer(store),"network suppresses negotiation and dismissal");
            Read();
            Expect((string)receipt["panel"]["tabs"][0]["label"]=="Services" && (int)receipt["panel"]["favor"]==5,
                   "visible tabs and favor");
            Expect(Send("read",since:token)=="unchanged","repeat read compact");
            Expect(Send("tab","1")=="tab_selected","select native tab");
            Read();
            Expect(Send("select","referral")=="service_selected","select without spending");
            Read();
            Expect((bool)receipt["panel"]["can_buy"] && (int)receipt["panel"]["cost"]==12,"selected quote");
            Expect(Send("buy","referral",13)=="quote_changed" && store.playerCash==100,"wrong price no payment");
            upgrade.cost=14;
            Expect(Send("buy","referral",12)=="panel_changed","stale panel no payment");
            upgrade.cost=12;
            Read();
            Expect(Send("buy","referral",12)=="purchased" && (int)receipt["cash_paid"]==7 &&
                   (int)receipt["favor_spent"]==5 && (int)receipt["cash_after"]==93 && (bool)receipt["unlocked"],
                   "native purchase spends both balances");
            Expect(Send("buy","referral",12)=="panel_changed" && store.playerCash==93,"second click cannot charge again");
            Read();
            Expect(Send("close")=="closed" && Send("read")=="panel_closed","explicit close");
            store.playerCash=originalCash;
            Console.WriteLine("Wild network replay passed: modal, tabs, selection, changed quote, favor/cash and close.");
        }
    }
}
