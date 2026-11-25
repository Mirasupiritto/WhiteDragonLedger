using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace WhiteDragon.Ledger
{
    public static class TypeHelper { public const string CONTRABAND="contraband"; }
    public static class ContrabandHelper {
        public static bool IsSmugglerBay(GameItem item) => item.identifier == "smuggler_bay" || item.identifier == "smuggler_bay_mini";
        public static bool IsBlockSearchContainer(GameItem item) => IsSmugglerBay(item);
        public static bool IsContraband(GameItem item) => item.IsTag(TypeHelper.CONTRABAND);
    }

    public sealed partial class InventoryObserver
    {
        private readonly Dictionary<int,JObject> known=new Dictionary<int,JObject>();
        private readonly HashSet<GameItem> bottles=new HashSet<GameItem>();

        private void ReplayBagStock()
        {
            var originalShop=EmporiumEntry.Instance;
            var shop=EmporiumEntry.Instance=new EmporiumEntry();
            areas[shop.invElement]="inventory";
            areas[shop.showcaseElement]="showcase";
            var bag=Item(600,"fanny_pack",15);
            var grid=new GameGridInventory(3,3) {owner=bag};
            bag.contentWindow=new PixelWindow {childElement=grid};
            var part=Item(650,"parts",5);
            var existing=Item(651,"fixture",1);
            existing.To(grid);
            loadScans=fullScans=0;
            var baseline=new JArray();
            PopulateStockBaseline(baseline);
            var id=LocalId(part);
            var events=new JArray(new JObject {["op"]="baseline",["items"]=baseline});
            Expect(baseline.Count==3 && (int)known[LocalId(existing)]["parent_id"]==LocalId(bag),"baseline includes closed bag");
            JObject Flush()
            {
                var upserts=new JArray();
                var removed=new JArray();
                PollBagAccess();
                ApplyStockChanges(upserts,removed);
                var ev=new JObject {["op"]="delta",["upsert"]=upserts,["remove"]=removed};
                events.Add(ev);
                return ev;
            }

            part.To(grid);
            changed.Add(part); // root remove callback
            grid.onSlotAddItemLateFunc(part,grid,null);
            Flush();
            Expect(known.Count==3 && (int)known[id]["parent_id"]==LocalId(bag) &&
                (string)known[id]["access"]=="unpack_first","same part filed inside bag");
            bag.To(shop.showcaseElement);
            changed.Add(bag);
            Flush();
            Expect(known.Count==3 && (string)known[id]["area"]=="showcase","bag move relocates descendants");
            part.To(shop.invElement);
            grid.onSlotRemoveItemLateFunc(part,grid,null); // child callback alone
            Flush();
            Expect(known.Count==3 && (string)known[id]["area"]=="inventory" && known[id]["parent_id"]==null &&
                loadScans==1 && fullScans==1,"same part retrieved, exactly one inventory baseline");

            string export=Environment.GetEnvironmentVariable("LEDGER_BAG_REPLAY_FILE");
            if (!string.IsNullOrEmpty(export))
                System.IO.File.WriteAllText(export,new JObject {["events"]=events,["part"]=id,["bag"]=LocalId(bag),["load_scans"]=loadScans}.ToString());

            var pouch=Item(652,"satchel",2);
            var pocket=new GameGridInventory(2,2) {owner=pouch};
            pouch.contentWindow=new PixelWindow {childElement=pocket};
            pouch.To(grid);
            grid.onSlotAddItemLateFunc(pouch,grid,null);
            Flush();
            existing.To(pocket);
            grid.onSlotRemoveItemLateFunc(existing,grid,null);
            pocket.onSlotAddItemLateFunc(existing,pocket,null);
            existing.unitCount=3;
            pocket.onSlotChangeAmountItemFunc(existing,pocket,null);
            Flush();
            var nested=known[LocalId(existing)];
            Expect((int)nested["parent_id"]==LocalId(pouch) && nested["container_path"].Count()==2 &&
                (int)nested["container_path"][0]["id"]==LocalId(bag) && (int)nested["quantity"]==3,
                "two-level bag path and stack count");
            bag.contentWindow.isLocked=true;
            Flush();
            Expect((string)known[LocalId(existing)]["access"]=="container_locked","lock change without slot event");
            bag.To(null); changed.Add(bag);
            Flush();
            Expect(!known.ContainsKey(LocalId(existing)) && grid.onSlotAddItemLateFunc==null,"detach and remove all bag goods on departure");
            DetachBagGrids();
            EmporiumEntry.Instance=originalShop;
            Console.WriteLine("Bag replay passed: baseline, pack, move, unpack, lock and leave; one full scan.");
        }
    }
}
