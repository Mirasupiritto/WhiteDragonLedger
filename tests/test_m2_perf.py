"""Unit tests for M2: non-blocking call_tool, read-only submodes, book_history CTE, and FileSnapshotCache."""
import asyncio
import json
import os
from pathlib import Path
import sqlite3
import sys
import tempfile
import threading
import time
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "mcp"))
sys.path.insert(0, str(Path(__file__).resolve().parent))

import book_history
from database import ensure_schema, transaction
from mcp_helpers import ScriptedGame
import pawnshop_tools
import scene
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

    def test_file_snapshot_cache_racy_mtime_rereads(self):
        cache = FileSnapshotCache()
        with tempfile.TemporaryDirectory(dir=os.environ.get("LEDGER_TEST_TEMP")) as temp:
            path = Path(temp) / "head.json"
            t = time.time_ns()
            path.write_text(json.dumps({"revision": 120}), encoding="utf-8")
            os.utime(path, ns=(t, t))
            self.assertEqual(cache.read_json(path), {"revision": 120})

            # Overwrite with identical size and identical mtime_ns (racy window).
            path.write_text(json.dumps({"revision": 121}), encoding="utf-8")
            os.utime(path, ns=(t, t))
            self.assertEqual(cache.read_json(path), {"revision": 121})

    def test_file_snapshot_cache_hits_when_mtime_older_than_racy_window(self):
        cache = FileSnapshotCache()
        with tempfile.TemporaryDirectory(dir=os.environ.get("LEDGER_TEST_TEMP")) as temp:
            path = Path(temp) / "head.json"
            old_t = time.time_ns() - FileSnapshotCache.RACY_NS - 500_000_000
            path.write_text(json.dumps({"revision": 120}), encoding="utf-8")
            os.utime(path, ns=(old_t, old_t))
            self.assertEqual(cache.read_json(path), {"revision": 120})

            with patch.object(Path, "read_text", side_effect=AssertionError("expected cache hit")):
                self.assertEqual(cache.read_json(path), {"revision": 120})

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

    async def test_concurrent_counter_calls_serialize_without_busy(self):
        with tempfile.TemporaryDirectory(dir=os.environ.get("LEDGER_TEST_TEMP")) as temp:
            root = Path(temp)
            db = root / "ledger.sqlite"
            game = ScriptedGame(root)
            game.publish_scene(status="ready", location="pawnshop", revision=1)
            game.publish_desk(
                revision=1,
                phase="MORNING",
                counter={
                    "stage": "idle",
                    "cash": 100,
                    "lines": [],
                    "waiting": 0,
                    "reception": {"can_ring": False},
                    "can_open_shop": True,
                    "inventory_sort_protocol": 1,
                },
            )

            server = scene_server.SceneServer(root)
            pawnshop_tools.register_pawnshop_tools(server, root, db)

            inbox = root / f"counter-{game.generation}.json"
            stop_responder = threading.Event()
            seen_actions = []

            def responder():
                while not stop_responder.is_set():
                    if inbox.exists():
                        try:
                            req = json.loads(inbox.read_text(encoding="utf-8"))
                        except (FileNotFoundError, json.JSONDecodeError):
                            time.sleep(0.01)
                            continue
                        time.sleep(0.06)
                        seen_actions.append(req["action"])
                        game.write_receipt(
                            req["command_id"],
                            {
                                "status": "observed",
                                "command_id": req["command_id"],
                                "generation": game.generation,
                                "revision": 1,
                            },
                            unlink_inbox=inbox,
                        )
                    else:
                        time.sleep(0.01)

            worker = threading.Thread(target=responder, daemon=True)
            worker.start()
            try:
                res1, res2 = await asyncio.gather(
                    server.call_tool("open_shop", {"generation": game.generation, "revision": 1}),
                    server.call_tool("sort_inventory", {"generation": game.generation, "revision": 1}),
                )
            finally:
                stop_responder.set()
                worker.join(timeout=2)

            p1 = json.loads(res1[0].text)
            p2 = json.loads(res2[0].text)
            self.assertEqual(p1["status"], "observed")
            self.assertEqual(p2["status"], "observed")
            self.assertEqual(seen_actions, ["open_shop", "sort_inventory"])

    async def test_get_scene_responds_immediately_while_sync_tool_holds_lock(self):
        with tempfile.TemporaryDirectory(dir=os.environ.get("LEDGER_TEST_TEMP")) as temp:
            root = Path(temp)
            game = ScriptedGame(root)
            game.publish_scene(status="ready", location="pawnshop", revision=1)

            server = scene_server.SceneServer(root)
            scene.register_scene_tools(server, root)
            started = threading.Event()

            @server.tool()
            def slow_locked_tool() -> dict:
                started.set()
                time.sleep(0.25)
                return {"status": "done"}

            slow_task = asyncio.create_task(server.call_tool("slow_locked_tool", {}))
            while not started.is_set():
                await asyncio.sleep(0.01)

            t0 = time.monotonic()
            scene_res = await server.call_tool("get_scene", {})
            elapsed = time.monotonic() - t0
            self.assertLess(elapsed, 0.15)
            self.assertEqual(json.loads(scene_res[0].text)["status"], "ready")
            await slow_task
