import json
import os
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from moves import move_item


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
        with patch("moves.time.sleep", side_effect=lambda _: respond()):
            self.assertEqual(self.request(), response)
        self.head["revision"] = 3
        self.publish_head()
        self.assertEqual(self.request(), response)
        self.assertFalse(self.inbox.exists())
        with self.assertRaises(ValueError):
            self.request(revision=3)

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

    def test_unconfirmed_is_not_success(self):
        with patch("moves.time.monotonic", side_effect=[0, 9]):
            result = self.request()
        self.assertEqual(result["status"], "unconfirmed")
        self.assertEqual(result["command_id"], self.command)


if __name__ == "__main__":
    unittest.main()
