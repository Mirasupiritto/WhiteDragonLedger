"""MCP tools available inside the pawnshop."""
import time
from typing import Literal
from uuid import uuid4

from pydantic import BaseModel, Field
import trade_journal
import trade_tools
import shopdesk
import counter
import shop_day
import machine_wait
import heat_control
from ledger import lookup, same_observation
from moves import move_item as submit_move, move_items as submit_batch


class SaleLine(BaseModel):
    item_id: int = Field(gt=0)
    min_price: int = Field(ge=0)


def _sync_revision(folder, generation, target_revision):
    view = counter.context(folder)
    if view.get("status") != "observed" or view.get("generation") != generation or view.get("revision", 0) >= target_revision:
        return
    deadline = time.monotonic() + 1.0
    while time.monotonic() < deadline:
        time.sleep(0.02)
        view = counter.context(folder)
        if view.get("status") != "observed" or view.get("generation") != generation or view.get("revision", 0) >= target_revision:
            return


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


def register_pawnshop_tools(server, folder, database):
    section_cache = getattr(server, "section_cache", None) or shop_day.SectionCache()
    server.section_cache = section_cache
    heat_control.register(server, folder)
    machine_wait.register(server, folder)
    trade_tools.register(server, folder, database)
    shop_day.register_day_tools(server, folder, database, section_cache=section_cache)

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
    def get_shop_ledger(slot: int, ticket_id: str | None = None, inventory: bool = False,
                        name: str | None = None, area: str | None = None, owned: bool | None = None) -> dict:
        """Read unfinished trade tickets for this save slot. Pass a ticket ID for its original tool,
        arguments and latest receipt. inventory=true or name, area, owned filters read the last cached stock
        for this slot, including when the game is closed."""
        return trade_journal.ledger(folder, database, slot, ticket_id, inventory=inventory, name=name, area=area, owned=owned)

    @server.tool()
    def dismiss_trade_ticket(slot: int, ticket_id: str, version: int) -> dict:
        """Archive the reviewed ticket locally using its last observed version. Already submitted
        game actions may finish independently; this tool never places, cancels or repeats a trade."""
        return trade_journal.dismiss(folder, database, slot, ticket_id, version)

    @server.tool()
    def query_inventory(name: str | None = None,
                        area: Literal["inventory", "showcase", "front", "back"] | None = None,
                        owned: bool | None = None,
                        verbose: bool = False) -> dict:
        """Query the most recently published inventory by literal name substring, area or owner.
        Items include native categories (the same as find_stock), size{w,h}, slot{x,y}, rotatable, estimated_value (reference estimate, before haggling/markup),
        actual water_ml, liquid_main and water-majority contains_water on water containers, and charge_remaining/liters_remaining on portable_water_purifier.
        Includes area capacity{w,h,free_cells,largest_free{w,h}} for inventory/showcase. IDs are scoped to generation.
        Unchanged calendar/trash sections are omitted after the first read unless verbose=True."""
        account = lookup(folder, database, name, area, owned)
        if account["status"] == "observed":
            status = shop_day.shop_status(folder)
            if status["status"] == "observed":
                if not same_observation(account, status):
                    return {"status": "observation_changed"}
                account.update(calendar=status["calendar"], trash=shop_day.trash_contents(folder, database, status))
                account = section_cache.filter(account.get("generation"), account, verbose=verbose)
            elif status["status"] in {"load_changed", "waiting_for_load"}:
                return {"status": "observation_changed"}
        return shopdesk.strip_protocols(account)

    @server.tool()
    def list_stock_tags(search: str | None = None, in_stock_only: bool = False) -> dict:
        """List native merchandise categories (code, localized label, in-stock flag) and observed bottle labels.
        Search by partial localized name, alias or code. Stock includes the four shop areas and tracked purifier slots."""
        return lookup(folder, database, stock_query=dict(list_tags=True, search=search, in_stock_only=in_stock_only))

    @server.tool()
    def find_stock(bottle_label: str | None = None, categories: list[str] | None = None,
                   exclude_categories: list[str] | None = None, owned: bool | None = None,
                   area: Literal["inventory", "showcase", "front", "back", "purifier"] | None = None,
                   exclude_contraband: bool = False, kinds: list[str] | None = None, exclude_kinds: list[str] | None = None) -> dict:
        """Pick stock by exact visible label, area, ownership and native item categories.
        Match any requested category or kind, then apply all exclusions. Searching category WATER also matches empty water containers categorized as HOUSEHOLD_GOOD and filled water containers with at least 50% water.
        Each item includes size{w,h}, slot{x,y}, rotatable, estimated_value (reference estimate), and purifier charge_remaining/liters_remaining when applicable.
        Visible variety restrictions group sellable matches by kind, one per kind for this visit.
        Bag contents include parent_id, outer-to-inner container_path and access (unpack_first/container_locked). Unpack before sale.
        Includes purifier slots. Apply exclude_contraband when the visible tablet shows that restriction."""
        return shopdesk.allocation_stock(folder, database, dict(bottle_label=bottle_label, categories=categories,
            exclude_categories=exclude_categories, owned=owned, area=area, exclude_contraband=exclude_contraband, kinds=kinds, exclude_kinds=exclude_kinds))

    @server.tool()
    def get_stock_items(item_ids: list[int], generation: str) -> dict:
        """Read this load's stock IDs in request order, with current locations, size/slot/rotatable, labels, water/purifier details, estimated_value and missing IDs."""
        return lookup(folder, database, stock_query=dict(item_ids=item_ids, generation=generation))

    @server.tool()
    def move_item(item_id: int, source: Literal["inventory", "showcase", "front", "back"],
                  destination: Literal["inventory", "showcase", "front", "back"], generation: str,
                  revision: int, command_id: str | None = None, visit: str | None = None) -> dict:
        """Pass item_id, source, destination, generation and revision. While closed, move owned stock between inventory and showcase. During trade pass the observed visit
        to stage items on the trade table (back), or return them (multiple owned items may be staged together
        during regular selling; guest goods require an empty table). Guest goods return to front and
        owned goods to inventory/showcase. Owned goods left on back when no customer is present can also be
        cleared to inventory/showcase with visit omitted. Placement alone does not confirm a trade. Reuse the command ID to retry."""
        return submit_move(folder, item_id, source, destination, generation, revision, command_id, visit=visit)

    @server.tool()
    def move_items(item_ids: list[int], source: Literal["inventory", "showcase", "front", "back"],
                   destination: Literal["inventory", "showcase"], generation: str, revision: int, visit: str | None = None) -> dict:
        """Move owned stock between visible inventory and showcase windows, including while open, or collect owned front/back items to inventory.
        Pass the current visit during any arrived customer visit without an active order (including ready, barter, seller-only or dialogue), or omit it at an empty counter. Pass the generation and
        starting revision from query_inventory. When space is insufficient, returns no_space or showcase_full with item_size and target_capacity instead of not_moved.
        Stops at the first unconfirmed or rejected item. The original arguments recover completed lines and can continue pending lines."""
        return submit_batch(folder, item_ids, source, destination, generation, revision, visit=visit)

    @server.tool()
    def sort_inventory(generation: str, revision: int) -> dict:
        """Compact the main inventory once using native big-first sorting. Compare before/after free_cells and largest_free.
        Returns external_sorter_active when the player uses a recognized Workshop sorter in auto mode."""
        return counter.command(folder, "sort_inventory", generation, revision)

    @server.tool()
    def query_purchase_lots(supplier: str | None = None, product: str | None = None, lot_id: str | None = None) -> dict:
        """Look up wholesale lots by seller, product or exact lot ID. Report paid, recovered and stock.
        Gross profit is shown only for fully sold lots with a confirmed payment."""
        return shopdesk.purchase_lots(folder, database, supplier, product, lot_id)

    @server.tool()
    def item_menu(item_id: int, generation: str, revision: int, mode: Literal["read", "choose", "targets"] = "read",
                  menu_token: str | None = None, verb: str | None = None, target_id: int | None = None,
                  target_ids: list[int] | None = None) -> dict:
        """Inspect or choose an owned item's native right-click actions. First read returns the shared verb legend and enabled verbs.
        On subsequent reads pass the last menu_token, even across items, for added/removed verbs and changed labels (null resets to base).
        Choose a displayed enabled verb with its matching item and token. activate is also read/consume depending on the native label.
        An expired or already-used token returns status="stale_menu_token" with the item's fresh menu_token and enabled verbs so you can retry in one step; menu_changed means the item's available options changed.
        For use, mode=targets checks up to 64 candidate target_ids via native predicates without using the item. Choose with the matching target_id. Water operations return the water_use route. Using a chemical scanner returns scan{components[id,name,ul,ml,percent], total_ml, tested, label, effects} and auto-closes a tool-opened scanner panel (panel_closed=True). invoked confirms the native action, not an overnight effect."""
        args = dict(item_id=item_id, mode=mode, menu_token=menu_token)
        if mode == "targets":
            if not menu_token or not target_ids or len(target_ids)>64 or any(i<=0 for i in target_ids):
                raise ValueError("Use the source menu_token and 1 to 64 positive candidate target_ids")
            args.update(target_ids=target_ids, read_id=uuid4().hex)
        elif mode == "read":
            args["read_id"] = uuid4().hex
        else:
            if not verb or not menu_token:
                raise ValueError("Choose an enabled verb with the token from item_menu")
            args.update(verb=verb, target_id=target_id)
        return counter.command(folder, "item_menu", generation, revision, **args)

    @server.tool()
    def item_panel(generation: str, revision: int, mode: Literal["read","choose","focus","close"]="read",
                   panel_token: str | None=None, control: str | None=None,
                   value: str | bool | int | float | None=None, panel: str | None=None) -> dict:
        """Read the current item's native panel. Reuse panel_token for changes. Choose a visible enabled control by ID
        using the last token. For select_panel read the desired panel by name, then focus or close with its token. Buttons take no value, choices take a zero-based index, scroll takes 0..1, rename takes text.
        Applied actions return the updated panel under after. Dedicated panels specify the next tool or player action.
        A chemical_scanner view adds a live scan (components by volume, tested/label purity, effects); token rereads return scan only when it changes."""
        if mode=="read":
            return counter.command(folder, "item_panel", generation, revision, mode=mode,
                                   panel_token=panel_token, read_id=uuid4().hex, **({"panel":panel} if panel else {}))
        if not panel_token or (mode=="choose" and not control):
            raise ValueError("Use the current panel_token and an enabled control ID")
        return counter.command(folder, "item_panel", generation, revision, mode=mode,
                               panel_token=panel_token, control=control, value=value, **({"panel":panel} if panel else {}))

    @server.tool()
    def inspect_item_window(owner_id: int, generation: str, revision: int, open_if_closed: bool = False,
                            since_token: str | None = None) -> dict:
        """Inspect an owned inventory/showcase item, or a directly held owned back-counter item after closing.
        Pass the local ID of the item itself as owner_id (never 0, the area or save_id). Pass open_if_closed=True to open and read in one call. Slot items include save_id for matching night loot.
        Read a document by its own ID, including inside an open archive. window.document contains original pages, text, structured terms, or a reason (image_only_guide for image pages). Other windows return visible slots, text and controls (single-item slots report accepts="one_item"; chem_finisher reports recipe_id and button actions configure/output; evaporator reports battery energy, container liquid summary, and distillate_capture status). Pass the last token as since_token for changes only."""
        return counter.command(folder, "inspect_window", generation, revision, owner_id=owner_id,
                               open_if_closed=open_if_closed, since_token=since_token, read_id=uuid4().hex)

    @server.tool()
    def inspect_archive(generation: str, revision: int, mode: Literal["read", "open", "close"] = "read",
                        window_token: str | None = None) -> dict:
        """Read the fixed filing box, or explicitly open/close it before or after business.
        For changes only pass the previous window token. Store/retrieve with transfer_slot_item using the returned owner and control."""
        if mode == "close":
            if not window_token:
                raise ValueError("Use the current archive window token to close it")
            return counter.command(folder, "close_archive", generation, revision, window_token=window_token)
        return counter.command(folder, "inspect_archive", generation, revision, open_if_closed=mode=="open",
                               since_token=window_token, read_id=uuid4().hex)

    @server.tool()
    def transfer_slot_item(owner_id: int, control: str, window_token: str, item_id: int,
                           source: Literal["inventory", "showcase", "slot"], generation: str, revision: int) -> dict:
        """Move owned goods into a visible slot, or from that slot into inventory. Reports observed quantity and changed controls."""
        return counter.command(folder, "transfer_slot", generation, revision, owner_id=owner_id, control=control,
                               window_token=window_token, item_id=item_id, source=source)

    @server.tool()
    def transfer_slot_items(owner_id: int, control: str, window_token: str, item_ids: list[int],
                            source: Literal["inventory", "showcase", "slot"], generation: str, revision: int) -> dict:
        """Transfer one batch to/from the same visible storage control. Inspect each result for partial or skipped moves."""
        if not item_ids or any(i < 1 for i in item_ids) or len(set(item_ids)) != len(item_ids):
            raise ValueError("Provide a nonempty list of distinct positive item IDs")
        return counter.command(folder, "transfer_slots", generation, revision, owner_id=owner_id, control=control,
                               window_token=window_token, item_ids=item_ids, source=source)

    @server.tool()
    def adjust_machine_control(owner_id: int, control: str, values: list[float], window_token: str,
                               generation: str, revision: int) -> dict:
        """Set a visible native machinery slider with [value] or range with [low, high]. Use observed limits.
        Returns the native snapped values and changed window controls. Radio buttons and tabs use press_machine_button."""
        if len(values) not in (1,2):
            raise ValueError("Provide one slider value or two range endpoints")
        return counter.command(folder, "adjust_control", generation, revision, owner_id=owner_id, control=control,
                               values=values, window_token=window_token)

    @server.tool()
    def press_machine_button(owner_id: int, control: str, label: str, window_token: str,
                             generation: str, revision: int, tab_index: int | None=None, quantity: int | None=None) -> dict:
        """For a tabs control pass its zero-based tab_index and exact page caption as label. Press one visible enabled machinery button by observed label and token. May consume supplies or start production.
        For the chemical finisher output only, quantity (1..64) reports produced, purity, grade, and a stop reason. Selecting a recipe in the chemical_finisher panel exposes recipe_id on each button. Returns changed controls."""
        return counter.command(folder, "press_control", generation, revision, owner_id=owner_id, control=control,
                               window_token=window_token, label=label, **({"tab_index":tab_index} if tab_index is not None else {}),
                               **({"quantity":quantity} if quantity is not None else {}))

    @server.tool()
    def water_use(source_id: int, target_id: int | Literal["faucet", "drain"], generation: str, revision: int,
                  mode: Literal["preview", "apply", "apply_now"] = "preview", use_token: str | None = None) -> dict:
        """Preview or apply a water interaction. Sources include bottles, rations, tablets, portable purifiers and fresh strips.
        An empty drain or pour source returns source_empty before a token is issued. A receiving filter adds component losses and final vessel volume to plan.filter.
        Low-risk operations (filling from target_id="faucet", purifying with portable_water_purifier, or testing with water_test_strip) support mode="apply_now" in a single call without preview. High-risk operations (drain, bottle-to-bottle pour, tablet, ration) reject apply_now with preview_required and must use preview + apply.
        To fill a container from the tap, pass the water container as source_id and target_id="faucet"; pass target_id="drain" to empty it.
        Use a single water_test_strip or water_tablet as source_id and the bottle as target_id. For boxed supplies, inspect_item_window(owner_id=box id, open_if_closed=True) then transfer_slot_item(source="slot").
        purifier_used reports charge_remaining and liters_remaining; tablet_used explains effect_pending (chlorine reacts over time/overnight and resets strip exposure).
        For mode="apply", submit the same two IDs with the preview use_token."""
        if source_id < 1 or (isinstance(target_id, int) and (target_id < 1 or target_id == source_id)):
            raise ValueError("Choose a source and a distinct water target from the current observation")
        if mode == "preview":
            return counter.command(folder, "water_use", generation, revision, source_id=source_id,
                                   target_id=target_id, mode=mode, read_id=uuid4().hex)
        if mode == "apply_now":
            return counter.command(folder, "water_use", generation, revision, source_id=source_id,
                                   target_id=target_id, mode=mode)
        if not use_token:
            raise ValueError("Use the token from the water_use preview")
        return counter.command(folder, "water_use", generation, revision, source_id=source_id,
                               target_id=target_id, mode=mode, use_token=use_token)

    @server.tool()
    def fill_and_purify_bottles(bottle_ids: list[int], purifier_id: int, generation: str, revision: int) -> dict:
        """Fill each empty bottle from the faucet and immediately purify it with the given portable_water_purifier using low-risk apply_now.
        Returns per-bottle status/water_ml/bottle_label and final purifier charge_remaining and liters_remaining. Stops on notice, empty charge, or closed faucet."""
        if not bottle_ids or any(i < 1 for i in bottle_ids) or len(set(bottle_ids)) != len(bottle_ids):
            raise ValueError("Provide a nonempty list of distinct positive bottle IDs")
        if purifier_id < 1 or purifier_id in bottle_ids:
            raise ValueError("Provide a valid purifier_id distinct from bottle_ids")
        cur_rev = revision
        results = []
        charge_remaining = None
        liters_remaining = None
        overall = "completed"
        for idx, bottle_id in enumerate(bottle_ids):
            fill_res = counter.command(folder, "water_use", generation, cur_rev,
                                       source_id=bottle_id, target_id="faucet", mode="apply_now")
            if "revision" in fill_res and isinstance(fill_res["revision"], int):
                cur_rev = max(cur_rev, fill_res["revision"])
            if fill_res.get("status") != "transferred":
                results.append({"bottle_id": bottle_id, "status": fill_res.get("status"),
                                "step": "fill", "reason": fill_res.get("reason")})
                overall = "partial" if any(r["status"] == "filled_and_purified" for r in results) else fill_res.get("status", "failed")
                results.extend({"bottle_id": pending, "status": "not_attempted"} for pending in bottle_ids[idx + 1:])
                break
            _sync_revision(folder, generation, cur_rev)
            purify_res = counter.command(folder, "water_use", generation, cur_rev,
                                         source_id=purifier_id, target_id=bottle_id, mode="apply_now")
            if "revision" in purify_res and isinstance(purify_res["revision"], int):
                cur_rev = max(cur_rev, purify_res["revision"])
            if "charge_remaining" in purify_res:
                charge_remaining = purify_res["charge_remaining"]
            if "liters_remaining" in purify_res:
                liters_remaining = purify_res["liters_remaining"]
            if purify_res.get("status") != "purifier_used":
                row = {"bottle_id": bottle_id, "status": purify_res.get("status"),
                       "step": "purify", "fill_status": "transferred"}
                for k in ("reason", "notice", "panel_token"):
                    if k in purify_res:
                        row[k] = purify_res[k]
                results.append(row)
                overall = "partial" if any(r["status"] == "filled_and_purified" for r in results) else purify_res.get("status", "failed")
                results.extend({"bottle_id": pending, "status": "not_attempted"} for pending in bottle_ids[idx + 1:])
                break
            row = {
                "bottle_id": bottle_id,
                "status": "filled_and_purified",
                "fill_status": "transferred",
                "purify_status": "purifier_used",
                "charge_used": purify_res.get("charge_used"),
                "charge_remaining": charge_remaining,
                "liters_remaining": liters_remaining,
            }
            for k in ("water_ml", "bottle_label"):
                if k in purify_res:
                    row[k] = purify_res[k]
                elif k in fill_res:
                    row[k] = fill_res[k]
            results.append(row)
            _sync_revision(folder, generation, cur_rev)
        if charge_remaining is None:
            info = shopdesk.machines(folder)
            if info.get("status") == "observed":
                for p in info.get("portable_purifiers", []):
                    if p.get("id") == purifier_id:
                        charge_remaining = p.get("charge_remaining")
                        liters_remaining = p.get("liters_remaining")
                        break
        completed_count = sum(1 for r in results if r.get("status") == "filled_and_purified")
        reply = {
            "status": overall,
            "generation": generation,
            "revision": cur_rev,
            "purifier_id": purifier_id,
            "completed_count": completed_count,
            "failed_count": len(bottle_ids) - completed_count,
            "items": results,
        }
        if charge_remaining is not None:
            reply["charge_remaining"] = charge_remaining
        if liters_remaining is not None:
            reply["liters_remaining"] = liters_remaining
        return reply

    @server.tool()
    def water_panel(generation: str, revision: int, mode: Literal["read", "match", "close", "notice_close"] = "read",
                    panel_token: str | None = None, bottle_id: int | None = None) -> dict:
        """Read an already open test-strip panel, including tested bottle, displayed result and unused strip count.
        Match an owned bottle label only with its observed ID and panel token, or close that panel by token. A visible single-button purifier notice can be acknowledged with notice_close."""
        if mode == "read":
            return counter.command(folder, "water_panel", generation, revision, mode=mode, read_id=uuid4().hex)
        if not panel_token or (mode == "match" and (bottle_id is None or bottle_id < 1)):
            raise ValueError("Use the observed panel_token and, for match, its bottle_id")
        return counter.command(folder, "water_panel", generation, revision, mode=mode,
                               panel_token=panel_token, bottle_id=bottle_id)

    @server.tool()
    def inspect_purifiers() -> dict:
        """Compare last night's and current bottle IDs, list portable_purifiers (with charge_remaining and liters_remaining), and read battery, extraction readiness and unused test strips."""
        return shopdesk.machines(folder)

    @server.tool()
    def take_purifier_bottle(machine_id: int, bottle_id: int, generation: str, revision: int,
                             command_id: str | None = None) -> dict:
        """While closed, retrieve the observed bottle from its purifier into inventory.
        Use IDs and revision from inspect_purifiers. A moved receipt confirms the actual
        destination, empty source slot and unused strip count. Does not test the water."""
        return submit_move(folder, bottle_id, "purifier", "inventory", generation, revision,
                           command_id, machine_id=machine_id)

    @server.tool()
    def put_purifier_bottle(machine_id: int, bottle_id: int, source: Literal["inventory", "showcase"],
                            generation: str, revision: int, command_id: str | None = None) -> dict:
        """Put a bottle in the stationary purifier slot. For the portable purifier use water_use(source_id=purifier, target_id=bottle)."""
        return submit_move(folder, bottle_id, source, "purifier", generation, revision,
                           command_id, machine_id=machine_id)

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
        result = {**{k:view[k] for k in ("status","slot","phase","generation","revision","day")}, **view["counter"], **day_info}
        result["trash"] = shop_day.trash_contents(folder, database, result)
        result = section_cache.filter(result.get("generation"), result, verbose=verbose)
        if isinstance(result.get("reception"), dict) and "token" in result["reception"]:
            result["reception_token"] = result["reception"]["token"]
        result = shopdesk.strip_protocols(counter.with_barter(folder, result))
        return counter.filter_finished_heat(database, trade_tools.summarize_offer(result, verbose=verbose))

    @server.tool()
    def ring_customer(generation: str, revision: int, waiting: int, verbose: bool = False) -> dict:
        """Ring once at an empty open counter, wait for the greeting (stopping at choices), and return status="called" with trade state.
        Large (>5 item) supplier offers return a summary unless verbose=True."""
        return counter.filter_finished_heat(database, trade_tools.summarize_offer(counter.ring(folder, generation, revision, waiting), verbose=verbose))

    @server.tool()
    def next_customer(generation: str | None = None, revision: int | None = None, waiting: int | None = None,
                      verbose: bool = False, timeout: float = 20.0) -> dict:
        """One-step guest admission: if counter is idle, ring the bell, wait through opening dialogue and tablet opening (up to timeout seconds, default 20, returning opening_timeout if exceeded) and return trade-ready state; if a customer is already at the counter, finish opening and return status="already_present".
        Auto-closes tool-opened item panels; player-opened panels return item_panel_open with current visit/customer/stage and after_close_next_tool.
        Large (>5 item) supplier offers return a summary unless verbose=True."""
        result = admit_customer(generation, revision, waiting, timeout)
        return counter.filter_finished_heat(database, trade_tools.summarize_offer(result, verbose=verbose))

    def admit_customer(generation, revision, waiting, timeout):
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
        while True:
            if view.get("stage") == "customer_departing":
                if time.monotonic() >= deadline:
                    return {**shopdesk.strip_protocols(view), "status":"customer_departing"}
                time.sleep(min(0.15, max(0, deadline-time.monotonic())))
                view = counter.context(folder)
            elif view.get("stage") == "idle" and not view.get("visit"):
                ring_wait = view.get("waiting", 0) if waiting is None else waiting
                result = counter.ring(folder, view["generation"], view["revision"], ring_wait,
                                      timeout=max(0, deadline-time.monotonic()))
                if result.get("status") not in {"occupied","bell_unavailable","customer_replaced","customer_left","queue_changed","customer_departing"}:
                    return result
                view = counter.context(folder)
                if not view.get("visit") and view.get("stage") != "customer_departing":
                    return result
            else:
                break
            if view.get("status") != "observed":
                return view
            if view.get("generation") != start_generation:
                return {"status":"stale_observation"}
            if view.get("phase") != "OPEN":
                return {"status":"shop_closed","phase":view.get("phase")}
        if not view.get("visit") and view.get("stage") == "arriving":
            arrival_deadline = min(deadline, time.monotonic()+5)
            while time.monotonic() < arrival_deadline:
                time.sleep(0.15)
                view = counter.context(folder)
                if view.get("status") != "observed":
                    return view
                if view.get("visit") or view.get("stage") != "arriving":
                    break
        if view.get("visit"):
            waited = counter.wait_opening(folder, view["generation"], view["visit"], timeout=max(0, deadline-time.monotonic()))
            if waited.get("status") == "observed":
                waited = {**waited, "status": "already_present", "already_at_counter": True}
            if "reception_token" not in waited and isinstance(waited.get("reception"), dict) and "token" in waited["reception"]:
                waited["reception_token"] = waited["reception"]["token"]
            return waited
        cleaned = shopdesk.strip_protocols(dict(view))
        if "reception_token" not in cleaned and isinstance(cleaned.get("reception"), dict) and "token" in cleaned["reception"]:
            cleaned["reception_token"] = cleaned["reception"]["token"]
        return cleaned

    @server.tool()
    def finish_customer(generation: str, revision: int, visit: str) -> dict:
        """Pass generation, revision and visit from the counter receipt. Dismiss this visitor. A brief native lock is retried once if the same visitor becomes dismissible.
        If blocked, returns status="dismiss_unavailable" with reason, blocking_items, reception_token and latest_revision; if visitor left or changed, returns customer_left or customer_replaced."""
        result = counter.command(folder, "dismiss", generation, revision, visit=visit)
        if result.get("status")=="dismiss_unavailable" and result.get("reason") in {"client_not_dismissable","bell_locked","dialogue_in_progress"}:
            deadline=time.monotonic()+1.5
            while time.monotonic()<deadline:
                current=counter.context(folder)
                if current.get("generation")!=generation or current.get("status")!="observed":
                    break
                if current.get("visit")!=visit:
                    return counter.visit_mismatch_status(current.get("visit"),current.get("customer"))
                if current.get("reception",{}).get("can_dismiss"):
                    result=counter.command(folder,"dismiss",generation,revision,visit=visit)
                    break
                time.sleep(0.1)
        if result.get("status") in {"dismissed", "customer_left", "customer_replaced"}:
            current = counter.context(folder)
            if current.get("generation") == generation and current.get("phase") == "OPEN":
                if current.get("visit") and current["visit"] != visit:
                    result = {**result, "next_customer": counter.filter_finished_heat(database, trade_tools.summarize_offer(shopdesk.strip_protocols(current))),
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

    @server.tool()
    def get_phone_contacts() -> dict:
        """Read known phonebook and owned business-card contacts, services, answerability and visible call choices. Never dials."""
        return shopdesk.strip_protocols(counter.phone_context(folder))

    @server.tool()
    def phone_keypad(mode: Literal["open", "close", "dial"], generation: str, revision: int,
                     phone_token: str, digits: str = "") -> dict:
        """Open or close the phone, or enter 1–9 digits on the empty native keypad. Preserve leading zeros."""
        if mode == "dial" and not (digits.isascii() and digits.isdecimal() and 1 <= len(digits) <= 9):
            return {"status": "invalid_number"}
        return counter.command(folder, "phone_keypad", generation, revision, phone_token=phone_token, mode=mode, digits=digits)

    @server.tool()
    def dial_contact(number: int, generation: str, revision: int, phone_token: str) -> dict:
        """Dial exactly one observed contact. Starting the native call does not select a menu option."""
        return counter.command(folder, "dial", generation, revision, phone_token=phone_token, number=number)

    @server.tool()
    def choose_phone_service(choice_id: int, generation: str, revision: int, phone_token: str) -> dict:
        """Click one enabled choice from the current phone menu. May place an order or change service."""
        return counter.command(folder, "phone_choice", generation, revision, phone_token=phone_token, choice_id=choice_id)

    @server.tool()
    def advance_phone_dialogue(generation: str, revision: int, phone_token: str) -> dict:
        """Turn one finished ordinary phone page when can_advance is true. Never chooses a service."""
        return counter.command(folder, "phone_advance", generation, revision, phone_token=phone_token)
