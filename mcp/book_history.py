"""Store published stock revisions for offline lookup and cursor-based deltas."""
import json


def schema(db):
    db.execute("""CREATE TABLE IF NOT EXISTS book_events(
        gen TEXT, rev INTEGER, slot INTEGER, day INTEGER, phase TEXT, PRIMARY KEY(gen,rev))""")
    columns = {row[1] for row in db.execute("PRAGMA table_info(book_events)")}
    if "identity_changes" not in columns:
        db.execute("ALTER TABLE book_events ADD COLUMN identity_changes TEXT")
    db.execute("""CREATE TABLE IF NOT EXISTS book_changes(
        gen TEXT, item INTEGER, rev INTEGER, payload TEXT, PRIMARY KEY(gen,item,rev))""")
    db.execute("CREATE INDEX IF NOT EXISTS book_change_revision ON book_changes(gen,rev)")
    db.execute("CREATE TABLE IF NOT EXISTS book_cursors(token TEXT PRIMARY KEY, fields TEXT)")


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


def visible_stock(payload):
    row = json.loads(payload)
    # Historical revisions also contain private stock-filter columns.
    row.pop("trade_contraband", None)
    row.pop("contraband", None)
    return row


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
    return [visible_stock(row[0]) for row in rows]


def delta(db, gen, since, rev):
    ids = db.execute("SELECT DISTINCT item FROM book_changes WHERE gen=? AND rev>? AND rev<=? ORDER BY item",
                     (gen, since, rev))
    upserts, removed = [], []
    for item, in ids:
        def value(at):
            row = db.execute("SELECT payload FROM book_changes WHERE gen=? AND item=? AND rev<=? ORDER BY rev DESC LIMIT 1",
                             (gen, item, at)).fetchone()
            return row[0] if row else None
        old, new = value(since), value(rev)
        if old != new and (not old or not new or visible_stock(old) != visible_stock(new)):
            if new is None:
                removed.append(item)
            else:
                upserts.append(visible_stock(new))
    return upserts, removed


def identity_changes(db, gen, since, rev):
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
    for item, in db.execute(
        "SELECT DISTINCT item FROM book_changes WHERE gen=? AND rev>? AND rev<=? ORDER BY item",
        (gen, since, rev),
    ):
        def payload(at):
            found = db.execute(
                "SELECT payload FROM book_changes WHERE gen=? AND item=? AND rev<=? ORDER BY rev DESC LIMIT 1",
                (gen, item, at),
            ).fetchone()
            return found[0] if found else None
        old, new = payload(since), payload(rev)
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
