import json
import os
import sqlite3
import sys
import tempfile
from contextlib import closing
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "mcp"))
from shopdesk import machines, purchase_lots, sell_order, summarize


class ShopDeskReplay(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory(dir=os.environ.get("LEDGER_TEST_TEMP"))
        self.addCleanup(temp.cleanup)
        self.folder = Path(temp.name)
        self.db = self.folder / "account.sqlite3"
        self.gen = "a"*32
        self.head = dict(status="observed",desk_protocol=1,generation=self.gen,
                         revision=1,slot=4,load_scans=1,full_scans=1,phase="MORNING")
        self.put("head.json", self.head)
        self.members = [dict(key=i,name="脱水纯水",kind="dry_water") for i in range(1,5)]
        self.lot = dict(lot_id="lot",supplier="乔治奥",supplier_id="georgio",
                        day=10,paid=9,members=self.members)

    def put(self, filename, obj):
        (self.folder/filename).write_text(json.dumps(obj),encoding="utf-8")

    def test_partial_and_complete_through_sqlite(self):
        self.members[0]["sale"] = 10
        def publish():
            self.put(f"lots-{self.gen}.json",dict(slot=4,generation=self.gen,lots=[self.lot]))
        publish()
        self.put(f"{self.gen}-1.json",dict(op="baseline",items=[
            dict(id=i+10, name="脱水纯水",kind="dry_water",area="inventory",quantity=1,owned=True,save_id=i)
            for i in [2,3,4]]))
        # Migrate an account created by 0.2 before reading the new native identifiers.
        with closing(sqlite3.connect(self.db)) as db, db:
            db.execute("CREATE TABLE stock (id INTEGER PRIMARY KEY, name TEXT, kind TEXT, area TEXT, owned INTEGER, quantity INTEGER)")
        supplier = "喬治奧" if os.name=="nt" else "乔治奥"
        product = "脫水純水" if os.name=="nt" else "脱水纯水"
        lot=purchase_lots(self.folder,self.db,supplier,product)["lots"][0]
        self.assertEqual((lot["paid"],lot["recovered"],lot["in_stock"]),(9,10,3))
        self.assertNotIn("gross_profit",lot)
        for member in self.members: member["sale"]=10
        publish()
        self.assertEqual(purchase_lots(self.folder,self.db,lot_id="lot")["lots"][0]["gross_profit"],31)
        self.assertEqual(purchase_lots(self.folder,self.db,supplier="someone else")["lots"],[])

    def test_missing_goods_and_unconfirmed_payment(self):
        self.lot["paid"]=None
        row=summarize(self.lot,{2,3,4})
        self.assertEqual((row["in_stock"],row["unlocated"],row["recovered"]),(3,1,0))
        for member in self.members: member["sale"]=10
        self.assertNotIn("gross_profit",summarize(self.lot,set()))

    def test_pending_sale_can_be_read_with_original_revision(self):
        from uuid import NAMESPACE_URL, uuid5
        from unittest.mock import patch
        visit = "d"*32
        plan = dict(lines=[dict(item_id=25, min_price=25)], generation=self.gen, revision=7,
                    visit=visit, stop_same_kind_on_budget=True)
        token = uuid5(NAMESPACE_URL, "white-dragon/sell-order/"+json.dumps(plan, sort_keys=True)).hex
        waiting = dict(command_id=token, generation=self.gen, visit=visit, revision=8,
                       item_id=25, min_price=25, status="sale_pending", stage="speaking")
        self.put(f"sale-progress-{self.gen}.json", waiting)
        snap = dict(status="observed", phase="OPEN", generation=self.gen, revision=8, customer=dict(visit=visit))
        with patch("shopdesk.buyer", return_value=snap), patch("shopdesk.exchange") as send:
            self.assertEqual(sell_order(self.folder, **plan), waiting)
            self.assertEqual(sell_order(self.folder, **dict(plan, lines=[dict(item_id=25, min_price=1)]))["status"], "stale_order")
            done = dict(waiting, status="finished", remaining_budget=62, items=[dict(item_id=25, received=40)])
            self.put(f"receipt-{token}.json", done)
            self.assertEqual(sell_order(self.folder, **plan), done)
            send.assert_not_called()

    def test_morning_keeps_unknown_water_unknown(self):
        machine=dict(machine_key=90,bottle_key=301,bottle_id=26,can_remove_bottle=True,inventory_has_room=True,
                     can_power=False,battery_present=True,energy_kj=0)
        snap=dict(generation=self.gen,revision=1,slot=4,phase="MORNING",day=11,customer=None,
                  available_strips=1,machines=[machine],last_night=[dict(machine,bottle_id=15)])
        self.put(f"desk-{self.gen}.json",snap)
        report=machines(self.folder)
        row=report["machines"][0]
        self.assertEqual((report["strip_summary"],row["available_strips"]),("全店可用試紙剩 1 張",1))
        self.assertTrue(row["same_bottle_as_last_night"] and row["ready_to_take_and_test"])
        self.assertFalse(row["can_power"])
        self.put(f"desk-{self.gen}.json", dict(snap, phase="OPEN"))
        open_row=machines(self.folder)["machines"][0]
        self.assertFalse(open_row["can_remove_to_inventory"] or open_row["ready_to_take_and_test"])
        machine["bottle_key"]=302
        snap["available_strips"]=0
        self.put(f"desk-{self.gen}.json",snap)
        report=machines(self.folder)
        row=report["machines"][0]
        self.assertEqual((report["strip_summary"],row["available_strips"]),("全店可用試紙剩 0 張",0))
        self.assertFalse(row["same_bottle_as_last_night"] or row["ready_to_take_and_test"])
        self.assertFalse(any("quality" in k or "purity" in k for k in row))
        self.assertEqual(sell_order(self.folder,[dict(item_id=26,min_price=10)],self.gen,1,"d"*32,True)["status"],"shop_closed")
        self.assertFalse(any(self.folder.glob("sale-*")))
