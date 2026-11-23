"""On-demand game actions scoped to the current scene; uses no shop journal."""
import json
import re
from typing import Literal
from uuid import NAMESPACE_URL, uuid4, uuid5

from command_transport import exchange, read_command_receipt
from scene import read_scene, tool_region

ACTION_REGIONS = {"scratch_card": "pawnshop", "shop_activity": "pawnshop", "travel_menu": "destination_selection", "explore": "afterhours"}
ACTION_REGIONS.update(choose_specialization="new_game", starting_perks="new_game_perks",
                      starting_difficulty="new_game_difficulty")
REGIONS = {**ACTION_REGIONS, "feedback_box": "pawnshop", "get_specializations": "new_game", "get_travel_options": "destination_selection"}


def command(folder, tool, mode, session, revision, view_token, **args):
    scene = read_scene(folder)
    reading = mode == "read"
    if reading:
        if tool_region(scene) != REGIONS[tool]:
            return {**scene, "tool_status": "unavailable_in_scene"}
        session, revision = scene.get("session", ""), scene.get("revision", 0)
    if not re.fullmatch("[0-9a-f]{32}", session) or revision < 1 or (not reading and not view_token):
        return {"status": "observation_required"}
    native_tool = "specializations" if tool in {"get_specializations", "choose_specialization"} else tool
    plan = dict(tool=native_tool, action=mode, generation=session, revision=revision, view_token=view_token, **args)
    command_id = (uuid4() if reading else uuid5(NAMESPACE_URL, "ledger/journey/" + json.dumps(plan, sort_keys=True))).hex

    def receipt():
        return read_command_receipt(folder, command_id, session)

    if re.fullmatch("[0-9a-f]{32}", session) and revision >= 1 and (reading or view_token):
        previous = receipt()
        if previous is not None:
            return previous
    if tool_region(scene) != REGIONS[tool]:
        if (not reading and re.fullmatch("[0-9a-f]{32}", session)
                and scene.get("session") == session and scene.get("revision") != revision):
            return {"status": "scene_changed", "scene": scene}
        return {**scene, "tool_status": "unavailable_in_scene"}
    if not re.fullmatch("[0-9a-f]{32}", session) or revision < 1 or (not reading and not view_token):
        return {"status": "observation_required"}
    if scene.get("session") != session or scene.get("revision") != revision:
        return {"status": "scene_changed", "scene": scene}
    if scene.get("journey_protocol") != 1:
        return {"status": "reload_updated_mod"}
    if tool == "scratch_card" and scene.get("scratch_card_protocol") != 2:
        return {"status": "reload_updated_mod"}
    return exchange(folder, folder / f"journey-{session}.json", {**plan, "command_id": command_id}, receipt)


def _compact_feedback(result):
    if "factions" not in result:
        return result
    factions = []
    for faction in result["factions"]:
        effects = faction.get("effects", [])
        active = [e for e in effects if e.get("active")]
        inactive = [e for e in effects if not e.get("active") and "threshold" in e]
        pos = [e for e in inactive if e["threshold"] >= 0]
        neg = [e for e in inactive if e["threshold"] < 0]
        next_thresholds = []
        if pos:
            nearest = min(pos, key=lambda e: e["threshold"])
            next_thresholds.append({k: nearest[k] for k in ("id", "name", "threshold") if k in nearest})
        if neg:
            nearest = max(neg, key=lambda e: e["threshold"])
            next_thresholds.append({k: nearest[k] for k in ("id", "name", "threshold") if k in nearest})
        entry = {k: faction[k] for k in ("id", "name", "reputation", "standing") if k in faction}
        entry["active_effects"] = active
        if next_thresholds:
            entry["next_thresholds"] = next_thresholds
        hidden = len(inactive) - len(next_thresholds)
        if hidden > 0:
            entry["hidden_inactive_count"] = hidden
        factions.append(entry)
    return {**result, "factions": factions}


def register(server, folder):
    @server.tool()
    def scratch_card(item_id: int, save_id: int, mode: Literal["read", "open", "reveal", "reveal_all", "close"] = "read",
                     position: Literal[1, 2, 3] | None = None, session: str = "", revision: int = 0,
                     view_token: str = "") -> dict:
        """Inspect, open, reveal, reveal_all or close an owned inventory scratcher. Use reveal with position (1 left, 2 middle, 3 right)
        or reveal_all to scratch all remaining positions in one call. Read first, then reuse the latest session, revision and view_token.
        Close the panel before opening another card."""
        if (mode == "reveal") != (position is not None):
            raise ValueError("supply position only for reveal")
        return command(folder, "scratch_card", mode, session, revision, view_token,
                       item_id=item_id, save_id=save_id, position=position)

    @server.tool()
    def feedback_box(full: bool = False, verbose: bool = False) -> dict:
        """Read shop faction reputation, effects and thresholds. After a baseline, return only changes.
        A new save load starts a fresh baseline. Use full=True to repeat the full list.
        By default (verbose=False), inactive effects are summarized to the nearest positive and negative thresholds; pass verbose=True for every inactive description."""
        if read_scene(folder).get("feedback_protocol") != 1:
            return {"status": "reload_updated_mod"}
        result = command(folder, "feedback_box", "read", "", 0, "", full=full)
        if result["status"] == "unchanged":
            return {"message": "無變動"}
        return result if verbose else _compact_feedback(result)

    @server.tool()
    def starting_difficulty(mode: Literal["read", "select"] = "read", difficulty_id: str = "",
                            session: str = "", revision: int = 0, view_token: str = "") -> dict:
        """Compare all difficulty cards. Normal lists baseline effects; other cards list differences and notes.
        Select a displayed ID to highlight its card and stay on the difficulty screen."""
        return command(folder, "starting_difficulty", mode, session, revision, view_token, difficulty_id=difficulty_id)

    @server.tool()
    def starting_perks(mode: Literal["read", "add", "remove", "continue"] = "read", perk_id: str = "",
                       session: str = "", revision: int = 0, view_token: str = "") -> dict:
        """Read all starting traits, signed point costs, effects, exclusions, selection and limits.
        Add/remove one ID or continue to difficulty. Reuse the returned token and state after each edit."""
        return command(folder, "starting_perks", mode, session, revision, view_token, perk_id=perk_id)

    @server.tool()
    def get_specializations(view_token: str = "") -> dict:
        """Read all visible new-game specializations across pages in game language. Each row includes
        starting equipment, hint, description and eligibility. Pass the previous token for a compact recheck."""
        return command(folder, "get_specializations", "read", "", 0, view_token)

    @server.tool()
    def choose_specialization(scenario_id: str, session: str, revision: int, view_token: str) -> dict:
        """Choose a visible playable specialization and continue to the native confirmation or perks page."""
        return command(folder, "choose_specialization", "choose", session, revision, view_token, scenario_id=scenario_id)

    @server.tool()
    def shop_activity(mode: Literal["read", "door", "confirm_packing", "close_report", "equip", "holster", "fire"] = "read",
                      session: str = "", revision: int = 0, view_token: str = "", visit: str = "",
                      confirm_fire: bool = False) -> dict:
        """Inspect shop door, packing confirmation, report and current in-game encounter. Read before acting.
        If door or confirm_packing immediately opens bag selection, page and response_choices (including each backpack's capacity{w,h,free_cells,largest_free{w,h}}) are attached to the receipt;
        otherwise use get_trade_context. Fire requires the player's identification and current visit."""
        return command(folder, "shop_activity", mode, session, revision, view_token, visit=visit, confirm_fire=confirm_fire)

    @server.tool()
    def travel_menu(mode: Literal["read", "previous", "next", "select", "depart", "close"] = "read",
                    session: str = "", revision: int = 0, view_token: str = "", location_id: str = "") -> dict:
        """Read active destination cards. Select, previous, and next return the updated revision, view_token and view.
        Depart can select and depart in one call for any displayed available location_id; wait_for_scene confirms arrival."""
        return command(folder, "travel_menu", mode, session, revision, view_token, location_id=location_id)

    @server.tool()
    def explore(mode: Literal["read", "move", "open_bag", "close_bag", "scavenge", "return"] = "read",
                session: str = "", revision: int = 0, view_token: str = "", item_id: int = 0,
                source: str = "", target: str = "", abandon_ground: bool = False) -> dict:
        """Read visible night inventory, pockets, bag slots (each item includes size/slot/rotatable; view.capacities covers left/right/bag/ground), injury status and scavenging conditions (including scavenge_blocked_reason and native notification).
        Move one observed item by its scene-local game ID and slot keys (returns no_space with item_size and target_capacity when full). Use the returned view.slots and view_token for the next action.
        Collect the ground before returning, or pass abandon_ground=True with mode='return' to discard remaining ground items."""
        return command(folder, "explore", mode, session, revision, view_token, item_id=item_id, source=source,
                       target=target, abandon_ground=abandon_ground)
