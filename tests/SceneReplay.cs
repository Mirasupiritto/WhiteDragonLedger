using Newtonsoft.Json.Linq;

namespace WhiteDragon.Ledger
{
    public partial class AfterhourLocation
    {
        public string Id;
        public string GetTitle() => "測試拾荒場";
        public static AfterhourLocation Get(string id) => new AfterhourLocation { Id=id };
    }
    public partial class MapUIManager
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
            var ending=GameOverUIManager.Instance=new GameOverUIManager();
            ending.reason.text="原生結局";
            ending.uiPanel.activeInHierarchy=true;
            Expect(!CanObserveStore() && sceneStatus=="game_over" &&
                (string)JObject.Parse(ModHelper.ReadModDataFileText(ModId,"scene.json"))["reason"]=="原生結局","game-over reason");
            ending.uiPanel.activeInHierarchy=false;
            Expect(CanObserveStore(),"can continue after native ending panel closes");
            MarkSceneReady(ShopScene,"當鋪");
            int before = sequence;
            ReplaySpecializations();
            ReplayFeedback();
            ReplayPerks();
            ReplayDifficulty();
            string savedGen = generation;
            generation = null;
            pending = false;
            ResetScene();
            Expect(!CanObserveStore() && sceneStatus=="waiting_for_load", "wait until load or new-game hook arms baseline");
            pending = true;
            Expect(CanObserveStore(), "armed new game may prepare baseline");
            pending = false;
            generation = savedGen;
            MarkSceneReady(ShopScene,"當鋪");
            ReplayTravelOptions();
            map.IsInTransit=true;
            Expect(!CanObserveStore() && sceneStatus=="transitioning" && destination=="forest", "first transit keeps selected native destination");
            TransitUI.IsOpen=false;
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
