import json
import os
from pathlib import Path
import tempfile
import unittest

from shop_day import trash_contents


class TrashLedger(unittest.TestCase):
    def test_discard_retrieve_midnight_survivor_and_reload(self):
        with tempfile.TemporaryDirectory(dir=os.environ.get("LEDGER_TEST_TEMP")) as temp:
            folder = Path(temp)
            db = folder / "account.sqlite3"
            first = dict(id=12, name="杯子", kind="cup", quantity=2)
            second = dict(id=15, name="保留物", kind="retained", quantity=1)

            def replay(gen, version, day, rows, slot=4):
                view = dict(generation=gen, day=day, slot=slot, trash=dict(status="observed",version=version))
                (folder/"head.json").write_text(json.dumps({"generation":gen,"slot":slot}),encoding="utf-8")
                snapshot = dict(generation=gen, version=version, day=day, items=rows)
                (folder/f"trash-{gen}-{version}.json").write_text(json.dumps(snapshot),encoding="utf-8")
                return trash_contents(folder,db,view)

            self.assertEqual(replay("a",1,10,[first])["items"], [first])
            self.assertEqual(replay("a",2,10,[])["items"], [])
            self.assertEqual(replay("a",3,10,[first,second])["items"], [first,second])
            self.assertEqual(replay("a",4,11,[second])["items"], [second])
            self.assertEqual(replay("b",1,10,[])["items"], [])
