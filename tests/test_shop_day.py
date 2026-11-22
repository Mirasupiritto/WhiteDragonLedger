import json
import os
from pathlib import Path
import tempfile
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "mcp"))
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

    def test_shop_status_exposes_night_report_appeal_and_status_summary(self):
        from shop_day import shop_status
        with tempfile.TemporaryDirectory(dir=os.environ.get("LEDGER_TEST_TEMP")) as temp:
            folder = Path(temp)
            gen = "a" * 32
            (folder / "head.json").write_text(json.dumps({
                "status": "observed", "desk_protocol": 1, "generation": gen, "slot": 4, "revision": 2
            }), encoding="utf-8")
            day_info = {
                "calendar": {"day": 3},
                "trash": {"status": "unavailable"},
                "sleep": {"service_cost": 0, "available": False},
                "night_report": {"lines": ["- 电费已结清。"], "reminders": ["别忘了补充展示柜存货！"]},
                "appeal": {"total": 14, "base": 25, "trash_penalty": 15, "projector_bonus": 4, "garbage_level": 3},
                "status_summary": {"estimated_net_worth": 320, "active_perks": ["thrifty"]},
            }
            (folder / f"desk-{gen}.json").write_text(json.dumps({
                "generation": gen, "slot": 4, "revision": 2, "phase": "MORNING", "day": 3,
                "counter": {"cash": 120}, "day_info": day_info,
            }), encoding="utf-8")
            status = shop_status(folder)
            self.assertEqual(status["night_report"]["lines"], ["- 电费已结清。"])
            self.assertEqual(status["appeal"]["total"], 14)
            self.assertEqual(status["status_summary"]["estimated_net_worth"], 320)
