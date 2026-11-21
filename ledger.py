"""Import the game observer's published changes, then query the local account."""
import json
import re
import sqlite3
import time
from contextlib import closing
from pathlib import Path


def read_head(folder):
    path = folder / "head.json"
    for attempt in range(3):
        try:
            return json.loads(path.read_text(encoding="utf-8-sig"))
        except FileNotFoundError:
            return {"status": "waiting_for_load"}
        except (PermissionError, json.JSONDecodeError):
            if attempt == 2:
                raise
            time.sleep(0.025)


def lookup(folder: Path, database: Path, name=None, area=None, owned=None, *, save_ids=None):
    database.parent.mkdir(parents=True, exist_ok=True)
    with closing(sqlite3.connect(database, timeout=8)) as db, db:
        db.row_factory = sqlite3.Row
        db.execute("BEGIN IMMEDIATE")
        head = read_head(folder)
        if head["status"] == "waiting_for_load":
            return {"status": "waiting_for_load", "items": []}
        generation = head["generation"]
        revision = head["revision"]
        if (head["status"] != "observed" or not re.fullmatch("[a-f0-9]{32}", generation)
                or type(revision) is not int or revision < 1):
            raise ValueError("Invalid game ledger head")
        db.execute("CREATE TABLE IF NOT EXISTS checkpoint (id INTEGER PRIMARY KEY, generation TEXT, revision INTEGER)")
        db.execute("CREATE TABLE IF NOT EXISTS stock (id INTEGER PRIMARY KEY, name TEXT, kind TEXT, area TEXT, owned INTEGER, quantity INTEGER)")
        if "save_id" not in {row["name"] for row in db.execute("PRAGMA table_info(stock)")}:
            db.execute("ALTER TABLE stock ADD COLUMN save_id INTEGER")
            db.execute("CREATE INDEX IF NOT EXISTS stock_save_id ON stock(save_id)")
        previous = db.execute("SELECT generation, revision FROM checkpoint WHERE id=1").fetchone()
        cursor = previous["revision"] if previous and previous["generation"] == generation else 0
        if cursor > revision:
            raise ValueError("Game journal revision moved backwards in the same load")
        for number in range(cursor + 1, revision + 1):
            event = json.loads((folder / f"{generation}-{number}.json").read_text(encoding="utf-8-sig"))
            if number == 1 and event["op"] == "baseline":
                db.execute("DELETE FROM stock")
                rows = event["items"]
            elif number > 1 and event["op"] == "delta":
                db.executemany("DELETE FROM stock WHERE id=?", ((item,) for item in event["remove"]))
                rows = event["upsert"]
            else:
                raise ValueError(f"Unexpected game journal event at {number}")
            db.executemany("INSERT OR REPLACE INTO stock(id,name,kind,area,owned,quantity,save_id) VALUES(:id,:name,:kind,:area,:owned,:quantity,:save_id)",
                           ({"save_id":None, **row} for row in rows))
        if cursor != revision:
            db.execute("INSERT OR REPLACE INTO checkpoint VALUES(1,?,?)", (generation, revision))
        filters, values = [], []
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
        items = [dict(row) for row in db.execute("SELECT * FROM stock" + condition + " ORDER BY id", values)]
        for item in items:
            item["owned"] = bool(item["owned"])
            if not item["save_id"]:
                del item["save_id"]
        return {**head, "applied_records": revision - cursor, "items": items}
