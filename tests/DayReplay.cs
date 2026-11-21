using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
namespace WhiteDragon.Ledger
{
    public class SlotMarker {}
    public partial class GameInventory {
        public Action<GameItem,GameInventory,SlotMarker> onSlotAddItemLateFunc,onSlotRemoveItemLateFunc,onSlotChangeAmountItemFunc;
    }
    public partial class EmporiumEntry { public GameInventory trashcanInvElement=new GameInventory(); }
    public class StoreService { public bool unlocked=true,isUsing=true; public int cost; }
    public class StoreEvent { public string GetDisplayName()=>"calendar entry"; }
    public class StoreEventManager { public List<StoreEvent> GetActiveEventOnDay(int d,bool include,bool known)=>new List<StoreEvent>(); }
    public partial class StoreStation { public StoreEventManager storeEventManager=new StoreEventManager(); }
    public partial class PlayerStore {
        public bool IsPropertyPaid,isPayingMortgage;
        public int mortgageWeeklyPayment=20,garbageLevel;
        public List<StoreService> storeService=new List<StoreService>();
        public int GetRentValue()=>100;
        public int GetRentDayCounter()=>(7-StoreStation.Instance.dayCounter%7)%7;
    }
    public class EndOfDayUIManager {
        public static EndOfDayUIManager Instance=new EndOfDayUIManager();
        public bool IsOpen; public int submissions;
        public void OnEndDayButtonClick(){submissions++;IsOpen=false;}
    }
    public class StoreBedroll { public void OnClick(){EndOfDayUIManager.Instance.IsOpen=true;} }
    public sealed partial class InventoryObserver {
        private void ReplayDay() {
            var store=PlayerStore.Instance;
            var station=StoreStation.Instance;
            station.dayCounter=10;
            var day=DayView();
            Expect((int)day["calendar"]["days_until_due"]==4 && (int)day["calendar"]["due_day"]==14,"rent due");
            var bin=EmporiumEntry.Instance.trashcanInvElement;
            var piece=Item(1378,"waste",0);
            piece.To(bin);bin.onSlotAddItemLateFunc(piece,bin,null);
            Expect((int)DayView()["trash"]["stacks"]==1,"item discarded");
            station.dayCounter++;piece.To(null);
            Expect((int)DayView()["trash"]["stacks"]==0,"observed next-day disposal");
            station.dayCounter++;piece.To(bin);
            Expect((int)DayView()["trash"]["stacks"]==1,"overnight survivor retained");
            var request=new JObject { ["day"]=station.dayCounter,["service_cost"]=0 };
            Expect(SubmitSleep(request,new JObject())=="sleep_requires_closed_shop","no sleep in store hours");
            store.storeState=PlayerStore.StoreState.CLOSED; store.playerCash=40;
            store.storeService.Add(new StoreService {cost=5});EndOfDayUIManager.Instance.IsOpen=true;
            Expect(SubmitSleep(request,new JObject())=="service_cost_changed","charges have changed");
            request["service_cost"]=5;
            Expect(SubmitSleep(request,new JObject())=="sleep_submitted" && SubmitSleep(request,new JObject())=="day_changed_or_pending" &&
                EndOfDayUIManager.Instance.submissions==1,"one night only");
        }
    }
}
