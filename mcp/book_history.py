"""Store published stock revisions for offline lookup and cursor-based deltas."""
import json

from database import ensure_schema


def schema(db):
    ensure_schema(db)


def record(db, head, rev, event):
    gen = head["generation"]
    if db.execute("SELECT 1 FROM book_events WHERE gen=? AND rev=?", (gen,rev)).fetchone():
        return
    changes = dict.fromkeys(event.get("remove", []))
    for row in event.get("items", event.get("upsert", [])):
        changes[row["id"]] = row
    if "purifier_stock" in event:
        # Stock still contains the previous revision, before lookup imports this event.
        for old, in db.execute("SELECT id FROM stock WHERE area='purifier'"):
            if rev > 1:
                changes.setdefault(old, None)
        for row in event["purifier_stock"]:
            changes[row["id"]] = row
    id_changes = event.get("identity_changed")
    db.execute("INSERT INTO book_events VALUES(?,?,?,?,?,?)",
               (gen, rev, head["slot"], event.get("day"), event.get("phase"),
                json.dumps(id_changes, ensure_ascii=False) if id_changes else None))
    db.executemany("INSERT INTO book_changes VALUES(?,?,?,?)",
                   ((gen, item, rev, json.dumps(row, ensure_ascii=False, sort_keys=True) if row else None)
                    for item, row in changes.items()))


def snapshot(db, gen, rev, name=None, area=None, owned=None):
    filters = ["payload IS NOT NULL"]
    args = [gen, rev]
    if name is not None:
        filters.append("instr(json_extract(payload,'$.name'),?)>0")
        args.append(name)
    for key, value in (("area",area), ("owned",owned)):
        if value is not None:
            filters.append(f"json_extract(payload,'$.{key}')=?")
            args.append(value)
    rows = db.execute("""SELECT payload FROM (
        SELECT item,payload,MAX(rev) FROM book_changes WHERE gen=? AND rev<=? GROUP BY item
        ) WHERE """ + " AND ".join(filters) + " ORDER BY item", args)
    return [json.loads(row[0]) for row in rows]


def changes_between(db, gen, since, rev):
    """Fetch before/after payloads for all items modified in (since, rev] in a single query."""
    return db.execute(
        """
        SELECT
            c.item,
            (SELECT payload FROM book_changes
             WHERE gen = ? AND item = c.item AND rev <= ?
             ORDER BY rev DESC LIMIT 1) AS old_payload,
            (SELECT payload FROM book_changes
             WHERE gen = ? AND item = c.item AND rev <= ?
             ORDER BY rev DESC LIMIT 1) AS new_payload
        FROM (
            SELECT DISTINCT item FROM book_changes
            WHERE gen = ? AND rev > ? AND rev <= ?
            ORDER BY item
        ) c
        """,
        (gen, since, gen, rev, gen, since, rev),
    ).fetchall()


def delta(db, gen, since, rev):
    upserts, removed = [], []
    for item, old, new in changes_between(db, gen, since, rev):
        if old != new:
            if new is None:
                removed.append(item)
            else:
                upserts.append(json.loads(new))
    return upserts, removed


def identity_changes(db, gen, since, rev, *, item_rows=None):
    changes = []
    seen = set()
    for row, in db.execute(
        "SELECT identity_changes FROM book_events WHERE gen=? AND rev>? AND rev<=? AND identity_changes IS NOT NULL ORDER BY rev",
        (gen, since, rev),
    ):
        for entry in json.loads(row):
            key = (entry.get("id"), entry.get("from"), entry.get("to"))
            if key not in seen:
                seen.add(key)
                changes.append(entry)
    rows = item_rows if item_rows is not None else changes_between(db, gen, since, rev)
    for item, old, new in rows:
        if not old or not new or old == new:
            continue
        old_obj, new_obj = json.loads(old), json.loads(new)
        if old_obj.get("name") != new_obj.get("name") or old_obj.get("kind") != new_obj.get("kind"):
            key = (item, old_obj.get("name"), new_obj.get("name"))
            if key not in seen:
                seen.add(key)
                entry = {"id": item, "from": old_obj.get("name"), "to": new_obj.get("name")}
                if old_obj.get("kind") != new_obj.get("kind"):
                    entry["from_kind"] = old_obj.get("kind")
                    entry["to_kind"] = new_obj.get("kind")
                changes.append(entry)
    return changes
