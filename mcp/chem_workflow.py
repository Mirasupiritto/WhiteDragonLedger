"""Automated chemist finishing workflow built on top of existing pawnshop commands."""
from uuid import uuid4

import counter
import ledger
import shopdesk


LIQUID_RAW_IDS = {"water", "alcohol"}


def _next_rev(receipt, folder, generation, fallback):
    update = receipt.get("shop_update") or {}
    if isinstance(update.get("revision"), int):
        return update["revision"]
    ctx = counter.context(folder)
    if ctx.get("status") == "observed" and ctx.get("generation") == generation and isinstance(ctx.get("revision"), int):
        return ctx["revision"]
    return fallback


def _find_owned_by_kind(items, kinds):
    if isinstance(kinds, str):
        kinds = {kinds}
    for item in items:
        if item.get("kind") in kinds and item.get("area") in {"inventory", "showcase"}:
            return item
    return None


def _slot_with_item(controls, item_id):
    for ctrl in controls:
        if ctrl.get("type") == "slot":
            for entry in ctrl.get("items") or []:
                if entry.get("id") == item_id:
                    return ctrl, entry
    return None, None


def _find_finisher_controls(controls):
    input_ctrl = None
    output_ctrl = None
    config_btn = None
    output_btn = None
    for ctrl in controls:
        ctype = ctrl.get("type")
        role = ctrl.get("role")
        if ctype == "slot":
            if role == "output":
                output_ctrl = ctrl
            elif role == "input" or (ctrl.get("accepts") in {"one_item", "one_container"} and ctrl.get("insert_allowed")):
                if input_ctrl is None:
                    input_ctrl = ctrl
        elif ctype == "button" and ctrl.get("enabled"):
            action = ctrl.get("action")
            if action == "configure":
                config_btn = ctrl
            elif action == "output":
                output_btn = ctrl
    return input_ctrl, output_ctrl, config_btn, output_btn


def _use_scanner(folder, scanner_id, target_id, generation, rev):
    read_res = counter.command(
        folder, "item_menu", generation, rev, item_id=scanner_id, mode="read", read_id=uuid4().hex
    )
    rev = _next_rev(read_res, folder, generation, rev)
    if read_res.get("status") != "observed":
        return False, read_res, rev
    token = read_res.get("menu_token")
    enabled = read_res.get("enabled") or []
    if not token or "use" not in enabled:
        return False, {"status": "scanner_use_unavailable", "receipt": read_res}, rev
    use_res = counter.command(
        folder, "item_menu", generation, rev, item_id=scanner_id, mode="choose",
        menu_token=token, verb="use", target_id=target_id,
    )
    rev = _next_rev(use_res, folder, generation, rev)
    if use_res.get("status") not in {"invoked", "applied", "observed"}:
        return False, use_res, rev
    return True, use_res, rev


def run_chem_finish(folder, database, vessel_id, recipe_id, generation, revision,
                    quantity=None, stash_ids=None, stash_count=None):
    if vessel_id < 1 or not recipe_id:
        raise ValueError("Provide a positive vessel_id and a non-empty recipe_id")
    if quantity is not None and not 1 <= quantity <= 64:
        raise ValueError("quantity must be between 1 and 64 when specified")
    if stash_count is not None and stash_count < 0:
        raise ValueError("stash_count must be non-negative")

    rev = revision
    steps = {}
    vessel_location = {"area": "unknown"}

    def stop(step_name, reason, **extra):
        out = {
            "status": "stopped",
            "stopped_at": step_name,
            "reason": reason,
            "steps": steps,
            "vessel_location": vessel_location,
            "revision": rev,
        }
        out.update(extra)
        return out

    stock = ledger.lookup(folder, database, owned=True)
    if stock.get("status") != "observed":
        return stop("locate_equipment", stock.get("status") or "stock_unavailable", receipt=stock)

    items = stock.get("items") or []
    finisher = _find_owned_by_kind(items, "chem_finisher")
    scanner = _find_owned_by_kind(items, "chem_scanner")
    if not finisher:
        return stop("locate_equipment", "missing_chem_finisher")
    if not scanner:
        return stop("locate_equipment", "missing_chemical_scanner")

    vessel_item = next((i for i in items if i.get("id") == vessel_id), None)
    if vessel_item and vessel_item.get("area") in {"inventory", "showcase"}:
        vessel_location = {"area": vessel_item["area"]}
        steps["retrieve_from_evaporator"] = {"status": "skipped", "reason": "already_in_stock", "area": vessel_item["area"]}
    else:
        evap = _find_owned_by_kind(items, "evaporator")
        if not evap:
            return stop("retrieve_from_evaporator", "missing_evaporator")
        evap_inspect = counter.command(
            folder, "inspect_window", generation, rev, owner_id=evap["id"], open_if_closed=True, read_id=uuid4().hex
        )
        rev = _next_rev(evap_inspect, folder, generation, rev)
        if evap_inspect.get("status") != "observed":
            return stop("retrieve_from_evaporator", evap_inspect.get("status"), receipt=evap_inspect)
        evap_ctrl, _ = _slot_with_item((evap_inspect.get("window") or {}).get("controls") or [], vessel_id)
        if not evap_ctrl:
            return stop("retrieve_from_evaporator", "vessel_not_in_evaporator", receipt=evap_inspect)
        vessel_location = {"area": "evaporator", "owner_id": evap["id"], "control": evap_ctrl["control"]}
        take_res = counter.command(
            folder, "transfer_slot", generation, rev, owner_id=evap["id"], control=evap_ctrl["control"],
            window_token=evap_inspect["window_token"], item_id=vessel_id, source="slot",
        )
        rev = _next_rev(take_res, folder, generation, rev)
        if take_res.get("status") != "moved":
            return stop("retrieve_from_evaporator", take_res.get("status"), receipt=take_res)
        vessel_location = {"area": "inventory"}
        steps["retrieve_from_evaporator"] = {"status": "moved", "evaporator_id": evap["id"]}

    ok, scan_res, rev = _use_scanner(folder, scanner["id"], vessel_id, generation, rev)
    if not ok:
        return stop("scan_vessel", scan_res.get("status") or "scan_failed", receipt=scan_res)
    scan = scan_res.get("scan") or {}
    remaining_raw = [
        c for c in (scan.get("components") or [])
        if c.get("id") in LIQUID_RAW_IDS and (c.get("ul", 0) > 0 or c.get("ml", 0) > 0)
    ]
    if (scan.get("total_ml") or 0) <= 0 and not (scan.get("components") or []):
        steps["scan_vessel"] = {"status": "empty_vessel", "scan": scan}
        return stop("scan_vessel", "empty_vessel", scan=scan)
    steps["scan_vessel"] = {
        "status": "scanned",
        "total_ml": scan.get("total_ml"),
        "components": scan.get("components"),
        "remaining_liquids": remaining_raw,
    }

    fin_inspect = counter.command(
        folder, "inspect_window", generation, rev, owner_id=finisher["id"], open_if_closed=True, read_id=uuid4().hex
    )
    rev = _next_rev(fin_inspect, folder, generation, rev)
    if fin_inspect.get("status") != "observed":
        return stop("insert_into_finisher", fin_inspect.get("status"), receipt=fin_inspect)
    win = fin_inspect.get("window") or {}
    win_token = fin_inspect.get("window_token")
    input_ctrl, output_ctrl, config_btn, output_btn = _find_finisher_controls(win.get("controls") or [])
    if not input_ctrl or not output_ctrl:
        return stop("insert_into_finisher", "finisher_slots_unavailable", receipt=fin_inspect)

    existing_input = input_ctrl.get("items") or []
    if any(i.get("id") == vessel_id for i in existing_input):
        vessel_location = {"area": "chem_finisher", "owner_id": finisher["id"], "control": input_ctrl["control"]}
        steps["insert_into_finisher"] = {"status": "already_in_slot"}
    elif existing_input:
        return stop("insert_into_finisher", "input_slot_occupied", occupant=existing_input[0])
    else:
        src_area = vessel_location["area"]
        ins_res = counter.command(
            folder, "transfer_slot", generation, rev, owner_id=finisher["id"], control=input_ctrl["control"],
            window_token=win_token, item_id=vessel_id, source=src_area,
        )
        rev = _next_rev(ins_res, folder, generation, rev)
        if ins_res.get("status") != "moved":
            return stop("insert_into_finisher", ins_res.get("status"), receipt=ins_res)
        win_token = ins_res.get("window_token") or win_token
        vessel_location = {"area": "chem_finisher", "owner_id": finisher["id"], "control": input_ctrl["control"]}
        steps["insert_into_finisher"] = {"status": "moved"}

    if win.get("recipe_id") == recipe_id:
        steps["select_recipe"] = {"status": "already_selected", "recipe_id": recipe_id}
    else:
        if not config_btn:
            return stop("select_recipe", "configure_button_unavailable")
        press_cfg = counter.command(
            folder, "press_control", generation, rev, owner_id=finisher["id"],
            control=config_btn["control"], label=config_btn["label"], window_token=win_token,
        )
        rev = _next_rev(press_cfg, folder, generation, rev)
        if press_cfg.get("status") != "pressed":
            return stop("select_recipe", press_cfg.get("status"), receipt=press_cfg)
        panel_read = counter.command(
            folder, "item_panel", generation, rev, mode="read", panel="chemical_finisher", read_id=uuid4().hex
        )
        rev = _next_rev(panel_read, folder, generation, rev)
        if panel_read.get("status") != "observed":
            return stop("select_recipe", panel_read.get("status"), receipt=panel_read)
        panel_controls = (panel_read.get("view") or {}).get("controls") or {}
        match_key = next(
            (k for k, v in panel_controls.items() if isinstance(v, dict) and v.get("recipe_id") == recipe_id),
            None,
        )
        if not match_key:
            return stop("select_recipe", "recipe_not_found", recipe_id=recipe_id, available=list(panel_controls.values()))
        if not panel_controls[match_key].get("enabled"):
            return stop("select_recipe", "recipe_disabled", recipe_id=recipe_id, control=panel_controls[match_key])
        choose_res = counter.command(
            folder, "item_panel", generation, rev, mode="choose",
            panel_token=panel_read["panel_token"], control=match_key, panel="chemical_finisher",
        )
        rev = _next_rev(choose_res, folder, generation, rev)
        if choose_res.get("status") != "applied":
            return stop("select_recipe", choose_res.get("status"), receipt=choose_res)
        after = choose_res.get("after") or {}
        if after.get("status") not in {"panel_closed", None} and after.get("panel_token"):
            close_res = counter.command(
                folder, "item_panel", generation, rev, mode="close",
                panel_token=after["panel_token"], panel="chemical_finisher",
            )
            rev = _next_rev(close_res, folder, generation, rev)
        fin_recheck = counter.command(
            folder, "inspect_window", generation, rev, owner_id=finisher["id"], open_if_closed=False, read_id=uuid4().hex
        )
        rev = _next_rev(fin_recheck, folder, generation, rev)
        if fin_recheck.get("status") != "observed":
            return stop("select_recipe", fin_recheck.get("status"), receipt=fin_recheck)
        win = fin_recheck.get("window") or {}
        win_token = fin_recheck.get("window_token")
        if win.get("recipe_id") != recipe_id:
            return stop("select_recipe", "recipe_selection_unconfirmed", actual_recipe_id=win.get("recipe_id"))
        _, output_ctrl, _, output_btn = _find_finisher_controls(win.get("controls") or [])
        steps["select_recipe"] = {"status": "selected", "recipe_id": recipe_id}

    if not output_btn:
        return stop("produce_batch", "output_button_unavailable")
    total_produced = 0
    stop_reason = None
    batch_purity = None
    batch_grade = None
    while True:
        req_qty = quantity if quantity is not None else 64
        prod_res = counter.command(
            folder, "press_control", generation, rev, owner_id=finisher["id"],
            control=output_btn["control"], label=output_btn["label"], window_token=win_token, quantity=req_qty,
        )
        rev = _next_rev(prod_res, folder, generation, rev)
        win_token = prod_res.get("window_token") or win_token
        pstatus = prod_res.get("status")
        if pstatus not in {"produced", "batch_incomplete"}:
            return stop("produce_batch", pstatus, produced=total_produced, receipt=prod_res)
        batch_count = prod_res.get("produced", 0)
        total_produced += batch_count
        if prod_res.get("purity") is not None:
            batch_purity = prod_res.get("purity")
        if prod_res.get("grade") is not None:
            batch_grade = prod_res.get("grade")
        stop_reason = prod_res.get("reason")
        if quantity is not None or pstatus == "batch_incomplete" or batch_count < 64:
            break

    if total_produced == 0 and stop_reason == "recipe_not_ready":
        steps["produce_batch"] = {"status": "recipe_not_ready", "produced": 0, "remaining_liquids": remaining_raw}
        return stop("produce_batch", "recipe_not_ready", remaining_liquids=remaining_raw)

    steps["produce_batch"] = {"status": "done", "produced": total_produced, "stop_reason": stop_reason}

    fin_after = counter.command(
        folder, "inspect_window", generation, rev, owner_id=finisher["id"], open_if_closed=False, read_id=uuid4().hex
    )
    rev = _next_rev(fin_after, folder, generation, rev)
    if fin_after.get("status") != "observed":
        return stop("collect_output", fin_after.get("status"), produced=total_produced, receipt=fin_after)
    win_after = fin_after.get("window") or {}
    win_token = fin_after.get("window_token")
    in_after, out_after, _, _ = _find_finisher_controls(win_after.get("controls") or [])
    produced_ids = [i["id"] for i in (out_after.get("items") or []) if isinstance(i.get("id"), int)]
    remaining_vessel = (in_after.get("items") or [None])[0] if in_after else None

    collected_ids = []
    if produced_ids:
        collect_res = counter.command(
            folder, "transfer_slots", generation, rev, owner_id=finisher["id"],
            control=out_after["control"], window_token=win_token, item_ids=produced_ids, source="slot",
        )
        rev = _next_rev(collect_res, folder, generation, rev)
        for row in collect_res.get("items") or []:
            if row.get("status") == "moved":
                collected_ids.append(row["item_id"])
        if collect_res.get("status") != "moved":
            steps["collect_output"] = {"status": collect_res.get("status"), "collected_ids": collected_ids}
            return stop("collect_output", collect_res.get("status"), produced_ids=produced_ids, collected_ids=collected_ids, receipt=collect_res)
        steps["collect_output"] = {"status": "moved", "collected_ids": collected_ids}
    else:
        steps["collect_output"] = {"status": "skipped", "reason": "no_output_items"}

    product_scan = None
    if collected_ids:
        ok, pscan_res, rev = _use_scanner(folder, scanner["id"], collected_ids[0], generation, rev)
        if ok:
            pscan = pscan_res.get("scan") or {}
            product_scan = {
                "item_id": collected_ids[0],
                "purity": batch_purity,
                "grade": batch_grade,
                "tested": pscan.get("tested"),
                "label": pscan.get("label"),
                "components": pscan.get("components"),
            }
            steps["scan_product"] = {"status": "scanned", "item_id": collected_ids[0]}
        else:
            steps["scan_product"] = {"status": "scan_failed", "reason": pscan_res.get("status")}

    stashed_ids = []
    if (stash_ids is not None or stash_count is not None) and collected_ids:
        want = len(collected_ids) if stash_count is None else min(stash_count, len(collected_ids))
        to_stash = collected_ids[:want]
        if stash_ids is not None:
            bay_ids = list(stash_ids)
        else:
            pub = shopdesk.published(folder)
            bay_ids = [b["id"] for b in (pub.get("smuggler_bays") or []) if isinstance(b, dict) and b.get("id")]
        if not bay_ids:
            return stop("stash_products", "no_smuggler_bay_found", collected_ids=collected_ids)
        pending = list(to_stash)
        for bay_id in bay_ids:
            if not pending:
                break
            bay_inspect = counter.command(
                folder, "inspect_window", generation, rev, owner_id=bay_id, open_if_closed=True, read_id=uuid4().hex
            )
            rev = _next_rev(bay_inspect, folder, generation, rev)
            if bay_inspect.get("status") != "observed":
                continue
            bay_slot = next((c for c in (bay_inspect.get("window") or {}).get("controls") or []
                             if c.get("type") == "slot" and c.get("insert_allowed")), None)
            if not bay_slot:
                continue
            st_res = counter.command(
                folder, "transfer_slots", generation, rev, owner_id=bay_id, control=bay_slot["control"],
                window_token=bay_inspect["window_token"], item_ids=pending, source="inventory",
            )
            rev = _next_rev(st_res, folder, generation, rev)
            moved_here = {r["item_id"] for r in (st_res.get("items") or []) if r.get("status") == "moved"}
            stashed_ids.extend(i for i in pending if i in moved_here)
            pending = [i for i in pending if i not in moved_here]
        if pending:
            steps["stash_products"] = {"status": "partial", "stashed_ids": stashed_ids, "remaining_ids": pending}
            return stop("stash_products", "stash_incomplete", collected_ids=collected_ids, stashed_ids=stashed_ids, remaining_ids=pending)
        steps["stash_products"] = {"status": "stashed", "stashed_ids": stashed_ids}

    return {
        "status": "completed",
        "recipe_id": recipe_id,
        "produced": total_produced,
        "produced_ids": collected_ids,
        "stashed_ids": stashed_ids,
        "product_scan": product_scan,
        "remaining_reagent": remaining_vessel,
        "remaining_liquids": remaining_raw,
        "stop_reason": stop_reason,
        "vessel_location": vessel_location,
        "steps": steps,
        "revision": rev,
    }


def register(server, folder, database):
    @server.tool()
    def chem_finish(vessel_id: int, recipe_id: str, generation: str, revision: int,
                    quantity: int | None = None, stash_ids: list[int] | None = None,
                    stash_count: int | None = None) -> dict:
        """Run the chemical finishing sequence for an evaporated vessel: retrieve from evaporator (if inside),
        scan contents (recording any remaining water/alcohol), insert into chem_finisher, select recipe_id by name,
        batch-produce (up to quantity, or until reagent runs out if omitted), collect outputs to inventory,
        scan one sample for purity/grade, and optionally move products into smuggler bays (stash_ids/stash_count).
        Stops immediately on any failure and reports the stopped step and current vessel location."""
        return run_chem_finish(folder, database, vessel_id, recipe_id, generation, revision,
                               quantity=quantity, stash_ids=stash_ids, stash_count=stash_count)
