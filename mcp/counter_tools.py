"""Counter admission, dialogue, negotiation, order, and network tools."""
import time
from typing import Literal
from uuid import uuid4

from pydantic import BaseModel, Field

from command_transport import poll
import counter
import database as db_mod
from flow import present_counter
import shop_day
import shopdesk
import trade_tools


class SaleLine(BaseModel):
    item_id: int = Field(gt=0)
    min_price: int = Field(ge=0)


def _summarize_wild_network(result, *, verbose=False):
    if verbose or not isinstance(result, dict) or result.get("status") != "observed":
        return result
    panel = result.get("panel")
    if not isinstance(panel, dict) or not isinstance(panel.get("services"), list):
        return result
    services = panel["services"]
    if len(services) <= 8:
        return result
    grouped = {}
    ready_count = unlocked_count = total_cost = ready_cost = 0
    for svc in services:
        cost = int(svc.get("cost") or 0)
        total_cost += cost
        ready = bool(svc.get("ready"))
        unlocked = bool(svc.get("unlocked"))
        if ready and not unlocked:
            ready_count += 1
            ready_cost += cost
        if unlocked:
            unlocked_count += 1
        cat = svc.get("tab") or "general"
        bucket = grouped.setdefault(cat, {"category": cat, "count": 0, "ready_count": 0,
                                          "unlocked_count": 0, "ready_cost": 0, "total_cost": 0})
        bucket["count"] += 1
        bucket["total_cost"] += cost
        if ready and not unlocked:
            bucket["ready_count"] += 1
            bucket["ready_cost"] += cost
        if unlocked:
            bucket["unlocked_count"] += 1
    out_panel = dict(panel)
    out_panel.pop("services", None)
    out_panel["service_summary"] = {
        "total_count": len(services),
        "ready_count": ready_count,
        "unlocked_count": unlocked_count,
        "locked_count": max(0, len(services) - ready_count - unlocked_count),
        "ready_total_cost": ready_cost,
        "total_cost": total_cost,
        "categories": list(grouped.values()),
        "summary_only": True,
    }
    return {**result, "panel": out_panel}


def register(server, folder, database, *, section_cache=None):
    state = getattr(server, "state", None)
    cache = section_cache if section_cache is not None else (
        state.section_cache if state is not None else (getattr(server, "section_cache", None) or shop_day.SectionCache())
    )

    def _present(payload, *, verbose=False, apply_barter=False, strip=False):
        return present_counter(
            payload,
            folder=folder,
            database=database,
            summarize_offer=trade_tools.summarize_offer,
            verbose=verbose,
            apply_barter=apply_barter,
            strip=strip,
        )

    @server.tool()
    def advance_dialogue(generation: str, revision: int, visit: str, page: str) -> dict:
        """Advance just the observed manual counter page. Get its page token from get_trade_context."""
        return counter.command(folder, "advance", generation, revision, visit=visit, page=page)

    @server.tool()
    def choose_dialogue(generation: str, revision: int, visit: str, page: str, choice_id: int) -> dict:
        """Select an enabled dialog_choices ID from the observed counter page."""
        return counter.command(folder, "choose_dialogue", generation, revision, visit=visit, page=page, choice_id=choice_id)

    @server.tool()
    def response_choice(generation: str, revision: int, page: str, choice_id: int, visit: str | None = None) -> dict:
        """Activate one visible response item. For nighttime bag selection use shop_generation, shop_revision, page, the choice id and visit=null from the door or packing receipt."""
        return counter.command(folder, "response_choice", generation, revision, visit=visit, page=page, choice_id=choice_id)

    @server.tool()
    def get_buyer_context() -> dict:
        """Read the current customer's visit token, remaining budget and revision before submitting an order."""
        return shopdesk.strip_protocols(shopdesk.buyer(folder))

    @server.tool()
    def sell_order(lines: list[SaleLine], generation: str, revision: int, visit: str,
                   stop_same_kind_on_budget: bool = False, bundle: bool = False) -> dict:
        """Sell only the listed owned items, each with its own minimum. Before staging, auto-returns any
        non-order owned counter items (excluding nuclear_waste) to inventory and reports cleared_items.
        counter_unavailable and stale_order always attach reason and blocking_items (with guidance for nuclear_waste_on_counter).
        Set bundle=True to stage all listed
        items onto the trade table together and settle in one click (rolling all items back to their original
        areas if any item is declined, below minimum, duplicate variety, or the combined quote exceeds budget).
        At a new revision of the same visit, a finished plan can be retried if any unsold item is still owned.
        If every item sold or left owned stock, the original finished receipt is returned.
        With bundle=False (default), sells one item at a time and can opt in to stop_same_kind_on_budget.
        A staged item waits through dialogue and a closed tablet. Reuse identical arguments (including the original
        revision) to read sale_pending or the final receipt with actual received and remaining_budget."""
        return shopdesk.sell_order(folder, [line.model_dump() for line in lines],
                                  generation, revision, visit, stop_same_kind_on_budget,
                                  database=database, bundle=bundle)

    @server.tool()
    def get_counter(verbose: bool = False) -> dict:
        """Read shop phase, waiting count, opening lines, and visible merchandise or the information dealer's receipt and price.
        Unchanged calendar/trash/sleep sections and large (>5 item) supplier offers return summaries unless verbose=True."""
        view = shopdesk.published(folder)
        if view["status"] != "observed" or view.get("counter_protocol") != 2:
            return counter.filter_finished_heat(database, counter.context(folder))
        if (view["counter"].get("stage") == "choice_required"
                and "dialog_choices" in view["counter"] and not view["counter"]["dialog_choices"]):
            counter.wait_choices_ready(folder, counter.context(folder))
            view = shopdesk.published(folder)
            if view["status"] != "observed" or view.get("counter_protocol") != 2:
                return counter.filter_finished_heat(database, counter.context(folder))
        day_info = {k: v for k, v in view["day_info"].items() if k in {"calendar", "trash", "sleep"}}
        result = {**{k: view[k] for k in ("status", "slot", "phase", "generation", "revision", "day")}, **view["counter"], **day_info}
        result["trash"] = shop_day.trash_contents(folder, database, result)
        result = cache.filter(result.get("generation"), result, verbose=verbose)
        return _present(result, verbose=verbose, apply_barter=True, strip=True)

    @server.tool()
    def ring_customer(generation: str, revision: int, waiting: int, verbose: bool = False) -> dict:
        """Ring once at an empty open counter, wait for the greeting (stopping at choices), and return status="called" with trade state.
        Large (>5 item) supplier offers return a summary unless verbose=True."""
        return _present(counter.ring(folder, generation, revision, waiting), verbose=verbose)

    @server.tool()
    def next_customer(generation: str | None = None, revision: int | None = None, waiting: int | None = None,
                      verbose: bool = False, timeout: float = 20.0, skip_current: str | None = None,
                      force: bool = False) -> dict:
        """One-step guest admission: if counter is idle, ring the bell, wait through opening dialogue and tablet opening (up to timeout seconds, default 20, returning opening_timeout if exceeded) and return status="called" with trade-ready state; if a customer was already at the counter before the call, finish opening and return status="already_present" with already_at_counter=True.
        Pass skip_current=<visit> to dismiss the current dismissible visitor mid-speech (interrupt=True) and immediately admit the next customer within the same timeout; stops without ringing if the visitor changed (customer_replaced), is protected (protected_visitor unless force=True), or cannot be dismissed.
        Auto-closes tool-opened item panels; player-opened panels return item_panel_open with current visit/customer/stage and after_close_next_tool.
        Large (>5 item) supplier offers return a summary unless verbose=True."""
        result = admit_customer(generation, revision, waiting, timeout, skip_current=skip_current, force=force)
        if isinstance(result, dict) and result.get("status") in {"called", "already_present"}:
            raw = shopdesk.published(folder)
            if isinstance(raw, dict) and raw.get("status") == "observed":
                dc = raw.get("counter") or {}
                db_mod.record_visited_client(database, raw.get("slot"), dc.get("customer_id"), dc.get("visit"), raw.get("day"))
        return _present(result, verbose=verbose)

    def admit_customer(generation, revision, waiting, timeout, skip_current=None, force=False):
        view = counter.context(folder)
        if view.get("status") != "observed":
            return view
        if ((generation is not None and generation != view["generation"])
                or (revision is not None and revision != view["revision"])):
            return {"status": "stale_observation", "generation": view["generation"], "latest_revision": view["revision"]}
        if view.get("phase") != "OPEN":
            return {"status": "shop_closed", "phase": view.get("phase"),
                    "generation": view["generation"], "revision": view["revision"],
                    "can_open_shop": view.get("can_open_shop")}
        deadline = time.monotonic() + min(20.0, max(0.0, timeout))
        start_generation = view["generation"]
        initially_at_counter = bool(view.get("visit"))
        skipped_already_left = False
        if skip_current is not None:
            if not view.get("visit"):
                initially_at_counter = False
                skipped_already_left = True
            elif view.get("visit") != skip_current:
                return counter.visit_mismatch_status(view.get("visit"), view.get("customer"))
            else:
                fin = finish_customer(view["generation"], view["revision"], skip_current, interrupt=True, force=force)
                if fin.get("status") not in {"dismissed", "customer_left"}:
                    return fin
                initially_at_counter = False
                view = counter.context(folder)
                if view.get("status") != "observed":
                    return view
                if view.get("generation") != start_generation:
                    return {"status": "stale_observation"}
                if view.get("phase") != "OPEN":
                    return {"status": "shop_closed", "phase": view.get("phase")}
        waited_for_bell = False
        ring_retries = 0
        while True:
            if view.get("stage") == "customer_departing":
                if time.monotonic() >= deadline:
                    return {**shopdesk.strip_protocols(view), "status": "customer_departing"}
                waited_for_bell = True
                time.sleep(min(0.15, max(0, deadline - time.monotonic())))
                view = counter.context(folder)
            elif view.get("stage") == "idle" and not view.get("visit"):
                if view.get("reception", {}).get("can_ring") is False and (view.get("waiting") or 0) > 0:
                    if time.monotonic() >= deadline:
                        return {**shopdesk.strip_protocols(view), "status": "bell_unavailable", "reason": "bell_cooldown_timeout"}
                    waited_for_bell = True
                    time.sleep(min(0.15, max(0, deadline - time.monotonic())))
                    view = counter.context(folder)
                else:
                    ring_wait = view.get("waiting", 0) if waiting is None else waiting
                    result = counter.ring(folder, view["generation"], view["revision"], ring_wait,
                                          timeout=max(0, deadline - time.monotonic()))
                    if result.get("status") not in {"occupied", "bell_unavailable", "customer_replaced", "customer_left", "queue_changed", "customer_departing"}:
                        if waited_for_bell and isinstance(result, dict):
                            result = {**result, "waited_for_bell": True}
                        if skipped_already_left and isinstance(result, dict):
                            result = {**result, "skipped_already_left": True}
                        return result
                    view = counter.context(folder)
                    in_cooldown = (view.get("stage") == "idle" and not view.get("visit")
                                   and view.get("reception", {}).get("can_ring") is False
                                   and (view.get("waiting") or 0) > 0)
                    retry_idle = (result.get("status") in {"customer_left", "queue_changed"}
                                  and view.get("status") == "observed"
                                  and view.get("generation") == start_generation
                                  and view.get("phase") == "OPEN"
                                  and view.get("stage") == "idle"
                                  and not view.get("visit")
                                  and (view.get("waiting") or 0) > 0
                                  and ring_retries < 3
                                  and time.monotonic() < deadline)
                    if retry_idle:
                        ring_retries += 1
                        continue
                    if not view.get("visit") and view.get("stage") != "customer_departing" and not in_cooldown:
                        return result
            else:
                break
            if view.get("status") != "observed":
                return view
            if view.get("generation") != start_generation:
                return {"status": "stale_observation"}
            if view.get("phase") != "OPEN":
                return {"status": "shop_closed", "phase": view.get("phase")}
        if not view.get("visit") and view.get("stage") == "arriving":
            def arrived():
                nxt = counter.context(folder)
                if nxt.get("status") != "observed" or nxt.get("visit") or nxt.get("stage") != "arriving":
                    return nxt
                return None

            nxt, timed_out = poll(arrived, timeout=min(5.0, max(0.0, deadline - time.monotonic())), interval=0.15, sleep=time.sleep)
            if not timed_out:
                if nxt.get("status") != "observed":
                    return nxt
                view = nxt
        if view.get("visit"):
            waited = counter.wait_opening(folder, view["generation"], view["visit"], timeout=max(0, deadline - time.monotonic()))
            if waited.get("status") == "observed":
                if initially_at_counter:
                    waited = {**waited, "status": "already_present", "already_at_counter": True}
                else:
                    waited = {**waited, "status": "called"}
                    if waited_for_bell:
                        waited["waited_for_bell"] = True
                    if skipped_already_left:
                        waited["skipped_already_left"] = True
            return waited
        return shopdesk.strip_protocols(dict(view))

    @server.tool()
    def finish_customer(generation: str, revision: int, visit: str, interrupt: bool = False, force: bool = False) -> dict:
        """Pass generation, revision and visit from the counter receipt. Dismiss this visitor. Set interrupt=True to dismiss a dismissible visitor mid-speech (still blocked on choice pages, active orders, and protected visitors). Story NPCs and first-visit named visitors return status="protected_visitor" (reason="story_npc" or "first_visit_named") unless force=True. A brief native lock is retried once if the same visitor becomes dismissible.
        If blocked, returns status="dismiss_unavailable" with reason, blocking_items, reception_token and latest_revision; if visitor left or changed, returns customer_left or customer_replaced."""
        dismiss_args = {"visit": visit}
        if interrupt:
            dismiss_args["interrupt"] = True
        if force:
            dismiss_args["force"] = True
        elif database is not None:
            raw = shopdesk.published(folder)
            if isinstance(raw, dict) and raw.get("status") == "observed":
                dc = raw.get("counter") or {}
                if dc.get("visit") == visit and db_mod.is_visited_client_known(
                    database, raw.get("slot"), dc.get("customer_id"), visit, raw.get("day")
                ):
                    dismiss_args["visited_known"] = True
        result = counter.command(folder, "dismiss", generation, revision, **dismiss_args)
        if result.get("status")=="dismiss_unavailable" and result.get("reason") in {"client_not_dismissable","bell_locked","dialogue_in_progress"} and result.get("reason_detail")!="story_npc":
            replaced = None

            def try_dismiss():
                nonlocal replaced
                current=counter.context(folder)
                if current.get("generation")!=generation or current.get("status")!="observed":
                    return result
                if current.get("visit")!=visit:
                    replaced = counter.visit_mismatch_status(current.get("visit"),current.get("customer"))
                    return replaced
                can_now = current.get("reception", {}).get("can_dismiss") or (
                    interrupt and current.get("reception", {}).get("reason") == "dialogue_in_progress"
                    and current.get("stage") != "choice_required"
                )
                if can_now:
                    return counter.command(folder,"dismiss",generation,revision,**dismiss_args)
                return None

            retried, timed_out = poll(try_dismiss, timeout=1.5, interval=0.1, sleep=time.sleep)
            if replaced is not None:
                return replaced
            if not timed_out:
                result = retried
        if result.get("status") in {"dismissed", "customer_left", "customer_replaced"}:
            current = counter.context(folder)
            if current.get("generation") == generation and current.get("phase") == "OPEN":
                if current.get("visit") and current["visit"] != visit:
                    result = {**result, "next_customer": _present(current, strip=True),
                              "next_stage": current["stage"], "next_action": "接續新客人的當前狀態。"}
                elif current.get("stage") == "customer_departing":
                    result = {**result, "next_stage": current["stage"], "next_action": current["next_action"]}
            if "next_customer" not in result and (result.get("waiting") or 0) > 0:
                result = {**result, "next_tool": "next_customer"}
        return result

    @server.tool()
    def request_supplier_quote(generation: str, revision: int, visit: str, item_id: int) -> dict:
        """Ask for a single retail supplier item price without paying. Briefly stage it on an empty counter, then return it to front.
        Check quote.price and returned_to_front before deciding on a purchase."""
        return counter.command(folder,"request_quote",generation,revision,visit=visit,item_id=item_id)

    @server.tool()
    def reopen_trade_tablet(generation: str, revision: int, visit: str) -> dict:
        """Reopen the native negotiation tablet for the same waiting buyer or supplier after closing an item panel.
        Requires the original visit. Leaves dialogue, pending confirmation and completed business alone."""
        return counter.command(folder,"tablet",generation,revision,visit=visit,explicit_reopen=True,read_id=uuid4().hex)

    @server.tool()
    def wait_for_opening(generation: str, visit: str) -> dict:
        """Continue waiting for this visit, turning finished ordinary pages and opening the trade tablet."""
        return counter.wait_opening(folder, generation, visit)

    @server.tool()
    def barter_offer(mode: Literal["select", "accept"], option: int, generation: str, revision: int,
                     visit: str, barter_token: str, payment_item_id: int | None = None) -> dict:
        """Select a numbered reward bundle from get_trade_context without exchanging anything.
        To accept, stage one food item with move_item, reread the quote, then supply its payment item ID."""
        if mode == "accept" and not payment_item_id:
            return {"status": "payment_item_required"}
        return counter.command(folder, "barter", generation, revision, visit=visit, mode=mode, option=option,
                               barter_token=barter_token, payment_item_id=payment_item_id)

    @server.tool()
    def buy_merchant_batch(generation: str, revision: int, visit: str, offer_token: str, max_total: int) -> dict:
        """Buy the observed entire offer at or below max_total. Reuse identical inputs to recover a receipt."""
        if max_total < 0:
            raise ValueError("Expected a non-negative batch price limit")
        return counter.command(folder, "buy", generation, revision, visit=visit,
                               offer_token=offer_token, max_total=max_total, database=database)

    @server.tool()
    def pay_info_receipt(generation: str, revision: int, visit: str, receipt_token: str, price: int) -> dict:
        """Buy information at the exact visible price. Returns actual paid and cash_after. Repeat identical inputs for the receipt."""
        if price < 0 or price > 2147483647:
            raise ValueError("Expected a game credit amount")
        return counter.command(folder, "pay_info", generation, revision, visit=visit,
                               receipt_token=receipt_token, price=price, database=database)

    @server.tool()
    def open_shop(generation: str, revision: int) -> dict:
        """Open the observed morning shutter once. Does not ring or close the shop."""
        return counter.command(folder, "open_shop", generation, revision)

    @server.tool()
    def dismiss_customer(generation: str, revision: int, visit: str) -> dict:
        """Dismiss this visit when reception.can_dismiss is true. Automatically returns non-order owned counter items (excluding nuclear_waste) to inventory as cleared_items without blocking dismissal. A newer inventory revision rechecks the same visit. After dismiss_unavailable (which always attaches reason and blocking_items), retry the original arguments when this visit can leave. A completed dismissal returns its original receipt."""
        return counter.command(folder, "dismiss", generation, revision, visit=visit)

    @server.tool()
    def set_auto_receive(enabled: bool, generation: str, revision: int, reception_token: str) -> dict:
        """Enable or disable automatic admission for this open day. Only an empty counter can take the next guest."""
        return counter.command(folder, "auto_receive", generation, revision, enabled=enabled, reception_token=reception_token)

    @server.tool()
    def close_shop(generation: str, revision: int, reception_token: str, confirm_remaining: int | None = None) -> dict:
        """Request closing. For early closing read waiting, then submit that exact count and the new reception token."""
        if confirm_remaining is not None and confirm_remaining < 0:
            raise ValueError("Expected the displayed remaining queue count")
        return counter.command(folder, "close_shop", generation, revision, reception_token=reception_token,
                               confirm_remaining=confirm_remaining)

    @server.tool()
    def wild_network(mode: Literal["read", "tab", "select", "buy", "close"] = "read",
                     generation: str | None = None, revision: int | None = None, panel_token: str | None = None,
                     target: str | None = None, cost: int | None = None, since_token: str | None = None,
                     verbose: bool = False) -> dict:
        """Read Joe's visible network catalog on entry and after changing tabs. Large catalogs (>8 services) return service_summary by category unless verbose=True. Pass since_token for an unchanged check.
        Select and buy return selected details (including favor_to_spend and cash_to_pay split) and a fresh top-level panel_token while the panel stays open.
        Chain using the receipt's generation, revision and panel_token. Buy requires selected_id and the quoted cost.
        The purchase receipt separates favor_spent and cash_paid."""
        if mode == "read":
            view = counter.context(folder)
            if view["status"] != "observed":
                return view
            if view.get("wild_network_protocol") != 1:
                return {"status":"reload_updated_mod"}
            if view["stage"] != "wild_network":
                return {"status":"panel_closed", "slot":view["slot"]}
            res = counter.command(folder, "wild_network", view["generation"], view["revision"],
                                  mode=mode, since_token=since_token, read_id=uuid4().hex)
            return _summarize_wild_network(res, verbose=verbose)
        if not generation or revision is None or not panel_token:
            raise ValueError("Use generation, revision and panel_token from the open panel")
        if mode in {"tab","select","buy"} and not target:
            raise ValueError("Use the visible tab or service ID")
        if mode == "buy" and (cost is None or cost < 0):
            raise ValueError("Use the exact displayed service cost")
        return counter.command(folder, "wild_network", generation, revision, mode=mode,
                               panel_token=panel_token, target=target, cost=cost)
