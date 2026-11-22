using System;
using Newtonsoft.Json.Linq;

namespace WhiteDragon.Ledger
{
    public sealed partial class InventoryObserver
    {
        private void ReplaySaleResume()
        {
            var store = PlayerStore.Instance;
            var ui = NegociationUIManager.Instance;
            var shop = EmporiumEntry.Instance;
            var dialog = new DialogUIManager();
            DialogUIManager.Instance = dialog;
            phase = "OPEN";
            store.currentClientInstance = new StoreClientInstance();
            ObserveVisit();
            // Recreate the slot-4 case; 40 is a replay quote, not an observed game price.
            var bottle = new GameItem { uniqueId=203, identifier="bottled_water", price=40 };
            ids.Clear();
            itemsById.Clear();
            nextId = 24;
            Expect(LocalId(bottle) == 25, "slot-4 bottle number");
            void Tick() { UnityEngine.Time.realtimeSinceStartup++; CheckOrder(); }
            foreach (string scenario in new[]{"speech", "closed", "floor", "swapped", "new_visit"})
            {
                store.playerCash = 276;
                merchant.budget = 102;
                bottle.owned = true;
                bottle.To(shop.showcaseElement);
                int floor = scenario == "floor" ? 45 : 25;
                ui.IsOpen = true;
                var id = Guid.NewGuid().ToString("N");
                var plan = new JObject { ["generation"]=generation, ["revision"]=sequence, ["command_id"]=id,
                    ["visit"]=visitId, ["expires_ms"]=DateTimeOffset.UtcNow.AddSeconds(6).ToUnixTimeMilliseconds(),
                    ["lines"]=new JArray(new JObject {["item_id"]=25, ["min_price"]=floor}),
                    ["stop_same_kind_on_budget"]=true };
                string inbox = "sale-" + generation + ".json";
                string path = "receipt-" + id + ".json";
                int placements = ModHelper.placed.Count;
                if (scenario == "speech")
                {
                    var paper = Item(64002,"newspaper",1);
                    paper.To(shop.frontInvinvElement);
                    QueueFrontPaper(paper,paper.parentInventory,null);
                    paper.To(shop.backInvinvElement);
                    FileCounterPapers();
                    Expect(paper.parentInventory==shop.invElement,"ready to sell after native counter sweep");
                }
                Write(inbox, plan);
                Tick();
                Expect(staged == bottle && ModHelper.placed.Count == placements+1,"placed original bottle exactly once");
                ui.IsOpen = false;
                if (scenario != "closed")
                {
                    dialog.current = new object();
                    dialog.typing = true;
                    Tick();
                    dialog.typing = false;
                    Tick();
                    Expect(!ui.IsOpen && order != null && store.playerCash == 276, "wait for complete line");
                    dialog.isWaitingForManualAdvance = true;
                    Tick();
                    Expect(!dialog.isWaitingForManualAdvance, "turn ordinary page");
                    dialog.choice = true;
                    Tick();
                    Expect(dialog.choice && !ui.IsOpen, "wait for player choice");
                    dialog.choice = false;
                    dialog.current = null;
                }
                Tick();
                Expect(ui.IsOpen && order != null && store.playerCash == 276,"reopen first, then revalidate");
                if (scenario == "swapped") bottle.To(shop.invElement);
                if (scenario == "new_visit") { store.currentClientInstance = new StoreClientInstance(); ObserveVisit(); }
                Tick();
                Tick();
                string saved = ModHelper.ReadModDataFileText(ModId,path);
                var answer = JObject.Parse(saved);
                var item = answer["items"][0];
                if (scenario == "speech" || scenario == "closed")
                    Expect((string)item["status"] == "sold" && (int)item["received"] == 40 &&
                        (int)item["cash_after"] == 316 && (int)answer["remaining_budget"] == 62, "exact cash and budget");
                else if (scenario == "floor")
                    Expect((string)item["status"] == "returned_below_minimum" && bottle.parentInventory == shop.showcaseElement,
                        "same floor and original shelf");
                else Expect((string)item["status"] == (scenario == "swapped" ? "counter_changed" : "buyer_or_ui_changed"),
                    "changed buyer or item stops sale");
                int cash = store.playerCash;
                Write(inbox,plan);
                Tick();
                Expect(ModHelper.ReadModDataFileText(ModId,path) == saved && store.playerCash == cash &&
                    ModHelper.placed.Count == placements+1,"duplicate original order uses identical receipt");
            }
            Console.WriteLine("Sale resume replay passed: speech, closed tablet, price floor, swapped item, visitor and duplicate order.");
        }
    }
}
