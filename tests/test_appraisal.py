import asyncio
import json
import os
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from mcp.server.fastmcp import FastMCP

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "mcp"))
from pawnshop_tools import register_pawnshop_tools
import shopdesk


class AppraisalToolsTest(unittest.TestCase):
    def setUp(self):
        tmp = tempfile.TemporaryDirectory(dir=os.environ.get("LEDGER_TEST_TEMP"))
        self.addCleanup(tmp.cleanup)
        self.folder = Path(tmp.name)
        self.db = self.folder / "ledger.sqlite"
        self.generation = "b" * 32
        self.visit = "a" * 32
        self.server = FastMCP("appraisal-test")
        register_pawnshop_tools(self.server, self.folder, self.db)

    def call(self, name, **args):
        res = asyncio.run(self.server.call_tool(name, args))
        return json.loads(res[0].text)

    def test_submit_appraisal_validation_and_forwarding(self):
        with self.assertRaises(Exception):
            self.call("submit_appraisal", item_id=1, estimate=0, visit=self.visit, generation=self.generation, revision=2)
        with self.assertRaises(Exception):
            self.call("submit_appraisal", item_id=0, estimate=100, visit=self.visit, generation=self.generation, revision=2)

        expected = {
            "status": "accepted",
            "item_id": 42,
            "estimate": 150,
            "cash_delta": 55,
            "remaining_count": 0,
            "all_done": True,
            "slip_text": "完美鑑定 (98%)",
        }
        with patch("counter.command", return_value=expected) as cmd:
            res = self.call("submit_appraisal", item_id=42, estimate=150, visit=self.visit, generation=self.generation, revision=5)
        self.assertEqual(res["status"], "accepted")
        self.assertEqual(res["cash_delta"], 55)
        self.assertTrue(res["all_done"])
        cmd.assert_called_once_with(
            self.folder, "appraisal", self.generation, 5, visit=self.visit, item_id=42, estimate=150
        )

    def test_trade_context_appraisal_role_and_shop_status_guild(self):
        head = {
            "status": "observed", "generation": self.generation, "revision": 3,
            "slot": 4, "phase": "OPEN", "load_scans": 1, "full_scans": 1, "appraisal_protocol": 1,
        }
        (self.folder / "head.json").write_text(json.dumps(head), encoding="utf-8")
        (self.folder / f"{self.generation}-1.json").write_text(json.dumps({"op": "baseline", "items": [], "day": 20}), encoding="utf-8")
        (self.folder / f"{self.generation}-2.json").write_text(json.dumps({"op": "delta", "upsert": [], "remove": [], "day": 20}), encoding="utf-8")
        (self.folder / f"{self.generation}-3.json").write_text(json.dumps({"op": "delta", "upsert": [], "remove": [], "day": 20}), encoding="utf-8")
        desk = {
            "status": "observed", "generation": self.generation, "revision": 3, "slot": 4,
            "phase": "OPEN", "day": 20, "counter_protocol": 2, "appraisal_protocol": 1,
            "day_info": {
                "calendar": {}, "trash": {"status": "unavailable"}, "sleep": {},
                "appraisal_guild": {"rank": 1, "rank_name": "鑑定師", "base_fee": 35, "progress": 42.5},
            },
            "counter": {
                "stage": "appraisal", "client_role": "appraisal", "visit": self.visit,
                "customer": "委託客", "lines": ["幫我鑑定一下這兩件。"], "cash": 100,
                "appraisal": {
                    "items": [{"item_id": 42, "name": "仿生機械手", "kind": "hand", "area": "front", "default_estimate": 100}],
                    "remaining_count": 1, "impatience": 0, "business_completed": False,
                },
                "next_action": "player_visual_check",
                "reception": {"token": "rt1", "waiting": 0},
            },
        }
        with patch.object(shopdesk, "published", return_value=desk), patch("shop_day.published", return_value=desk):
            ctx = self.call("get_trade_context")
            status = self.call("get_shop_status")
        self.assertEqual(ctx["client_role"], "appraisal")
        self.assertEqual(ctx["appraisal"]["remaining_count"], 1)
        self.assertEqual(ctx["next_action"], "player_visual_check")
        self.assertEqual(status["appraisal_guild"]["rank_name"], "鑑定師")

    def test_appraisal_assist_opt_in_registration_and_forwarding(self):
        # Default (off): get_appraisal_assist must NOT be registered
        default_names = {t.name for t in asyncio.run(self.server.list_tools())}
        self.assertNotIn("get_appraisal_assist", default_names)

        # Enabled via env var -> registered and forwards appraisal_assist
        with patch.dict(os.environ, {"WHITE_DRAGON_APPRAISAL_ASSIST": "1"}):
            assist_server = FastMCP("appraisal-assist-test")
            register_pawnshop_tools(assist_server, self.folder, self.db)
        assist_names = {t.name for t in asyncio.run(assist_server.list_tools())}
        self.assertIn("get_appraisal_assist", assist_names)

        expected = {
            "status": "observed",
            "source": "appraisal_assist",
            "visit": self.visit,
            "items": [{"item_id": 42, "name": "仿生機械手", "kind": "hand", "recommended_estimate": 470}],
        }
        with patch("counter.command", return_value=expected) as cmd:
            res = asyncio.run(assist_server.call_tool("get_appraisal_assist", {
                "visit": self.visit, "generation": self.generation, "revision": 5,
            }))
        payload = json.loads(res[0].text)
        self.assertEqual((payload["status"], payload["source"], payload["items"][0]["recommended_estimate"]),
                         ("observed", "appraisal_assist", 470))
        cmd.assert_called_once_with(self.folder, "appraisal_assist", self.generation, 5, visit=self.visit)

        # V1: assist_disabled attaches next_action="ask_player_to_enable_in_mod_settings"
        disabled_resp = {"status": "assist_disabled", "reason": "appraisal_assist_off"}
        with patch("counter.command", return_value=disabled_resp):
            res_off = asyncio.run(assist_server.call_tool("get_appraisal_assist", {
                "visit": self.visit, "generation": self.generation, "revision": 5,
            }))
        payload_off = json.loads(res_off[0].text)
        self.assertEqual(payload_off["status"], "assist_disabled")
        self.assertEqual(payload_off["next_action"], "ask_player_to_enable_in_mod_settings")

    def test_appraisal_inspect_action_spec_and_demand_highlight_forwarding(self):
        head = {
            "status": "observed", "generation": self.generation, "revision": 1,
            "slot": 4, "phase": "OPEN", "load_scans": 1, "full_scans": 1,
            "counter_protocol": 2, "appraisal_protocol": 1,
        }
        (self.folder / "head.json").write_text(json.dumps(head), encoding="utf-8")
        (self.folder / f"{self.generation}-1.json").write_text(json.dumps({"op": "baseline", "items": [], "day": 20}), encoding="utf-8")
        desk_hl = {
            "status": "observed", "generation": self.generation, "revision": 1, "slot": 4,
            "phase": "OPEN", "day": 20, "counter_protocol": 2, "appraisal_protocol": 1,
            "counter": {
                "stage": "appraisal", "client_role": "appraisal", "visit": self.visit,
                "customer": "委託客", "lines": ["幫我鑑定一下。"], "appraisal_protocol": 1,
                "highlighted_item_ids": [42], "demand_highlight_status": "compat_unavailable",
                "reception": {"token": "rt1", "waiting": 0},
            },
        }
        with patch.object(shopdesk, "published", return_value=desk_hl), \
             patch("counter.published", return_value=desk_hl), \
             patch("counter.exchange", return_value={"status": "inspected", "item_id": 42, "item_index": 0, "inspecting": True}) as ex:
            res = self.call("appraisal_inspect", visit=self.visit, generation=self.generation, revision=1, item_index=0)
            ctx = self.call("get_trade_context")
        self.assertEqual((res["status"], res["item_id"], res["inspecting"]), ("inspected", 42, True))
        self.assertEqual(ex.call_args.args[2]["action"], "appraisal_inspect")
        self.assertEqual(ctx["highlighted_item_ids"], [42])
        self.assertEqual(ctx["demand_highlight_status"], "compat_unavailable")


if __name__ == "__main__":
    unittest.main()
