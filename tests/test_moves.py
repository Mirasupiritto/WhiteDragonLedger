import json
import os
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "mcp"))
from moves import move_item, move_items


class MoveMailbox(unittest.TestCase):
    def setUp(self):
        tmp = tempfile.TemporaryDirectory(dir=os.environ.get("LEDGER_TEST_TEMP"))
        self.addCleanup(tmp.cleanup)
        self.folder = Path(tmp.name)
        self.generation = "1" * 32
        self.command = "a" * 32
        self.head = dict(status="observed", generation=self.generation, revision=2,
                         slot=4, phase="MORNING", move_protocol=1, load_scans=1)
        self.publish_head()
        self.inbox = self.folder / f"move-{self.generation}.json"

    def publish_head(self):
        (self.folder / "head.json").write_text(json.dumps(self.head), encoding="utf-8")

    def request(self, revision=2):
        return move_item(self.folder, 26, "inventory", "showcase", self.generation, revision, self.command)

    def test_game_receipt_and_replay(self):
        response = dict(command_id=self.command, item_id=26, generation=self.generation,
                        expected_revision=2, revision=3, status="moved", actual_area="showcase",
                        requested_area="showcase", load_scans=1)
        response["from"] = "inventory"
        def respond():
            data = json.loads(self.inbox.read_text(encoding="utf-8"))
            self.assertEqual(data["destination"], "showcase")
            path = self.folder / f"receipt-{self.command}.json"
            path.write_text(json.dumps(response), encoding="utf-8")
            self.inbox.unlink()
        with patch("command_transport.time.sleep", side_effect=lambda _: respond()):
            self.assertEqual(self.request(), response)
        self.head["revision"] = 3
        self.publish_head()
        self.assertEqual(self.request(), response)
        self.assertFalse(self.inbox.exists())
        with self.assertRaises(ValueError):
            self.request(revision=3)

    def test_counter_move_requires_visit_and_replays_only_the_same_visit(self):
        visit = "b"*32
        def stage(current):
            return move_item(self.folder,26,"front","back",self.generation,2,self.command,visit=current)
        with self.assertRaises(ValueError):
            stage(None)
        self.head["phase"] = "OPEN"
        self.publish_head()
        self.assertEqual(stage(visit)["status"],"reload_updated_mod")
        self.head["counter_move_protocol"] = 1
        self.publish_head()
        def tick(_):
            plan = json.loads(self.inbox.read_text())
            self.assertEqual(plan["visit"],visit)
            receipt = dict(status="moved",command_id=self.command,generation=self.generation,expected_revision=2,
                           item_id=26,requested_area="back",actual_area="back",visit=visit,load_scans=1)
            receipt["from"]="front"
            (self.folder/f"receipt-{self.command}.json").write_text(json.dumps(receipt))
            self.inbox.unlink()
        with patch("command_transport.time.sleep",side_effect=tick):
            settled = stage(visit)
        self.assertEqual(stage(visit),settled)
        self.assertEqual((settled["actual_area"],settled["load_scans"]),("back",1))
        with self.assertRaises(ValueError):
            stage("c"*32)
        self.assertFalse(self.inbox.exists())

    def test_preconditions_and_contention(self):
        self.assertEqual(self.request(1)["status"], "stale_observation")
        self.head["phase"] = "OPEN"
        self.publish_head()
        self.assertEqual(self.request()["status"], "shop_open")
        self.head["phase"] = "CLOSED"
        del self.head["move_protocol"]
        self.publish_head()
        self.assertEqual(self.request()["status"], "reload_updated_mod")
        self.assertFalse(self.inbox.exists())
        self.head["move_protocol"] = 1
        self.publish_head()
        self.inbox.write_text(json.dumps(dict(command_id="b"*32)), encoding="utf-8")
        self.assertEqual(self.request()["status"], "busy")
        self.assertEqual(json.loads(self.inbox.read_text())["command_id"], "b"*32)

    def batch(self, ids):
        return move_items(self.folder, ids, "showcase", "inventory", self.generation, 2)

    def test_batch_chains_native_receipts_and_replays(self):
        posted = []
        def game_tick(_):
            request = json.loads(self.inbox.read_text(encoding="utf-8"))
            posted.append((request["item_id"], request["revision"]))
            revision = request["revision"] + 1
            receipt = dict(command_id=request["command_id"], item_id=request["item_id"],
                           generation=self.generation, expected_revision=request["revision"],
                           revision=revision, slot=4, phase="MORNING", full_scans=2,
                           load_scans=1, status="moved", actual_area="inventory",
                           requested_area="inventory")
            receipt["from"] = "showcase"
            self.head["revision"] = revision
            self.publish_head()
            (self.folder / ("receipt-" + request["command_id"] + ".json")).write_text(json.dumps(receipt))
            self.inbox.unlink()
        with patch("command_transport.time.sleep", side_effect=game_tick):
            result = self.batch([25, 26])
            self.assertEqual(self.batch([25, 26]), result)
        self.assertEqual(posted, [(25, 2), (26, 3)])
        self.assertEqual([(r["item_id"], r["actual_area"]) for r in result["items"]],
                         [(25, "inventory"), (26, "inventory")])
        self.assertEqual((result["moved_count"],result["load_scans"],result["revision"]), (2,1,4))

    def test_batch_stops_with_one_row_per_item(self):
        first = dict(status="moved", actual_area="inventory", slot=4, phase="MORNING",
                     revision=3, load_scans=1, full_scans=2)
        with patch("moves.move_item", side_effect=[first, {"status":"unconfirmed"}]) as submit:
            result = self.batch([25,26,27])
            self.assertEqual(submit.call_count, 2)
        self.assertEqual(result["status"], "partial")
        self.assertEqual([(r["item_id"],r["status"]) for r in result["items"]],
                         [(25,"moved"),(26,"unconfirmed"),(27,"not_attempted")])
        self.assertIsNone(result["items"][1]["actual_area"])

    def test_invalid_batch_has_no_side_effect(self):
        for invalid in ([], [25,25], [25,0]):
            with self.assertRaises(ValueError):
                self.batch(invalid)
        self.assertFalse(self.inbox.exists())

    def test_purifier_gate_and_machine_specific_replay(self):
        def take(machine):
            return move_item(self.folder, 27, "purifier", "inventory", self.generation, 2,
                             self.command, machine_id=machine)
        self.assertEqual(take(23)["status"], "reload_updated_mod")
        self.assertFalse(self.inbox.exists())
        self.head["purifier_take_protocol"] = 1
        self.publish_head()
        def game_tick(_):
            request = json.loads(self.inbox.read_text())
            self.assertEqual((request["machine_id"],request["item_id"]),(23,27))
            response = dict(request, status="moved", expected_revision=2, revision=3,
                            requested_area="inventory", actual_area="inventory",
                            source_slot_empty=True, available_strips=6)
            response["from"] = "purifier"
            (self.folder/f"receipt-{self.command}.json").write_text(json.dumps(response))
            self.inbox.unlink()
        with patch("command_transport.time.sleep", side_effect=game_tick):
            self.assertEqual(take(23)["available_strips"],6)
        self.assertTrue(take(23)["source_slot_empty"])
        with self.assertRaises(ValueError):
            take(24)

    def test_put_requires_new_native_protocol_and_matching_origin(self):
        for origin in ("inventory", "showcase"):
            result = move_item(self.folder, 26, origin, "purifier", self.generation, 2, machine_id=23)
            self.assertEqual(result["status"], "reload_updated_mod")
        self.assertFalse(self.inbox.exists())
        with self.assertRaises(ValueError):
            move_item(self.folder, 26, "front", "purifier", self.generation, 2, machine_id=23)

    def test_unconfirmed_is_not_success(self):
        with patch("command_transport.time.monotonic", side_effect=[0, 9]):
            result = self.request()
        self.assertEqual(result["status"], "unconfirmed")
        self.assertEqual(result["command_id"], self.command)


if __name__ == "__main__":
    unittest.main()
