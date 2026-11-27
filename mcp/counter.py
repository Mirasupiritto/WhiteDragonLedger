"""Receive one customer, inspect the visible offer and execute a capped purchase."""
from dataclasses import dataclass
import time
from typing import Any, Callable
from uuid import uuid4

from command_transport import (
    deterministic_id,
    exchange,
    is_hex32,
    poll,
    read_command_receipt,
    replayed,
    submit_command,
)
import trade_journal
from shopdesk import published, strip_protocols


from database import transaction


def filter_finished_heat(database, result):
    if not isinstance(result, dict):
        return result
    heat = result.get("heat")
    if not isinstance(heat, dict) or heat.get("status") == "running" or not heat.get("run_id"):
        return result
    with transaction(database) as db:
        if db.execute("INSERT OR IGNORE INTO notified_heat VALUES(?,?)",
                      (result["generation"], heat["run_id"])).rowcount:
            return result
    return {k: v for k, v in result.items() if k != "heat"}


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

    def check():
        nxt = context(folder)
        if nxt.get("status") != "observed":
            return nxt
        if nxt.get("generation") != gen or nxt.get("visit") != visit:
            return nxt
        if nxt.get("stage") != "choice_required" or nxt.get("dialog_choices"):
            return nxt
        return None

    matched, timed_out = poll(check, timeout=timeout, interval=0.05, sleep=time.sleep)
    return context(folder) if timed_out else matched


@dataclass(frozen=True)
class ActionSpec:
    channel: str = "counter"
    phone: bool = False
    phone_protocol: str | None = None
    requires_open: bool = True
    required_phase: str | None = None
    phase_error: str | None = None
    protocol: str | None = None
    protocol_error: dict[str, Any] | None = None
    requires_visit: bool = False
    requires_page: bool = False
    retry_statuses: frozenset[str] | None = None
    retry_namespace: str | None = None
    check: Callable[[dict, dict, int], dict | None] | None = None


def _check_report_client(view, args, _rev):
    if not view.get("report_protocol"):
        return {"status": "reload_updated_mod"}
    return None


def _check_item_menu(view, args, _rev):
    if args.get("mode") == "targets":
        if view.get("item_panel_protocol") != 1:
            return {"status": "reload_updated_mod"}
    elif view.get("item_menu_protocol") != 1:
        return {"status": "reload_updated_mod"}
    return None


def _check_press_control(view, args, _rev):
    if "tab_index" in args and view.get("item_panel_protocol") != 1:
        return {"status": "reload_updated_mod"}
    if view.get("workstation_protocol") != 1:
        return {"status": "reload_updated_mod"}
    if view["phase"] == "OPEN" and not view.get("item_menu_protocol"):
        return {"status": "shop_open"}
    return None


def _check_workstation_slot(view, args, _rev):
    if view.get("workstation_protocol") != 1:
        return {"status": "reload_updated_mod"}
    if view["phase"] == "OPEN" and not view.get("item_menu_protocol"):
        return {"status": "shop_open"}
    return None


def _check_transfer_slots(view, args, _rev):
    if view.get("batch_slot_protocol") != 1:
        return {"status": "reload_updated_mod"}
    return _check_workstation_slot(view, args, _rev)


def _check_open_shop(view, args, _rev):
    if "can_open_shop" not in view:
        return {"status": "reload_updated_mod"}
    if not view["can_open_shop"]:
        return {"status": "shutter_unavailable", "phase": view["phase"]}
    return None


def _check_water_panel(view, args, _rev):
    if args.get("mode") == "notice_close" and view.get("purifier_notice_protocol") != 1:
        return {"status": "reload_updated_mod"}
    if view.get("water_panel_protocol") != 1:
        return {"status": "reload_updated_mod"}
    return None


def _check_archive(action):
    def _inner(view, args, _rev):
        if view.get("archive_protocol") != 1:
            return {"status": "reload_updated_mod"}
        if (action == "close_archive" or args.get("open_if_closed")) and view["phase"] == "OPEN":
            return {"status": "shop_open"}
        return None
    return _inner


def _check_sleep(view, args, _rev):
    if "bill_token" in args and view.get("night_bill_protocol") != 1:
        return {"status": "reload_updated_mod"}
    if view["phase"] != "CLOSED":
        return {"status": "sleep_requires_closed_shop", "phase": view["phase"]}
    return None


def _check_dismiss(view, args, revision):
    if "can_dismiss" not in view.get("reception", {}):
        return {"status": "reload_updated_mod"}
    if view.get("visit") != args["visit"] or not view.get("visit"):
        return visit_mismatch_status(view.get("visit"), view.get("customer"))
    if view["revision"] >= revision and not view["reception"]["can_dismiss"]:
        reception = view["reception"]
        if (args.get("interrupt") and reception.get("reason") == "dialogue_in_progress"
                and view.get("stage") != "choice_required"):
            return None
        blockers = {
            "reason": reception.get("reason", "dismiss_unavailable"),
            "blocking_items": reception.get("blocking_items", []),
            "visit": view.get("visit"),
            "latest_revision": view["revision"],
        }
        if "reason_detail" in reception:
            blockers["reason_detail"] = reception["reason_detail"]
        if "token" in reception:
            blockers["reception_token"] = reception["token"]
        if "guidance" in reception:
            blockers["guidance"] = reception["guidance"]
        return {"status": "dismiss_unavailable", **blockers}
    return None


def _check_barter(view, args, _rev):
    if view.get("barter_protocol") != 1:
        return {"status": "reload_updated_mod"}
    if view.get("visit") != args["visit"]:
        return visit_mismatch_status(view.get("visit"), view.get("customer"))
    if view["stage"] != "barter":
        return {"status": view["stage"]}
    return None


def _check_reception(view, _args, _rev):
    if "reception" not in view:
        return {"status": "reload_updated_mod"}
    return None


ACTION_SPECS: dict[str, ActionSpec] = {
    "dial": ActionSpec(phone=True, requires_open=False),
    "phone_choice": ActionSpec(phone=True, requires_open=False),
    "phone_advance": ActionSpec(phone=True, requires_open=False),
    "phone_keypad": ActionSpec(phone=True, phone_protocol="keypad_protocol", requires_open=False),
    "report_client": ActionSpec(channel="report", requires_open=False, check=_check_report_client),
    "heat_control": ActionSpec(
        requires_open=False,
        protocol="heat_control_protocol",
        protocol_error={"status": "reload_updated_mod", "need": "mod", "required_protocol": "heat_control_protocol"},
    ),
    "choose_dialogue": ActionSpec(requires_open=False, protocol="dialogue_protocol", requires_visit=True, requires_page=True),
    "response_choice": ActionSpec(requires_open=False, protocol="dialogue_protocol", requires_visit=True, requires_page=True),
    "advance": ActionSpec(requires_open=False, requires_visit=True, requires_page=True),
    "prepare_sleep": ActionSpec(requires_open=False, required_phase="CLOSED", phase_error="sleep_requires_closed_shop", protocol="night_bill_protocol"),
    "night_service": ActionSpec(requires_open=False, required_phase="CLOSED", phase_error="sleep_requires_closed_shop", protocol="night_bill_protocol"),
    "wild_network": ActionSpec(requires_open=False, protocol="wild_network_protocol"),
    "visible_windows": ActionSpec(requires_open=False, protocol="trade_tools_protocol"),
    "item_panel": ActionSpec(requires_open=False, protocol="item_panel_protocol"),
    "adjust_control": ActionSpec(requires_open=False, protocol="item_panel_protocol"),
    "item_menu": ActionSpec(requires_open=False, check=_check_item_menu),
    "sort_inventory": ActionSpec(
        requires_open=False,
        protocol="inventory_sort_protocol",
        retry_statuses=frozenset({"inventory_sort_disabled", "external_sorter_active", "sort_unavailable"}),
        retry_namespace="white-dragon/retry-sort/",
    ),
    "toggle_poster": ActionSpec(requires_open=False, protocol="poster_protocol"),
    "open_shop": ActionSpec(requires_open=False, check=_check_open_shop),
    "water_use": ActionSpec(requires_open=False, protocol="water_use_protocol"),
    "water_panel": ActionSpec(requires_open=False, check=_check_water_panel),
    "inspect_archive": ActionSpec(requires_open=False, check=_check_archive("inspect_archive")),
    "close_archive": ActionSpec(requires_open=False, check=_check_archive("close_archive")),
    "inspect_window": ActionSpec(requires_open=False, protocol="workstation_protocol"),
    "transfer_slot": ActionSpec(requires_open=False, check=_check_workstation_slot),
    "transfer_slots": ActionSpec(requires_open=False, check=_check_transfer_slots),
    "press_control": ActionSpec(requires_open=False, check=_check_press_control),
    "sleep": ActionSpec(requires_open=False, check=_check_sleep),
    "dismiss": ActionSpec(
        retry_statuses=frozenset({"dismiss_unavailable"}),
        retry_namespace="white-dragon/retry-dismiss/",
        check=_check_dismiss,
    ),
    "barter": ActionSpec(check=_check_barter),
    "pay_info": ActionSpec(protocol="information_protocol"),
    "auto_receive": ActionSpec(check=_check_reception),
    "close_shop": ActionSpec(check=_check_reception),
    "ring": ActionSpec(
        retry_statuses=frozenset({"customer_departing", "bell_unavailable", "occupied", "queue_changed"}),
        retry_namespace="white-dragon/retry-ring/",
    ),
}
_DEFAULT_SPEC = ActionSpec()


def command(folder, action, generation, revision, *, database=None, **args):
    if not is_hex32(generation) or revision < 1:
        raise ValueError("Expected generation and revision from get_counter")
    spec = ACTION_SPECS.get(action, _DEFAULT_SPEC)
    plan = dict(action=action, generation=generation, revision=revision, **args)
    token = deterministic_id("white-dragon/counter/", plan)
    channel = spec.channel
    def receipt():
        return read_command_receipt(folder, token, generation, channel)

    result = receipt()
    if spec.retry_statuses and result is not None:
        while result is not None and result["status"] in spec.retry_statuses:
            token = deterministic_id(spec.retry_namespace, token)
            previous = result
            result = receipt()
            if action == "ring" and result is None:
                current = context(folder)
                if not (current.get("generation") == generation and current.get("revision") == revision
                        and current.get("stage") == "idle" and current.get("waiting") == args["waiting"]
                        and current.get("reception", {}).get("can_ring")):
                    return replayed(previous)
    if result is not None:
        return replayed(trade_journal.recover_recorded_receipt(database, result))
    view = phone_context(folder) if spec.phone else context(folder)
    if view["status"] != "observed":
        return view
    # A sale receipt can also lead the published desk by one frame. Unity checks
    # the requested revision, live visit and eligibility together before dismissal.
    visit_guarded = action == "dismiss" and view.get("dismiss_recheck_protocol") == 1
    if view["generation"] != generation or (view["revision"] != revision and not visit_guarded):
        return {"status": "stale_observation"}
    if spec.phone_protocol and not view.get(spec.phone_protocol):
        return {"status": "reload_updated_mod"}
    if spec.phone and view["phone_token"] != args["phone_token"]:
        return {"status": "phone_changed"}
    needs_chemist = ((action=="item_panel" and (args.get("panel") or args.get("mode") in {"focus","close"}))
                     or args.get("explicit_reopen") or "quantity" in args or action=="request_quote")
    if needs_chemist and view.get("chemist_protocol")!=1:
        return {"status":"reload_updated_mod"}
    if spec.protocol and view.get(spec.protocol) != 1:
        return dict(spec.protocol_error) if spec.protocol_error else {"status": "reload_updated_mod"}
    if spec.required_phase and view["phase"] != spec.required_phase:
        return {"status": spec.phase_error}
    if spec.requires_visit and view.get("visit") != args["visit"]:
        return visit_mismatch_status(view.get("visit"), view.get("customer"))
    if spec.requires_page and view.get("page") != args["page"]:
        return {"status": "page_changed"}
    if spec.requires_open and not spec.phone and view["phase"] != "OPEN":
        return {"status": "shop_closed"}
    if spec.check is not None:
        err = spec.check(view, args, revision)
        if err is not None:
            return err
    request = dict(plan, command_id=token)
    def send():
        return submit_command(folder, channel, generation, request, read_receipt=receipt, transport=exchange)
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
    tablet_attempts = 0
    sent_tablet_rev = None
    last_tablet_at = None

    def with_attempts(payload):
        if tablet_attempts > 0 and isinstance(payload, dict):
            return {**payload, "tablet_attempts": tablet_attempts}
        return payload

    while True:
        view = context(folder)
        if view["status"] != "observed":
            return with_attempts(view)
        if view["generation"] != generation:
            return with_attempts({"status": "stale_observation"})
        if view["visit"] != visit:
            return with_attempts(visit_mismatch_status(view.get("visit"), view.get("customer")))
        stage = view["stage"]
        tablet_pending = (
            stage == "not_selling"
            and (view.get("demand") or {}).get("status") == "panel_closed"
            and view.get("client_role") not in {"dialogue", "service"}
        )
        if stage not in {"arriving", "awaiting_opening", "speaking", "advance_required", "tablet_closed", "no_goods", "awaiting_information_receipt"} and not tablet_pending:
            ready_view = wait_choices_ready(folder, view)
            if ready_view.get("status") != "observed":
                return with_attempts(ready_view)
            if ready_view.get("generation") != generation:
                return with_attempts({"status": "stale_observation"})
            if ready_view.get("visit") != visit:
                return with_attempts(visit_mismatch_status(ready_view.get("visit"), ready_view.get("customer")))
            return with_attempts(strip_protocols(with_barter(folder, ready_view)))
        now = time.monotonic()
        if now >= deadline:
            return with_attempts(strip_protocols({**view, "status": "opening_timeout"}))
        if stage == "advance_required":
            result = command(folder, "advance", generation, view["revision"], visit=visit, page=view["page"])
            if result["status"] not in {
                "advanced", "tablet_requested", "page_changed", "stale_observation", "ready", "speaking",
                "awaiting_opening", "choice_required", "information", "awaiting_information_receipt", "barter",
            }:
                return with_attempts(result)
        elif (stage == "tablet_closed" or tablet_pending) and (
            sent_tablet_rev != view["revision"] or (last_tablet_at is not None and now - last_tablet_at >= 2.0)
        ):
            tablet_attempts += 1
            sent_tablet_rev = view["revision"]
            last_tablet_at = now
            result = command(
                folder, "tablet", generation, view["revision"], visit=visit, read_id=f"{tablet_attempt}-{tablet_attempts}"
            )
            if result["status"] not in {
                "advanced", "tablet_requested", "page_changed", "stale_observation", "ready", "speaking",
                "awaiting_opening", "choice_required", "information", "awaiting_information_receipt", "barter",
            }:
                return with_attempts(result)
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
