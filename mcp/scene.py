"""Read the game-owned location receipt, without accessing any shop data."""
import asyncio
import json
from pathlib import Path
from contextvars import ContextVar


shop_visit = ContextVar("shop_visit", default=None)


def interrupted_shop_call(folder):
    expected = shop_visit.get()
    if expected is None:
        return None
    scene = read_scene(folder)
    if not at_shop(scene) or expected != (scene.get("session"), scene.get("revision")):
        return {"status": "scene_changed", "scene": scene}
    return None


def read_scene(folder: Path, *, include_options=False):
    try:
        scene = json.loads((folder / "scene.json").read_text(encoding="utf-8-sig"))
        if not include_options:
            scene.pop("travel_options", None)
        return scene
    except FileNotFoundError:
        return {"status": "waiting_for_scene_observer", "message": "請載入已啟用場景觀察的模組"}
    except (PermissionError, json.JSONDecodeError):
        return {"status": "reading_scene"}


def at_shop(scene):
    return scene.get("status") == "ready" and scene.get("location") == "pawnshop"


def tool_region(scene):
    if at_shop(scene):
        return "pawnshop"
    if scene.get("status") == "selecting_difficulty":
        return "new_game_difficulty"
    if scene.get("status") == "selecting_perks":
        return "new_game_perks"
    if scene.get("status") == "selecting_specialization":
        return "new_game"
    if scene.get("status") == "selecting_destination":
        return "destination_selection"
    if scene.get("status") in {"waiting_for_load", "waiting_for_scene_observer"}:
        return "offline"
    if scene.get("status") == "ready" and scene.get("location"):
        return "afterhours"
    return "scene_only"


def register_scene_tools(server, folder):
    @server.tool()
    def get_scene() -> dict:
        """Read the current location, transition destination, and most recent journey."""
        return read_scene(folder)

    @server.tool()
    def get_travel_options() -> dict:
        """Read the displayed destination cards, selected location and departure button."""
        scene = read_scene(folder, include_options=True)
        options = scene.pop("travel_options", None)
        if scene.get("status") != "selecting_destination":
            return {**scene, "tool_status": "unavailable_in_scene"}
        return {**scene, "options": options}

    @server.tool()
    async def wait_for_scene(session: str, after_revision: int, timeout: float = 20) -> dict:
        """Wait for the next ready location after a receipt, or report a new load or timeout."""
        if not 0 <= timeout <= 60:
            raise ValueError("timeout must be between 0 and 60 seconds")
        loop = asyncio.get_running_loop()
        deadline = loop.time() + timeout
        while True:
            scene = read_scene(folder)
            if scene.get("session") and scene["session"] != session:
                return {**scene, "wait_result": "new_session"}
            if scene.get("status") == "ready" and scene["revision"] > after_revision:
                return {**scene, "wait_result": "arrived"}
            remaining = deadline - loop.time()
            if remaining <= 0:
                return {**scene, "wait_result": "timeout"}
            await asyncio.sleep(min(.1, remaining))
