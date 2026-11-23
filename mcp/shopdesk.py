"""Views of the current save's confirmed purchases and visible purifier panel."""
import json
import re
import time
from uuid import NAMESPACE_URL, uuid5

from search_text import search_text
from scene import interrupted_shop_call
from ledger import lookup, same_observation, read_head
from command_transport import exchange, read_command_receipt, read_optional_json
import trade_journal


NUCLEAR_WASTE_GUIDANCE = (
    "交易檯（back）上有核廢料（nuclear_waste）。請先用 move_items 將核廢料移至 inventory（庫存）或 showcase（展示櫃）；"
    "垃圾桶無法消除核廢料。若庫存滿了或放不下，需由玩家清出空間或於夜間 CLOSED 後，在店內用 inspect_item_window＋transfer_slot_items "
    "裝入夠大的背包（如 6x6 小背包以上，3x3 腰包放不下），再帶去垃圾場（dumping_ground）移到 ground 後以 abandon_ground=True 返店"
    "（或由玩家於 UI 放進背包／手部帶去垃圾場拋棄；拋棄達標會觸發喬治奧來店歸還一次）。"
)


def strip_protocols(data):
    if isinstance(data, dict):
        return {k: strip_protocols(v) for k, v in data.items() if not k.endswith("_protocol")}
    return data


def published(folder, stream="desk"):
    changed = interrupted_shop_call(folder)
    if changed:
        return changed
    for attempt in range(3):
        head = read_head(folder)
        if head.get("status") != "observed":
            return {"status": "waiting_for_load"}
        if head.get("desk_protocol") != 1:
            return {"status": "reload_updated_mod"}
        generation = head["generation"]
        if not re.fullmatch("[a-f0-9]{32}", generation):
            raise ValueError("Invalid game load identifier")
        try:
            data = json.loads((folder / f"{stream}-{generation}.json").read_text(encoding="utf-8-sig"))
        except (FileNotFoundError, PermissionError, json.JSONDecodeError):
            if attempt < 2:
                time.sleep(.05)
                continue
            return {"status": "awaiting_observation"}
        if read_head(folder).get("generation") == data["generation"] == generation:
            counter = data.get("counter", {})
            if "demand" in counter and counter["demand"].get("source") != "trade_panel":
                counter["demand"] = {"source": "trade_panel", "status": "reload_updated_mod"}
            return {**data, "status": "observed"}
    return {"status": "load_changed"}


def summarize(lot, on_hand):
    members = lot["members"]
    sold = [member for member in members if "sale" in member]
    remaining = [member for member in members if "sale" not in member]
    recovered = sum(member["sale"] for member in sold)
    result = {k: lot[k] for k in ("lot_id", "supplier", "day", "paid")}
    result.update(products=sorted({m["name"] for m in members}), purchased=len(members),
                  sold=len(sold), recovered=recovered, remaining_unsold=len(remaining),
                  in_stock=sum(m["key"] in on_hand for m in remaining))
    if lot["paid"] is None:
        result["payment_status"] = "unconfirmed"
    if result["in_stock"] != len(remaining):
        result["unlocated"] = len(remaining) - result["in_stock"]
    if not remaining and lot["paid"] is not None:
        result["gross_profit"] = recovered - lot["paid"]
    return result


def purchase_lots(folder, database, supplier=None, product=None, lot_id=None):
    view = published(folder, "lots")
    if view["status"] != "observed":
        return view
    supplier = search_text(supplier) if supplier else None
    product = search_text(product) if product else None
    lots = [lot for lot in view["lots"]
            if (lot_id is None or lot_id == lot["lot_id"])
            and (supplier is None or any(supplier in search_text(lot[k]) for k in ("supplier", "supplier_id")))
            and (product is None or any(product in search_text(m["name"]+" "+m["kind"]) for m in lot["members"]))]
    keys = {m["key"] for lot in lots for m in lot["members"] if "sale" not in m}
    stock = lookup(folder, database, owned=True, save_ids=keys)
    if stock.get("generation") != view["generation"]:
        return {"status": "load_changed"}
    on_hand = {r["save_id"] for r in stock["items"]}
    return {"status": "observed", "slot": view["slot"], "generation": view["generation"],
            "lots": [summarize(lot, on_hand) for lot in lots]}


def machines(folder):
    view = published(folder)
    if view["status"] != "observed":
        return view
    available_strips = view["available_strips"]
    current = {row["machine_key"]:dict(row) for row in view["machines"]}
    previous = {row["machine_key"]:row for row in view["last_night"]}
    for key, row in current.items():
        last = previous.get(key)
        row["last_night_bottle_key"] = last["bottle_key"] if last else None
        row["same_bottle_as_last_night"] = (row["bottle_key"] == last["bottle_key"]
                                               if last and last["bottle_key"] is not None else None)
        row["can_remove_to_inventory"] = bool(view["phase"] in ("MORNING", "CLOSED") and
                                              row["can_remove_bottle"] and row["inventory_has_room"])
        row["available_strips"] = available_strips
        row["ready_to_take_and_test"] = bool(row["can_remove_to_inventory"] and available_strips > 0)
    return {**{k:view[k] for k in ("status","slot","day","phase","generation","revision","available_strips")},
            "strip_summary": f"全店可用試紙剩 {available_strips} 張",
            "machines":list(current.values()),
            "missing_since_last_night":[r for k,r in previous.items() if k not in current]}


def allocation_stock(folder, database, query):
    result = lookup(folder, database, stock_query=query)
    if result["status"] != "observed" or query.get("owned") is False:
        return result
    view = published(folder)
    if view.get("status") != "observed" or view["phase"] != "OPEN":
        return result
    counter = view["counter"]
    demand = counter.get("demand", {})
    if not (demand.get("source") == "trade_panel" and demand.get("status") == "visible" and demand.get("max_per_kind") == 1):
        demand = counter.get("visit_variety", {})
        if (demand.get("visit") != counter.get("visit") or demand.get("status") != "last_visible_for_visit"
                or demand.get("source") != "trade_panel" or demand.get("max_per_kind") != 1):
            return result
    if not counter.get("visit") or not same_observation(view, result):
        return {**result, "allocation_status":"observation_changed"}
    sold = set(demand["sold_kinds"])
    groups = {}
    for item in result["items"]:
        if item["owned"] and item["area"] in {"inventory","showcase"}:
            groups.setdefault(item["kind"], []).append(item["id"])
    result.update(visit=counter["visit"], allocation_status=demand["status"], sold_kinds=demand["sold_kinds"],
                  allocation_hint=demand["allocation_hint"],
                  kind_groups=[dict(kind=kind, item_ids=ids, count=len(ids),
                                    recommended_count=0 if kind in sold else 1)
                               for kind, ids in groups.items()])
    return result


def buyer(folder):
    view = published(folder)
    if view["status"] != "observed":
        return view
    result = {k:view[k] for k in ("status","slot","phase","generation","revision","customer")}
    if "trade_tools_protocol" in view.get("counter", {}):
        result["trade_tools_protocol"] = view["counter"]["trade_tools_protocol"]
    if "demand" in view.get("counter", {}):
        result["demand"] = view["counter"]["demand"]
    return result


def sell_order(folder, lines, generation, revision, visit, stop_same_kind_on_budget=False, *, database=None, tool="sell_order", on_table=False, bundle=False):
    if not all(re.fullmatch("[a-f0-9]{32}", token) for token in (generation, visit)) or revision < 1:
        raise ValueError("Use the generation, revision and visit from the buyer context")
    ids = [line["item_id"] for line in lines]
    if not lines or len(ids) != len(set(ids)) or any(i<1 for i in ids) or any(l.get("min_price", l.get("max_price", -1))<0 for l in lines):
        raise ValueError("Expected distinct positive item IDs and a non-negative minimum for each")
    plan = dict(lines=lines, generation=generation, revision=revision, visit=visit,
                stop_same_kind_on_budget=stop_same_kind_on_budget)
    if bundle:
        plan["bundle"] = True
    if tool != "sell_order":
        plan.update(tool=tool, on_table=on_table)
    command = uuid5(NAMESPACE_URL, "white-dragon/sell-order/"+json.dumps(plan,sort_keys=True)).hex
    def receipt():
        return read_command_receipt(folder, command, generation)

    previous = receipt()
    if previous is not None:
        return previous

    def progress():
        data = read_optional_json(folder/f"sale-progress-{generation}.json")
        if data is not None and (data["command_id"], data["generation"], data["visit"]) == (command, generation, visit):
            return data
        return None

    context = buyer(folder)
    if context["status"] != "observed":
        return context
    if tool != "sell_order" and context.get("trade_tools_protocol") != 1:
        return {"status":"reload_updated_mod"}
    if context["phase"] != "OPEN":
        return {"status":"shop_closed"}
    if (context["generation"], (context["customer"] or {}).get("visit")) == (generation, visit):
        pending = progress()
        if pending:
            return receipt() or pending
    if (context["generation"],context["revision"],(context["customer"] or {}).get("visit")) != (generation,revision,visit):
        table_items = context.get("table_items")
        if table_items is None:
            desk_view = published(folder)
            table_items = desk_view.get("counter", {}).get("table_items", []) if desk_view.get("status") == "observed" else []
        blocking = [{"id": r["id"], "kind": r["kind"], "area": r["area"]} for r in table_items]
        if context["generation"] != generation:
            stale_reason = "generation_changed"
        elif (context["customer"] or {}).get("visit") != visit:
            stale_reason = "visit_changed"
        elif any(r.get("hazardous") for r in table_items):
            stale_reason = "nuclear_waste_on_counter"
        elif blocking:
            stale_reason = "trade_table_not_empty"
        else:
            stale_reason = "revision_changed"
        stale = {"status": "stale_order", "reason": stale_reason, "blocking_items": blocking,
                 "latest_revision": context["revision"]}
        if stale_reason == "nuclear_waste_on_counter":
            stale["guidance"] = NUCLEAR_WASTE_GUIDANCE
        return stale
    request = dict(plan, command_id=command)
    def send():
        return exchange(folder, folder/f"sale-{generation}.json", request, receipt)
    result = send() if database is None else trade_journal.dispatch(
        database, folder, tool, request, context["slot"], context["customer"].get("name"), send)
    if result["status"] == "unconfirmed":
        return receipt() or progress() or result
    return result
