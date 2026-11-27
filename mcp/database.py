"""SQLite transaction and centralized once-per-process schema manager for the shop ledger."""
from contextlib import closing, contextmanager
from pathlib import Path
import sqlite3
import threading


_EXTRA_STOCK_COLUMNS = (
    ("size_w", "INTEGER"), ("size_h", "INTEGER"), ("slot_x", "INTEGER"), ("slot_y", "INTEGER"),
    ("rotatable", "INTEGER"), ("water_ml", "INTEGER"), ("liquid_ml", "INTEGER"), ("capacity_ml", "INTEGER"),
    ("empty", "INTEGER"), ("water_container", "INTEGER"), ("contains_water", "INTEGER"), ("liquid_main", "TEXT"),
    ("charge_remaining", "INTEGER"), ("charge_max", "INTEGER"), ("liters_remaining", "REAL"),
    ("estimated_value", "INTEGER"), ("parent_id", "INTEGER"), ("container_path", "TEXT"), ("access", "TEXT"),
    ("stolen", "INTEGER"), ("stolen_heat", "INTEGER"),
)

_SCHEMA_LOCK = threading.Lock()
_INITIALIZED_DBS: set[Path] = set()
_INITIALIZED_TICKET_DBS: set[Path] = set()


def ensure_trade_tickets_schema(db):
    """Ensure trade_tickets table and index exist on the given SQLite connection."""
    db.execute("""CREATE TABLE IF NOT EXISTS trade_tickets (
        id TEXT PRIMARY KEY, slot INTEGER NOT NULL, tool TEXT NOT NULL, args TEXT NOT NULL,
        customer TEXT, state TEXT NOT NULL, version INTEGER NOT NULL, receipt TEXT, evidence INTEGER NOT NULL DEFAULT 0
    )""")
    db.execute("CREATE INDEX IF NOT EXISTS trade_tickets_slot ON trade_tickets(slot,state)")


def is_trade_tickets_initialized(db_path: Path) -> bool:
    resolved = db_path.resolve()
    with _SCHEMA_LOCK:
        return db_path.exists() and resolved in _INITIALIZED_TICKET_DBS


def mark_trade_tickets_initialized(db_path: Path):
    resolved = db_path.resolve()
    with _SCHEMA_LOCK:
        _INITIALIZED_TICKET_DBS.add(resolved)


def ensure_schema(db):
    """Ensure all ledger tables, columns, and indexes exist on the given SQLite connection."""
    db.execute("CREATE TABLE IF NOT EXISTS checkpoint (id INTEGER PRIMARY KEY, generation TEXT, revision INTEGER)")
    db.execute("CREATE TABLE IF NOT EXISTS ledger_meta (key TEXT PRIMARY KEY, value TEXT)")
    db.execute("CREATE TABLE IF NOT EXISTS stock (id INTEGER PRIMARY KEY, name TEXT, kind TEXT, area TEXT, owned INTEGER, quantity INTEGER)")
    columns = {row[1] for row in db.execute("PRAGMA table_info(stock)")}
    if "bottle_label" not in columns:
        db.execute("ALTER TABLE stock ADD COLUMN bottle_label TEXT")
    if "save_id" not in columns:
        db.execute("ALTER TABLE stock ADD COLUMN save_id INTEGER")
        db.execute("CREATE INDEX IF NOT EXISTS stock_save_id ON stock(save_id)")
    if "categories" not in columns:
        db.execute("ALTER TABLE stock ADD COLUMN categories TEXT")
        db.execute("CREATE INDEX IF NOT EXISTS stock_categories ON stock(categories)")
        db.execute("CREATE INDEX IF NOT EXISTS stock_label ON stock(bottle_label)")
    if "contraband" not in columns:
        db.execute("ALTER TABLE stock ADD COLUMN contraband INTEGER")
    for column in ("trade_contraband", "machine_id", "can_take_now"):
        if column not in columns:
            db.execute(f"ALTER TABLE stock ADD COLUMN {column} INTEGER")
    migrated_liquid = "liquid_main" not in columns
    for column, col_type in _EXTRA_STOCK_COLUMNS:
        if column not in columns:
            db.execute(f"ALTER TABLE stock ADD COLUMN {column} {col_type}")
    db.execute("CREATE INDEX IF NOT EXISTS stock_kind ON stock(kind)")
    if migrated_liquid and columns:
        # An older DB without liquid_main must re-import composition from published events.
        db.execute("DELETE FROM checkpoint WHERE id=1")

    db.execute("""CREATE TABLE IF NOT EXISTS book_events(
        gen TEXT, rev INTEGER, slot INTEGER, day INTEGER, phase TEXT, PRIMARY KEY(gen,rev))""")
    book_cols = {row[1] for row in db.execute("PRAGMA table_info(book_events)")}
    if "identity_changes" not in book_cols:
        db.execute("ALTER TABLE book_events ADD COLUMN identity_changes TEXT")
    db.execute("""CREATE TABLE IF NOT EXISTS book_changes(
        gen TEXT, item INTEGER, rev INTEGER, payload TEXT, PRIMARY KEY(gen,item,rev))""")
    db.execute("CREATE INDEX IF NOT EXISTS book_change_revision ON book_changes(gen,rev)")
    db.execute("CREATE TABLE IF NOT EXISTS book_cursors(token TEXT PRIMARY KEY, fields TEXT)")

    db.execute("CREATE TABLE IF NOT EXISTS trash_checkpoint (slot INTEGER PRIMARY KEY,generation TEXT,version INTEGER)")
    db.execute("CREATE TABLE IF NOT EXISTS trash_items (slot INTEGER,id INTEGER,name TEXT,kind TEXT,quantity INTEGER,PRIMARY KEY(slot,id))")
    db.execute("""CREATE TABLE IF NOT EXISTS notified_heat(
        generation TEXT, run_id TEXT, PRIMARY KEY(generation, run_id))""")


@contextmanager
def transaction(database):
    db_path = Path(database)
    db_path.parent.mkdir(parents=True, exist_ok=True)
    resolved = db_path.resolve()
    needs_schema = False
    with _SCHEMA_LOCK:
        if not db_path.exists():
            _INITIALIZED_DBS.discard(resolved)
            _INITIALIZED_TICKET_DBS.discard(resolved)
        if resolved not in _INITIALIZED_DBS:
            needs_schema = True
    with closing(sqlite3.connect(db_path, timeout=8)) as db, db:
        db.row_factory = sqlite3.Row
        db.execute("BEGIN IMMEDIATE")
        if needs_schema:
            ensure_schema(db)
        yield db
    if needs_schema:
        with _SCHEMA_LOCK:
            _INITIALIZED_DBS.add(resolved)
