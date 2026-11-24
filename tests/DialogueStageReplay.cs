using System;
using Newtonsoft.Json.Linq;

namespace WhiteDragon.Ledger
{
    public sealed partial class InventoryObserver
    {
        private void ReplayDialogueStage()
        {
            var shop = EmporiumEntry.Instance;
            var store = PlayerStore.Instance;
            phase = "OPEN";
            store.storeState = PlayerStore.StoreState.OPEN;
            store.isClientArrived = true;
            var cartel = new StoreClient
            {
                identifier = "cartelHandlerWeekly",
                displayName = "卡特爾接頭人",
                clientIntent = StoreClient.ClientIntent.DIALOGUE
            };
            store.currentClientInstance = new StoreClientInstance { client = cartel };
            ObserveVisit();
            openingLines.Clear();
            openingLines.Add("10份梦尘准备好了吗？");
            openingLines.Add("纯度必须至少达到40%。");
            openingLines.Add("把货放到柜台上。");
            var prevTalk = DialogUIManager.Instance;
            var talk = new DialogUIManager { current = new object(), choice = true };
            DialogUIManager.Instance = talk;
            var option = new DialogChoiceBubble { index = 0, enabled = true };
            option.gameObject.activeInHierarchy = true;
            option.TMP.text = "继续";
            talk.dialogChoiceBubbles.Add(option);
            shop.invWindow.visible = true;
            shop.invWindow.isLocked = false;
            shop.backInvWindow.visible = true;
            shop.backInvWindow.isLocked = false;

            var dust1 = Item(8901, "dream_dust", 1);
            dust1.associatedId = 61;
            dust1.bookTagValue = "street_standard";
            dust1.waterTags.Add("CHEMICAL_PRODUCT_PURITY_TAG");
            var dust2 = Item(8902, "dream_dust", 1);
            dust2.associatedId = 62;
            dust2.bookTagValue = "street_standard";
            dust2.waterTags.Add("CHEMICAL_PRODUCT_PURITY_TAG");

            var viewBefore = CounterView();
            Expect((string)viewBefore["stage"] == "choice_required" &&
                (string)viewBefore["consequence"] == "unknown" &&
                viewBefore["counter_request"] != null &&
                ((JArray)viewBefore["counter_request"]["from_lines"]).Count == 3 &&
                ((JArray)viewBefore["counter_request"]["table_items"]).Count == 0,
                "cartel dialogue choice exposes counter_request with unknown consequence");

            JObject MoveTable(GameItem item, string src, string dst) => MoveCounterStock(new JObject
            {
                ["command_id"] = Guid.NewGuid().ToString("N"),
                ["generation"] = generation,
                ["revision"] = sequence,
                ["visit"] = visitId,
                ["item_id"] = LocalId(item),
                ["source"] = src,
                ["destination"] = dst,
                ["expires_ms"] = long.MaxValue
            }, item, src, dst);

            var m1 = MoveTable(dust1, "inventory", "back");
            var m2 = MoveTable(dust2, "inventory", "back");
            Expect((string)m1["status"] == "moved" && (string)m2["status"] == "moved" &&
                dust1.parentInventory == shop.backInvinvElement && dust2.parentInventory == shop.backInvinvElement,
                "dialogue choice allows placing multiple owned items onto back counter");

            var viewAfter = CounterView();
            Expect((string)viewAfter["stage"] == "choice_required" &&
                ((JArray)viewAfter["dialog_choices"]).Count == 1 &&
                ((JArray)viewAfter["counter_request"]["table_items"]).Count == 2 &&
                (string)viewAfter["counter_request"]["table_items"][0]["kind"] == "dream_dust" &&
                (double)viewAfter["counter_request"]["table_items"][0]["purity"] == 61.0,
                "dialogue stage stays choice_required and counter_request lists placed items");

            var backOut = MoveTable(dust2, "back", "inventory");
            Expect((string)backOut["status"] == "moved" && dust2.parentInventory == shop.invElement,
                "dialogue choice allows retrieving owned item from back counter");

            shop.backInvWindow.isLocked = true;
            var lockedMove = MoveTable(dust2, "inventory", "back");
            Expect((string)lockedMove["status"] == "trade_unavailable" &&
                (string)lockedMove["reason"] == "counter_locked",
                "locked counter refuses dialogue stage move with reason");
            shop.backInvWindow.isLocked = false;

            store.lockAllInventoryFlag = 1;
            var invLockedMove = MoveTable(dust2, "inventory", "back");
            Expect((string)invLockedMove["status"] == "trade_unavailable" &&
                (string)invLockedMove["reason"] == "inventory_locked",
                "locked inventory refuses dialogue stage move with reason");
            store.lockAllInventoryFlag = 0;

            talk.choice = false;
            talk.dialogChoiceBubbles.Clear();
            talk.current = null;
            var nonChoiceView = CounterView();
            Expect(nonChoiceView["counter_request"] == null,
                "counter_request appears only on dialogue choice_required page");

            cartel.clientIntent = StoreClient.ClientIntent.MERCHANT;
            cartel.isClientSelling = false;
            cartel.isBusinessComplete = true;
            var nonDialogueMove = MoveTable(dust2, "inventory", "back");
            Expect((string)nonDialogueMove["status"] == "trade_unavailable",
                "non-dialogue non-ready client still refuses counter move");

            dust1.To(null);
            dust2.To(null);
            openingLines.Clear();
            DialogUIManager.Instance = prevTalk;
            store.currentClientInstance = null;
            ObserveVisit();
        }
    }
}
