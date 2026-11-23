"""Run a real stdio client across a simulated game departure and return."""
import asyncio
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest

from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client


class SceneRouting(unittest.IsolatedAsyncioTestCase):
    async def test_retry_departure_after_arrival_recovers_original_receipt(self):
        with tempfile.TemporaryDirectory(dir=os.environ.get("LEDGER_TEST_TEMP")) as temp:
            folder = Path(temp)
            session = "e" * 32
            def publish(status, location, revision):
                (folder / "scene.json").write_text(json.dumps(dict(session=session, status=status,
                    location=location, revision=revision, journey_protocol=1)), encoding="utf-8")
            publish("selecting_destination", "pawnshop", 8)
            server = StdioServerParameters(command=sys.executable, args=["-B",
                str(Path(__file__).resolve().parents[1] / "mcp" / "ledger_mcp.py"), "--journal", temp,
                "--database", str(folder / "ledger.sqlite")])
            inbox = folder / f"journey-{session}.json"
            async with stdio_client(server) as streams, ClientSession(*streams) as client:
                await client.initialize()
                args = dict(mode="depart", session=session, revision=8, view_token="page", location_id="forest")
                submitted = asyncio.create_task(client.call_tool("travel_menu", args))
                async with asyncio.timeout(5):
                    while not inbox.exists():
                        await asyncio.sleep(.02)
                plan = json.loads(inbox.read_text())
                self.assertEqual((plan["tool"], plan["action"], plan["location_id"]), ("travel_menu", "depart", "forest"))
                receipt = dict(command_id=plan["command_id"], generation=session, status="departure_requested")
                publish("ready", "forest", 9)
                (folder / f"receipt-{plan['command_id']}.json").write_text(json.dumps(receipt))
                inbox.unlink()
                self.assertEqual(json.loads((await submitted).content[0].text), receipt)
                retried = await client.call_tool("travel_menu", args)
                self.assertEqual(json.loads(retried.content[0].text), receipt)
                changed = await client.call_tool("travel_menu", dict(args, location_id="ruins"))
                self.assertEqual(json.loads(changed.content[0].text)["status"], "scene_changed")
                self.assertFalse(inbox.exists())
                self.assertFalse((folder / "ledger.sqlite").exists())

    async def test_permanent_tool_list_scene_gating_and_arrival(self):
        changes = asyncio.Queue()

        async def handle(message):
            if getattr(getattr(message, "root", None), "method", None) == "notifications/tools/list_changed":
                changes.put_nowait(True)

        with tempfile.TemporaryDirectory(dir=os.environ.get("LEDGER_TEST_TEMP")) as temp:
            folder = Path(temp)
            database = folder / "account.sqlite3"
            def publish(revision, status, location, **details):
                record = dict(session="test", revision=revision, status=status, location=location, **details)
                temp_file = folder / "scene.tmp"
                temp_file.write_text(json.dumps(record), encoding="utf-8")
                temp_file.replace(folder / "scene.json")

            publish(1, "ready", "pawnshop")
            server = StdioServerParameters(command=sys.executable, args=[
                "-B", str(Path(__file__).resolve().parents[1] / "mcp" / "ledger_mcp.py"),
                "--journal", temp, "--database", str(database)])
            async with stdio_client(server) as streams, ClientSession(*streams, message_handler=handle) as client:
                init = await client.initialize()
                self.assertFalse(bool(init.capabilities.tools and init.capabilities.tools.listChanged))
                listed = (await client.list_tools()).tools
                tools = {t.name for t in listed}
                wait_tool = next(t for t in listed if t.name == "wait_for_scene")
                self.assertEqual(wait_tool.inputSchema["properties"]["timeout"]["maximum"], 60)
                for expected in (
                    "get_scene", "wait_for_scene", "get_shop_ledger", "dismiss_trade_ticket",
                    "water_use", "fill_and_purify_bottles", "dismiss_customer", "next_customer",
                    "finish_customer", "water_panel", "pay_info_receipt", "barter_offer",
                    "get_phone_contacts", "query_inventory", "find_stock", "list_stock_tags",
                    "get_stock_items", "get_travel_options", "travel_menu", "explore",
                    "get_specializations", "choose_specialization", "starting_perks", "starting_difficulty",
                ):
                    self.assertIn(expected, tools)

                async def read(name, **args):
                    result = await client.call_tool(name, args)
                    self.assertFalse(result.isError, result.content)
                    return json.loads(result.content[0].text)

                self.assertEqual((await read("explore", mode="read"))["tool_status"], "unavailable_in_scene")
                options = {"destinations": [{"id": "forest", "available": True}], "selected_id": None}
                publish(2, "selecting_destination", "pawnshop", travel_options=options)
                self.assertEqual({t.name for t in (await client.list_tools()).tools}, tools)
                self.assertEqual((await read("get_travel_options"))["options"], options)
                self.assertNotIn("travel_options", await read("get_scene"))
                self.assertEqual((await read("query_inventory"))["tool_status"], "unavailable_in_scene")
                self.assertEqual((await read("find_stock", owned=True))["tool_status"], "unavailable_in_scene")
                self.assertEqual((await read("wait_for_scene", session="test", after_revision=1, timeout=0))["wait_result"], "timeout")
                publish(3, "transitioning", "pawnshop")
                self.assertEqual((await read("get_travel_options"))["tool_status"], "unavailable_in_scene")
                self.assertEqual({t.name for t in (await client.list_tools()).tools}, tools)
                waiter = asyncio.create_task(client.call_tool("wait_for_scene", {"session":"test", "after_revision":2}))
                publish(4, "ready", "dumping_ground")
                arrival = await waiter
                self.assertFalse(arrival.isError, arrival.content)
                arrived = json.loads(arrival.content[0].text)
                self.assertEqual((arrived["wait_result"], arrived["location"]), ("arrived", "dumping_ground"))
                (folder/"head.json").write_text("store must not be read", encoding="utf-8")
                for name, args in (("get_shop_ledger", {"slot":4}), ("query_inventory", {}), ("open_shop", {"generation":"old", "revision":1})):
                    reply = await client.call_tool(name, args)
                    self.assertFalse(reply.isError, reply.content)
                    self.assertEqual(json.loads(reply.content[0].text)["tool_status"], "unavailable_in_scene")
                self.assertFalse(database.exists())
                publish(5, "ready", "pawnshop")
                publish(6, "selecting_specialization", None)
                self.assertEqual((await read("query_inventory"))["tool_status"], "unavailable_in_scene")
                publish(7, "selecting_perks", None)
                self.assertEqual((await read("get_specializations"))["tool_status"], "unavailable_in_scene")
                publish(8, "selecting_difficulty", None)
                self.assertEqual((await read("starting_perks"))["tool_status"], "unavailable_in_scene")
                publish(9, "waiting_for_load", None)
                self.assertEqual((await read("get_shop_ledger", slot=4))["open_tickets"], [])
                # wait_for_scene must not emit repeated new_session while status=waiting_for_load
                (folder / "scene.json").write_text(json.dumps(dict(
                    session="reloading", status="waiting_for_load", location=None, revision=1)), encoding="utf-8")
                self.assertEqual((await read("wait_for_scene", session="test", after_revision=9, timeout=0))["wait_result"], "timeout")
                load_waiter = asyncio.create_task(client.call_tool("wait_for_scene", {"session": "test", "after_revision": 9, "timeout": 5}))
                await asyncio.sleep(0.15)
                (folder / "scene.json").write_text(json.dumps(dict(
                    session="reloaded_sess", status="ready", location="pawnshop", revision=2)), encoding="utf-8")
                settled = json.loads((await load_waiter).content[0].text)
                self.assertEqual((settled["wait_result"], settled["session"]), ("new_session", "reloaded_sess"))
                self.assertTrue(changes.empty())

    async def test_explore_abandon_ground_and_scratch_card_close(self):
        with tempfile.TemporaryDirectory(dir=os.environ.get("LEDGER_TEST_TEMP")) as temp:
            folder = Path(temp)
            session = "f" * 32
            def publish(status, location, revision):
                (folder / "scene.json").write_text(json.dumps(dict(
                    session=session, status=status, location=location, revision=revision,
                    journey_protocol=1, scratch_card_protocol=2)), encoding="utf-8")
            publish("ready", "dumping_ground", 2)
            server = StdioServerParameters(command=sys.executable, args=["-B",
                str(Path(__file__).resolve().parents[1] / "mcp" / "ledger_mcp.py"), "--journal", temp,
                "--database", str(folder / "ledger.sqlite")])
            inbox = folder / f"journey-{session}.json"
            async with stdio_client(server) as streams, ClientSession(*streams) as client:
                await client.initialize()
                submitted = asyncio.create_task(client.call_tool("explore", dict(
                    mode="return", session=session, revision=2, view_token="tok1", abandon_ground=True)))
                async with asyncio.timeout(5):
                    while not inbox.exists():
                        await asyncio.sleep(.02)
                plan = json.loads(inbox.read_text())
                self.assertEqual((plan["tool"], plan["action"], plan["abandon_ground"]), ("explore", "return", True))
                receipt = dict(command_id=plan["command_id"], generation=session, status="return_requested", abandoned_ground_count=4)
                (folder / f"receipt-{plan['command_id']}.json").write_text(json.dumps(receipt))
                inbox.unlink()
                self.assertEqual(json.loads((await submitted).content[0].text), receipt)

                publish("ready", "pawnshop", 3)
                bad_close = await client.call_tool("scratch_card", dict(
                    item_id=1, save_id=122, mode="close", position=1, session=session, revision=3, view_token="tok2"))
                self.assertTrue(bad_close.isError)
                closing = asyncio.create_task(client.call_tool("scratch_card", dict(
                    item_id=1, save_id=122, mode="close", session=session, revision=3, view_token="tok2")))
                async with asyncio.timeout(5):
                    while not inbox.exists():
                        await asyncio.sleep(.02)
                plan = json.loads(inbox.read_text())
                self.assertEqual((plan["tool"], plan["action"], plan["position"]), ("scratch_card", "close", None))
                receipt = dict(command_id=plan["command_id"], generation=session, status="closed", view_token="tok3")
                (folder / f"receipt-{plan['command_id']}.json").write_text(json.dumps(receipt))
                inbox.unlink()
                self.assertEqual(json.loads((await closing).content[0].text), receipt)

                bad_reveal_all = await client.call_tool("scratch_card", dict(
                    item_id=1, save_id=122, mode="reveal_all", position=1, session=session, revision=3, view_token="tok3"))
                self.assertTrue(bad_reveal_all.isError)
                revealing = asyncio.create_task(client.call_tool("scratch_card", dict(
                    item_id=1, save_id=122, mode="reveal_all", session=session, revision=3, view_token="tok3")))
                async with asyncio.timeout(5):
                    while not inbox.exists():
                        await asyncio.sleep(.02)
                plan = json.loads(inbox.read_text())
                self.assertEqual((plan["tool"], plan["action"], plan["position"]), ("scratch_card", "reveal_all", None))
                receipt = dict(command_id=plan["command_id"], generation=session, status="revealed",
                               symbols=[{"position": 1, "symbol": {"id": 1, "texture": "fruit"}}], view_token="tok4")
                (folder / f"receipt-{plan['command_id']}.json").write_text(json.dumps(receipt))
                inbox.unlink()
                self.assertEqual(json.loads((await revealing).content[0].text), receipt)

    def test_feedback_box_compact_and_verbose(self):
        sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "mcp"))
        from journey import _compact_feedback
        raw = {
            "status": "baseline",
            "reputation_scale": 200,
            "factions": [{
                "id": "lower",
                "name": "Lower",
                "reputation": "5.00",
                "standing": "neutral",
                "effects": [
                    {"id": "p0", "name": "Active", "description": "long active desc", "threshold": 0, "always_active": True, "active": True},
                    {"id": "p20", "name": "NextPos", "description": "long pos 20", "threshold": 20, "always_active": False, "active": False},
                    {"id": "p100", "name": "FarPos", "description": "long pos 100", "threshold": 100, "always_active": False, "active": False},
                    {"id": "n20", "name": "NextNeg", "description": "long neg -20", "threshold": -20, "always_active": False, "active": False},
                    {"id": "n100", "name": "FarNeg", "description": "long neg -100", "threshold": -100, "always_active": False, "active": False},
                ],
            }],
        }
        compact = _compact_feedback(raw)
        f = compact["factions"][0]
        self.assertEqual([e["id"] for e in f["active_effects"]], ["p0"])
        self.assertEqual(f["next_thresholds"], [
            {"id": "p20", "name": "NextPos", "threshold": 20},
            {"id": "n20", "name": "NextNeg", "threshold": -20},
        ])
        self.assertEqual(f["hidden_inactive_count"], 2)
        self.assertNotIn("effects", f)
