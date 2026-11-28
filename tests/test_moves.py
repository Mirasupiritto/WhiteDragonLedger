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
        self.assertEqual(self.request(), dict(response, replayed=True))
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
        self.assertEqual(stage(visit), dict(settled, replayed=True))
        self.assertEqual((settled["actual_area"],settled["load_scans"]),("back",1))
        with self.assertRaises(ValueError):
            stage("c"*32)
        self.assertFalse(self.inbox.exists())

    def test_idle_back_clear_without_visit(self):
        self.head["counter_move_protocol"] = 1
        for phase, dest, cmd in (("OPEN", "inventory", "c"*32), ("CLOSED", "showcase", "d"*32), ("MORNING", "inventory", "e"*32)):
            self.head["phase"] = phase
            self.publish_head()
            def tick(_, destination=dest, command=cmd):
                plan = json.loads(self.inbox.read_text())
                self.assertNotIn("visit", plan)
                receipt = dict(status="moved", command_id=command, generation=self.generation, expected_revision=2,
                               item_id=16, requested_area=destination, actual_area=destination, load_scans=1)
                receipt["from"] = "back"
                (self.folder / f"receipt-{command}.json").write_text(json.dumps(receipt))
                self.inbox.unlink()
            with patch("command_transport.time.sleep", side_effect=tick):
                settled = move_item(self.folder, 16, "back", dest, self.generation, 2, cmd)
            self.assertEqual((settled["status"], settled["actual_area"]), ("moved", dest))
            self.assertEqual(move_item(self.folder, 16, "back", dest, self.generation, 2, cmd), dict(settled, replayed=True))
            with self.assertRaises(ValueError):
                move_item(self.folder, 16, "back", dest, self.generation, 2, cmd, visit="b"*32)
        for src, dst in (("back", "front"), ("inventory", "back"), ("showcase", "back")):
            with self.assertRaises(ValueError):
                move_item(self.folder, 16, src, dst, self.generation, 2)
        self.assertFalse(self.inbox.exists())

    def test_old_batch_revision_can_confirm_location_but_cannot_move_next_item(self):
        already = dict(status="already_in_destination", actual_area="inventory", revision=4, slot=4,
                       phase="MORNING", full_scans=1, load_scans=1)
        with patch("moves.move_item", side_effect=[already, {"status": "stale_observation"}]) as move:
            result = self.batch([25, 26])
        self.assertEqual([call.args[5] for call in move.call_args_list], [2, 2])
        self.assertEqual((result["already_count"], result["moved_count"]), (1, 0))
        self.assertEqual([row["status"] for row in result["items"]], ["already_in_destination", "stale_observation"])

    def test_preconditions_and_contention(self):
        self.assertEqual(self.request(1)["status"], "stale_observation")
        self.head["phase"] = "OPEN"
        self.publish_head()
        visit = "b23e2f27c9984ab3ab33ff692bf4185c"
        with patch("moves.exchange") as exchange:
            move_item(self.folder, 58, "inventory", "showcase", self.generation, 2, visit=visit)
            self.assertEqual(exchange.call_args.args[2]["visit"], visit)
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
        self.check_batch_replay("showcase")

    def test_idle_back_batch_via_registered_tool(self):
        self.head.update(phase="OPEN", counter_move_protocol=1)
        self.publish_head()
        self.check_batch_replay("back")

    def check_batch_replay(self, source):
        import asyncio
        from mcp.server.fastmcp import FastMCP
        from pawnshop_tools import register_pawnshop_tools

        server = FastMCP("replay")
        register_pawnshop_tools(server, self.folder, self.folder / "ledger.sqlite")
        args = dict(item_ids=[25, 26], source=source, destination="inventory",
                    generation=self.generation, revision=2)
        def call():
            content = asyncio.run(server.call_tool("move_items", args))
            return json.loads(content[0].text)
        posted = []
        def game_tick(_):
            request = json.loads(self.inbox.read_text(encoding="utf-8"))
            self.assertNotIn("visit", request)
            self.assertEqual((request["source"], request["destination"]), (source, "inventory"))
            posted.append((request["item_id"], request["revision"]))
            revision = request["revision"] + 1
            receipt = dict(command_id=request["command_id"], item_id=request["item_id"],
                           generation=self.generation, expected_revision=request["revision"],
                           revision=revision, slot=4, phase=self.head["phase"], full_scans=2,
                           load_scans=1, status="moved", actual_area="inventory",
                           requested_area="inventory")
            receipt["from"] = source
            self.head["revision"] = revision
            self.publish_head()
            (self.folder / ("receipt-" + request["command_id"] + ".json")).write_text(json.dumps(receipt))
            self.inbox.unlink()
        with patch("command_transport.time.sleep", side_effect=game_tick):
            result = call()
            self.assertEqual(call(), result)
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

    def test_front_batch_accepts_observed_visit(self):
        with patch("moves.exchange", return_value={"status":"busy"}) as mailbox:
            self.head.update(phase="OPEN", counter_move_protocol=1)
            self.publish_head()
            visit = "c" * 32
            result = move_items(self.folder, [26], "front", "inventory", self.generation, 2, visit=visit)
            self.assertEqual(result["items"][0]["status"], "busy")
            self.assertEqual(mailbox.call_args.args[2]["visit"], visit)

    def test_back_batch_customer_arrives_or_stack_moves_partway(self):
        def clear():
            return move_items(self.folder, [25, 26, 27], "back", "inventory", self.generation, 2)
        first = dict(status="moved", actual_area="inventory", slot=4, phase="OPEN",
                     revision=3, load_scans=1, full_scans=2)
        with patch("moves.move_item", side_effect=[first, {"status": "visit_changed"}]) as submit:
            result = clear()
            self.assertEqual(submit.call_count, 2)
        self.assertEqual((result["status"], result["moved_count"]), ("partial", 1))
        self.assertEqual([(row["status"], row["actual_area"]) for row in result["items"]],
                         [("moved", "inventory"), ("visit_changed", None), ("not_attempted", None)])
        with patch("moves.move_item", return_value=dict(status="partial", actual_area="back",
                                                        quantity=5, observed_quantity=2)) as submit:
            result = clear()
            self.assertEqual(submit.call_count, 1)
        self.assertEqual((result["status"], result["moved_count"]), ("partial", 0))
        row = result["items"][0]
        self.assertEqual((row["actual_area"], row["quantity"], row["observed_quantity"]), ("back", 5, 2))
        with self.assertRaises(ValueError):
            move_items(self.folder, [25], "back", "showcase", self.generation, 2)

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

    def test_move_items_preserves_space_failure_details_and_identity_changed(self):
        receipt_data = dict(
            status="no_space", reason="inventory_full", actual_area="showcase", revision=3,
            item_size={"w": 2, "h": 3},
            target_capacity={"w": 24, "h": 10, "free_cells": 4, "largest_free": {"w": 2, "h": 2}},
            identity_changed=[{"id": 26, "from": "紅牌香菸", "to": "紅牌香菸（贗品）"}],
        )
        with patch("moves.move_item", return_value=receipt_data):
            res = move_items(self.folder, [26, 27], "showcase", "inventory", self.generation, 2)
        self.assertEqual(res["status"], "partial")
        self.assertEqual(res["skipped_ids"], [26, 27])
        self.assertEqual(res["items"][0]["status"], "no_space")
        self.assertEqual(res["items"][0]["item_size"], {"w": 2, "h": 3})
        self.assertEqual(res["items"][0]["target_capacity"]["largest_free"], {"w": 2, "h": 2})
        self.assertEqual(res["identity_changed"][0]["to"], "紅牌香菸（贗品）")
        self.assertEqual(res["items"][1]["status"], "no_space")

    def test_smaller_item_moves_after_showcase_full(self):
        moved = dict(status="moved", revision=3, load_scans=1, full_scans=1, phase="CLOSED",slot=4)
        with patch("moves.move_item",side_effect=[moved,dict(status="showcase_full"),dict(moved,revision=4)]):
            res = move_items(self.folder,[25,26,27],"inventory","showcase",self.generation,2)
        self.assertEqual((res["status"],res["skipped_ids"],res["moved_count"]),("partial",[26],2))
        self.assertEqual([row["status"] for row in res["items"]],["moved","showcase_full","moved"])

    def test_all_already_in_destination_batch_and_front_back_verify_location(self):
        self.head.update(revision=5, move_location_protocol=1, counter_move_protocol=1, phase="OPEN")
        self.publish_head()
        already = dict(status="already_in_destination", actual_area="inventory", revision=5, slot=4,
                       phase="OPEN", full_scans=1, load_scans=1, quantity=1)
        def reply(folder, inbox, request, reader, previous_command=None):
            return dict(already, command_id=request["command_id"], expected_revision=request["revision"],
                        item_id=request["item_id"], requested_area="inventory", **{"from": "front"})
        with patch("moves.exchange", side_effect=reply):
            res = move_items(self.folder, [25, 26], "front", "inventory", self.generation, 2, visit="c" * 32)
        self.assertEqual(res["status"], "already_in_destination")
        self.assertEqual(res["already_count"], 2)
        self.assertEqual([r["status"] for r in res["items"]], ["already_in_destination", "already_in_destination"])

    def test_move_items_to_back_requires_visit_and_stages_batch(self):
        missing = move_items(self.folder, [25, 26, 27], "inventory", "back", self.generation, 2)
        self.assertEqual((missing["status"], missing["reason"]), ("invalid_route", "visit_required"))

        self.head.update(revision=2, counter_move_protocol=1, phase="OPEN")
        self.publish_head()
        visit = "c" * 32
        moved = lambda rev: dict(status="moved", actual_area="back", revision=rev, load_scans=1, full_scans=1, phase="OPEN", slot=4)
        with patch("moves.move_item", side_effect=[moved(3), moved(4), moved(5)]) as call:
            res = move_items(self.folder, [25, 26, 27], "inventory", "back", self.generation, 2, visit=visit)
        self.assertEqual((res["status"], res["moved_count"]), ("moved", 3))
        self.assertEqual([r["actual_area"] for r in res["items"]], ["back", "back", "back"])
        self.assertEqual(call.call_count, 3)

    def test_smuggler_bay_routes_and_from_container(self):
        with self.assertRaises(ValueError):
            move_item(self.folder, 83, "bay:0", "inventory", self.generation, 2)
        with self.assertRaises(ValueError):
            move_item(self.folder, 83, "bay:32", "back", self.generation, 2)

        self.head.update(revision=2, counter_move_protocol=1, phase="OPEN")
        self.publish_head()
        visit = "c" * 32
        bay_moved = lambda rev, cid: dict(
            status="moved", actual_area="back", revision=rev, load_scans=1, full_scans=1,
            phase="OPEN", slot=4, from_container={"owner_id": 32, "kind": "smuggler_bay"},
            command_id=cid, item_id=83, expected_revision=2, requested_area="back",
        )
        with patch("moves.move_item", side_effect=[bay_moved(3, "1"*32), bay_moved(4, "2"*32)]):
            res = move_items(self.folder, [83, 84], "bay:32", "back", self.generation, 2, visit=visit)
        self.assertEqual((res["status"], res["moved_count"]), ("moved", 2))
        self.assertEqual(res["items"][0]["from_container"], {"owner_id": 32, "kind": "smuggler_bay"})

    def test_trash_hidden_morning_and_stale_retry(self):
        with self.assertRaises(ValueError):
            move_item(self.folder, 83, "front", "trash", self.generation, 2)
        with self.assertRaises(ValueError):
            move_item(self.folder, 83, "trash", "showcase", self.generation, 2)
        # S4: MORNING allows inventory->hidden and bay:32<->hidden
        self.head.update(phase="MORNING", revision=2)
        self.publish_head()
        hidden_ok = dict(status="moved", actual_area="hidden", revision=3, load_scans=1, full_scans=1, phase="MORNING", slot=4)
        with patch("moves.exchange", return_value=hidden_ok):
            self.assertEqual(move_item(self.folder, 83, "bay:32", "hidden", self.generation, 2)["status"], "moved")
        # S2: trash destination
        trash_ok = dict(status="moved", actual_area="trash", revision=4, load_scans=1, full_scans=1, phase="MORNING", slot=4)
        with patch("moves.exchange", return_value=trash_ok):
            self.assertEqual(move_item(self.folder, 83, "inventory", "trash", self.generation, 2)["actual_area"], "trash")
        # S7: move_items retries stale_observation on same generation, stops on changed generation
        moved = lambda rev: dict(status="moved", actual_area="showcase", revision=rev, load_scans=1, full_scans=1, phase="CLOSED", slot=4)
        self.head.update(phase="CLOSED", revision=12)
        self.publish_head()
        with patch("moves.move_item", side_effect=[moved(11), {"status": "stale_observation"}, moved(13)]) as call:
            res = move_items(self.folder, [25, 26], "inventory", "showcase", self.generation, 10)
        self.assertEqual((res["status"], res["moved_count"], call.call_count), ("moved", 2, 3))
        self.assertEqual(res["stale_retries"], 1)
        self.assertEqual(res["items"][1]["stale_retries"], 1)
        self.assertNotIn("stale_retries", res["items"][0])
        # S7 with visit: head.json has no visit field, must still retry stale_observation
        visit = "c" * 32
        self.head.update(phase="OPEN", revision=14)
        self.publish_head()
        with patch("moves.move_item", side_effect=[{"status": "stale_observation"}, moved(15)]) as call:
            res_visit = move_items(self.folder, [25], "bay:32", "back", self.generation, 12, visit=visit)
        self.assertEqual((res_visit["status"], res_visit["moved_count"], call.call_count), ("moved", 1, 2))
        self.head["generation"] = "2" * 32
        self.publish_head()
        with patch("moves.move_item", return_value={"status": "stale_observation"}) as call:
            res2 = move_items(self.folder, [25, 26], "inventory", "showcase", self.generation, 10)
        self.assertEqual((res2["status"], call.call_count), ("stopped", 1))


if __name__ == "__main__":
    unittest.main()
