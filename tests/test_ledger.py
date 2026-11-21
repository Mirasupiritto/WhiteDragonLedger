import json
import os
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from ledger import lookup


class LedgerReplay(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(dir=os.environ.get("LEDGER_TEST_TEMP"))
        self.addCleanup(self.temp.cleanup)
        self.folder = Path(self.temp.name)
        self.db = self.folder / "account.sqlite3"
        self.generation = "a" * 32
        self.bottle = dict(id=7, name="水瓶", kind="water", area="inventory", owned=True, quantity=1)

    def put(self, name, value):
        (self.folder / name).write_text(json.dumps(value), encoding="utf-8")

    def event(self, seq, **value):
        self.put(f"{self.generation}-{seq}.json", value)

    def head(self, seq):
        self.put("head.json", dict(status="observed", slot=3, phase="CLOSED",
            generation=self.generation, revision=seq, full_scans=1, load_scans=1))

    def read(self, **filters):
        return lookup(self.folder, self.db, **filters)

    def test_queries_and_move(self):
        self.assertEqual(self.read()["status"], "waiting_for_load")
        self.event(1, op="baseline", items=[self.bottle, dict(self.bottle, id=8, owned=False)])
        self.head(1)
        first = self.read(name="水")
        self.assertEqual([x["id"] for x in first["items"]], [7, 8])
        self.assertEqual(self.read(area="showcase")["items"], [])
        self.assertEqual(self.read(owned=True)["applied_records"], 0)
        self.event(2, op="delta", upsert=[], remove=[7])
        self.event(3, op="delta", upsert=[dict(self.bottle, area="showcase")], remove=[])
        self.head(3)
        moved = self.read(area="showcase")
        self.assertEqual(moved["applied_records"], 2)
        self.assertEqual(moved["items"][0]["id"], 7)
        self.assertEqual(len(self.read(area="inventory")["items"]), 1)

    def test_reload_and_interrupted_publication(self):
        self.event(1, op="baseline", items=[self.bottle])
        self.head(1)
        self.read()
        self.event(2, op="delta", upsert=[], remove=[7])
        self.head(3)
        with self.assertRaises(FileNotFoundError):
            self.read()
        self.head(1)
        self.assertEqual(self.read()["items"], [self.bottle])
        self.generation = "b" * 32
        self.event(1, op="baseline", items=[])
        self.head(1)
        self.assertEqual(self.read()["items"], [])


if __name__ == "__main__":
    unittest.main()
