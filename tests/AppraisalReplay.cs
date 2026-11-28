using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace WhiteDragon.Ledger
{
    public class Dialogue
    {
        public string plainText;
        public RichTextBuilder text;
        public Dialogue nextDialogue;
    }

    public partial class StoreClient
    {
        public int clientImpatience;
    }

    public partial class PlayerStore
    {
        public bool isAppraisalIntroduced;
        public double appraisalProgress;
    }

    public static partial class AppraisalRankHelper
    {
        public class RankData { public float progressToPromote = 100f; }
        public static RankData Current = new RankData();
        public static int ReplayBaseFee = 35;
        public static bool ReplayCanProgress = true;
        public static string GetRankName(int rank) => rank == 0 ? "見習鑑定師" : rank == 1 ? "鑑定師" : rank == 2 ? "高級鑑定師" : "大師鑑定師";
        public static int GetBaseFee() => ReplayBaseFee;
        public static bool CanProgress() => ReplayCanProgress;
    }

    public class InspectionUIManager
    {
        public static InspectionUIManager Instance = new InspectionUIManager();
        public GameItem currentInspectingItem;
        public int closeCalls;
        public void CloseUI() { currentInspectingItem = null; closeCalls++; }
    }

    public static class AppraisalServiceHelper
    {
        public static Action<GameItem, int> ReplayEstimate;
        public static Func<Dialogue> ReplayTooHighDialog;
        public static Func<Dialogue> ReplayTooLowDialog;
        public static void OnEstimate(GameItem item, int value) => ReplayEstimate?.Invoke(item, value);
        public static Dialogue GetTooHighDialog() => ReplayTooHighDialog?.Invoke();
        public static Dialogue GetTooLowDialog() => ReplayTooLowDialog?.Invoke();
    }

    public static class AppraisalHelper
    {
        public static Func<GameItem, int> ReplayCorrectValue;
        public static int GetCorrectBaseValue(GameItem item) => ReplayCorrectValue != null ? ReplayCorrectValue(item) : 250;
    }

    public sealed partial class InventoryObserver
    {
        private void ReplayAppraisal()
        {
            var shop = EmporiumEntry.Instance;
            var store = PlayerStore.Instance;
            shop.frontInvinvElement.childItems.Clear();
            shop.backInvinvElement.childItems.Clear();

            // T3: not introduced omits appraisal_guild; introduced attaches rank/progress/unlocked_types
            store.isAppraisalIntroduced = false;
            var statusBefore = DayView();
            Expect(statusBefore["appraisal_guild"] == null, "T3 unintroduced shop omits appraisal_guild");

            store.isAppraisalIntroduced = true;
            store.appraisalProgress = 42.5;
            AppraisalRankHelper.ReplayRank = 1;
            AppraisalRankHelper.ReplayBaseFee = 35;
            AppraisalRankHelper.Current.progressToPromote = 100f;
            AppraisalItemRegistry.ReplayUnlocked = new[] {
                new AppraisalItemType { id = "hand", name = "仿生機械手" },
                new AppraisalItemType { id = "ribwich", name = "肋排三明治" }
            };
            AppraisalItemRegistry.ReplayBlocked = new HashSet<string> { "hand" };
            var statusAfter = DayView();
            var guild = (JObject)statusAfter["appraisal_guild"];
            Expect(guild != null && (int)guild["rank"] == 1 && (string)guild["rank_name"] == "鑑定師" &&
                (int)guild["base_fee"] == 35 && Math.Abs((double)guild["progress"] - 42.5) < 0.01 &&
                (bool)guild["unlocked_types"][0]["blocked"] && !(bool)guild["unlocked_types"][1]["blocked"],
                "T3 introduced shop includes appraisal_guild summary");

            // T1: appraisal client identification and counter item list (without GetCorrectBaseValue)
            phase = "OPEN";
            store.storeState = PlayerStore.StoreState.OPEN;
            store.currentClientInstance = new StoreClientInstance
            {
                client = new StoreClient
                {
                    identifier = "scavAppraisalRandom",
                    displayName = "委託客",
                    clientIntent = StoreClient.ClientIntent.APPRAISAL_SERVICE,
                    isIntroduced = true
                }
            };
            store.isClientArrived = true;
            ObserveVisit();
            DialogUIManager.Instance = new DialogUIManager();

            var app1 = Item(9501, "hand", 0);
            app1.owned = false;
            app1.name = "仿生機械手A";
            app1.waterTags.Add("APPRAISAL_ITEM");
            app1.scratchTagReader = key => key == "APPRAISAL_DEFAULT_VALUE" ? new GameItem { associatedId = 100 } : app1;
            app1.To(shop.frontInvinvElement);

            var app2 = Item(9502, "hand", 0);
            app2.owned = false;
            app2.name = "仿生機械手B";
            app2.waterTags.Add("APPRAISAL_ITEM");
            app2.scratchTagReader = key => key == "APPRAISAL_DEFAULT_VALUE" ? new GameItem { associatedId = 100 } : app2;
            app2.To(shop.backInvinvElement);
            FlushChanges();

            var view = CounterView();
            Expect((string)view["client_role"] == "appraisal" && (string)view["stage"] == "appraisal" &&
                (int)view["appraisal"]["remaining_count"] == 2 &&
                (int)view["appraisal"]["items"][0]["default_estimate"] == 100 &&
                (string)view["next_action"] == "player_visual_check",
                "T1 appraisal client exposes client_role=appraisal, items, default_estimate, and player_visual_check");

            // T2-4: invalid estimate, invalid item_id, customer_replaced, wrong_client_intent
            var badEst = new JObject();
            Expect(ActAtCounter(new JObject {
                ["action"] = "appraisal", ["generation"] = generation, ["revision"] = sequence,
                ["visit"] = visitId, ["item_id"] = LocalId(app1), ["estimate"] = 0, ["expires_ms"] = long.MaxValue
            }, badEst) == "invalid_request" && (string)badEst["reason"] == "invalid_estimate",
                "T2 non-positive estimate rejected");

            var badItem = new JObject();
            Expect(ActAtCounter(new JObject {
                ["action"] = "appraisal", ["generation"] = generation, ["revision"] = sequence,
                ["visit"] = visitId, ["item_id"] = 999999, ["estimate"] = 120, ["expires_ms"] = long.MaxValue
            }, badItem) == "item_unavailable" && (string)badItem["reason"] == "item_not_found",
                "T2 missing item_id returns item_unavailable");

            var wrongVisit = new JObject();
            Expect(ActAtCounter(new JObject {
                ["action"] = "appraisal", ["generation"] = generation, ["revision"] = sequence,
                ["visit"] = "00000000000000000000000000000000", ["item_id"] = LocalId(app1), ["estimate"] = 120, ["expires_ms"] = long.MaxValue
            }, wrongVisit) == "customer_replaced",
                "T2 mismatched visit returns customer_replaced");

            // T2-2: rejected (impatience += 49, item stays on counter, line returned)
            AppraisalServiceHelper.ReplayEstimate = (it, val) => {
                merchant.clientImpatience += 49;
                DialogUIManager.Instance.clientBubble = new Bubble { textfield = new Label { text = "這也太低了吧！" } };
            };
            var rejRes = new JObject();
            Expect(ActAtCounter(new JObject {
                ["action"] = "appraisal", ["generation"] = generation, ["revision"] = sequence,
                ["visit"] = visitId, ["item_id"] = LocalId(app1), ["estimate"] = 50, ["expires_ms"] = long.MaxValue
            }, rejRes) == "rejected" && (int)rejRes["cash_delta"] == 0 &&
                (int)rejRes["impatience_delta"] == 49 && (string)rejRes["line"] == "這也太低了吧！" &&
                app1.parentInventory == shop.frontInvinvElement,
                "T2 rejected returns line, impatience_delta and keeps item");

            // T2-1: accepted (item destroyed, cash_delta > 0, slip_text captured, InspectionUI closed)
            InspectionUIManager.Instance.currentInspectingItem = app1;
            int cashBefore = store.playerCash;
            AppraisalServiceHelper.ReplayEstimate = (it, val) => {
                store.playerCash += 55;
                var slip = Item(9599, "slip", 0);
                slip.owned = true;
                slip.scratchTagReader = key => key == "SLIP_BODY" ? new GameItem { bookTagValue = "完美鑑定 (98%)\n估價: 150\n真實價值: 150\n鑑定費: 55" } : slip;
                slip.To(shop.backInvinvElement);
                it.To(null);
            };
            var acc1 = new JObject();
            Expect(ActAtCounter(new JObject {
                ["action"] = "appraisal", ["generation"] = generation, ["revision"] = sequence,
                ["visit"] = visitId, ["item_id"] = LocalId(app1), ["estimate"] = 150, ["expires_ms"] = long.MaxValue
            }, acc1) == "accepted" && (int)acc1["cash_delta"] == 55 && (int)acc1["remaining_count"] == 1 &&
                !(bool)acc1["all_done"] && ((string)acc1["slip_text"]).Contains("完美鑑定") &&
                InspectionUIManager.Instance.closeCalls > 0,
                "T2 accepted returns cash_delta, remaining_count, slip_text and closes inspection UI");

            // Accept last item -> all_done = true
            var acc2 = new JObject();
            Expect(ActAtCounter(new JObject {
                ["action"] = "appraisal", ["generation"] = generation, ["revision"] = sequence,
                ["visit"] = visitId, ["item_id"] = LocalId(app2), ["estimate"] = 150, ["expires_ms"] = long.MaxValue
            }, acc2) == "accepted" && (int)acc2["remaining_count"] == 0 && (bool)acc2["all_done"],
                "T2 final accepted item sets all_done=true");

            // T2-3: walked_out when impatience >= 100
            var app3 = Item(9503, "hand", 0);
            app3.owned = false;
            app3.waterTags.Add("APPRAISAL_ITEM");
            app3.To(shop.frontInvinvElement);
            FlushChanges();
            AppraisalServiceHelper.ReplayEstimate = (it, val) => {
                merchant.clientImpatience = 100;
                DialogUIManager.Instance.clientBubble = new Bubble { textfield = new Label { text = "我不奉陪了！" } };
            };
            var walkRes = new JObject();
            Expect(ActAtCounter(new JObject {
                ["action"] = "appraisal", ["generation"] = generation, ["revision"] = sequence,
                ["visit"] = visitId, ["item_id"] = LocalId(app3), ["estimate"] = 999, ["expires_ms"] = long.MaxValue
            }, walkRes) == "walked_out" && (string)walkRes["reason"] == "patience_exhausted" &&
                (string)walkRes["line"] == "我不奉陪了！",
                "T2 walked_out returns patience_exhausted and line");

            // Wrong client intent
            merchant.clientIntent = StoreClient.ClientIntent.MERCHANT;
            var wrongIntent = new JObject();
            Expect(ActAtCounter(new JObject {
                ["action"] = "appraisal", ["generation"] = generation, ["revision"] = sequence,
                ["visit"] = visitId, ["item_id"] = LocalId(app3), ["estimate"] = 100, ["expires_ms"] = long.MaxValue
            }, wrongIntent) == "wrong_client_intent" && (string)wrongIntent["reason"] == "not_appraisal_service",
                "T2 non-appraisal client returns wrong_client_intent");

            // U2: AppraisalAssistAction wrong_client_intent when not APPRAISAL_SERVICE
            var assistWrong = new JObject();
            Expect(ActAtCounter(new JObject {
                ["action"] = "appraisal_assist", ["generation"] = generation, ["revision"] = sequence,
                ["visit"] = visitId, ["expires_ms"] = long.MaxValue
            }, assistWrong) == "wrong_client_intent", "U2 appraisal_assist rejects non-appraisal client");

            // U1 & U2: hand, ribwich, plasma_fuel visual_features, next_action, and AppraisalAssistAction
            shop.frontInvinvElement.childItems.Clear();
            shop.backInvinvElement.childItems.Clear();
            merchant.clientIntent = StoreClient.ClientIntent.APPRAISAL_SERVICE;
            var uHand = Item(9601, "hand", 0);
            uHand.owned = false;
            uHand.waterTags.Add("APPRAISAL_ITEM");
            uHand.waterTags.Add("HAND_TAG");
            uHand.scratchTagReader = key =>
                key == "HAND_MATERIAL" ? new GameItem { bookTagValue = "GOLD_PLATED" } :
                key == "HAND_INTERFACE" ? new GameItem { bookTagValue = "NEURAL" } :
                key == "HAND_INTERFACE_MARKING" ? new GameItem { bookTagValue = "FULL" } :
                key == "HAND_CONDITION" ? new GameItem { bookTagValue = "NONE" } :
                key == "HAND_ADDON_ARMORED" ? new GameItem { bookTagValue = "ARMORED" } :
                key == "HAND_ADDON_BLADED" ? new GameItem { bookTagValue = "NONE" } :
                key == "APPRAISAL_DEFAULT_VALUE" ? new GameItem { associatedId = 100 } : uHand;
            uHand.To(shop.frontInvinvElement);

            var uRib = Item(9602, "ribwich", 0);
            uRib.owned = false;
            uRib.waterTags.Add("APPRAISAL_ITEM");
            uRib.waterTags.Add("RIBWICH_TAG");
            uRib.scratchTagReader = key =>
                key == "RIBWICH_WRAP_TAG" ? new GameItem { associatedId = 0 } :
                key == "RIBWICH_PICKLES_TAG" ? new GameItem { associatedId = 1 } :
                key == "RIBWICH_MEAT_TAG" ? new GameItem { associatedId = 1 } :
                key == "RIBWICH_BUN_TAG" ? new GameItem { associatedId = 0 } :
                key == "RIBWICH_DAVRE_LOGO_TAG" ? new GameItem { associatedId = 0 } :
                key == "RIBWICH_RIBWICH_LOGO_TAG" ? new GameItem { associatedId = 2 } :
                key == "RIBWICH_PRODUCTION_YEAR_TAG" ? new GameItem { associatedId = 427 } :
                key == "RIBWICH_PRODUCTION_MONTH_TAG" ? new GameItem { associatedId = 1 } :
                key == "RIBWICH_FACILITY_NUMBER_TAG" ? new GameItem { associatedId = 5 } :
                key == "APPRAISAL_DEFAULT_VALUE" ? new GameItem { associatedId = 150 } : uRib;
            uRib.To(shop.frontInvinvElement);

            var uPlasma = Item(9603, "plasma_fuel", 0);
            uPlasma.owned = false;
            uPlasma.waterTags.Add("APPRAISAL_ITEM");
            uPlasma.waterTags.Add("PLASMA_CELL_TAG");
            uPlasma.scratchTagReader = key =>
                key == "PLASMA_CASING" ? new GameItem { bookTagValue = "STANDARD" } :
                key == "PLASMA_CASING_CONDITION_CRACK" ? new GameItem { bookTagValue = "NONE" } :
                key == "PLASMA_CASING_CONDITION_WEAR" ? new GameItem { bookTagValue = "WEAR" } :
                key == "PLASMA_CASING_CONDITION_UNCOVERED" ? new GameItem { bookTagValue = "NONE" } :
                key == "PLASMA_CASING_CONDITION_REINFORCED" ? new GameItem { bookTagValue = "REINFORCED" } :
                key == "PLASMA_SERIAL" ? new GameItem { bookTagValue = "SN-01" } :
                key == "PLASMA_COLOR" ? new GameItem { bookTagValue = "HIGH_QUALITY" } :
                key == "PLASMA_SEAL" ? new GameItem { bookTagValue = "IGNITION_LAB" } :
                key == "APPRAISAL_DEFAULT_VALUE" ? new GameItem { associatedId = 300 } : uPlasma;
            uPlasma.To(shop.backInvinvElement);
            FlushChanges();

            var uView = CounterView();
            Expect((string)uView["next_action"] == "appraise_from_features" &&
                (string)uView["appraisal"]["items"][0]["visual_features"]["type"] == "hand" &&
                (string)uView["appraisal"]["items"][0]["visual_features"]["material"] == "GOLD_PLATED" &&
                (bool)uView["appraisal"]["items"][0]["visual_features"]["addon_armored"] &&
                !(bool)uView["appraisal"]["items"][0]["visual_features"]["addon_bladed"] &&
                (string)uView["appraisal"]["items"][1]["visual_features"]["type"] == "ribwich" &&
                (string)uView["appraisal"]["items"][1]["visual_features"]["wrap"] == "sealed" &&
                (bool)uView["appraisal"]["items"][1]["visual_features"]["davre_logo_valid"] &&
                !(bool)uView["appraisal"]["items"][1]["visual_features"]["ribwich_logo_valid"] &&
                (int)uView["appraisal"]["items"][1]["visual_features"]["facility_number"] == 5 &&
                (string)uView["appraisal"]["items"][2]["visual_features"]["type"] == "plasma_fuel" &&
                (string)uView["appraisal"]["items"][2]["visual_features"]["seal"] == "IGNITION_LAB" &&
                uView["appraisal"]["items"][0]["recommended_estimate"] == null,
                "U1 visual_features populated for hand/ribwich/plasma_fuel with appraise_from_features and no hidden value");

            AppraisalHelper.ReplayCorrectValue = it => it == uHand ? 470 : it == uRib ? 40 : 280;
            var assistOk = new JObject();
            Expect(ActAtCounter(new JObject {
                ["action"] = "appraisal_assist", ["generation"] = generation, ["revision"] = sequence,
                ["visit"] = visitId, ["expires_ms"] = long.MaxValue
            }, assistOk) == "observed" && (string)assistOk["source"] == "appraisal_assist" &&
                (int)assistOk["items"][0]["recommended_estimate"] == 470 &&
                (int)assistOk["items"][1]["recommended_estimate"] == 40 &&
                (int)assistOk["items"][2]["recommended_estimate"] == 280,
                "U2 appraisal_assist returns recommended_estimate for all counter items");

            var assistReplaced = new JObject();
            Expect(ActAtCounter(new JObject {
                ["action"] = "appraisal_assist", ["generation"] = generation, ["revision"] = sequence,
                ["visit"] = "00000000000000000000000000000000", ["expires_ms"] = long.MaxValue
            }, assistReplaced) == "customer_replaced", "U2 appraisal_assist returns customer_replaced on visit mismatch");

            var uUnknown = Item(9604, "mystery_relic", 0);
            uUnknown.owned = false;
            uUnknown.waterTags.Add("APPRAISAL_ITEM");
            uUnknown.To(shop.frontInvinvElement);
            FlushChanges();
            var uUnknownView = CounterView();
            Expect((string)uUnknownView["next_action"] == "player_visual_check" &&
                uUnknownView["appraisal"]["items"][2]["visual_features"] == null,
                "U1 unknown appraisal species falls back to player_visual_check");
            AppraisalHelper.ReplayCorrectValue = null;
            ModHelper.sortConfig.appraisalEntry.Value = false;

            AppraisalServiceHelper.ReplayEstimate = null;
            shop.frontInvinvElement.childItems.Clear();
            shop.backInvinvElement.childItems.Clear();
            store.currentClientInstance = null;
            store.isAppraisalIntroduced = false;
            Console.WriteLine("Appraisal replay passed.");
        }
    }
}
