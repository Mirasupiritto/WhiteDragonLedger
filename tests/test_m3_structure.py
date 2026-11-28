"""Tests for M3: Flow, present_counter, pawnshop_tools facade split, and MCP tool snapshot."""
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "mcp"))
sys.path.insert(0, str(Path(__file__).resolve().parent))

import chem_workflow
from flow import Flow, present_counter
import journey
import pawnshop_tools
import scene
import scene_server
import stock_query


EXPECTED_TOOL_NAMES = {
    # scene
    "get_scene", "get_travel_options", "wait_for_scene",
    # journey
    "scratch_card", "feedback_box", "starting_difficulty", "starting_perks",
    "get_specializations", "choose_specialization", "shop_activity", "travel_menu", "explore",
    # chem_workflow
    "chem_finish",
    # trade_tools
    "report_client", "bargain", "get_shop_updates", "get_trade_context",
    "wait_for_shop_event", "get_shop_state", "trade_batch", "confirm_trade", "submit_appraisal",
    # shop_day
    "get_shop_status", "get_trash", "prepare_sleep", "night_service", "sleep",
    # counter_tools
    "advance_dialogue", "choose_dialogue", "response_choice", "get_buyer_context",
    "sell_order", "get_counter", "ring_customer", "next_customer", "finish_customer",
    "request_supplier_quote", "reopen_trade_tablet", "wait_for_opening",
    "barter_offer", "buy_merchant_batch", "pay_info_receipt", "open_shop",
    "dismiss_customer", "set_auto_receive", "close_shop", "wild_network",
    # stock_tools
    "get_shop_ledger", "dismiss_trade_ticket", "query_inventory", "list_stock_tags",
    "find_stock", "get_stock_items", "move_item", "move_items", "sort_inventory",
    "toggle_poster", "query_purchase_lots",
    # workstation_tools
    "item_menu", "item_panel", "inspect_item_window", "inspect_archive",
    "transfer_slot_item", "transfer_slot_items", "adjust_machine_control",
    "press_machine_button", "control_heat", "wait_for_machine",
    # water_tools
    "water_use", "fill_and_purify_bottles", "water_panel", "inspect_purifiers",
    "take_purifier_bottle", "put_purifier_bottle",
    # phone_tools
    "get_phone_contacts", "phone_keypad", "dial_contact",
    "choose_phone_service", "advance_phone_dialogue",
}


class M3StructureTests(unittest.IsolatedAsyncioTestCase):
    async def test_exposed_tool_list_snapshot(self):
        with tempfile.TemporaryDirectory(dir=os.environ.get("LEDGER_TEST_TEMP")) as temp:
            root = Path(temp)
            db = root / "ledger.sqlite"
            server = scene_server.SceneServer(root)
            scene.register_scene_tools(server, root)
            journey.register(server, root)
            pawnshop_tools.register_pawnshop_tools(server, root, db)
            chem_workflow.register(server, root, db)

            listed = server._tool_manager.list_tools()
            names = {tool.name for tool in listed}
            self.assertEqual(names, EXPECTED_TOOL_NAMES)
            for tool in listed:
                self.assertTrue(tool.description, f"Missing description for {tool.name}")
                self.assertIsInstance(tool.parameters, dict)

    def test_flow_and_present_counter_and_search_text(self):
        gen = "a" * 32
        flow = Flow(Path("."), gen, 3)
        with patch("counter.command", return_value={"status": "observed", "generation": gen, "revision": 4}):
            res = flow.command("item_menu", item_id=1, mode="read")
        self.assertEqual((res["revision"], flow.rev), (4, 4))
        stopped = flow.stop("step_a", "failed", extra=1)
        self.assertEqual(stopped, {
            "status": "stopped",
            "stopped_at": "step_a",
            "reason": "failed",
            "steps": {},
            "revision": 4,
            "extra": 1,
        })

        view = {
            "status": "observed",
            "generation": gen,
            "some_protocol": 1,
            "reception": {"token": "rt-1"},
            "heat": {"run_id": "r1", "status": "finished"},
        }
        with tempfile.TemporaryDirectory(dir=os.environ.get("LEDGER_TEST_TEMP")) as temp:
            db = Path(temp) / "ledger.sqlite"
            first = present_counter(view, folder=Path("."), database=db,
                                    summarize_offer=lambda x, verbose=False: x, strip=True)
            self.assertEqual(first["reception_token"], "rt-1")
            self.assertNotIn("some_protocol", first)
            self.assertIn("heat", first)

            second = present_counter(view, folder=Path("."), database=db,
                                     summarize_offer=lambda x, verbose=False: x, strip=True)
            self.assertNotIn("heat", second)

        self.assertEqual(stock_query.search_text("ABC"), "abc")
