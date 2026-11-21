using Newtonsoft.Json.Linq;

namespace WhiteDragon.Ledger
{
    public class AfterhourLocation
    {
        public string Id;
        public string GetTitle() => "測試拾荒場";
        public static AfterhourLocation Get(string id) => new AfterhourLocation { Id=id };
    }
    public class MapUIManager
    {
        public static MapUIManager Instance = new MapUIManager();
        public bool IsInTransit;
        public AfterhourLocation Current;
    }
    public class SceneFade { public Window gameObject = new Window(); }

    public sealed partial class InventoryObserver
    {
        private bool pending=false;
        private void Publish(JObject change) { ++sequence; }
        private void ReplayScenes()
        {
            var map = MapUIManager.Instance;
            var fade = StoreUIManager.Instance.blackscreen = new SceneFade();
            ResetScene();
            Expect(CanObserveStore(), "initial loaded store may prepare baseline");
            MarkSceneReady(ShopScene,"當鋪");
            int before = sequence;
            map.IsInTransit=true;
            Expect(!CanObserveStore() && sceneStatus=="transitioning", "early travel suspends store before destination is known");
            OnArriveEarly("dumping_ground");
            Expect(destination=="dumping_ground" && (string)lastJourney["from"]==ShopScene, "named departure");
            map.IsInTransit=false;
            map.Current=AfterhourLocation.Get(destination);
            Expect(!CanObserveStore() && sceneStatus=="transitioning", "wait for late hook");
            fade.gameObject.activeInHierarchy=true;
            OnTravelLate("dumping_ground");
            Expect(!CanObserveStore() && sceneStatus=="transitioning", "wait until fade clears");
            fade.gameObject.activeInHierarchy=false;
            Expect(!CanObserveStore() && currentLocation=="dumping_ground" && sceneStatus=="ready", "arrived outside");
            int stable=sceneRevision;
            Expect(!CanObserveStore() && sceneRevision==stable && sequence==before, "outside publishes no shop ticks");
            OnLeaveEarly("dumping_ground");
            map.Current=null;
            Expect(!CanObserveStore(), "return is not ready at early hook");
            OnTravelLate("dumping_ground");
            Expect(CanObserveStore(), "prepare returning store");
            InvalidatePreviousShopVisit();
            InvalidatePreviousShopVisit();
            MarkSceneReady(ShopScene,"當鋪");
            Expect(sequence==before+1 && (string)lastJourney["from"]=="dumping_ground" && currentLocation==ShopScene,
                "return invalidates pre-trip commands once");
        }
    }
}
