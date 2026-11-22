"""SQLite transaction shared by the tables in the local shop ledger."""
import sqlite3
from contextlib import closing, contextmanager


@contextmanager
def transaction(database):
    database.parent.mkdir(parents=True, exist_ok=True)
    with closing(sqlite3.connect(database, timeout=8)) as db, db:
        db.row_factory = sqlite3.Row
        db.execute("BEGIN IMMEDIATE")
        yield db
