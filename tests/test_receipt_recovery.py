import asyncio
import json
import os
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "mcp"))
import workstation_tools
from moves import move_item
from receipt_store import read_command_receipt


class RecoveryTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory(dir=os.environ.get("LEDGER_TEST_TEMP"))
        self.addCleanup(temp.cleanup)
        self.folder = Path(temp.name)
        self.generation = "1" * 32
        self.command = "a" * 32
        self.path = self.folder / f"receipt-{self.command}.json"

    def test_corrupt_receipt_requires_matching_channel_copy(self):
        self.path.write_bytes(b'{"')
        backup = self.folder / "last-receipt-sale.json"
        known = dict(command_id=self.command, generation=self.generation, status="finished", paid=8)
        backup.write_text(json.dumps(known))
        actual = read_command_receipt(self.folder, self.command, self.generation, "sale")
        self.assertEqual(actual, {**known, "recovered_from": "channel_receipt"})
        backup.write_text(json.dumps({**known, "generation": "2"*32}))
        self.assertEqual(read_command_receipt(self.folder, self.command, self.generation, "sale")["reason"], "receipt_corrupt")

    def test_move_with_unrecoverable_receipt_never_reposts(self):
        self.path.write_text("{")
        with patch("moves.exchange") as exchange:
            result = move_item(self.folder, 26, "inventory", "showcase", self.generation, 2, self.command)
            self.assertEqual((result["status"],result["reason"]), ("outcome_unknown","receipt_corrupt"))
            exchange.assert_not_called()

    def test_machine_wait_submits_once_and_reports_final_value(self):
        view = dict(status="observed", generation=self.generation, revision=2)
        args = self.folder, 4, self.generation, 2, "current_c", "at_least", 45, 10
        with patch("counter.context", return_value=view), patch("workstation_tools.exchange") as exchange:
            self.assertEqual(workstation_tools.wait_machine(*args)["need"], "mod")
            exchange.assert_not_called()
            view["machine_wait_protocol"] = 1
            def finish(folder, inbox, request, read, *, wait_seconds):
                self.assertEqual((inbox.name, request["control"], request["threshold"],wait_seconds),
                                 (f"watch-{self.generation}.json","current_c",45,18))
                final = dict(command_id=request["command_id"], generation=self.generation,
                             status="threshold_reached", value=46, elapsed_ms=600)
                (folder / f"receipt-{request['command_id']}.json").write_text(json.dumps(final))
                return read()
            exchange.side_effect = finish
            from mcp.server.fastmcp import FastMCP
            server = FastMCP("machine-wait-test")
            workstation_tools.register(server, self.folder)
            response = asyncio.run(server.call_tool("wait_for_machine", dict(owner_id=4, generation=self.generation,
                                    revision=2, control="current_c", threshold=45)))
            self.assertEqual(json.loads(response[0].text)["value"], 46)
            exchange.assert_called_once()
