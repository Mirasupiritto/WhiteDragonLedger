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
                "-B", str(Path(__file__).resolve().parents[1]/"ledger_mcp.py"),
                "--journal", temp, "--database", str(database)])
            async with stdio_client(server) as streams, ClientSession(*streams, message_handler=handle) as client:
                init = await client.initialize()
                self.assertTrue(init.capabilities.tools.listChanged)
                tools = {t.name for t in (await client.list_tools()).tools}
                self.assertIn("water_use", tools)
                self.assertIn("dismiss_customer", tools)
                self.assertIn("water_panel", tools)
                self.assertIn("pay_info_receipt", tools)
                self.assertIn("get_phone_contacts", tools)
                self.assertIn("query_inventory", tools)
                self.assertNotIn("get_travel_options", tools)
                common = {"get_scene", "wait_for_scene"}
                options = {"destinations": [{"id": "forest", "available": True}], "selected_id": None}
                publish(2, "selecting_destination", "pawnshop", travel_options=options)
                await asyncio.wait_for(changes.get(), 4)
                self.assertEqual({t.name for t in (await client.list_tools()).tools}, common | {"get_travel_options"})
                async def read(name, **args):
                    result = await client.call_tool(name, args)
                    self.assertFalse(result.isError, result.content)
                    return json.loads(result.content[0].text)
                self.assertEqual((await read("get_travel_options"))["options"], options)
                self.assertNotIn("travel_options", await read("get_scene"))
                self.assertEqual((await read("query_inventory"))["tool_status"], "unavailable_in_scene")
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
                for name, args in (("query_inventory", {}), ("open_shop", {"generation":"old", "revision":1})):
                    reply = await client.call_tool(name, args)
                    self.assertFalse(reply.isError, reply.content)
                    self.assertEqual(json.loads(reply.content[0].text)["tool_status"], "unavailable_in_scene")
                self.assertFalse(database.exists())
                self.assertEqual({t.name for t in (await client.list_tools()).tools}, common)
                publish(5, "ready", "pawnshop")
                await asyncio.wait_for(changes.get(), 4)
                self.assertEqual({t.name for t in (await client.list_tools()).tools}, tools)
