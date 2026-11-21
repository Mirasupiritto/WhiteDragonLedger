"""Expose only tools for the current game area on this stdio connection."""
import anyio
from mcp.server.fastmcp import FastMCP
from mcp.server.lowlevel.server import NotificationOptions
from mcp.server.stdio import stdio_server

from scene import at_shop, read_scene, shop_visit


def tool_region(scene):
    if at_shop(scene):
        return "pawnshop"
    if scene.get("status") == "selecting_destination":
        return "destination_selection"
    return "scene_only"


class SceneServer(FastMCP):
    def __init__(self, folder):
        super().__init__("白龍店帳")
        self.folder = folder
        self.global_tools = {"get_scene", "wait_for_scene"}
        self.client_session = None
        self.listed_region = None

    async def list_tools(self):
        scene = read_scene(self.folder)
        region = tool_region(scene)
        self.client_session = self.get_context().session
        self.listed_region = region
        registered = await super().list_tools()
        if region == "pawnshop":
            return [tool for tool in registered if tool.name != "get_travel_options"]
        allowed = self.global_tools
        if region == "destination_selection":
            allowed = allowed | {"get_travel_options"}
        return [tool for tool in registered if tool.name in allowed]

    async def call_tool(self, name, arguments):
        if name != "get_travel_options" and name not in self.global_tools:
            scene = read_scene(self.folder)
            if not at_shop(scene):
                return {**scene, "tool_status": "unavailable_in_scene"}
            token = shop_visit.set((scene["session"], scene["revision"]))
            try:
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
