"""Store published stock revisions for offline lookup and cursor-based deltas."""
import json


def schema(db):
    db.execute("""CREATE TABLE IF NOT EXISTS book_events(
        gen TEXT, rev INTEGER, slot INTEGER, day INTEGER, phase TEXT, PRIMARY KEY(gen,rev))""")
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
    db.execute("INSERT INTO book_events VALUES(?,?,?,?,?)",
               (gen, rev, head["slot"], event.get("day"), event.get("phase")))
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
        if old != new:
            if new is None:
                removed.append(item)
            else:
                upserts.append(json.loads(new))
    return upserts, removed
