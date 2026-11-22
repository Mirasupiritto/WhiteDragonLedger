import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import counter
import shopdesk
import trade_journal as journal


class TradeJournal(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory(dir=os.environ.get("LEDGER_TEST_TEMP"))
        self.addCleanup(temp.cleanup)
        self.folder = Path(temp.name)
        self.database = self.folder / "ledger.sqlite3"
        self.generation = "b" * 32
        self.visit = "c" * 32

    def write(self, name, value):
        (self.folder / name).write_text(json.dumps(value), encoding="utf-8")

    def tickets(self, slot=4):
        return journal.ledger(self.folder, self.database, slot)["open_tickets"]

    def detail(self, ticket_id):
        return journal.ledger(self.folder, self.database, 4, ticket_id)["ticket"]

    def dismiss(self, ticket):
        return journal.dismiss(self.folder, self.database, 4, ticket["ticket_id"], ticket["version"])

    def test_sale_recovery_new_evidence_and_local_close(self):
        view = dict(status="observed", slot=4, phase="OPEN", generation=self.generation,
                    revision=12, customer=dict(name="客人", visit=self.visit))
        lines = [dict(item_id=26, min_price=10)]
        def send(folder, inbox, request, receipt):
            ticket, = self.tickets()
            self.assertEqual(ticket["ticket_id"], request["command_id"])
            self.assertEqual(self.detail(ticket["ticket_id"])["arguments"]["lines"], lines)
            self.write(inbox.name, request)
            return dict(status="unconfirmed", command_id=request["command_id"])
        with patch("shopdesk.buyer", return_value=view), patch("shopdesk.exchange", side_effect=send) as exchange:
            response = shopdesk.sell_order(self.folder, lines, self.generation, 12, self.visit, database=self.database)
            ticket, = self.tickets()
            token = response["command_id"]
            self.assertEqual(ticket["customer"], "客人")
            self.assertEqual(self.tickets(3), [])
            partial = dict(command_id=token, status="sale_pending", generation=self.generation,
                           visit=self.visit, received=10, remaining_budget=19)
            self.write(f"sale-progress-{self.generation}.json", partial)
            self.assertEqual(self.dismiss(ticket)["status"], "ticket_changed")
            latest = self.detail(token)
            self.assertEqual(latest["last_receipt"], partial)
            self.assertEqual(self.dismiss(latest)["status"], "dismissed")
            self.assertEqual(self.dismiss(latest)["status"], "already_dismissed")
            self.assertTrue((self.folder / f"sale-{self.generation}.json").exists())
            self.assertEqual(self.tickets(), [])
            self.assertEqual(self.detail(token)["arguments"]["revision"], 12)
            exchange.assert_called_once()

    def test_upgrade_import_only_matching_inbox_and_receipt(self):
        token = "a" * 32
        plan = dict(command_id=token, generation=self.generation, visit=self.visit, lines=[], revision=7)
        self.write("head.json", dict(slot=4, generation=self.generation))
        self.write(f"sale-{self.generation}.json", plan)
        self.write(f"sale-progress-{self.generation}.json", dict(command_id="d"*32, status="sale_pending"))
        ticket, = self.tickets()
        self.assertEqual(ticket["last_status"], "prepared")
        self.write(f"receipt-{token}.json", dict(command_id=token, status="finished", received=13))
        self.assertEqual(self.dismiss(ticket)["status"], "already_complete")
        self.assertEqual(self.tickets(), [])
        self.write("head.json", dict(slot=3, generation="e"*32))
        (self.folder / f"receipt-{token}.json").unlink()
        self.assertEqual(self.detail(token)["last_receipt"]["received"], 13)
        self.assertEqual(journal.dismiss(self.folder,self.database,3,token,1)["status"], "ticket_not_found")

    def test_new_trade_plans_return_callable_arguments(self):
        view = dict(status="observed", slot=4, phase="OPEN", generation=self.generation, trade_tools_protocol=1,
                    revision=3, customer=dict(name="客人", visit=self.visit))
        common = dict(generation=self.generation,revision=3,visit=self.visit)
        def pending(folder,inbox,plan,receipt):
            return dict(status="unconfirmed",command_id=plan["command_id"])
        with patch("shopdesk.buyer",return_value=view), patch("shopdesk.exchange",side_effect=pending):
            for tool in ("trade_batch","confirm_trade"):
                line = dict(item_id=31,side="buy",max_price=8)
                if tool == "confirm_trade":
                    line["exact_price"] = 8
                result = shopdesk.sell_order(self.folder,[line],**common,database=self.database,tool=tool,
                                             on_table=tool=="confirm_trade")
                args = self.detail(result["command_id"])["arguments"]
                expected = dict(orders=[dict(item_id=31,side="buy",price_limit=8)]) if tool=="trade_batch" else dict(item_id=31,side="buy",price=8)
                self.assertEqual(args,{**common, **expected})

    def test_both_purchase_paths_keep_plan_and_final_amount(self):
        view = dict(status="observed", slot=4, phase="OPEN", generation=self.generation,
                    revision=7, customer="賣家", information_protocol=1,
                    offer=dict(items=[dict(id=11)], total=9), info_receipt=dict(price=9))
        def send(folder, inbox, plan, receipt):
            ticket, = self.tickets()
            self.assertEqual(ticket["ticket_id"], plan["command_id"])
            result = dict(status="purchased" if plan["action"]=="buy" else "information_purchased",
                          command_id=plan["command_id"], generation=self.generation, paid=9, cash_after=81)
            self.write(f"receipt-{plan['command_id']}.json", result)
            return result
        with patch("counter.context", return_value=view), patch("counter.exchange", side_effect=send) as exchange:
            for action, args in [("buy", dict(offer_token="offer", max_total=12)),
                                 ("pay_info", dict(receipt_token="paper", price=9))]:
                result = counter.command(self.folder, action, self.generation, 7, visit=self.visit,
                                         database=self.database, **args)
                detail = self.detail(result["command_id"])
                self.assertEqual(detail["arguments"]["visit"], self.visit)
                self.assertNotIn("observed_offer", detail["arguments"])
                self.assertEqual(detail["observed_offer"], view["offer" if action=="buy" else "info_receipt"])
                self.assertEqual(detail["last_receipt"]["paid"], 9)
                self.assertEqual(self.tickets(), [])
                self.assertEqual(detail["state"], "complete")
                self.assertEqual(counter.command(self.folder, action, self.generation, 7, visit=self.visit,
                                                database=self.database, **args), result)
            self.assertEqual(exchange.call_count, 2)
