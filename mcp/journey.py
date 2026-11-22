"""On-demand game actions scoped to the current scene; uses no shop journal."""
import json
import re
from typing import Literal
from uuid import NAMESPACE_URL, uuid4, uuid5

from command_transport import exchange, read_command_receipt
from scene import read_scene, tool_region

ACTION_REGIONS = {"shop_activity": "pawnshop", "travel_menu": "destination_selection", "explore": "afterhours"}
REGIONS = {**ACTION_REGIONS, "get_travel_options": "destination_selection"}


def command(folder, tool, mode, session, revision, view_token, **args):
    scene = read_scene(folder)
    reading = mode == "read"
    if reading:
        if tool_region(scene) != REGIONS[tool]:
            return {**scene, "tool_status": "unavailable_in_scene"}
        session, revision = scene.get("session", ""), scene.get("revision", 0)
    if not re.fullmatch("[0-9a-f]{32}", session) or revision < 1 or (not reading and not view_token):
        return {"status": "observation_required"}
    plan = dict(tool=tool, action=mode, generation=session, revision=revision, view_token=view_token, **args)
    command_id = (uuid4() if reading else uuid5(NAMESPACE_URL, "ledger/journey/" + json.dumps(plan, sort_keys=True))).hex

    def receipt():
        return read_command_receipt(folder, command_id, session)

    previous = receipt()
    if previous is not None:
        return previous
    if scene.get("session") != session or scene.get("revision") != revision or tool_region(scene) != REGIONS[tool]:
        return {"status": "scene_changed", "scene": scene}
    if scene.get("journey_protocol") != 1:
        return {"status": "reload_updated_mod"}
    return exchange(folder, folder / f"journey-{session}.json", {**plan, "command_id": command_id}, receipt)


def register(server, folder):
    @server.tool()
    def shop_activity(mode: Literal["read", "door", "confirm_packing", "close_report", "equip", "holster", "fire"] = "read",
                      session: str = "", revision: int = 0, view_token: str = "", visit: str = "",
                      confirm_fire: bool = False) -> dict:
        """Inspect shop door, packing confirmation, report and current in-game encounter. Read before acting.
        After confirming packing, use the counter response choices. Fire requires the player's identification and current visit."""
        return command(folder, "shop_activity", mode, session, revision, view_token, visit=visit, confirm_fire=confirm_fire)

    @server.tool()
    def travel_menu(mode: Literal["read", "previous", "next", "select", "depart", "close"] = "read",
                    session: str = "", revision: int = 0, view_token: str = "", location_id: str = "") -> dict:
        """Read active destination cards. Select or depart only for the displayed location ID; wait_for_scene confirms arrival."""
        return command(folder, "travel_menu", mode, session, revision, view_token, location_id=location_id)

    @server.tool()
    def explore(mode: Literal["read", "move", "open_bag", "close_bag", "scavenge", "return"] = "read",
                session: str = "", revision: int = 0, view_token: str = "", item_id: int = 0,
                source: str = "", target: str = "") -> dict:
        """Read visible night inventory, pockets, bag slots and scavenging conditions. Move one observed item
        by its scene-local game ID and slot keys, or request one night action. Collect the ground before returning."""
        return command(folder, "explore", mode, session, revision, view_token, item_id=item_id, source=source, target=target)
