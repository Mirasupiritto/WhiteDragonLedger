"""Views of the current save's confirmed purchases and visible purifier panel."""
import ctypes
import json
import os
import re
import time
from uuid import NAMESPACE_URL, uuid5

from scene import interrupted_shop_call
from ledger import lookup, read_head
from moves import exchange


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


def search_text(text):
    text = text.casefold()
    if not text or os.name != "nt":
        return text
    # Windows NLS maps traditional and simplified search terms to the same form.
    convert = ctypes.windll.kernel32.LCMapStringEx
    size = convert("zh-CN", 0x02000000, text, len(text.encode("utf-16-le")) // 2, None, 0, None, None, 0)
    if not size:
        raise ctypes.WinError()
    output = ctypes.create_unicode_buffer(size + 1)
    if not convert("zh-CN", 0x02000000, text, len(text.encode("utf-16-le")) // 2, output, size, None, None, 0):
        raise ctypes.WinError()
    return output.value


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
        row["can_remove_to_inventory"] = bool(row["can_remove_bottle"] and row["inventory_has_room"])
        row["available_strips"] = available_strips
        row["ready_to_take_and_test"] = bool(row["can_remove_to_inventory"] and available_strips > 0)
    return {**{k:view[k] for k in ("status","slot","day","phase","generation","revision","available_strips")},
            "strip_summary": f"全店可用試紙剩 {available_strips} 張",
            "machines":list(current.values()),
            "missing_since_last_night":[r for k,r in previous.items() if k not in current]}


def buyer(folder):
    view = published(folder)
    if view["status"] != "observed":
        return view
    return {k:view[k] for k in ("status","slot","phase","generation","revision","customer")}


def sell_order(folder, lines, generation, revision, visit, stop_same_kind_on_budget=False):
    if not all(re.fullmatch("[a-f0-9]{32}", token) for token in (generation, visit)) or revision < 1:
        raise ValueError("Use the generation, revision and visit from the buyer context")
    ids = [line["item_id"] for line in lines]
    if not lines or len(ids) != len(set(ids)) or any(i<1 for i in ids) or any(l["min_price"]<0 for l in lines):
        raise ValueError("Expected distinct positive item IDs and a non-negative minimum for each")
    plan = dict(lines=lines, generation=generation, revision=revision, visit=visit,
                stop_same_kind_on_budget=stop_same_kind_on_budget)
    command = uuid5(NAMESPACE_URL, "white-dragon/sell-order/"+json.dumps(plan,sort_keys=True)).hex
    def receipt():
        try:
            result=json.loads((folder/f"receipt-{command}.json").read_text(encoding="utf-8-sig"))
        except (FileNotFoundError, PermissionError, json.JSONDecodeError):
            return None
        if result["command_id"] != command or result["generation"] != generation:
            raise ValueError("Receipt belongs to another sale order")
        return result
    previous = receipt()
    if previous is not None:
        return previous

    def progress():
        try:
            data = json.loads((folder/f"sale-progress-{generation}.json").read_text(encoding="utf-8-sig"))
        except (FileNotFoundError, PermissionError, json.JSONDecodeError):
            return None
        if (data["command_id"], data["generation"], data["visit"]) == (command, generation, visit):
            return data
        return None

    context = buyer(folder)
    if context["status"] != "observed":
        return context
    if context["phase"] != "OPEN":
        return {"status":"shop_closed"}
    if (context["generation"], (context["customer"] or {}).get("visit")) == (generation, visit):
        pending = progress()
        if pending:
            return receipt() or pending
    if (context["generation"],context["revision"],(context["customer"] or {}).get("visit")) != (generation,revision,visit):
        return {"status":"stale_order"}
    request = dict(plan, command_id=command)
    result = exchange(folder, folder/f"sale-{generation}.json", request, receipt)
    if result["status"] == "unconfirmed":
        return receipt() or progress() or result
    return result
