"""Unit tests for M1 base infrastructure: ServerState, command_transport, and mcp_helpers."""
import os
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "mcp"))
sys.path.insert(0, str(Path(__file__).resolve().parent))

import command_transport
import counter
import receipt_store
from mcp_helpers import ScriptedGame, call_tool_json, open_mcp_session


class M1BaseTests(unittest.TestCase):
    def test_heat_seen_notifies_again_when_generation_changes(self):
        gen1 = "a" * 32
        gen2 = "b" * 32
        r1 = {"generation": gen1, "heat": {"run_id": "run-1", "status": "finished"}}
        r2 = {"generation": gen2, "heat": {"run_id": "run-1", "status": "finished"}}

        with tempfile.TemporaryDirectory(dir=os.environ.get("LEDGER_TEST_TEMP")) as temp:
            db = Path(temp) / "ledger.sqlite"
            self.assertIn("heat", counter.filter_finished_heat(db, r1))
            self.assertNotIn("heat", counter.filter_finished_heat(db, r1))
            # Switching generation notifies the same run_id again.
            self.assertIn("heat", counter.filter_finished_heat(db, r2))
            self.assertNotIn("heat", counter.filter_finished_heat(db, r2))

    def test_poll_early_exit_and_timeout(self):
        calls = 0

        def check_ready():
            nonlocal calls
            calls += 1
            return "ok" if calls == 3 else None

        sleeps = []
        result, timed_out = command_transport.poll(check_ready, timeout=1.0, interval=0.05, sleep=sleeps.append)
        self.assertEqual((result, timed_out, calls, len(sleeps)), ("ok", False, 3, 2))

        zero_calls = 0

        def check_never():
            nonlocal zero_calls
            zero_calls += 1
            return None

        zero_sleeps = []
        result, timed_out = command_transport.poll(check_never, timeout=0, interval=0.05, sleep=zero_sleeps.append)
        self.assertEqual((result, timed_out, zero_calls, zero_sleeps), (None, True, 1, []))

    def test_is_hex32_deterministic_id_and_receipt_compat(self):
        self.assertTrue(command_transport.is_hex32("0123456789abcdef" * 2))
        self.assertFalse(command_transport.is_hex32("0123456789ABCDEF" * 2))
        self.assertFalse(command_transport.is_hex32("short"))
        self.assertFalse(command_transport.is_hex32(None))

        cid1 = command_transport.deterministic_id("ns/", {"b": 2, "a": 1})
        cid2 = command_transport.deterministic_id("ns/", {"a": 1, "b": 2})
        self.assertEqual(cid1, cid2)
        self.assertTrue(command_transport.is_hex32(cid1))
        self.assertIs(receipt_store.replayed, command_transport.replayed)
        self.assertIs(receipt_store.read_command_receipt, command_transport.read_command_receipt)


class M1HelpersStdioTests(unittest.IsolatedAsyncioTestCase):
    async def test_mcp_helpers_and_scripted_game(self):
        with tempfile.TemporaryDirectory(dir=os.environ.get("LEDGER_TEST_TEMP")) as temp:
            root = Path(temp)
            game = ScriptedGame(root)
            game.publish_scene(status="ready", location="pawnshop", revision=3)
            async with open_mcp_session(root, root / "ledger.sqlite") as client:
                scene = await call_tool_json(client, "get_scene")
                self.assertEqual((scene["status"], scene["location"], scene["revision"]), ("ready", "pawnshop", 3))
