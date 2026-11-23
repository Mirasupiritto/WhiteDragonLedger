import json
import os
import sqlite3
from contextlib import closing
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "mcp"))
from ledger import lookup


class LedgerReplay(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(dir=os.environ.get("LEDGER_TEST_TEMP"))
        self.addCleanup(self.temp.cleanup)
        self.folder = Path(self.temp.name)
        self.db = self.folder / "account.sqlite3"
        self.generation = "a" * 32
        self.bottle = dict(id=7, name="水瓶", kind="water", area="inventory", owned=True, quantity=1)

    def put(self, name, value):
        (self.folder / name).write_text(json.dumps(value), encoding="utf-8")

    def event(self, seq, **value):
        self.put(f"{self.generation}-{seq}.json", value)

    def head(self, seq):
        self.put("head.json", dict(status="observed", slot=3, phase="CLOSED",
            generation=self.generation, revision=seq, full_scans=1, load_scans=1))

    def read(self, **filters):
        return lookup(self.folder, self.db, **filters)

    def test_queries_and_move(self):
        self.assertEqual(self.read()["status"], "waiting_for_load")
        self.event(1, op="baseline", items=[self.bottle, dict(self.bottle, id=8, owned=False)])
        self.head(1)
        first = self.read(name="水")
        self.assertEqual([x["id"] for x in first["items"]], [7, 8])
        self.assertEqual(self.read(area="showcase")["items"], [])
        self.assertEqual(self.read(owned=True)["applied_records"], 0)
        self.event(2, op="delta", upsert=[], remove=[7])
        self.event(3, op="delta", upsert=[dict(self.bottle, area="showcase")], remove=[])
        self.head(3)
        moved = self.read(area="showcase")
        self.assertEqual(moved["applied_records"], 2)
        self.assertEqual(moved["items"][0]["id"], 7)
        self.assertEqual(len(self.read(area="inventory")["items"]), 1)

    def test_label_change_at_same_location(self):
        self.event(1, op="baseline", items=[dict(self.bottle, bottle_label="鐵鏽水")])
        self.head(1)
        self.assertEqual(self.read()["items"][0]["bottle_label"], "鐵鏽水")
        self.event(2, op="delta", upsert=[dict(self.bottle, bottle_label="新瓶貼")], remove=[])
        self.head(2)
        receipt = self.read()
        self.assertEqual(receipt["items"], [dict(self.bottle, bottle_label="新瓶貼")])
        self.assertEqual(receipt["load_scans"], 1)

    def test_pick_by_label_and_batch_ids_across_a_reload(self):
        self.event(1, op="baseline", items=[dict(self.bottle, bottle_label="基础水"),
            dict(self.bottle, id=8, owned=False, bottle_label="基础水")])
        self.head(1)
        pick = self.read(stock_query=dict(bottle_label="基礎水" if os.name=="nt" else "基础水", owned=True))
        self.assertEqual([x["id"] for x in pick["items"]], [7])
        self.assertEqual(pick["load_scans"], 1)
        self.assertEqual(set(pick), {"items","slot","generation","revision","status","load_scans"})
        batch = dict(item_ids=[8, 41, 7, 8], generation=self.generation)
        self.assertEqual(self.read(stock_query=batch)["missing_ids"], [41])
        self.assertEqual([x["id"] for x in self.read(stock_query=batch)["items"]], [8, 7])
        self.assertEqual(self.read(stock_query={"categories":["FOOD"]})["status"], "reload_updated_mod")
        self.event(2, op="delta", remove=[8], upsert=[dict(self.bottle, area="showcase", bottle_label="铁锈水")])
        self.head(2)
        self.assertEqual(self.read(stock_query=batch)["missing_ids"], [8, 41])
        self.assertEqual(self.read(stock_query=dict(bottle_label="基礎水"))["items"], [])
        self.assertEqual(self.read(stock_query=dict(bottle_label="鐵鏽水" if os.name=="nt" else "铁锈水"))["items"][0]["area"], "showcase")
        self.generation = "c"*32
        self.event(1, op="baseline", items=[dict(self.bottle, id=7)])
        self.head(1)
        self.assertEqual(self.read(stock_query=batch)["status"], "stale_generation")

    def test_discover_tags_and_observed_bottle_labels(self):
        catalog = {"FOOD": "食物", "DORMANT": "配件", "MODULE_A": "模组", "MODULE_B": "模组"}
        self.put(self.generation+"-stock-categories.json", catalog)
        self.event(1, op="baseline", items=[
            dict(self.bottle, categories=["MODULE_A","Mod.Custom"], bottle_label="基础水"),
            dict(self.bottle, id=8, categories=["MODULE_B"])])
        self.head(1)
        h = json.loads((self.folder/"head.json").read_text())
        self.put("head.json", dict(h, stock_categories_protocol=1))
        def listing(**query):
            return self.read(stock_query=dict(list_tags=True, **query))
        result = listing()
        self.assertEqual(len(result["categories"]),5)
        self.assertEqual(result["bottle_labels"],["基础水"])
        self.assertEqual([r["code"] for r in listing(search="食品")["categories"]],["FOOD"])
        self.assertEqual(listing(search="配件",in_stock_only=True)["categories"],[])
        self.assertEqual(listing(search="水")["bottle_labels"],["基础水"])
        def pick(**query):
            return self.read(stock_query=query)
        self.assertEqual([i["id"] for i in pick(categories=["模组"])["items"]],[7,8])
        self.assertEqual([i["id"] for i in pick(categories=["mod.custom"])["items"]],[7])
        self.assertEqual(pick(categories=["DORMANT"])["status"],"observed")
        self.assertEqual(pick(exclude_categories=["typo"])["status"],"unknown_categories")
        self.assertEqual(pick(exclude_categories=["ALL_TYPE"])["items"],[])
        self.event(2, op="delta", remove=[7], upsert=[])
        self.put("head.json", dict(h, revision=2, stock_categories_protocol=1))
        self.assertEqual(listing()["bottle_labels"],[])
        self.assertEqual([r["code"] for r in listing(in_stock_only=True)["categories"]],["MODULE_B"])
        self.assertEqual(listing()["load_scans"],1)

    def test_filter_native_categories_and_exclusions(self):
        def row(i, kind, tags, illegal=False):
            return dict(self.bottle, id=i, kind=kind, categories=tags, contraband=illegal)
        self.event(1, op="baseline", items=[row(1,"apple",["FOOD"]),row(2,"cloth",["MATERIAL","HOUSEHOLD_GOOD"]),
            row(3,"tin",["HOUSEHOLD_GOOD"]), row(4,"paper",["DOCUMENT"],True)])
        self.put(self.generation+"-stock-categories.json", {"HOUSEHOLD_GOOD":"生活用品"})
        self.head(1)
        h = json.loads((self.folder/"head.json").read_text())
        self.put("head.json", dict(h, stock_categories_protocol=1))
        def ids(**query):
            return [item["id"] for item in self.read(stock_query=query)["items"]]
        self.assertEqual(ids(categories=["食品","生活用品"], exclude_categories=["材料"]),[1,3])
        self.assertEqual(ids(categories=["MATERIAL"], kinds=["paper"], exclude_kinds=["cloth"]), [4])
        self.assertEqual(ids(exclude_categories=["日用品"]), [1,4])
        self.assertEqual(ids(categories=["ALL_TYPE_EXCEPT_CONTRABAND"]), [1,2,3])
        self.assertEqual(ids(categories=["food"], exclude_categories=["food"]), [])
        self.assertNotIn("categories",self.read()["items"][0])
        with closing(sqlite3.connect(self.db)) as old_client, old_client:
            old_client.execute("UPDATE stock SET categories=NULL WHERE id=2")
        self.assertEqual(ids(categories=["材料"]), [2])
        self.assertEqual(self.read()["load_scans"], 1)

    def test_linked_purifier_and_panel_contraband_filter(self):
        top = dict(self.bottle, categories=["WATER"], contraband=False, trade_contraband=True)
        bottle = dict(top, id=27, area="purifier", machine_id=23, bottle_label="铁锈水",
                      can_take_now=False, trade_contraband=False)
        def publish(n, **event):
            self.event(n, **event)
            self.head(n)
            path = self.folder/"head.json"
            self.put("head.json", dict(json.loads(path.read_text()), trade_panel_protocol=1, stock_categories_protocol=1))
        self.put(self.generation+"-stock-categories.json", {"WATER":"水"})
        publish(1, op="baseline", items=[top], purifier_stock=[bottle])
        def pick(**query):
            return self.read(stock_query=query)["items"]
        linked = pick(categories=["水"], owned=True, exclude_contraband=True)
        self.assertEqual([(i["id"],i["area"],i["machine_id"],i["can_take_now"]) for i in linked],
                         [(27,"purifier",23,False)])
        self.assertEqual([i["id"] for i in self.read()["items"]], [7])
        self.assertEqual(self.read(stock_query=dict(list_tags=True))["bottle_labels"], ["铁锈水"])
        removed = dict(top,id=27,bottle_label="铁锈水",trade_contraband=False)
        publish(2, op="delta", upsert=[removed], remove=[], purifier_stock=[])
        self.assertEqual(pick(area="purifier"),[])
        self.assertEqual(pick(exclude_contraband=True)[0]["area"], "inventory")
        publish(3, op="delta", upsert=[], remove=[27], purifier_stock=[dict(bottle,can_take_now=True)])
        with closing(sqlite3.connect(self.db)) as old, old:
            old.execute("UPDATE checkpoint SET revision=3 WHERE id=1")
        details = self.read(stock_query=dict(item_ids=[27],generation=self.generation))
        self.assertEqual(details["items"][0]["machine_id"],23)
        self.assertTrue(details["items"][0]["can_take_now"])
        self.assertEqual(details["load_scans"],1)
        publish(4, op="delta", upsert=[], remove=[], purifier_stock=[])
        self.assertEqual(pick(exclude_contraband=True),[])

    def test_reload_and_interrupted_publication(self):
        self.event(1, op="baseline", items=[self.bottle])
        self.head(1)
        self.read()
        self.event(2, op="delta", upsert=[], remove=[7])
        self.head(3)
        with self.assertRaises(FileNotFoundError):
            self.read()
        self.head(1)
        self.assertEqual(self.read()["items"], [self.bottle])
        self.generation = "b" * 32
        self.event(1, op="baseline", items=[])
        self.head(1)
        self.assertEqual(self.read()["items"], [])

    def test_item_details_water_containers_purifiers_and_capacities(self):
        self.put(self.generation + "-stock-categories.json", {"WATER": "水", "HOUSEHOLD_GOOD": "日用品"})
        empty_bottle = dict(
            id=31, name="沟渠水", kind="bottle", area="inventory", owned=True, quantity=1,
            categories=["HOUSEHOLD_GOOD"], size={"w": 1, "h": 2}, slot={"x": 0, "y": 1}, rotatable=True,
            water_container=True, water_ml=0, capacity_ml=500, empty=True, contains_water=False, estimated_value=12,
        )
        soap = dict(
            id=32, name="香皂", kind="soap", area="inventory", owned=True, quantity=1,
            categories=["HOUSEHOLD_GOOD"], size={"w": 1, "h": 1}, slot={"x": 1, "y": 1}, rotatable=False,
            estimated_value=15,
        )
        purifier = dict(
            id=33, name="微型淨水器", kind="portable_water_purifier", area="inventory", owned=True, quantity=1,
            categories=["HOUSEHOLD_GOOD"], size={"w": 2, "h": 1}, slot={"x": 2, "y": 0}, rotatable=True,
            charge_remaining=25, charge_max=300, liters_remaining=2.5, estimated_value=120,
        )
        caps = {"inventory": {"w": 24, "h": 10, "free_cells": 235, "largest_free": {"w": 20, "h": 10}},
                "showcase": {"w": 7, "h": 5, "free_cells": 35, "largest_free": {"w": 7, "h": 5}}}
        self.event(1, op="baseline", items=[empty_bottle, soap, purifier], capacities=caps)
        self.put("head.json", dict(
            status="observed", slot=3, phase="CLOSED", generation=self.generation, revision=1,
            full_scans=1, load_scans=1, item_details_protocol=1, stock_categories_protocol=1, trade_panel_protocol=1,
        ))
        inv = self.read(area="inventory")
        self.assertEqual(inv["capacity"], caps["inventory"])
        self.assertEqual(inv["items"][0]["size"], {"w": 1, "h": 2})
        self.assertEqual(inv["items"][0]["slot"], {"x": 0, "y": 1})
        self.assertTrue(inv["items"][0]["rotatable"])
        self.assertTrue(inv["items"][0]["empty"])
        self.assertEqual(inv["items"][0]["water_ml"], 0)
        self.assertEqual(inv["items"][0]["estimated_value"], 12)
        self.assertEqual(inv["items"][2]["liters_remaining"], 2.5)
        water_matches = self.read(stock_query=dict(categories=["WATER"], owned=True))["items"]
        self.assertEqual([i["id"] for i in water_matches], [31])
        self.assertFalse(water_matches[0]["contains_water"])
        by_id = self.read(stock_query=dict(item_ids=[33], generation=self.generation))["items"][0]
        self.assertEqual((by_id["charge_remaining"], by_id["liters_remaining"]), (25, 2.5))


if __name__ == "__main__":
    unittest.main()
