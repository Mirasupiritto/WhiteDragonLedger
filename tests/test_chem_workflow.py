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
                {"id": 101, "kind": "evaporator", "area": "inventory"},
                {"id": 102, "kind": "chem_finisher", "area": "inventory"},
                {"id": 103, "kind": "chemical_scanner", "area": "inventory"},
                {"id": 104, "kind": "smuggler_bay", "area": "inventory"},
            ],
        }

    def test_chem_finish_success_with_stash(self):
        rev = [10]
        calls = []

        def fake_command(folder, action, generation, revision, **args):
            calls.append((action, args))
            rev[0] += 1
            r = rev[0]
            if action == "inspect_window" and args["owner_id"] == 101:
                return {
                    "status": "observed",
                    "window_token": "w_evap",
                    "shop_update": {"revision": r},
                    "window": {
                        "controls": [
                            {"control": "content/2/body", "type": "slot", "items": [{"id": 201, "kind": "bottle"}]},
                        ]
                    },
                }
            if action == "transfer_slot" and args["owner_id"] == 101:
                return {"status": "moved", "shop_update": {"revision": r}}
            if action == "item_menu" and args["mode"] == "read":
                return {"status": "observed", "menu_token": "m_scan", "verbs": {"use": {"enabled": True}}, "shop_update": {"revision": r}}
            if action == "item_menu" and args["mode"] == "choose" and args["target_id"] == 201:
                return {
                    "status": "invoked",
                    "shop_update": {"revision": r},
                    "scan": {"total_ml": 10.0, "components": [{"id": "dream_cap_extract", "ul": 10000, "ml": 10.0, "percent": 100.0}]},
                }
            if action == "inspect_window" and args["owner_id"] == 102:
                # First inspect (before recipe select), second (after recipe select), third (after produce)
                inspect_count = sum(1 for a, x in calls if a == "inspect_window" and x["owner_id"] == 102)
                if inspect_count == 1:
                    return {
                        "status": "observed",
                        "window_token": "w_fin_1",
                        "shop_update": {"revision": r},
                        "window": {
                            "recipe_id": "caffeine_pill",
                            "controls": [
                                {"control": "content/0/body", "type": "slot", "role": "input", "accepts": "one_container", "insert_allowed": True, "items": []},
                                {"control": "content/1/body", "type": "slot", "role": "output", "items": []},
                                {"control": "content/2/body", "type": "button", "label": "Output", "enabled": True},
                                {"control": "content/3/body", "type": "button", "label": "Select Recipe", "enabled": True},
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
                                {"control": "content/0/body", "type": "slot", "role": "input", "accepts": "one_container", "insert_allowed": True, "items": [{"id": 201}]},
                                {"control": "content/1/body", "type": "slot", "role": "output", "items": []},
                                {"control": "content/2/body", "type": "button", "label": "Output", "enabled": True},
                                {"control": "content/3/body", "type": "button", "label": "Select Recipe", "enabled": True},
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
                            {"control": "content/0/body", "type": "slot", "role": "input", "items": [{"id": 201, "liquid": {"total_ml": 1.0}}]},
                            {"control": "content/1/body", "type": "slot", "role": "output", "items": [{"id": 301}, {"id": 302}]},
                        ],
                    },
                }
            if action == "transfer_slot" and args["owner_id"] == 102:
                return {"status": "moved", "window_token": "w_fin_1b", "shop_update": {"revision": r}}
            if action == "press_control" and args["owner_id"] == 102 and args.get("quantity") is None:
                return {"status": "pressed", "window_token": "w_fin_1c", "shop_update": {"revision": r}}
            if action == "item_panel" and args["mode"] == "read":
                return {
                    "status": "observed",
                    "panel_token": "p_1",
                    "shop_update": {"revision": r},
                    "view": {"controls": {"-57964": {"type": "button", "recipe_id": "dream_dust", "enabled": True}}},
                }
            if action == "item_panel" and args["mode"] == "choose":
                return {"status": "applied", "after": {"status": "panel_closed"}, "shop_update": {"revision": r}}
            if action == "press_control" and args["owner_id"] == 102 and args.get("quantity") == 64:
                return {
                    "status": "batch_incomplete",
                    "produced": 2,
                    "reason": "recipe_not_ready",
                    "purity": 61.0,
                    "grade": "street_standard",
                    "window_token": "w_fin_2b",
                    "shop_update": {"revision": r},
                }
            if action == "transfer_slots" and args["owner_id"] == 102:
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
            if action == "inspect_window" and args["owner_id"] == 104:
                return {
                    "status": "observed",
                    "window_token": "w_bay",
                    "shop_update": {"revision": r},
                    "window": {"controls": [{"control": "content/0/body", "type": "slot", "insert_allowed": True}]},
                }
            if action == "transfer_slots" and args["owner_id"] == 104:
                return {"status": "moved", "items": [{"item_id": 301, "status": "moved"}], "shop_update": {"revision": r}}
            raise AssertionError(f"Unexpected call: {action} {args}")

        with patch.object(chem_workflow.ledger, "lookup", return_value=self.stock), \
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

    def test_chem_finish_stops_when_alcohol_remains(self):
        rev = [10]

        def fake_command(folder, action, generation, revision, **args):
            rev[0] += 1
            r = rev[0]
            if action == "inspect_window" and args["owner_id"] == 101:
                return {
                    "status": "observed",
                    "window_token": "w_evap",
                    "shop_update": {"revision": r},
                    "window": {"controls": [{"control": "content/2/body", "type": "slot", "items": [{"id": 201}]}]},
                }
            if action == "transfer_slot" and args["owner_id"] == 101:
                return {"status": "moved", "shop_update": {"revision": r}}
            if action == "item_menu" and args["mode"] == "read":
                return {"status": "observed", "menu_token": "m_scan", "verbs": {"use": {"enabled": True}}, "shop_update": {"revision": r}}
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
            raise AssertionError(f"Unexpected call after scan failure: {action} {args}")

        with patch.object(chem_workflow.ledger, "lookup", return_value=self.stock), \
             patch.object(chem_workflow.counter, "command", side_effect=fake_command), \
             patch.object(chem_workflow.counter, "context", return_value={"status": "observed", "generation": self.gen, "revision": 10}):
            res = chem_workflow.run_chem_finish(
                self.folder, self.db, vessel_id=201, recipe_id="dream_dust",
                generation=self.gen, revision=10,
            )

        self.assertEqual(res["status"], "stopped")
        self.assertEqual(res["stopped_at"], "scan_vessel")
        self.assertEqual(res["reason"], "liquid_raw_material_remaining")
        self.assertEqual(res["vessel_location"], {"area": "inventory"})
        self.assertEqual(res["remaining_liquids"][0]["id"], "alcohol")


if __name__ == "__main__":
    unittest.main()
