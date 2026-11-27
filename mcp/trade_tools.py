"""Decision-sized trade reads, native order entry points and revision receipts."""
import hashlib
import json
import time
from typing import Literal
from uuid import uuid4

from pydantic import BaseModel, Field

import book_history
import counter
from flow import present_counter
import scene as scene_mod
import shopdesk
import shop_day
from ledger import lookup, same_observation
from database import transaction
from server_state import ServerState


class BatchLine(BaseModel):
    item_id: int = Field(gt=0)
    side: Literal["buy", "sell"]
    price_limit: int = Field(ge=0)


def context(view):
    if view["status"] != "observed":
        return view
    desk = view["counter"]
    buyer = view.get("customer") or {}
    offer = desk.get("offer") or {}
    demand = desk.get("demand", {})
    lines = desk.get("lines", [])
    needs = demand.get("interests") or []
    summary = "、".join(needs) if needs else (lines[0] if lines else "需求待說明")
    remaining = buyer.get("remaining")
    bottles = [row for row in offer.get("items", []) if "bottle_label" in row]
    result = {key:view[key] for key in ("status", "slot", "generation", "revision", "phase")}
    result.update(visit=desk.get("visit"), customer=desk.get("customer"), stage=desk["stage"], lines=lines,
                  demand=demand, remaining_budget=remaining, quote=desk.get("quote"),
                  customer_line=f"{desk.get('customer') or '空櫃檯'}｜{summary}｜剩餘 {remaining if remaining is not None else '未顯示'}",
                  next_action="可以報價" if desk.get("quote_ready", desk["stage"] == "ready") else desk["stage"])
    if desk.get("stage") == "barter_unsupported":
        result["reason"] = desk["reason"]
    if desk.get("next_action"):
        result["next_action"] = desk["next_action"]
    if desk["stage"] == "wild_network":
        result["next_action"] = "wild_network"
    for key in ("client_role", "appraisal", "offer", "offer_token", "info_receipt", "visit_variety", "page", "dialog_choices", "response_choices",
                "forced_choice", "forced_choice_reason", "wanted_name_match", "poster_match", "story_branch", "consequence_hint", "consequence", "counter_request", "inspection_exposure", "heat", "barter", "stale_during_speech", "table_items"):
        if key in desk:
            result[key] = desk[key]
    if isinstance(desk.get("reception"), dict) and "token" in desk["reception"]:
        result["reception_token"] = desk["reception"]["token"]
    if bottles:
        result["seller_water"] = {"count":len(bottles), "ids":[row["id"] for row in bottles]}
    return result


def _has_group_fields(opt):
    factors = opt.get("offer_price_factors")
    rep = opt.get("reputation_multipliers")
    return (
        isinstance(opt, dict)
        and opt.get("type") is not None
        and "percent" in opt
        and "leave_chance" in opt
        and "success_chance" in opt
        and isinstance(factors, dict) and "buy" in factors and "sell" in factors
        and isinstance(rep, dict) and "buy" in rep and "sell" in rep
    )


def group_bargain_options(options):
    by_type = {}
    for opt in options:
        by_type.setdefault(opt.get("type"), []).append(opt)
    result = []
    for bucket in by_type.values():
        if not all(_has_group_fields(opt) for opt in bucket):
            result.extend({**opt, "grouped": False} for opt in bucket)
            continue
        first = bucket[0]
        group = {k: first.get(k) for k in ("type", "uses_roll", "crime", "reputation_faction")}
        for opt in bucket:
            factors = opt.get("offer_price_factors") or {}
            rep = opt.get("reputation_multipliers") or {}
            for key, value in (("percents", opt.get("percent")), ("leave_chance", opt.get("leave_chance")),
                               ("success_chance", opt.get("success_chance")), ("buy_factor", factors.get("buy")),
                               ("sell_factor", factors.get("sell")), ("reputation_buy", rep.get("buy")),
                               ("reputation_sell", rep.get("sell"))):
                group.setdefault(key, []).append(value)
            for key in ("budget_cut_percent", "budget_increase_percent"):
                if key in opt:
                    group.setdefault(key, []).append(opt[key])
        result.append(group)
    return result


def summarize_offer(payload, *, verbose=False):
    if verbose or not isinstance(payload, dict):
        return payload
    offer = payload.get("offer")
    if not isinstance(offer, dict):
        return payload
    items = offer.get("items") or []
    if len(items) <= 5:
        return payload
    grouped = {}
    total_qty = 0
    for item in items:
        qty = item.get("quantity", 1)
        total_qty += qty
        cats = item.get("categories") or []
        cat = cats[0] if cats else ("water" if "bottle_label" in item else item.get("kind") or "misc")
        bucket = grouped.setdefault(cat, {"category": cat, "count": 0, "quantity": 0, "subtotal": 0})
        bucket["count"] += 1
        bucket["quantity"] += qty
        bucket["subtotal"] += item.get("reference_price", item.get("price", 0))
        bucket["reference_subtotal"] = bucket["subtotal"]
    out = dict(payload)
    out["offer"] = {
        "visit": offer.get("visit"),
        "wholesale": offer.get("wholesale"),
        "total": offer.get("total"),
        **{key: offer[key] for key in ("reference_total", "quoted_total", "price_note") if key in offer},
        "item_count": len(items),
        "total_quantity": total_qty,
        "categories": list(grouped.values()),
        "summary_only": True,
    }
    return out


def wait_event(folder, event, timeout):
    if not 0 <= timeout <= 15:
        raise ValueError("timeout_seconds must be between 0 and 15")
    initial = None
    deadline = time.monotonic() + timeout
    while True:
        view = shopdesk.published(folder)
        if view["status"] != "observed":
            return view
        desk = view["counter"]
        stage = desk["stage"]
        identity = (view["generation"], view["day"], desk.get("visit"))
        if initial is None:
            initial = identity
        if stage in {"wild_network","response_required"}:
            reason = "input_needed"
        elif view["phase"] != "OPEN":
            reason = "shop_unavailable"
        elif identity[:2] != initial[:2]:
            reason = "visit_changed"
        elif event == "next_customer_available" and "can_ring" not in desk["reception"]:
            reason = "reload_updated_mod"
        elif event == "next_customer_available" and desk["reception"].get("can_ring"):
            reason = event
        elif identity != initial:
            reason = "customer_left" if not identity[2] else "customer_replaced"
        elif (stage == "choice_required" and "dialog_choices" in desk
              and not desk["dialog_choices"] and time.monotonic() < deadline):
            time.sleep(min(.05, max(0, deadline - time.monotonic())))
            continue
        elif stage in {"advance_required","confirmation_required","choice_required","information","conversation_paused"}:
            reason = "input_needed"
        elif event == "next_customer_available" and not desk.get("waiting"):
            reason = "no_customers_waiting"
        elif event != "next_customer_available" and not identity[2]:
            reason = "no_current_customer"
        elif event == "goods_offered" and (stage == "barter" or desk.get("offer", {}).get("items")):
            reason = event
        elif event == "opening_finished" and (desk.get("quote_ready") or stage in {"ready","barter","not_selling","business_complete","whole_batch_unavailable"}):
            reason = event
        elif time.monotonic() >= deadline:
            reason = "timeout"
        else:
            time.sleep(min(.2, max(0, deadline-time.monotonic())))
            continue
        return {**counter.with_barter(folder, context(view)), "reason":reason}


class Updates:
    def __init__(self, folder, database, state=None):
        self.folder = folder
        self.database = database
        self.cursor = None
        self.state = state if state is not None else ServerState()

    def report(self, since=None, full=False):
        with self.state.lock:
            return self._report_locked(since=since, full=full)

    def _report_locked(self, since=None, full=False):
        view = shopdesk.published(self.folder)
        if view["status"] != "observed":
            return view
        head = lookup(self.folder, self.database, sync_only=True)
        gen, rev = view["generation"], view["revision"]
        if not same_observation(head, view):
            return {"status":"observation_changed"}
        desk = view["counter"]
        fields = dict(day=view["day"], phase=view["phase"], cash=desk.get("cash"),
                      visit=desk.get("visit"), customer=desk.get("customer"), stage=desk["stage"],
                      waiting=desk.get("waiting"), remaining_budget=(view.get("customer") or {}).get("remaining"),
                      strips=view.get("available_strips"), quote=desk.get("quote"), offer_token=desk.get("offer_token"),
                      offer_total=(desk.get("offer") or {}).get("total"), demand=desk.get("demand"),
                      latest_line=(desk.get("lines") or [None])[-1], can_ring=desk["reception"].get("can_ring"),
                      can_dismiss=desk["reception"].get("can_dismiss"), can_open_shop=desk.get("can_open_shop"))
        if "heat" in desk:
            notified = counter.filter_finished_heat(self.database, {"generation": gen, "heat": desk["heat"]})
            if "heat" in notified:
                fields["heat"] = notified["heat"]
        fields["page"] = desk.get("page")
        encoded = json.dumps(fields, sort_keys=True, ensure_ascii=False)
        token = f"{gen}:{rev}:{hashlib.sha256(encoded.encode()).hexdigest()[:24]}"
        previous = since if since is not None else self.cursor
        with transaction(self.database) as db:
            row = db.execute("SELECT fields FROM book_cursors WHERE token=?", (previous,)).fetchone()
            base = None
            if row and previous.split(":")[0] == gen:
                base = int(previous.split(":")[1])
            result = dict(status="observed", slot=head["slot"], generation=gen, revision=rev,
                          cursor=token, load_scans=head["load_scans"])
            if full or base is None or base > rev:
                result.update(mode="baseline", fields=fields, items=book_history.snapshot(db, gen, rev))
                if previous and base is None:
                    result["reason"] = "cursor_reset"
            else:
                old = json.loads(row[0])
                item_rows = book_history.changes_between(db, gen, base, rev)
                upsert, remove = [], []
                for item, old_p, new_p in item_rows:
                    if old_p != new_p:
                        if new_p is None:
                            remove.append(item)
                        else:
                            upsert.append(json.loads(new_p))
                id_changes = book_history.identity_changes(db, gen, base, rev, item_rows=item_rows)
                changed = {k:v for k,v in fields.items() if old.get(k)!=v}
                checkpoints = []
                last = old["phase"]
                for step in db.execute("SELECT rev,day,phase FROM book_events WHERE gen=? AND rev>? AND rev<=? ORDER BY rev",
                                       (gen,base,rev)):
                    if step["phase"] and step["phase"] != last:
                        checkpoints.append(dict(step))
                        last = step["phase"]
                if last != fields["phase"]:
                    checkpoints.append(dict(rev=rev, day=fields["day"], phase=fields["phase"]))
                if not upsert and not remove and not changed and not checkpoints and not id_changes:
                    result = dict(status="unchanged", revision=rev, cursor=token)
                else:
                    result.update(mode="delta", upsert=upsert, remove=remove, fields=changed)
                    if checkpoints:
                        result["checkpoints"] = checkpoints
                    if id_changes:
                        result["identity_changed"] = id_changes
            db.execute("INSERT OR IGNORE INTO book_cursors VALUES(?,?)", (token,encoded))
        self.cursor = token
        return result


def register(server, folder, database, *, appraisal_assist: bool | None = None):
    state = getattr(server, "state", None)
    tracker = Updates(folder, database, state=state)
    server.updates = tracker
    if state is not None:
        state.updates = tracker

    @server.tool()
    def report_client(mode: Literal["read","report"] = "read", visit: str | None = None,
                      report_token: str | None = None, generation: str | None = None,
                      revision: int | None = None) -> dict:
        """Read the visible alarm or press it with the current visit, token, generation and revision.
        Read again with the reported visit until arrest_finished. On report_intercepted, follow get_trade_context until speech finishes, then call report_client(mode="read") again for the fresh light and report_token to decide whether to report or dismiss.
        Compare feedback_box before and after an arrest."""
        if mode == "read":
            view = counter.context(folder)
            if view["status"] != "observed":
                return view
            return counter.command(folder, "report_client", generation or view["generation"], view["revision"],
                                   mode=mode, visit=visit or view.get("visit"), read_id=uuid4().hex)
        if not (generation and revision and visit and report_token):
            raise ValueError("Report requires generation, revision, visit and report_token from read")
        return counter.command(folder, "report_client", generation, revision, mode=mode, visit=visit, report_token=report_token)

    @server.tool()
    def bargain(mode: Literal["read","offer"], generation: str, revision: int, visit: str,
                type: str | None = None, percent: int | None = None, bargain_token: str | None = None, verbose: bool = False) -> dict:
        """Read options grouped by type (verbose=True for individual buttons), chances, budget effects, trade reputation multipliers and the actual table quote. Offer one listed type/percent
        with its bargain_token. After bargain_pending, read with the same visit and current revision until the dialogue settles."""
        args = dict(visit=visit, mode=mode)
        if mode == "read":
            args["read_id"] = uuid4().hex
        else:
            if not type or percent is None or not bargain_token:
                raise ValueError("Offer needs a listed type, percent and bargain_token")
            args.update(type=type, percent=percent, bargain_token=bargain_token)
        result = counter.command(folder,"bargain",generation,revision,**args)
        if mode == "read" and not verbose and "options" in result:
            result = {**result, "options": group_bargain_options(result["options"])}
        return result

    @server.tool()
    def get_shop_updates(since: str | None = None, full: bool = False) -> dict:
        """Start with a baseline, then return changed fields, item upserts/removals and shutter checkpoints.
        Save the returned cursor for explicit replay. Mutations on this connection also receive deltas."""
        return tracker.report(since, full)

    @server.tool()
    def get_trade_context(verbose: bool = False) -> dict:
        """Read the customer, budget, tablet requirements, quote, offered water, wanted name matches, story consequences, forced_choice warnings, counter_request (from dialogue lines and owned table items), and any page awaiting a choice.
        During stage=speaking, retains the last observed demand/barter with stale_during_speech=True until speech settles.
        Supplier offers with >5 items return a category/count/total summary unless verbose=True."""
        ctx = counter.with_barter(folder, context(shopdesk.published(folder)))
        if ctx.get("status") == "observed" and ctx.get("stage") == "choice_required" and "dialog_choices" in ctx and not ctx["dialog_choices"]:
            counter.wait_choices_ready(folder, ctx)
            ctx = counter.with_barter(folder, context(shopdesk.published(folder)))
        return present_counter(ctx, folder=folder, database=database,
                               summarize_offer=summarize_offer, verbose=verbose)

    @server.tool()
    def wait_for_shop_event(event: Literal["goods_offered","next_customer_available","opening_finished"],
                            timeout_seconds: int = 6) -> dict:
        """Observe the same visit until the requested event, required input, departure or timeout."""
        return wait_event(folder, event, timeout_seconds)

    @server.tool()
    def get_shop_state() -> dict:
        """Read a full stock, shop, counter and visible window snapshot on demand."""
        view = shopdesk.published(folder)
        if view["status"] != "observed":
            return view
        head = lookup(folder, database, sync_only=True)
        gen, rev = view["generation"], view["revision"]
        if not same_observation(head, view):
            return {"status":"observation_changed"}
        windows = counter.command(folder,"visible_windows",gen,rev,read_id=uuid4().hex)
        if windows["status"] not in {"observed","reload_updated_mod"}:
            return windows
        latest = shopdesk.published(folder)
        if latest != view:
            return {"status":"observation_changed"}
        with transaction(database) as db:
            items = book_history.snapshot(db, gen, rev)
        day = {**view, **view["day_info"]}
        trash = shop_day.trash_contents(folder, database, day)
        return shopdesk.strip_protocols({**view, "trash":trash, "items":items, "load_scans":head["load_scans"],
                "windows":windows.get("windows"), "windows_status":windows["status"],
                "dialog_choices":windows.get("dialog_choices"), "response_choices":windows.get("response_choices")})

    @server.tool()
    def trade_batch(generation: str, revision: int, visit: str, orders: list[BatchLine]) -> dict:
        """Pass orders=[{item_id, side, price_limit}, ...] for the same visit. price_limit is a sell floor or buy ceiling.
        Auto-returns non-order owned counter clutter (excluding nuclear_waste) to inventory as cleared_items before staging.
        counter_unavailable and stale_order always attach reason and blocking_items (plus guidance for nuclear_waste_on_counter).
        Returns each actual payment, placement and remaining budget. Whole-lot offers require buy_merchant_batch."""
        lines = [{"item_id":r.item_id, "side":r.side,
                  ("min_price" if r.side=="sell" else "max_price"):r.price_limit} for r in orders]
        return shopdesk.sell_order(folder,lines,generation,revision,visit,database=database,tool="trade_batch")

    @server.tool()
    def confirm_trade(generation: str, revision: int, visit: str, item_id: int, side: Literal["buy","sell"],
                      price: int = Field(ge=0)) -> dict:
        """Confirm only the single currently staged item, matching the exact side, quote and visit."""
        line = {"item_id":item_id,"side":side,"exact_price":price,
                ("min_price" if side=="sell" else "max_price"):price}
        return shopdesk.sell_order(folder,[line],generation,revision,visit,database=database,tool="confirm_trade",on_table=True)

    @server.tool()
    def submit_appraisal(item_id: int, estimate: int, visit: str, generation: str, revision: int) -> dict:
        """Submit an appraisal estimate for an unowned APPRAISAL_ITEM on the counter for an APPRAISAL_SERVICE client.
        Never guess the estimate: ask the player to inspect the item visually (or use read_item_details when item_details_compat is enabled) before calling this tool.
        Returns accepted (with cash_delta, slip_text, remaining_count, all_done), rejected (with dialogue line and impatience_delta; wait for dialogue or re-read get_trade_context before retrying), or walked_out (patience_exhausted)."""
        if item_id < 1 or estimate < 1:
            raise ValueError("Expected positive item_id and estimate")
        return counter.command(folder, "appraisal", generation, revision, visit=visit, item_id=item_id, estimate=estimate)

    if scene_mod.appraisal_assist_enabled(appraisal_assist, folder=folder):
        @server.tool()
        def get_appraisal_assist(visit: str, generation: str, revision: int) -> dict:
            """Read recommended appraisal estimates for the current APPRAISAL_SERVICE visitor's counter items.
            Only registered when WHITE_DRAGON_APPRAISAL_ASSIST=1 or --appraisal-assist is enabled."""
            return counter.command(folder, "appraisal_assist", generation, revision, visit=visit)
