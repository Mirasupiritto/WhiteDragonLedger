using System.Collections.Generic;
using Newtonsoft.Json.Linq;
namespace WhiteDragon.Ledger {
    public class StoreShutterButton {
        public static StoreShutterButton Instance=new StoreShutterButton();
        public int clicks;
        public void OnClick(){
            clicks++;
            var store=PlayerStore.Instance;
            if(store.storeState==PlayerStore.StoreState.MORNING) store.storeState=PlayerStore.StoreState.OPEN;
            else if(store.waiting>0) StoreUIManager.Instance.closeShutterConfirmationPanel.activeInHierarchy=true;
            else EndAndClose();
        }
        public void EndAndClose(){
            StoreUIManager.Instance.closeShutterConfirmationPanel.activeInHierarchy=false;
            PlayerStore.Instance.storeState=PlayerStore.StoreState.CLOSED;
        }
    }
    public partial class PlayerStore {
        public enum StoreState { MORNING, OPEN, CLOSED }
        public StoreState storeState=StoreState.OPEN;
        public int lockShutterButtonFlag; public bool isClientBeingArrested,isHandlingShootout;
        public bool CanStartDay()=>true;
        public Dictionary<long,StorePhoneClient> PhoneClientDict=new Dictionary<long,StorePhoneClient>(); }
    public class StorePhoneClient {
        public const long WILD_CLIENT_NUMBER=1, RED_IMP_REP_NUMBER=2, MINING_NUMBER=3, PHARMA_NUMBER=4, CHEM_NUMBER=5, REV_MERCHANT_NUMBER=6;
        public bool listed, answers=true;
        public string dialogFuncId,phoneState="Regular";
        public int currentCooldown;
        public bool ShownInPhoneBook()=>listed;
        public string GetLocalizedDisplayName()=>dialogFuncId;
    }
    public static class LocHelper { public static string GetLocalizedUI(string key) => key; public static string GetLocalizedDialoguePhone(string key)=>key; }
    public class PhoneUIManager {
        public static PhoneUIManager Instance=new PhoneUIManager();
        public Window phonePanel=new Window(); public StorePhoneClient currentPhoneClient;
        public int calls;
        public static bool WillAnswerCall(long n)=>PlayerStore.Instance.PhoneClientDict[n].answers;
        public void OpenUI(){phonePanel.activeInHierarchy=true;}
        public void AutoCall(long n){calls++;currentPhoneClient=PlayerStore.Instance.PhoneClientDict[n];}
    }
    public class DialogChoiceBubble {
        public int type, index, clicks; public Window gameObject=new Window(); public Label TMP=new Label(); public bool enabled;
        public bool IsInteractable()=>enabled;
        public void OnDialogChoiceClick(){clicks++;}
    }
    public class DialoguePhoneUIManager {
        public static DialoguePhoneUIManager Instance=new DialoguePhoneUIManager();
        public object page; public Bubble clientBubble; public bool isWaitingForManualAdvance,choice;
        public List<DialogChoiceBubble> dialogChoiceBubbles=new List<DialogChoiceBubble>();
        public bool IsChoice()=>choice; public object GetCurrentDialog()=>page;
        public void OnAdvancePressed(){isWaitingForManualAdvance=false;}
    }
    public sealed partial class InventoryObserver {
        private void ReplayPhone() {
            var store=PlayerStore.Instance;
            store.storeState=PlayerStore.StoreState.MORNING;
            var shutter=new JObject { ["action"]="open_shop", ["generation"]=generation, ["revision"]=sequence,
                ["expires_ms"]=System.DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds() };
            var opened=new JObject();
            store.lockShutterButtonFlag=1;
            Expect(ActAtCounter(shutter,opened)=="shutter_unavailable","locked shutter");
            store.lockShutterButtonFlag=0;
            Expect(ActAtCounter(shutter,opened)=="opened" && (string)opened["phase"]=="OPEN","native morning opening");
            Expect(ActAtCounter(shutter,opened)=="shutter_unavailable" && StoreShutterButton.Instance.clicks==1,"open never closes");
            var directory=PlayerStore.Instance.PhoneClientDict;
            directory.Add(1,new StorePhoneClient {dialogFuncId="Wild"});
            directory.Add(3,new StorePhoneClient {listed=true, dialogFuncId="Mining"});
            directory.Add(4,new StorePhoneClient {dialogFuncId="Pharma"});
            var card=Item(803,"joe_card",0); phoneCards.Add(card);
            var read=PhoneView();
            var phone=PhoneUIManager.Instance;
            Expect(((JArray)read["contacts"]).Count==2 && phone.calls==0 && !phone.phonePanel.activeInHierarchy,"passive directory with card and hidden contact filtered");
            var request=new JObject {["action"]="dial",["number"]=4,["phone_token"]=read["phone_token"]};
            var result=new JObject();
            Expect(ActOnPhone(request,result)=="unknown_contact","cannot call hidden number");
            request["number"]=3; directory[3].answers=false;
            Expect(ActOnPhone(request,result)=="phone_changed","stale directory");
            request["phone_token"]=PhoneView()["phone_token"];
            Expect(ActOnPhone(request,result)=="unavailable","unavailable contact");
            request["number"]=1;
            Expect(ActOnPhone(request,result)=="connected" && phone.calls==1,"explicit dial");
            var talk=DialoguePhoneUIManager.Instance;
            talk.page=new object();talk.choice=true;
            var option=new DialogChoiceBubble {index=0,enabled=true};option.gameObject.activeInHierarchy=true;option.TMP.text="order";
            talk.dialogChoiceBubbles.Add(option);
            read=PhoneView();
            Expect(((JArray)read["choices"]).Count==1 && option.clicks==0,"menu read does not order");
            request["action"]="phone_choice";request["choice_id"]=0;request["phone_token"]=read["phone_token"];
            talk.page=new object();
            Expect(ActOnPhone(request,result)=="phone_changed" && option.clicks==0,"old choice blocked");
            request["phone_token"]=PhoneView()["phone_token"];
            Expect(ActOnPhone(request,result)=="choice_submitted" && option.clicks==1,"explicit service selection");
        }
    }
}
