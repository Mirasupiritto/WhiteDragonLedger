import sys
import unittest
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "mcp"))
import counter


class ReportReplay(unittest.TestCase):
    def test_same_submission_reads_original_report_receipt(self):
        # Slot 13 generation, revision and visit from the day-three nurse receipt.
        generation = "cc0ce363910e49eeaa4640d8b0f6f1de"
        visit = "b23e2f27c9984ab3ab33ff692bf4185c"
        view = dict(status="observed", generation=generation, revision=107, phase="OPEN", report_protocol=1)
        with patch.object(counter, "context", return_value=view), patch.object(counter, "exchange") as send, \
                patch.object(counter, "read_command_receipt") as lookup:
            lookup.return_value = None
            args = dict(mode="report", visit=visit, report_token=f"{generation}/107/{visit}/True")
            counter.command(Path("."), "report_client", generation, 107, **args)
            folder, inbox, request, _ = send.call_args.args
            self.assertEqual(inbox.name, f"report-{generation}.json")
            lookup.return_value = dict(status="reported", command_id=request["command_id"], generation=generation)
            replay = counter.command(folder, "report_client", generation, 107, **args)
            self.assertTrue(replay["replayed"])
            self.assertEqual(replay["command_id"], request["command_id"])
            send.assert_called_once()
