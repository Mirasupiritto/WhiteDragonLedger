"""Receive one customer, inspect the visible offer and execute a capped purchase."""
import json
import re
import time
from uuid import NAMESPACE_URL, uuid5

from moves import exchange
from shopdesk import published


def context(folder):
    view = published(folder)
    if view["status"] != "observed":
        return view
    if view.get("counter_protocol") != 2:
        return {"status": "reload_updated_mod", "slot": view["slot"]}
    return {**{key: view[key] for key in ("status", "generation", "revision", "slot", "phase")}, **view["counter"]}


def phone_context(folder):
    view = published(folder)
    if view["status"] != "observed":
        return view
    if "phone" not in view:
        return {"status": "reload_updated_mod", "slot": view["slot"]}
    return {**{key: view[key] for key in ("status", "generation", "revision", "slot", "phase")}, **view["phone"]}


def command(folder, action, generation, revision, **args):
    if not re.fullmatch("[0-9a-f]{32}", generation) or revision < 1:
        raise ValueError("Expected generation and revision from get_counter")
    plan = dict(action=action, generation=generation, revision=revision, **args)
    token = uuid5(NAMESPACE_URL, "white-dragon/counter/"+json.dumps(plan, sort_keys=True)).hex
    def receipt():
        try:
            data = json.loads((folder/f"receipt-{token}.json").read_text(encoding="utf-8-sig"))
        except (FileNotFoundError, PermissionError, json.JSONDecodeError):
            return None
        if data["command_id"] != token or data["generation"] != generation:
            raise ValueError("Receipt belongs to another command")
        return data
    result = receipt()
    if result is not None:
        return result
    phone_action = action in {"dial", "phone_choice", "phone_advance"}
    view = phone_context(folder) if phone_action else context(folder)
    if view["status"] != "observed":
        return view
    if view["generation"] != generation or view["revision"] != revision:
        return {"status": "stale_observation"}
    if phone_action and view["phone_token"] != args["phone_token"]:
        return {"status": "phone_changed"}
    if action == "open_shop":
        if "can_open_shop" not in view:
            return {"status": "reload_updated_mod"}
        if not view["can_open_shop"]:
            return {"status": "shutter_unavailable", "phase": view["phase"]}
    elif not phone_action and view["phase"] != "OPEN":
        return {"status": "shop_closed"}
    return exchange(folder, folder/f"counter-{generation}.json", dict(plan, command_id=token), receipt)


def wait_opening(folder, generation, visit, timeout=25):
    deadline = time.monotonic() + timeout
    while True:
        view = context(folder)
        if view["status"] != "observed":
            return view
        if view["generation"] != generation or view["visit"] != visit:
            return {"status": "visit_changed"}
        stage = view["stage"]
        if stage not in {"arriving", "awaiting_opening", "speaking", "advance_required", "tablet_closed", "no_goods"}:
            return view
        if time.monotonic() >= deadline:
            return {**view, "status": "still_waiting"}
        if stage in {"advance_required", "tablet_closed"}:
            action = "advance" if stage == "advance_required" else "tablet"
            extra = {"page": view["page"]} if action == "advance" else {}
            result = command(folder, action, generation, view["revision"], visit=visit, **extra)
            if result["status"] not in {"advanced", "tablet_requested", "page_changed", "stale_observation", "ready", "speaking", "awaiting_opening", "choice_required"}:
                return result
        time.sleep(0.2)


def ring(folder, generation, revision, waiting):
    result = command(folder, "ring", generation, revision, waiting=waiting)
    if result["status"] != "called":
        return result
    view = wait_opening(folder, generation, result["visit"])
    return {**view, "bell_receipt": result}
