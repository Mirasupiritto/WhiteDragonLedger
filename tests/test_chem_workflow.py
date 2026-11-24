import sys
import unittest
from pathlib import Path
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "mcp"))

import chem_workflow


class ChemWorkflowTests(unittest.TestCase):
    def setUp(self):
        self.folder = Path("unused")
        self.db = Path("unused.sqlite3")
        self.gen = "0" * 32
        self.stock = {
            "status": "observed",
            "items": [
                {"id": 11, "kind": "evaporator", "name": "蒸发器", "area": "inventory", "owned": True},
                {"id": 12, "kind": "chem_finisher", "name": "成型机", "area": "inventory", "owned": True},
                {"id": 13, "kind": "chem_scanner", "name": "成分光谱仪", "area": "inventory", "owned": True},
                {"id": 17, "kind": "smuggler_bay_mini", "name": "迷你走私储藏区", "categories": ["STORAGE"], "area": "inventory", "owned": True},
            ],
        }
        self.published = {
            "status": "observed",
            "generation": self.gen,
            "revision": 10,
            "smuggler_bays": [
                {"id": 17, "name": "迷你走私储藏区", "kind": "smuggler_bay_mini", "area": "inventory"},
            ],
        }

    def test_chem_finish_success_with_real_receipt_shapes(self):
        rev = [10]
        calls = []

        def fake_command(folder, action, generation, revision, **args):
            calls.append((action, args))
            rev[0] += 1
            r = rev[0]
            if action == "inspect_window" and args["owner_id"] == 11:
                return {
                    "status": "observed",
                    "window_token": "w_evap",
                    "shop_update": {"revision": r},
                    "window": {
                        "controls": [
                            {"control": "content/2/body", "type": "slot", "accepts": "one_item", "items": [{"id": 201, "kind": "bottle"}]},
                        ]
                    },
                }
            if action == "transfer_slot" and args["owner_id"] == 11:
                return {"status": "moved", "shop_update": {"revision": r}}
            if action == "item_menu" and args["mode"] == "read":
                return {
                    "status": "observed",
                    "item_id": 13,
                    "base_actions": {"use": "使用", "equip": "装备", "pin": "固定"},
                    "enabled": ["use", "equip", "pin"],
                    "labels": {},
                    "menu_token": "d7fd29611c3e4a2cbff197b9fa369dd6",
                    "shop_update": {"revision": r},
                }
            if action == "item_menu" and args["mode"] == "choose" and args["target_id"] == 201:
                return {
                    "status": "invoked",
                    "shop_update": {"revision": r},
                    "scan": {"total_ml": 10.0, "components": [{"id": "dream_cap_extract", "ul": 10000, "ml": 10.0, "percent": 100.0}]},
                }
            if action == "inspect_window" and args["owner_id"] == 12:
                inspect_count = sum(1 for a, x in calls if a == "inspect_window" and x["owner_id"] == 12)
                if inspect_count == 1:
                    return {
                        "status": "observed",
                        "window_token": "w_fin_1",
                        "shop_update": {"revision": r},
                        "window": {
                            "recipe_id": "",
                            "controls": [
                                {"control": "content/1/body", "type": "slot", "role": "input", "accepts": "one_item", "insert_allowed": True, "items": []},
                                {"control": "content/2/body", "type": "slot", "role": "output", "items": []},
                                {"control": "content/3/body/0", "role": "control", "label": "配置", "type": "button", "action": "configure", "enabled": True},
                                {"control": "content/3/body/1", "role": "control", "label": "控制面板", "type": "text", "text": "未选择目标"},
                                {"control": "content/3/body/2", "role": "control", "label": "控制面板", "type": "text", "text": "状态：未选择配方"},
                                {"control": "content/3/body/3", "role": "control", "label": "输出", "type": "button", "action": "output", "enabled": True},
                            ],
                        },
                    }
                if inspect_count == 2:
                    return {
                        "status": "observed",
                        "window_token": "w_fin_2",
                        "shop_update": {"revision": r},
                        "window": {
                            "recipe_id": "dream_dust",
                            "controls": [
                                {"control": "content/1/body", "type": "slot", "role": "input", "accepts": "one_item", "insert_allowed": True, "items": [{"id": 201}]},
                                {"control": "content/2/body", "type": "slot", "role": "output", "items": []},
                                {"control": "content/3/body/0", "role": "control", "label": "配置", "type": "button", "action": "configure", "enabled": True},
                                {"control": "content/3/body/3", "role": "control", "label": "输出", "type": "button", "action": "output", "enabled": True},
                            ],
                        },
                    }
                return {
                    "status": "observed",
                    "window_token": "w_fin_3",
                    "shop_update": {"revision": r},
                    "window": {
                        "recipe_id": "dream_dust",
                        "controls": [
                            {"control": "content/1/body", "type": "slot", "role": "input", "accepts": "one_item", "insert_allowed": True, "items": [{"id": 201, "liquid": {"total_ml": 1.0}}]},
                            {"control": "content/2/body", "type": "slot", "role": "output", "items": [{"id": 301}, {"id": 302}]},
                        ],
                    },
                }
            if action == "transfer_slot" and args["owner_id"] == 12:
                return {"status": "moved", "window_token": "w_fin_1b", "shop_update": {"revision": r}}
            if action == "press_control" and args["owner_id"] == 12 and args.get("quantity") is None:
                self.assertEqual(args["control"], "content/3/body/0")
                return {"status": "pressed", "window_token": "w_fin_1c", "shop_update": {"revision": r}}
            if action == "item_panel" and args["mode"] == "read":
                return {
                    "status": "observed",
                    "panel_token": "p_1",
                    "shop_update": {"revision": r},
                    "view": {"controls": {"-57964": {"type": "button", "label": "选择", "recipe_id": "dream_dust", "enabled": True}}},
                }
            if action == "item_panel" and args["mode"] == "choose":
                return {"status": "applied", "after": {"status": "panel_closed"}, "shop_update": {"revision": r}}
            if action == "press_control" and args["owner_id"] == 12 and args.get("quantity") == 64:
                self.assertEqual(args["control"], "content/3/body/3")
                return {
                    "status": "batch_incomplete",
                    "produced": 2,
                    "reason": "recipe_not_ready",
                    "purity": 61.0,
                    "grade": "street_standard",
                    "window_token": "w_fin_2b",
                    "shop_update": {"revision": r},
                }
            if action == "transfer_slots" and args["owner_id"] == 12:
                return {
                    "status": "moved",
                    "items": [{"item_id": 301, "status": "moved"}, {"item_id": 302, "status": "moved"}],
                    "shop_update": {"revision": r},
                }
            if action == "item_menu" and args["mode"] == "choose" and args["target_id"] == 301:
                return {
                    "status": "invoked",
                    "shop_update": {"revision": r},
                    "scan": {"tested": {"percent": 61.0}, "label": {"display": "Street Standard"}, "components": [{"id": "dream_dust", "percent": 61.0}]},
                }
            if action == "inspect_window" and args["owner_id"] == 17:
                return {
                    "status": "observed",
                    "window_token": "w_bay",
                    "shop_update": {"revision": r},
                    "window": {"controls": [{"control": "content/0/body", "type": "slot", "insert_allowed": True}]},
                }
            if action == "transfer_slots" and args["owner_id"] == 17:
                return {"status": "moved", "items": [{"item_id": 301, "status": "moved"}], "shop_update": {"revision": r}}
            raise AssertionError(f"Unexpected call: {action} {args}")

        with patch.object(chem_workflow.ledger, "lookup", return_value=self.stock), \
             patch.object(chem_workflow.shopdesk, "published", return_value=self.published), \
             patch.object(chem_workflow.counter, "command", side_effect=fake_command), \
             patch.object(chem_workflow.counter, "context", return_value={"status": "observed", "generation": self.gen, "revision": 10}):
            res = chem_workflow.run_chem_finish(
                self.folder, self.db, vessel_id=201, recipe_id="dream_dust",
                generation=self.gen, revision=10, stash_count=1,
            )

        self.assertEqual(res["status"], "completed")
        self.assertEqual(res["produced"], 2)
        self.assertEqual(res["produced_ids"], [301, 302])
        self.assertEqual(res["stashed_ids"], [301])
        self.assertEqual(res["stop_reason"], "recipe_not_ready")
        self.assertEqual(res["product_scan"]["purity"], 61.0)
        self.assertEqual(res["product_scan"]["grade"], "street_standard")

    def test_chem_finish_stops_when_native_finisher_rejects_wet_vessel(self):
        rev = [10]

        def fake_command(folder, action, generation, revision, **args):
            rev[0] += 1
            r = rev[0]
            if action == "inspect_window" and args["owner_id"] == 11:
                return {
                    "status": "observed",
                    "window_token": "w_evap",
                    "shop_update": {"revision": r},
                    "window": {"controls": [{"control": "content/2/body", "type": "slot", "items": [{"id": 201}]}]},
                }
            if action == "transfer_slot" and args["owner_id"] == 11:
                return {"status": "moved", "shop_update": {"revision": r}}
            if action == "item_menu" and args["mode"] == "read":
                return {
                    "status": "observed",
                    "item_id": 13,
                    "base_actions": {"use": "使用"},
                    "enabled": ["use", "equip", "pin"],
                    "labels": {},
                    "menu_token": "m_scan",
                    "shop_update": {"revision": r},
                }
            if action == "item_menu" and args["mode"] == "choose":
                return {
                    "status": "invoked",
                    "shop_update": {"revision": r},
                    "scan": {
                        "total_ml": 15.0,
                        "components": [
                            {"id": "dream_cap_extract", "ul": 10000, "ml": 10.0, "percent": 66.67},
                            {"id": "alcohol", "ul": 5000, "ml": 5.0, "percent": 33.33},
                        ],
                    },
                }
            if action == "inspect_window" and args["owner_id"] == 12:
                return {
                    "status": "observed",
                    "window_token": "w_fin",
                    "shop_update": {"revision": r},
                    "window": {
                        "recipe_id": "dream_dust",
                        "controls": [
                            {"control": "content/1/body", "type": "slot", "role": "input", "accepts": "one_item", "insert_allowed": True, "items": []},
                            {"control": "content/2/body", "type": "slot", "role": "output", "items": []},
                            {"control": "content/3/body/0", "role": "control", "label": "配置", "type": "button", "action": "configure", "enabled": True},
                            {"control": "content/3/body/3", "role": "control", "label": "输出", "type": "button", "action": "output", "enabled": True},
                        ],
                    },
                }
            if action == "transfer_slot" and args["owner_id"] == 12:
                return {"status": "moved", "window_token": "w_fin_b", "shop_update": {"revision": r}}
            if action == "press_control" and args["owner_id"] == 12 and args.get("quantity") == 64:
                return {"status": "batch_incomplete", "produced": 0, "reason": "recipe_not_ready", "shop_update": {"revision": r}}
            raise AssertionError(f"Unexpected call: {action} {args}")

        with patch.object(chem_workflow.ledger, "lookup", return_value=self.stock), \
             patch.object(chem_workflow.counter, "command", side_effect=fake_command), \
             patch.object(chem_workflow.counter, "context", return_value={"status": "observed", "generation": self.gen, "revision": 10}):
            res = chem_workflow.run_chem_finish(
                self.folder, self.db, vessel_id=201, recipe_id="dream_dust",
                generation=self.gen, revision=10,
            )

        self.assertEqual(res["status"], "stopped")
        self.assertEqual(res["stopped_at"], "produce_batch")
        self.assertEqual(res["reason"], "recipe_not_ready")
        self.assertEqual(res["vessel_location"], {"area": "chem_finisher", "owner_id": 12, "control": "content/1/body"})
        self.assertEqual(res["remaining_liquids"][0]["id"], "alcohol")


if __name__ == "__main__":
    unittest.main()
