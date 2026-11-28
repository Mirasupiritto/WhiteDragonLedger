"""Import the game observer's published changes, then query the local account."""
import json
import time
from contextlib import nullcontext
from sqlite3 import Connection
import book_history
from pathlib import Path

from command_transport import is_hex32
from database import ensure_schema, transaction
from server_state import snapshot_cache
from stock_query import STOCK_DETAIL_COLUMNS, row_to_item, select_stock


def _db_stock_row(row):
    size = row.get("size") or {}
    slot = row.get("slot") or {}
    return {
        "save_id": None, "bottle_label": None, "contraband": None,
        "trade_contraband": None, "machine_id": None, "can_take_now": None,
        **row,
        "parent_id": row.get("parent_id"), "access": row.get("access"),
        "container_path": json.dumps(row["container_path"], ensure_ascii=False) if row.get("container_path") else None,
        "categories": json.dumps(sorted(set(row["categories"]))) if "categories" in row else None,
        "size_w": size.get("w"), "size_h": size.get("h"),
        "slot_x": slot.get("x"), "slot_y": slot.get("y"),
        "rotatable": None if "rotatable" not in row else int(bool(row["rotatable"])),
        "liquid_main": json.dumps(row["liquid_main"], ensure_ascii=False) if row.get("liquid_main") else None,
        "water_ml": row.get("water_ml"), "liquid_ml": row.get("liquid_ml"), "capacity_ml": row.get("capacity_ml"),
        "empty": None if "empty" not in row else int(bool(row["empty"])),
        "water_container": None if "water_container" not in row else int(bool(row["water_container"])),
        "contains_water": None if "contains_water" not in row else int(bool(row["contains_water"])),
        "charge_remaining": row.get("charge_remaining"), "charge_max": row.get("charge_max"),
        "liters_remaining": row.get("liters_remaining"), "estimated_value": row.get("estimated_value"),
        "stolen": None if "stolen" not in row else int(bool(row["stolen"])),
        "stolen_heat": row.get("stolen_heat"),
    }


def same_observation(left, right):
    return (left.get("status") == right.get("status") == "observed"
            and left["generation"] == right["generation"]
            and left["revision"] == right["revision"])


def read_head(folder):
    path = folder / "head.json"
    for attempt in range(3):
        try:
            return snapshot_cache.read_json(path)
        except FileNotFoundError:
            return {"status": "waiting_for_load"}
        except (PermissionError, json.JSONDecodeError):
            if attempt == 2:
                raise
            time.sleep(0.025)


def lookup(folder: Path, database: Path | Connection, name=None, area=None, owned=None, *, save_ids=None, stock_query=None, sync_only=False):
    with (nullcontext(database) if isinstance(database, Connection) else transaction(database)) as db:
        if isinstance(database, Connection):
            ensure_schema(db)
        head = read_head(folder)
        if head["status"] == "waiting_for_load":
            return {"status": "waiting_for_load", "items": []}
        generation = head["generation"]
        revision = head["revision"]
        if (head["status"] != "observed" or not is_hex32(generation)
                or type(revision) is not int or revision < 1):
            raise ValueError("Invalid game ledger head")
        previous = db.execute("SELECT generation, revision FROM checkpoint WHERE id=1").fetchone()
        cursor = previous["revision"] if previous and previous["generation"] == generation else 0
        if cursor > revision:
            raise ValueError("Game journal revision moved backwards in the same load")
        # An older MCP process can import this load while omitting its new columns.
        # Rebuild the local cache from published records; the game keeps its scan count.
        if cursor and head.get("stock_categories_protocol") and db.execute(
            "SELECT 1 FROM stock WHERE categories IS NULL LIMIT 1"
        ).fetchone():
            cursor = 0
        if cursor and head.get("item_details_protocol") and db.execute(
            "SELECT 1 FROM stock WHERE size_w IS NULL LIMIT 1"
        ).fetchone():
            cursor = 0
        if head.get("trade_panel_protocol"):
            # Older readers advance checkpoint 1 without importing purifier snapshots.
            extended = db.execute("SELECT revision FROM checkpoint WHERE id=2 AND generation=?", (generation,)).fetchone()
            cursor = min(cursor, extended["revision"]) if extended else 0
        if head.get("bag_stock_protocol"):
            bag_cursor = db.execute("SELECT revision FROM checkpoint WHERE id=3 AND generation=?", (generation,)).fetchone()
            cursor = min(cursor, bag_cursor["revision"]) if bag_cursor else 0
        recorded = db.execute("SELECT MAX(rev) FROM book_events WHERE gen=?", (generation,)).fetchone()[0]
        cursor = min(cursor, recorded or 0)
        for number in range(cursor + 1, revision + 1):
            event = json.loads((folder / f"{generation}-{number}.json").read_text(encoding="utf-8-sig"))
            book_history.record(db, head, number, event)
            if number == 1 and event["op"] == "baseline":
                db.execute("DELETE FROM stock")
                rows = event["items"]
            elif number > 1 and event["op"] == "delta":
                db.executemany("DELETE FROM stock WHERE id=?", ((item,) for item in event["remove"]))
                rows = event["upsert"]
            else:
                raise ValueError(f"Unexpected game journal event at {number}")
            if "purifier_stock" in event:
                db.execute("DELETE FROM stock WHERE area='purifier'")
                rows = [*rows, *event["purifier_stock"]]
            if "capacities" in event:
                db.execute("INSERT OR REPLACE INTO ledger_meta VALUES(?,?)",
                           (f"capacities:{generation}", json.dumps(event["capacities"], ensure_ascii=False)))
            db.executemany(
                "INSERT OR REPLACE INTO stock(id,name,kind,area,owned,quantity,save_id,bottle_label,categories,"
                "contraband,trade_contraband,machine_id,can_take_now," + STOCK_DETAIL_COLUMNS + ") "
                "VALUES(:id,:name,:kind,:area,:owned,:quantity,:save_id,:bottle_label,:categories,"
                ":contraband,:trade_contraband,:machine_id,:can_take_now,"
                ":size_w,:size_h,:slot_x,:slot_y,:rotatable,"
                ":water_ml,:liquid_ml,:capacity_ml,:empty,:water_container,:contains_water,:liquid_main,"
                ":charge_remaining,:charge_max,:liters_remaining,:estimated_value,:parent_id,:container_path,:access,"
                ":stolen,:stolen_heat)",
                (_db_stock_row(row) for row in rows),
            )
        if cursor != revision:
            db.execute("INSERT OR REPLACE INTO checkpoint VALUES(1,?,?)", (generation, revision))
            if head.get("trade_panel_protocol"):
                db.execute("INSERT OR REPLACE INTO checkpoint VALUES(2,?,?)", (generation, revision))
            if head.get("bag_stock_protocol"):
                db.execute("INSERT OR REPLACE INTO checkpoint VALUES(3,?,?)", (generation, revision))
        if sync_only:
            return head
        if stock_query is not None:
            return select_stock(folder, db, head, stock_query)
        filters, values = [], []
        if save_ids is None:
            filters.append("area != 'purifier'")
            if area is None:
                filters.append("area != 'trash'")
        if name is not None:
            filters.append("instr(name,?) > 0")
            values.append(name)
        for field, value in (("area", area), ("owned", owned)):
            if value is not None:
                filters.append(field + " = ?")
                values.append(value)
        if save_ids is not None:
            filters.append("save_id IN (SELECT value FROM json_each(?))")
            values.append(json.dumps(sorted(save_ids)))
        condition = " WHERE " + " AND ".join(filters) if filters else ""
        items = [row_to_item(row) for row in db.execute(
            "SELECT id,name,kind,area,owned,quantity,bottle_label,categories,save_id," + STOCK_DETAIL_COLUMNS + " FROM stock" + condition + " ORDER BY id",
            values,
        )]
        extra = {}
        meta_row = db.execute("SELECT value FROM ledger_meta WHERE key=?", (f"capacities:{generation}",)).fetchone()
        if meta_row:
            caps = json.loads(meta_row[0])
            if area in caps:
                extra["capacity"] = caps[area]
            elif area is None and caps:
                extra["capacities"] = caps
        return {**head, "applied_records": revision - cursor, "items": items, **extra}
