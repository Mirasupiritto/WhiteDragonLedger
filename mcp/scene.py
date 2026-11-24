"""Read the game-owned location receipt, without accessing any shop data."""
import asyncio
import json
import re
from functools import lru_cache
from pathlib import Path
from contextvars import ContextVar
from typing import Annotated
from pydantic import Field

from server_state import snapshot_cache


shop_visit = ContextVar("shop_visit", default=None)


def interrupted_shop_call(folder):
    expected = shop_visit.get()
    if expected is None:
        return None
    scene = read_scene(folder)
    if not at_shop(scene) or expected != (scene.get("session"), scene.get("revision")):
        return {"status": "scene_changed", "scene": scene}
    return None


@lru_cache(maxsize=4)
def _last_loader_audit(path, modified, size):
    # Player.log is recreated on startup. Only consult its audit while a scene receipt is missing or waiting.
    audit = None
    with path.open(encoding="utf-8", errors="replace") as log:
        for line in log:
            if "[ModLoader] audit: white_dragon_ledger " in line:
                audit = line.strip()
    if not audit or "running=False" not in audit:
        return None
    detail = re.search(r' note="(.*?)"', audit)
    trust = re.search(r' trust=(\S+)', audit)
    return {"status": "mod_not_loaded", "tool_region": "offline",
            "trust": trust.group(1) if trust else None,
            "message": "遊戲載入器記錄白龍店帳未啟動。請在遊戲模組選單確認啟用與授權，再重新啟動。",
            "reason": detail.group(1) if detail else "running=False"}


def read_scene(folder: Path, *, include_options=False):
    try:
        scene = snapshot_cache.read_json(folder / "scene.json")
        if not include_options:
            scene.pop("travel_options", None)
    except FileNotFoundError:
        scene = {"status": "waiting_for_scene_observer", "message": "等待白龍店帳回報場景"}
    except (PermissionError, json.JSONDecodeError):
        return {"status": "reading_scene"}

    if (scene.get("status") in {"waiting_for_load", "waiting_for_scene_observer"}
            and folder.name == "white_dragon_ledger" and folder.parent.name == "ModData"):
        log = folder.parent.parent / "Player.log"
        try:
            stat = log.stat()
            blocked = _last_loader_audit(log, stat.st_mtime_ns, stat.st_size)
            if blocked:
                return dict(blocked)
        except (FileNotFoundError, PermissionError):
            pass
    return scene


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
    if scene.get("status") in {"waiting_for_load", "waiting_for_scene_observer", "mod_not_loaded"}:
        return "offline"
    if scene.get("status") == "ready" and scene.get("location"):
        return "afterhours"
    return "scene_only"


def register_scene_tools(server, folder):
    @server.tool()
    def get_scene() -> dict:
        """Read the current location, transition destination, most recent journey, or the game_over reason shown by the game."""
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
    async def wait_for_scene(
        session: str,
        after_revision: int,
        timeout: Annotated[float, Field(default=20, ge=0, le=60, description="Maximum seconds to wait (0-60).")] = 20,
    ) -> dict:
        """Use session and after_revision from the last scene receipt. Wait (up to 60s) for the next ready location or interactive selection screen, or report a new load or timeout."""
        if not 0 <= timeout <= 60:
            raise ValueError("timeout must be between 0 and 60 seconds")
        loop = asyncio.get_running_loop()
        deadline = loop.time() + timeout
        while True:
            scene = read_scene(folder)
            status = scene.get("status")
            if status == "game_over":
                return {**scene, "tool_status":"game_over"}
            if status == "mod_not_loaded":
                return {**scene, "wait_result": "mod_not_loaded"}
            if status not in {"waiting_for_load", "transitioning", "reading_scene", "waiting_for_scene_observer"}:
                if scene.get("session") and scene["session"] != session:
                    return {**scene, "wait_result": "new_session"}
                if status in {"ready", "selecting_specialization", "selecting_perks", "selecting_difficulty"} and scene["revision"] > after_revision:
                    return {**scene, "wait_result": "arrived"}
            remaining = deadline - loop.time()
            if remaining <= 0:
                return {**scene, "wait_result": "timeout"}
            await asyncio.sleep(min(.1, remaining))
