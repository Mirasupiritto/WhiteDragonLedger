"""Receive one customer, inspect the visible offer and execute a capped purchase."""
import json
import re
import time
from uuid import NAMESPACE_URL, uuid4, uuid5

from command_transport import exchange
from receipt_store import replayed, read_command_receipt
import trade_journal
from shopdesk import published, strip_protocols


_seen_finished_heat = set()


def filter_finished_heat(folder, result):
    if not isinstance(result, dict):
        return result
    heat = result.get("heat")
    if not isinstance(heat, dict) or heat.get("status") == "running":
        return result
    key = (str(folder), result.get("generation"), heat.get("run_id"), heat.get("status"))
    if key in _seen_finished_heat:
        filtered = dict(result)
        filtered.pop("heat", None)
        return filtered
    _seen_finished_heat.add(key)
    return result


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


def visit_mismatch_status(current_visit, customer=None):
    status = "customer_left" if not current_visit else "customer_replaced"
    return {"status": status, "current_visit": current_visit, "customer": customer}


def wait_choices_ready(folder, view, timeout=1.5):
    if view.get("stage") != "choice_required" or "dialog_choices" not in view or view["dialog_choices"]:
        return view
    gen, visit = view.get("generation"), view.get("visit")
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        time.sleep(0.05)
        nxt = context(folder)
        if nxt.get("status") != "observed":
            return nxt
        if nxt.get("generation") != gen or nxt.get("visit") != visit:
            return nxt
        if nxt.get("stage") != "choice_required" or nxt.get("dialog_choices"):
            return nxt
    return context(folder)


def command(folder, action, generation, revision, *, database=None, **args):
    if not re.fullmatch("[0-9a-f]{32}", generation) or revision < 1:
        raise ValueError("Expected generation and revision from get_counter")
    plan = dict(action=action, generation=generation, revision=revision, **args)
    token = uuid5(NAMESPACE_URL, "white-dragon/counter/"+json.dumps(plan, sort_keys=True)).hex
    def receipt():
        return read_command_receipt(folder, token, generation, "counter")

    result = receipt()
    # Only this refusal is proof that the native dismiss action never ran.
    # Concurrent callers follow the same attempt IDs, including after reconnect.
    while action == "dismiss" and result is not None and result["status"] == "dismiss_unavailable":
        token = uuid5(NAMESPACE_URL, "white-dragon/retry-dismiss/"+token).hex
        result = receipt()
    # A settings change or unlocked inventory can permit sorting at the same revision.
    while action == "sort_inventory" and result is not None and result["status"] in {
        "inventory_sort_disabled", "external_sorter_active", "sort_unavailable",
    }:
        token = uuid5(NAMESPACE_URL, "white-dragon/retry-sort/"+token).hex
        result = receipt()
    # A rejected bell never admitted anyone. Retry at the same revision only once the published desk is ringable.
    if action == "ring" and result is not None:
        refused = {"customer_departing", "bell_unavailable", "occupied", "queue_changed"}
        while result is not None and result["status"] in refused:
            token = uuid5(NAMESPACE_URL, "white-dragon/retry-ring/"+token).hex
            previous = result
            result = receipt()
            if result is None:
                current = context(folder)
                if not (current.get("generation") == generation and current.get("revision") == revision
                        and current.get("stage") == "idle" and current.get("waiting") == args["waiting"]
                        and current.get("reception", {}).get("can_ring")):
                    return replayed(previous)
    if result is not None:
        return replayed(trade_journal.recover_recorded_receipt(database, result))
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
    needs_chemist = ((action=="item_panel" and (args.get("panel") or args.get("mode") in {"focus","close"}))
                     or args.get("explicit_reopen") or "quantity" in args or action=="request_quote")
    if needs_chemist and view.get("chemist_protocol")!=1:
        return {"status":"reload_updated_mod"}
    if action == "heat_control":
        if view.get("heat_control_protocol") != 1:
            return {"status":"reload_updated_mod", "need":"mod", "required_protocol":"heat_control_protocol"}
    elif action in {"choose_dialogue","response_choice","advance"}:
        if action != "advance" and view.get("dialogue_protocol") != 1:
            return {"status":"reload_updated_mod"}
        if view.get("visit") != args["visit"]:
            return visit_mismatch_status(view.get("visit"), view.get("customer"))
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
    elif action in {"item_panel","adjust_control"} or (action == "item_menu" and args.get("mode") == "targets") or (action == "press_control" and "tab_index" in args):
        if view.get("item_panel_protocol") != 1:
            return {"status": "reload_updated_mod"}
    elif action == "item_menu":
        if view.get("item_menu_protocol") != 1:
            return {"status": "reload_updated_mod"}
    elif action == "sort_inventory":
        if view.get("inventory_sort_protocol") != 1:
            return {"status": "reload_updated_mod"}
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
        if action != "inspect_window" and view["phase"] == "OPEN" and not view.get("item_menu_protocol"):
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
            return visit_mismatch_status(view.get("visit"), view.get("customer"))
        if view["revision"] >= revision and not view["reception"]["can_dismiss"]:
            reception = view["reception"]
            blockers = {
                "reason": reception.get("reason", "dismiss_unavailable"),
                "blocking_items": reception.get("blocking_items", []),
                "visit": view.get("visit"),
                "latest_revision": view["revision"],
            }
            if "token" in reception:
                blockers["reception_token"] = reception["token"]
            if "guidance" in reception:
                blockers["guidance"] = reception["guidance"]
            return {"status": "dismiss_unavailable", **blockers}
    if action == "barter":
        if view.get("barter_protocol") != 1:
            return {"status": "reload_updated_mod"}
        if view.get("visit") != args["visit"]:
            return visit_mismatch_status(view.get("visit"), view.get("customer"))
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


def wait_opening(folder, generation, visit, timeout=20):
    deadline = time.monotonic() + timeout
    tablet_attempt = uuid4().hex
    sent_tablet_rev = None
    while True:
        view = context(folder)
        if view["status"] != "observed":
            return view
        if view["generation"] != generation:
            return {"status": "stale_observation"}
        if view["visit"] != visit:
            return visit_mismatch_status(view.get("visit"), view.get("customer"))
        stage = view["stage"]
        tablet_pending = (
            stage == "not_selling"
            and (view.get("demand") or {}).get("status") == "panel_closed"
            and view.get("client_role") not in {"dialogue", "service"}
        )
        if stage not in {"arriving", "awaiting_opening", "speaking", "advance_required", "tablet_closed", "no_goods", "awaiting_information_receipt"} and not tablet_pending:
            ready_view = wait_choices_ready(folder, view)
            if ready_view.get("status") != "observed":
                return ready_view
            if ready_view.get("generation") != generation:
                return {"status": "stale_observation"}
            if ready_view.get("visit") != visit:
                return visit_mismatch_status(ready_view.get("visit"), ready_view.get("customer"))
            return strip_protocols(with_barter(folder, ready_view))
        if time.monotonic() >= deadline:
            return strip_protocols({**view, "status": "opening_timeout"})
        if stage == "advance_required":
            result = command(folder, "advance", generation, view["revision"], visit=visit, page=view["page"])
            if result["status"] not in {
                "advanced", "tablet_requested", "page_changed", "stale_observation", "ready", "speaking",
                "awaiting_opening", "choice_required", "information", "awaiting_information_receipt", "barter",
            }:
                return result
        elif (stage == "tablet_closed" or tablet_pending) and sent_tablet_rev != view["revision"]:
            sent_tablet_rev = view["revision"]
            result = command(folder, "tablet", generation, view["revision"], visit=visit, read_id=tablet_attempt)
            if result["status"] not in {
                "advanced", "tablet_requested", "page_changed", "stale_observation", "ready", "speaking",
                "awaiting_opening", "choice_required", "information", "awaiting_information_receipt", "barter",
            }:
                return result
        time.sleep(0.2)


def ring(folder, generation, revision, waiting, timeout=20):
    result = command(folder, "ring", generation, revision, waiting=waiting)
    if result["status"] != "called":
        return {**result, "bell_receipt": result}
    view = wait_opening(folder, generation, result["visit"], timeout=timeout)
    opening_status = view.get("status")
    status = "opening_timeout" if opening_status == "opening_timeout" else "called" if opening_status in {"observed", "still_waiting"} else opening_status
    merged = {**view, "status": status, "opening_status": opening_status, "bell_receipt": result}
    if "reception_token" not in merged:
        token = (merged.get("reception") or {}).get("token") or result.get("reception_token")
        if token is not None:
            merged["reception_token"] = token
    return merged
