using System;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using TMPro;

namespace WhiteDragon.Ledger
{
    public partial class EmporiumEntry
    {
        public PixelWindow invWindow=new PixelWindow(), showcaseWindow=new PixelWindow();
        public PixelWindow frontInvWindow = new PixelWindow(), backInvWindow = new PixelWindow(),
            drainInvWindow = new PixelWindow(), faucetWindow = new PixelWindow();
        public GameItem faucet = new GameItem { identifier="faucet" }, drain = new GameItem { identifier="drain" };
    }
    public partial class GameInventory
    {
        public GameItem owner;
        public GameItem GetParentItem() => owner;
    }
    public class GenericConfirmationUIManager
    {
        public static GenericConfirmationUIManager Instance = new GenericConfirmationUIManager();
        public Window panel = new Window();
        public Window confirmButton=new Window {activeInHierarchy=true}, cancelButton=new Window();
        public int clicks;
        public void ConfirmButtonPressed() { clicks++;panel.activeInHierarchy=false; }
        public TextMeshProUGUI bodyTMP = new TextMeshProUGUI { text="mech_portable_purifier_not_enough_charge" };
        public TextMeshProUGUI confirmButtonTMP = new TextMeshProUGUI { text="確認" };
        public TextMeshProUGUI cancelButtonTMP = new TextMeshProUGUI { text="取消" };
    }
    public static class DurabilityHelper {
        public static int GetRealDurability(GameItem item) => item.charge;
        public static int GetMaxDurability(GameItem item) => item.maxCharge;
    }
    public static class WaterHelper {
        public static GameItem GetFilterItem(GameItem item)=>item.filter;
        public static int GetFreeCapacity(GameItem item)=>500000-item.volume;
        public static int[] GetLiquidParts(GameItem item)=>new[]{item.volume-item.alcoholUl, item.alcoholUl};
        public static int[] SplitProportionally(int[] parts,int amount)=>parts; // Replay vessels have room for the full pour.
        public static int GetTotalCapacityML(GameItem item) => 500;
        public static int GetCurrentCapacityUL(GameItem item) => item.volume;
        public static bool IsEmpty(GameItem item) => item == null || !item.IsTag("LIQUID_CONTAINER_TAG") || item.volume <= 0;
    }
    public class ItemBehaviourManager
    {
        public static ItemBehaviourManager current = new ItemBehaviourManager();
        public bool MayTarget(GameItem source, GameItem target) => source.identifier == "water_tablet";
        public bool CanTarget(GameItem source, GameItem target) => source.parentInventory != null;
        public void Target(GameItem source, GameItem target)
        {
            if (MayTarget(source, target)) source.To(null);
        }
    }
    public partial class GameItem
    {
        public GameItem filter;
        public int alcoholUl;
        public int volume, charge, maxCharge = 300, associatedId, waterClicks;
        public readonly HashSet<string> waterTags = new HashSet<string>();
        public bool MaySelectSlotItem() => true;
        public bool CanSelectSlotItem() => true;
        public bool MayTarget(GameItem target) => parentInventory != null;
        public bool CanTarget(GameItem target) => identifier != "blocked";
        public void Target(GameItem target)
        {
            waterClicks++;
            switch (identifier)
            {
                case "module_extractor":
                    target.waterTags.Remove("MODULE_STUCK_TAG");
                    target.mayRemove=true;
                    break;
                case "water_test_strip":
                    waterTags.Add(WaterTestHelper.IS_SPENT);
                    associatedId = target.uniqueId;
                    WaterStripUIManager.Instance.currentWaterContainer = target;
                    WaterStripUIManager.Instance.panel.activeInHierarchy = true;
                    break;
                case "portable_water_purifier":
                    if (charge > 0) charge--;
                    else GenericConfirmationUIManager.Instance.panel.activeInHierarchy = true;
                    break;
                case "bottle":
                    if (target.identifier == "faucet") volume += 50;
                    else if (volume > 0)
                    {
                        if (target.identifier != "drain") target.volume += volume;
                        volume = 0;
                    }
                    break;
                case "water_ration":
                    target.volume += 30;
                    To(null);
                    break;
            }
        }
    }

    public sealed partial class InventoryObserver
    {
        private void ReplayWaterUse()
        {
            phase = "MORNING";
            var shop = EmporiumEntry.Instance;
            var store = PlayerStore.Instance;
            var source = Item(971, "bottle", 1);
            var dest = Item(972, "bottle", 1);
            var strip = Item(973, "water_test_strip", 1);
            strip.waterTags.Add("WATER_TESTING_STRIP_TAG");
            strip.waterTags.Add("CAN_USE_ON_NON_OWNED_TAG");
            stripSupplies.Add(strip);
            source.volume = 100;
            var request = new JObject { ["action"]="water_use", ["generation"]=generation, ["revision"]=sequence,
                ["expires_ms"]=DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds() };
            var receipt = new JObject();
            string Preview(GameItem item, JToken target)
            {
                request["mode"]="preview";
                request["source_id"]=LocalId(item);
                request["target_id"]=target;
                receipt = new JObject();
                string status=ActAtCounter(request,receipt);
                request["use_token"]=receipt["use_token"];
                return status;
            }
            string Apply()
            {
                request["mode"]="apply";
                receipt = new JObject();
                return ActAtCounter(request,receipt);
            }

            Expect(Preview(source, LocalId(dest))=="preview" && source.volume==100 && dest.volume==0, "preview cannot pour");
            source.volume=250000;
            source.alcoholUl=175000;
            var mixture=StockRow(source,"inventory");
            Expect(!(bool)mixture["contains_water"] && mixture["bottle_label"]==null && (double)mixture["water_ml"]==75 &&
                (string)mixture["liquid_main"]["id"]=="alcohol" && !mixture["categories"].ToObject<string[]>().Contains("WATER"),
                "alcohol mixture is not water stock");
            dest.filter=new GameItem {identifier="cloth", charge=2};
            Expect(Preview(source,LocalId(dest))=="preview" && (double)receipt["plan"]["filter"]["removed"][0]["ml"]==1 &&
                (double)receipt["plan"]["filter"]["target_ml_after"]==249 && dest.filter.charge==2,"filter preview leaves durability intact");
            dest.filter=null;
            source.alcoholUl=0;
            source.volume = 90;
            Expect(Apply()=="water_plan_changed" && dest.volume==0, "stale volumes");
            Preview(source,LocalId(dest));
            Expect(Apply()=="transferred" && (int)receipt["source_change_ul"]==-90 && dest.volume==90,"native pour");
            Expect(Apply()=="water_plan_changed" && source.waterClicks==1,"single use");
            Expect(Preview(source,"faucet")=="target_unavailable","hidden tap");
            shop.faucetWindow.visible=true;
            Preview(source,"faucet");
            Expect(Apply()=="transferred" && source.volume==50,"visible tap");
            shop.drainInvWindow.visible=true;
            Preview(source,"drain");
            Expect(Apply()=="transferred" && source.volume==0,"drain");

            var ration = Item(974,"water_ration",1);
            Preview(ration,LocalId(dest));
            Expect(Apply()=="ration_used" && (int)receipt["units_used"]==1 && dest.volume==120,"water ration");
            var tabletBox = Item(977, "water_tablet_container", 1);
            Expect(Preview(tabletBox, LocalId(dest)) == "source_unavailable" &&
                (string)receipt["reason"] == "open_container_first", "tablet container prompts open_container_first");
            var kit = Item(9802,"water_test_container",1);
            Expect(Preview(kit,LocalId(dest))=="source_unavailable" && (string)receipt["required_kind"]=="water_test_strip" &&
                (int)receipt["arguments"]["owner_id"]==LocalId(kit) && (bool)receipt["arguments"]["open_if_closed"],"kit exposes path to individual strip");
            var tablet = Item(975,"water_tablet",1);
            tablet.waterTags.Add("WATER_PURIFICATION_TABLET");
            Preview(tablet,LocalId(dest));
            Expect(Apply()=="tablet_used" && tablet.waterClicks==0 &&
                (bool)receipt["effect_pending"] && !string.IsNullOrEmpty((string)receipt["effect_pending_reason"]),
                "tablet_used reports effect_pending explanation");
            var purifier = Item(976,"portable_water_purifier",1);
            purifier.charge=15;
            Preview(purifier,LocalId(dest));
            Expect(Apply()=="purifier_used" && (int)receipt["charge_used"]==1 &&
                (int)receipt["charge_remaining"]==14 && Math.Abs((double)receipt["liters_remaining"] - 1.4) < 0.01,
                "purifier_used returns liters_remaining");
            purifier.charge = 0;
            Preview(purifier,LocalId(dest));
            Expect(Apply()=="purifier_notice" && (string)receipt["notice"]=="mech_portable_purifier_not_enough_charge","native notice");
            GenericConfirmationUIManager.Instance.panel.activeInHierarchy=false;

            phase="OPEN";
            store.currentClientInstance=new StoreClientInstance();
            ObserveVisit();
            store.isClientArrived=true;
            DialogUIManager.Instance=new DialogUIManager();
            NegociationUIManager.Instance.IsOpen=true;
            store.currentClientInstance.client.isIntroduced=true;
            dest.owned=false;
            dest.To(shop.frontInvinvElement);
            Expect(Preview(strip,LocalId(dest))=="offered_water_unavailable","hidden merchant bottle");
            shop.frontInvWindow.visible=true;
            Expect(Preview(purifier,LocalId(dest))=="offered_water_unavailable","never purify merchant goods");
            int unused=AvailableStrips();
            Expect(Preview(strip,LocalId(dest))=="preview" && AvailableStrips()==unused,"merchant strip preview");
            store.currentClientInstance=new StoreClientInstance();
            ObserveVisit();
            Expect(Apply()=="water_plan_changed","new visit");
            Preview(strip,LocalId(dest));
            Expect(Apply()=="sample_taken" && (int)receipt["panel"]["bottle_id"]==LocalId(dest) &&
                (int)receipt["available_strips"]==unused-1 && !(bool)receipt["panel"]["can_match"],"sample identity and remaining supply");
            Expect(Preview(strip,LocalId(dest))=="source_unavailable","used strip cannot sample again");

            // P2-10: low-risk apply_now works in one step for faucet, purify, test; rejects high-risk drain/pour/tablet/ration.
            phase = "MORNING";
            store.currentClientInstance = null;
            ObserveVisit();
            source.volume = 0;
            purifier.charge = 10;
            var quickBottle = Item(978, "bottle", 1);
            quickBottle.volume = 0;
            var quickStrip = Item(979, "water_test_strip", 1);
            quickStrip.waterTags.Add("WATER_TESTING_STRIP_TAG");
            stripSupplies.Add(quickStrip);
            string ApplyNow(GameItem item, JToken target)
            {
                request.Remove("use_token");
                request["mode"] = "apply_now";
                request["source_id"] = LocalId(item);
                request["target_id"] = target;
                receipt = new JObject();
                return ActAtCounter(request, receipt);
            }
            Expect(ApplyNow(quickBottle, "faucet") == "transferred" && quickBottle.volume == 50,
                "apply_now fills owned bottle directly from faucet");
            Expect(ApplyNow(purifier, LocalId(quickBottle)) == "purifier_used" && (int)receipt["charge_remaining"] == 9,
                "apply_now purifies bottle directly");
            Expect(ApplyNow(quickStrip, LocalId(quickBottle)) == "sample_taken",
                "apply_now tests bottle directly with strip");
            Expect(ApplyNow(quickBottle, "drain") == "preview_required" && quickBottle.volume == 50,
                "apply_now rejects high-risk drain with preview_required");
            Expect(ApplyNow(quickBottle, LocalId(source)) == "preview_required" && source.volume == 0,
                "apply_now rejects bottle-to-bottle pour with preview_required");
            Console.WriteLine("Water use replay passed.");
        }
    }
}
