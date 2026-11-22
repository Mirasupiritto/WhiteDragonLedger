"""Receive one customer, inspect the visible offer and execute a capped purchase."""
import json
import re
import time
from uuid import NAMESPACE_URL, uuid4, uuid5

from command_transport import exchange, read_command_receipt
import trade_journal
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


def command(folder, action, generation, revision, *, database=None, **args):
    if not re.fullmatch("[0-9a-f]{32}", generation) or revision < 1:
        raise ValueError("Expected generation and revision from get_counter")
    plan = dict(action=action, generation=generation, revision=revision, **args)
    token = uuid5(NAMESPACE_URL, "white-dragon/counter/"+json.dumps(plan, sort_keys=True)).hex
    def receipt():
        return read_command_receipt(folder, token, generation)

    result = receipt()
    # Only this refusal is proof that the native dismiss action never ran.
    # Concurrent callers follow the same attempt IDs, including after reconnect.
    while action == "dismiss" and result is not None and result["status"] == "dismiss_unavailable":
        token = uuid5(NAMESPACE_URL, "white-dragon/retry-dismiss/"+token).hex
        result = receipt()
    if result is not None:
        return result
    phone_action = action in {"dial", "phone_choice", "phone_advance", "phone_keypad"}
    view = phone_context(folder) if phone_action else context(folder)
    if view["status"] != "observed":
        return view
    # A sale receipt can also lead the published desk by one frame. Unity checks
    # the requested revision, live visit and eligibility together before dismissal.
    visit_guarded = action == "dismiss" and view.get("dismiss_recheck_protocol") == 1
    if view["generation"] != generation or (view["revision"] != revision and not visit_guarded):
        return {"status": "stale_observation"}
    if action == "phone_keypad" and not view.get("keypad_protocol"):
        return {"status": "reload_updated_mod"}
    if phone_action and view["phone_token"] != args["phone_token"]:
        return {"status": "phone_changed"}
    if action in {"choose_dialogue","response_choice","advance"}:
        if action != "advance" and view.get("dialogue_protocol") != 1:
            return {"status":"reload_updated_mod"}
        if view.get("visit") != args["visit"]:
            return {"status":"visit_changed"}
        if view.get("page") != args["page"]:
            return {"status":"page_changed"}
    elif action in {"prepare_sleep","night_service"}:
        if view.get("night_bill_protocol") != 1:
            return {"status":"reload_updated_mod"}
        if view["phase"] != "CLOSED":
            return {"status":"sleep_requires_closed_shop"}
    elif action == "wild_network":
        if view.get("wild_network_protocol") != 1:
            return {"status":"reload_updated_mod"}
    elif action == "visible_windows":
        if view.get("trade_tools_protocol") != 1:
            return {"status":"reload_updated_mod"}
    elif action == "open_shop":
        if "can_open_shop" not in view:
            return {"status": "reload_updated_mod"}
        if not view["can_open_shop"]:
            return {"status": "shutter_unavailable", "phase": view["phase"]}
    elif action == "water_use":
        if view.get("water_use_protocol") != 1:
            return {"status": "reload_updated_mod"}
    elif action == "water_panel":
        if args.get("mode") == "notice_close" and view.get("purifier_notice_protocol") != 1:
            return {"status":"reload_updated_mod"}
        if view.get("water_panel_protocol") != 1:
            return {"status": "reload_updated_mod"}
    elif action in {"inspect_archive", "close_archive"}:
        if view.get("archive_protocol") != 1:
            return {"status": "reload_updated_mod"}
        if (action == "close_archive" or args.get("open_if_closed")) and view["phase"] == "OPEN":
            return {"status": "shop_open"}
    elif action in {"inspect_window", "transfer_slot", "transfer_slots", "press_control"}:
        if action == "transfer_slots" and view.get("batch_slot_protocol") != 1:
            return {"status": "reload_updated_mod"}
        if view.get("workstation_protocol") != 1:
            return {"status": "reload_updated_mod"}
        if action != "inspect_window" and view["phase"] == "OPEN":
            return {"status": "shop_open"}
    elif action == "sleep":
        if "bill_token" in args and view.get("night_bill_protocol") != 1:
            return {"status":"reload_updated_mod"}
        if view["phase"] != "CLOSED":
            return {"status":"sleep_requires_closed_shop", "phase":view["phase"]}
    elif not phone_action and view["phase"] != "OPEN":
        return {"status": "shop_closed"}
    if action == "dismiss":
        if "can_dismiss" not in view.get("reception", {}):
            return {"status": "reload_updated_mod"}
        if view.get("visit") != args["visit"] or not view.get("visit"):
            return {"status":"visit_changed"}
        if view["revision"] >= revision and not view["reception"]["can_dismiss"]:
            return {"status":"dismiss_unavailable"}
    if action == "barter":
        if view.get("barter_protocol") != 1:
            return {"status": "reload_updated_mod"}
        if view.get("visit") != args["visit"]:
            return {"status": "visit_changed"}
        if view["stage"] != "barter":
            return {"status": view["stage"]}
    if action == "pay_info" and view.get("information_protocol") != 1:
        return {"status": "reload_updated_mod"}
    if action in {"auto_receive", "close_shop"} and "reception" not in view:
        return {"status": "reload_updated_mod"}
    request = dict(plan, command_id=token)
    def send():
        return exchange(folder, folder/f"counter-{generation}.json", request, receipt)
    if database is not None and action in trade_journal.TOOLS:
        return trade_journal.dispatch(database, folder, trade_journal.TOOLS[action], request,
                                      view["slot"], view.get("customer"), send,
                                      observation=view.get("offer" if action == "buy" else "info_receipt"))
    return send()


def with_barter(folder, view):
    if view.get("stage") != "barter":
        return view
    result = command(folder, "barter", view["generation"], view["revision"], visit=view["visit"],
                     mode="read", read_id=uuid4().hex)
    if result.get("status") != "observed":
        return {**view, "status": result["status"]}
    return {**view, "revision": result["revision"], "barter": result["barter"]}


def wait_opening(folder, generation, visit, timeout=25):
    deadline = time.monotonic() + timeout
    while True:
        view = context(folder)
        if view["status"] != "observed":
            return view
        if view["generation"] != generation or view["visit"] != visit:
            return {"status": "visit_changed"}
        stage = view["stage"]
        if stage not in {"arriving", "awaiting_opening", "speaking", "advance_required", "tablet_closed", "no_goods", "awaiting_information_receipt"}:
            return with_barter(folder, view)
        if time.monotonic() >= deadline:
            return {**view, "status": "still_waiting"}
        if stage in {"advance_required", "tablet_closed"}:
            action = "advance" if stage == "advance_required" else "tablet"
            extra = {"page": view["page"]} if action == "advance" else {}
            result = command(folder, action, generation, view["revision"], visit=visit, **extra)
            if result["status"] not in {
                "advanced", "tablet_requested", "page_changed", "stale_observation", "ready", "speaking",
                "awaiting_opening", "choice_required", "information", "awaiting_information_receipt", "barter",
            }:
                return result
        time.sleep(0.2)


def ring(folder, generation, revision, waiting):
    result = command(folder, "ring", generation, revision, waiting=waiting)
    if result["status"] != "called":
        return result
    view = wait_opening(folder, generation, result["visit"])
    return {**view, "bell_receipt": result}
