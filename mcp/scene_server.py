"""Expose only tools for the current game area on this stdio connection."""
import json
import sqlite3

import anyio
from mcp.server.fastmcp import FastMCP
from mcp.server.lowlevel.server import NotificationOptions
from mcp.server.stdio import stdio_server

from journey import ACTION_REGIONS, REGIONS
from scene import read_scene, shop_visit, tool_region


class SceneServer(FastMCP):
    def __init__(self, folder):
        super().__init__("白龍店帳")
        self.folder = folder
        self.global_tools = {"get_scene", "wait_for_scene"}
        self.ledger_tools = {"get_shop_ledger", "dismiss_trade_ticket"}
        self.client_session = None
        self.listed_region = None

    async def list_tools(self):
        scene = read_scene(self.folder)
        region = tool_region(scene)
        self.client_session = self.get_context().session
        self.listed_region = region
        registered = await super().list_tools()
        return [tool for tool in registered if self.available(tool.name, region)]

    def available(self, name, region):
        if name in self.global_tools:
            return True
        if name in REGIONS:
            return REGIONS[name] == region
        return region == "pawnshop" or (region == "offline" and name in self.ledger_tools)

    async def call_tool(self, name, arguments):
        if name in ACTION_REGIONS:
            # A departure can finish before the client receives its reply. The command
            # layer can recover an existing receipt across regions, then gates new work.
            return await super().call_tool(name, arguments)
        if name not in self.global_tools:
            scene = read_scene(self.folder)
            region = tool_region(scene)
            if not self.available(name, region):
                return {**scene, "tool_status": "unavailable_in_scene"}
            if name in REGIONS or region == "offline":
                return await super().call_tool(name, arguments)
            token = shop_visit.set((scene["session"], scene["revision"]))
            try:
                mutations = {"move_item","move_items","sell_order","trade_batch","confirm_trade","buy_merchant_batch",
                             "pay_info_receipt","open_shop","close_shop","ring_customer","dismiss_customer",
                             "advance_dialogue","choose_dialogue","response_choice","prepare_sleep","night_service",
                             "set_auto_receive","sleep","transfer_slot_item","press_machine_button","put_purifier_bottle",
                             "take_purifier_bottle","water_use","water_panel","dial_contact","phone_keypad","choose_phone_service",
                             "advance_phone_dialogue","inspect_archive","inspect_item_window","wait_for_opening"}
                if (name in mutations or (name == "wild_network" and arguments.get("mode", "read") != "read")) and self.updates.cursor is not None:
                    tool = self._tool_manager.get_tool(name)
                    result = await tool.run(arguments, context=self.get_context())
                    try:
                        result["shop_update"] = self.updates.report()
                    except (OSError, sqlite3.Error, json.JSONDecodeError) as error:
                        # The action may already have settled. Keep its receipt even when the next read fails.
                        result["shop_update"] = {"status":"update_unavailable", "detail":str(error)}
                    return tool.fn_metadata.convert_result(result)
                return await super().call_tool(name, arguments)
            finally:
                shop_visit.reset(token)
        return await super().call_tool(name, arguments)

    async def notify_scene_changes(self):
        while True:
            await anyio.sleep(.2)
            if self.client_session is None:
                continue
            scene = read_scene(self.folder)
            if scene["status"] == "reading_scene":
                continue
            region = tool_region(scene)
            if region != self.listed_region:
                self.listed_region = region
                await self.client_session.send_tool_list_changed()

    async def run_stdio_async(self):
        options = self._mcp_server.create_initialization_options(NotificationOptions(tools_changed=True))
        async with stdio_server() as streams, anyio.create_task_group() as workers:
            workers.start_soon(self.notify_scene_changes)
            try:
                await self._mcp_server.run(*streams, options)
            finally:
                workers.cancel_scope.cancel()
