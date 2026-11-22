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

    async def test_dynamic_list_cached_calls_and_arrival(self):
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
                self.assertTrue(init.capabilities.tools.listChanged)
                tools = {t.name for t in (await client.list_tools()).tools}
                self.assertIn("get_shop_ledger", tools)
                self.assertIn("dismiss_trade_ticket", tools)
                self.assertIn("water_use", tools)
                self.assertIn("dismiss_customer", tools)
                self.assertIn("water_panel", tools)
                self.assertIn("pay_info_receipt", tools)
                self.assertIn("barter_offer", tools)
                self.assertIn("get_phone_contacts", tools)
                self.assertIn("query_inventory", tools)
                self.assertIn("find_stock", tools)
                self.assertIn("list_stock_tags", tools)
                self.assertIn("get_stock_items", tools)
                self.assertNotIn("get_travel_options", tools)
                common = {"get_scene", "wait_for_scene"}
                options = {"destinations": [{"id": "forest", "available": True}], "selected_id": None}
                publish(2, "selecting_destination", "pawnshop", travel_options=options)
                await asyncio.wait_for(changes.get(), 4)
                self.assertEqual({t.name for t in (await client.list_tools()).tools}, common | {"get_travel_options", "travel_menu"})
                async def read(name, **args):
                    result = await client.call_tool(name, args)
                    self.assertFalse(result.isError, result.content)
                    return json.loads(result.content[0].text)
                self.assertEqual((await read("get_travel_options"))["options"], options)
                self.assertNotIn("travel_options", await read("get_scene"))
                self.assertEqual((await read("query_inventory"))["tool_status"], "unavailable_in_scene")
                self.assertEqual((await read("find_stock", owned=True))["tool_status"], "unavailable_in_scene")
                self.assertEqual((await read("wait_for_scene", session="test", after_revision=1, timeout=0))["wait_result"], "timeout")
                publish(3, "transitioning", "pawnshop")
                await asyncio.wait_for(changes.get(), 4)
                self.assertEqual((await read("get_travel_options"))["tool_status"], "unavailable_in_scene")
                self.assertEqual({t.name for t in (await client.list_tools()).tools}, common)
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
                self.assertEqual({t.name for t in (await client.list_tools()).tools}, common | {"explore"})
                publish(5, "ready", "pawnshop")
                await asyncio.wait_for(changes.get(), 4)
                self.assertEqual({t.name for t in (await client.list_tools()).tools}, tools)
                publish(6, "waiting_for_load", None)
                await asyncio.wait_for(changes.get(), 4)
                self.assertEqual({t.name for t in (await client.list_tools()).tools},
                                 common | {"get_shop_ledger", "dismiss_trade_ticket"})
                self.assertEqual((await read("get_shop_ledger", slot=4))["open_tickets"], [])
