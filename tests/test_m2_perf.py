"""Unit tests for M2: non-blocking call_tool, read-only submodes, book_history CTE, and FileSnapshotCache."""
import asyncio
import json
import os
from pathlib import Path
import sqlite3
import sys
import tempfile
import time
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "mcp"))
sys.path.insert(0, str(Path(__file__).resolve().parent))

import book_history
from database import ensure_schema, transaction
from mcp_helpers import ScriptedGame
import pawnshop_tools
import scene_server
from server_state import FileSnapshotCache
import trade_tools


class M2PerfTests(unittest.TestCase):
    def test_file_snapshot_cache_invalidates_and_isolates_mutations(self):
        cache = FileSnapshotCache()
        with tempfile.TemporaryDirectory(dir=os.environ.get("LEDGER_TEST_TEMP")) as temp:
            path = Path(temp) / "sample.json"
            path.write_text(json.dumps({"a": 1, "nested": {"k": "v"}}), encoding="utf-8")

            first = cache.read_json(path)
            first["a"] = 999
            first["nested"]["k"] = "mutated"

            # Mutating returned dict must not pollute cached copy.
            second = cache.read_json(path)
            self.assertEqual(second, {"a": 1, "nested": {"k": "v"}})

            # Updating file content invalidates cache.
            path.write_text(json.dumps({"a": 2, "nested": {"k": "v2"}}), encoding="utf-8")
            third = cache.read_json(path)
            self.assertEqual(third, {"a": 2, "nested": {"k": "v2"}})

    def test_book_history_changes_between_matches_delta_and_identity(self):
        conn = sqlite3.connect(":memory:")
        self.addCleanup(conn.close)
        conn.row_factory = sqlite3.Row
        ensure_schema(conn)
        gen = "a" * 32
        head = {"generation": gen, "slot": 1}

        book_history.record(conn, head, 1, {
            "op": "baseline", "day": 1, "phase": "MORNING",
            "items": [
                {"id": 1, "name": "Bottle", "kind": "bottle", "area": "inventory", "owned": True, "quantity": 1},
                {"id": 2, "name": "Scrap", "kind": "scrap", "area": "inventory", "owned": True, "quantity": 1},
            ],
        })
        book_history.record(conn, head, 2, {
            "op": "delta", "day": 1, "phase": "OPEN",
            "remove": [2],
            "upsert": [
                {"id": 1, "name": "Purified Bottle", "kind": "purified_water", "area": "showcase", "owned": True, "quantity": 1},
                {"id": 3, "name": "Coin", "kind": "currency", "area": "inventory", "owned": True, "quantity": 5},
            ],
        })

        upserts, removed = book_history.delta(conn, gen, 1, 2)
        self.assertEqual(removed, [2])
        self.assertEqual([row["id"] for row in upserts], [1, 3])
        self.assertEqual(upserts[0]["name"], "Purified Bottle")

        id_changes = book_history.identity_changes(conn, gen, 1, 2)
        self.assertEqual(id_changes, [{
            "id": 1,
            "from": "Bottle",
            "to": "Purified Bottle",
            "from_kind": "bottle",
            "to_kind": "purified_water",
        }])

    def test_transaction_rollback_does_not_poison_schema_cache(self):
        with tempfile.TemporaryDirectory(dir=os.environ.get("LEDGER_TEST_TEMP")) as temp:
            db_path = Path(temp) / "rollback_test.sqlite"
            with self.assertRaises(RuntimeError):
                with transaction(db_path):
                    raise RuntimeError("boom before commit")

            # Subsequent transaction must re-run ensure_schema and succeed.
            with transaction(db_path) as db:
                tables = {row[0] for row in db.execute("SELECT name FROM sqlite_master WHERE type='table'")}
            self.assertIn("stock", tables)
            self.assertIn("book_events", tables)


class M2AsyncServerTests(unittest.IsolatedAsyncioTestCase):
    async def test_readonly_submodes_skip_updates_report(self):
        with tempfile.TemporaryDirectory(dir=os.environ.get("LEDGER_TEST_TEMP")) as temp:
            root = Path(temp)
            db = root / "ledger.sqlite"
            game = ScriptedGame(root)
            game.publish_scene(status="ready", location="pawnshop", revision=1)
            game.publish_desk(revision=1, phase="OPEN")

            server = scene_server.SceneServer(root)
            pawnshop_tools.register_pawnshop_tools(server, root, db)
            server.updates.cursor = "a" * 32 + ":1:0"

            readonly_calls = [
                ("item_menu", {"item_id": 1, "generation": game.generation, "revision": 1, "mode": "read"}),
                ("item_menu", {"item_id": 1, "generation": game.generation, "revision": 1, "mode": "targets", "menu_token": "m1", "target_ids": [2]}),
                ("item_panel", {"generation": game.generation, "revision": 1, "mode": "read"}),
                ("water_use", {"source_id": 1, "target_id": 2, "generation": game.generation, "revision": 1, "mode": "preview"}),
                ("water_panel", {"generation": game.generation, "revision": 1, "mode": "read"}),
                ("control_heat", {"mode": "status"}),
                ("inspect_item_window", {"owner_id": 1, "generation": game.generation, "revision": 1, "open_if_closed": False}),
                ("inspect_archive", {"generation": game.generation, "revision": 1, "mode": "read"}),
            ]
            with (patch.object(server.updates, "report") as report_mock,
                  patch("counter.command", return_value={"status": "observed"})):
                for name, args in readonly_calls:
                    await server.call_tool(name, args)
                report_mock.assert_not_called()

    async def test_sync_tool_offloaded_does_not_block_event_loop(self):
        with tempfile.TemporaryDirectory(dir=os.environ.get("LEDGER_TEST_TEMP")) as temp:
            root = Path(temp)
            game = ScriptedGame(root)
            game.publish_scene(status="ready", location="pawnshop", revision=1)

            server = scene_server.SceneServer(root)

            @server.tool()
            def slow_sync_tool() -> dict:
                time.sleep(0.25)
                return {"status": "done"}

            ticks = 0

            async def heartbeat():
                nonlocal ticks
                for _ in range(5):
                    await asyncio.sleep(0.03)
                    ticks += 1

            hb = asyncio.create_task(heartbeat())
            call_task = asyncio.create_task(server.call_tool("slow_sync_tool", {}))
            await hb
            self.assertGreaterEqual(ticks, 5)
            await call_task

    async def test_report_client_read_attaches_shop_update_only_on_arrest_finished(self):
        with tempfile.TemporaryDirectory(dir=os.environ.get("LEDGER_TEST_TEMP")) as temp:
            root = Path(temp)
            db = root / "ledger.sqlite"
            game = ScriptedGame(root)
            game.publish_scene(status="ready", location="pawnshop", revision=1)
            game.publish_desk(revision=1, phase="OPEN")

            server = scene_server.SceneServer(root)
            pawnshop_tools.register_pawnshop_tools(server, root, db)
            server.updates.cursor = "a" * 32 + ":1:0"

            with (patch.object(server.updates, "report", return_value={"status": "unchanged", "revision": 1, "cursor": "c"}) as report_mock,
                  patch("counter.command", side_effect=[{"status": "observed"}, {"status": "arrest_finished"}])):
                waiting = await server.call_tool("report_client", {"mode": "read"})
                self.assertEqual(json.loads(waiting[0].text), {"status": "observed"})
                report_mock.assert_not_called()

                arrested = await server.call_tool("report_client", {"mode": "read"})
                payload = json.loads(arrested[0].text)
                self.assertEqual(payload["status"], "arrest_finished")
                self.assertIn("shop_update", payload)
                report_mock.assert_called_once()
