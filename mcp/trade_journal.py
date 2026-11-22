"""Local trade plans and confirmed receipts. Reading and dismissing never post game commands."""
import json
import book_history
import re
from contextlib import contextmanager

from command_transport import read_optional_json
from database import transaction


TOOLS = {"buy": "buy_merchant_batch", "pay_info": "pay_info_receipt"}
SUCCESS = {"finished", "purchased", "information_purchased"}


@contextmanager
def ticket_transaction(database):
    with transaction(database) as db:
        db.execute("""CREATE TABLE IF NOT EXISTS trade_tickets (
            id TEXT PRIMARY KEY, slot INTEGER NOT NULL, tool TEXT NOT NULL, args TEXT NOT NULL,
            customer TEXT, state TEXT NOT NULL, version INTEGER NOT NULL, receipt TEXT, evidence INTEGER NOT NULL DEFAULT 0
        )""")
        db.execute("CREATE INDEX IF NOT EXISTS trade_tickets_slot ON trade_tickets(slot,state)")
        yield db


def insert(db, tool, request, slot, customer, observation=None):
    ticket_id = request["command_id"]
    args = {k:v for k,v in request.items() if k not in ("action", "command_id", "expires_ms")}
    if observation is not None:
        args["observed_offer"] = observation
    db.execute("""INSERT OR IGNORE INTO trade_tickets(id,slot,tool,args,customer,state,version)
                  VALUES(?,?,?,?,?,'open',1)""",
               (ticket_id, slot, tool, json.dumps(args, ensure_ascii=False), customer))
    return db.execute("SELECT * FROM trade_tickets WHERE id=?", (ticket_id,)).fetchone()


def refresh(db, folder, row, fallback=None):
    if row["state"] == "complete":
        return row
    args = json.loads(row["args"])
    evidence = 3
    result = read_optional_json(folder / f"receipt-{row['id']}.json")
    if not result and row["tool"] in {"sell_order", "trade_batch", "confirm_trade"}:
        evidence = 2
        result = read_optional_json(folder / f"sale-progress-{args['generation']}.json")
        if result and result.get("command_id") != row["id"]:
            result = None
    if not result:
        evidence = 1
        result = fallback
    if evidence < row["evidence"]:
        return row
    if not result or result.get("command_id") != row["id"]:
        return row
    if result.get("generation", args["generation"]) != args["generation"] or result.get("slot", row["slot"]) != row["slot"]:
        raise ValueError("Trade receipt does not belong to this load and slot")
    if result.get("visit", args["visit"]) != args["visit"]:
        raise ValueError("Trade receipt does not belong to this customer")
    payload = json.dumps(result, ensure_ascii=False, sort_keys=True)
    state = row["state"]
    if state == "open" and result["status"] in SUCCESS:
        state = "complete"
    if payload != row["receipt"] or evidence != row["evidence"]:
        db.execute("UPDATE trade_tickets SET state=?, receipt=?, evidence=?, version=version+1 WHERE id=?",
                   (state, payload, evidence, row["id"]))
        row = db.execute("SELECT * FROM trade_tickets WHERE id=?", (row["id"],)).fetchone()
    return row


def describe(row, detail=False):
    args = json.loads(row["args"])
    receipt = json.loads(row["receipt"]) if row["receipt"] else None
    result = dict(ticket_id=row["id"], slot=row["slot"], version=row["version"], state=row["state"],
                  tool=row["tool"], customer=row["customer"], visit=args["visit"], generation=args["generation"],
                  last_status=receipt["status"] if receipt else "prepared")
    if detail:
        offer = args.pop("observed_offer", None)
        if offer is not None:
            result["observed_offer"] = offer
        if row["tool"] in {"trade_batch","confirm_trade"}:
            source_lines = args["lines"]
            args = {key:args[key] for key in ("generation","revision","visit")}
            if row["tool"] == "trade_batch":
                args["orders"] = [dict(item_id=line["item_id"], side=line["side"],
                                       price_limit=line["max_price"] if line["side"]=="buy" else line["min_price"])
                                  for line in source_lines]
            else:
                line = source_lines[0]
                args.update(item_id=line["item_id"], side=line["side"], price=line["exact_price"])
        result.update(arguments=args, last_receipt=receipt)
    return result


def dispatch(database, folder, tool, request, slot, customer, send, *, observation=None):
    with ticket_transaction(database) as db:
        row = insert(db, tool, request, slot, customer, observation)
        row = refresh(db, folder, row)
        if row["state"] == "complete" or (row["state"] == "open" and row["evidence"] == 3):
            return json.loads(row["receipt"])
        if row["state"] == "dismissed":
            return {"status":"ticket_dismissed", "ticket_id":row["id"], "version":row["version"]}
    # Commit the original plan before dispatch. No database lock while waiting on Unity.
    result = send()
    with ticket_transaction(database) as db:
        row = db.execute("SELECT * FROM trade_tickets WHERE id=?", (request["command_id"],)).fetchone()
        refresh(db, folder, row, result)
    return result


def import_inflight(db, folder, slot):
    head = read_optional_json(folder / "head.json") or {}
    gen = head.get("generation", "")
    if head.get("slot") != slot or not re.fullmatch("[0-9a-f]{32}", gen):
        return
    # A trade submitted before the journal upgrade can still have a live inbox.
    for prefix in ("sale", "counter"):
        plan = read_optional_json(folder / f"{prefix}-{gen}.json")
        if not plan or plan.get("generation") != gen or not plan.get("visit"):
            continue
        tool = plan.get("tool", "sell_order") if prefix == "sale" else TOOLS.get(plan.get("action"))
        if tool and re.fullmatch("[0-9a-f]{32}", plan.get("command_id", "")):
            insert(db, tool, plan, slot, None)


def ledger(folder, database, slot, ticket_id=None, *, inventory=False, name=None, area=None, owned=None):
    with ticket_transaction(database) as db:
        import_inflight(db, folder, slot)
        if ticket_id is not None:
            row = db.execute("SELECT * FROM trade_tickets WHERE slot=? AND id=?", (slot,ticket_id)).fetchone()
            if not row:
                return {"status":"ticket_not_found", "slot":slot}
            return {"status":"observed", "ticket":describe(refresh(db,folder,row), detail=True)}
        rows = db.execute("SELECT * FROM trade_tickets WHERE slot=? AND state='open' ORDER BY rowid", (slot,)).fetchall()
        updated = [refresh(db, folder, row) for row in rows]
        result = {"status":"observed", "slot":slot, "open_tickets":[describe(row) for row in updated if row["state"]=="open"]}
        if inventory or any(value is not None for value in (name, area, owned)):
            book_history.schema(db)
            saved = db.execute("SELECT gen,rev FROM book_events WHERE slot=? ORDER BY rowid DESC LIMIT 1", (slot,)).fetchone()
            if saved is None:
                result["inventory_status"] = "never_observed"
            else:
                result.update(inventory_status="cached", generation=saved["gen"], revision=saved["rev"],
                              items=book_history.snapshot(db, saved["gen"], saved["rev"], name, area, owned))
        return result


def dismiss(folder, database, slot, ticket_id, version):
    with ticket_transaction(database) as db:
        row = db.execute("SELECT * FROM trade_tickets WHERE slot=? AND id=?", (slot,ticket_id)).fetchone()
        if not row:
            return {"status":"ticket_not_found", "slot":slot}
        row = refresh(db, folder, row)
        if row["state"] != "open":
            return {"status":"already_"+row["state"], **describe(row)}
        if row["version"] != version:
            return {"status":"ticket_changed", **describe(row)}
        db.execute("UPDATE trade_tickets SET state='dismissed', version=version+1 WHERE id=?", (ticket_id,))
        return {"status":"dismissed", "slot":slot, "ticket_id":ticket_id, "version":version+1}
