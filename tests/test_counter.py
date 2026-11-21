import unittest
from pathlib import Path
from unittest.mock import patch

import counter


class CounterFlow(unittest.TestCase):
    def setUp(self):
        self.generation = "b" * 32
        self.visit = "a" * 32
        self.folder = Path(".")
        self.view = dict(status="observed", phase="OPEN", generation=self.generation,
                         revision=2, visit=self.visit)

    @patch("counter.published")
    def test_legacy_native_ready_is_not_an_offer(self, published):
        published.return_value = dict(self.view, slot=4, counter={"stage": "ready", "offer_token": "old"})
        self.assertEqual(counter.context(self.folder), {"status": "reload_updated_mod", "slot": 4})

    @patch("counter.exchange")
    @patch("counter.context")
    def test_workstation_read_and_action_gates(self, context, exchange):
        context.return_value = self.view
        self.assertEqual(counter.command(self.folder, "inspect_window", self.generation, 2)["status"], "reload_updated_mod")
        context.return_value = dict(self.view, workstation_protocol=1)
        self.assertEqual(counter.command(self.folder, "press_control", self.generation, 2)["status"], "shop_open")
        exchange.assert_not_called()
        exchange.return_value = {"status": "observed"}
        self.assertEqual(counter.command(self.folder, "inspect_window", self.generation, 2, owner_id=8), exchange.return_value)

    @patch("counter.exchange")
    @patch("counter.phone_context")
    def test_phone_actions_use_current_menu_even_in_morning(self, context, exchange):
        context.return_value = dict(self.view, phase="MORNING", phone_token="current")
        reply = counter.command(self.folder, "phone_choice", self.generation, 2, phone_token="old", choice_id=1)
        self.assertEqual(reply["status"], "phone_changed")
        exchange.assert_not_called()
        exchange.return_value = {"status": "connected"}
        reply = counter.command(self.folder, "dial", self.generation, 2, phone_token="current", number=123)
        self.assertEqual(reply["status"], "connected")
        self.assertEqual(exchange.call_args.args[2]["action"], "dial")

    @patch("counter.exchange")
    @patch("counter.context")
    def test_open_requires_observed_available_morning_shutter(self, context, exchange):
        context.return_value = dict(self.view, can_open_shop=False)
        self.assertEqual(counter.command(self.folder, "open_shop", self.generation, 2)["status"], "shutter_unavailable")
        exchange.assert_not_called()
        context.return_value = dict(self.view, phase="MORNING", can_open_shop=True)
        exchange.return_value = {"status": "opened"}
        self.assertEqual(counter.command(self.folder, "open_shop", self.generation, 2)["status"], "opened")
        self.assertEqual(exchange.call_args.args[2]["action"], "open_shop")

    @patch("counter.exchange")
    @patch("counter.context")
    def test_closed_and_stale_never_submit(self, context, exchange):
        context.return_value = dict(self.view, phase="MORNING")
        self.assertEqual(counter.command(self.folder, "ring", self.generation, 2, waiting=1)["status"], "shop_closed")
        context.return_value = self.view
        self.assertEqual(counter.command(self.folder, "ring", self.generation, 1, waiting=1)["status"], "stale_observation")
        exchange.assert_not_called()

    @patch("counter.time.sleep")
    @patch("counter.command")
    @patch("counter.context")
    def test_wait_ordinary_page_then_choice(self, context, command, sleep):
        context.side_effect = [dict(self.view, stage="speaking"), dict(self.view, stage="advance_required", page="p"),
                               dict(self.view, stage="choice_required")]
        command.return_value = {"status": "advanced"}
        result = counter.wait_opening(self.folder, self.generation, self.visit)
        self.assertEqual(result["stage"], "choice_required")
        command.assert_called_once_with(self.folder, "advance", self.generation, 2, visit=self.visit, page="p")

    @patch("counter.time.sleep")
    @patch("counter.command")
    @patch("counter.context")
    def test_ring_once_then_wait_for_observed_offer(self, context, command, sleep):
        command.side_effect = [dict(status="called", visit=self.visit), dict(status="tablet_requested")]
        offer = dict(total=9, items=[dict(id=12, quantity=1)])
        context.side_effect = [dict(self.view, stage="tablet_closed"), dict(self.view, stage="ready", offer=offer)]
        result = counter.ring(self.folder, self.generation, 2, 3)
        self.assertEqual(result["offer"], offer)
        self.assertEqual([call.args[1] for call in command.call_args_list], ["ring", "tablet"])

    @patch("counter.time.sleep")
    @patch("counter.command")
    @patch("counter.context")
    def test_wait_for_merchant_to_set_out_goods(self, context, command, sleep):
        context.side_effect = [dict(self.view, stage="arriving"), dict(self.view, stage="awaiting_opening", lines=[]),
                               dict(self.view, stage="speaking", lines=["我正在囤水。"]),
                               dict(self.view, stage="speaking"),
                               dict(self.view, stage="ready", lines=["我正在囤水。", "我有149信用点。"],
                                    offer={"items": [{"id": 29}, {"id": 30}], "total": 9})]
        result = counter.wait_opening(self.folder, self.generation, self.visit)
        self.assertEqual(result["offer"]["total"], 9)
        self.assertEqual(len(result["lines"]), 2)
        command.assert_not_called()

    @patch("counter.time.sleep")
    @patch("counter.command")
    @patch("counter.context")
    def test_information_dealer_waits_for_visible_receipt_without_payment(self, context, command, sleep):
        quote = dict(price=12, receipt_token="q", can_pay=True)
        context.side_effect = [dict(self.view, stage="speaking"),
                               dict(self.view, stage="awaiting_information_receipt"),
                               dict(self.view, stage="information_quote", info_receipt=quote)]
        self.assertEqual(counter.wait_opening(self.folder, self.generation, self.visit)["info_receipt"], quote)
        command.assert_not_called()

    @patch("counter.exchange")
    @patch("counter.context")
    def test_information_payment_requires_new_mod_and_replays_receipt(self, context, exchange):
        import json
        import tempfile
        context.return_value = self.view
        args = dict(visit=self.visit, receipt_token="q", price=12)
        with tempfile.TemporaryDirectory() as folder_name:
            folder = Path(folder_name)
            def pay():
                return counter.command(folder, "pay_info", self.generation, 2, **args)
            self.assertEqual(pay()["status"], "reload_updated_mod")
            exchange.assert_not_called()
            context.return_value = dict(self.view, information_protocol=1)
            def submit(folder, inbox, plan, receipt):
                token = plan["command_id"]
                result = dict(command_id=token, generation=self.generation, status="information_purchased", paid=12, cash_after=88)
                (folder/f"receipt-{token}.json").write_text(json.dumps(result), encoding="utf-8")
                return receipt()
            exchange.side_effect = submit
            first = pay()
            self.assertEqual(first["cash_after"], 88)
            self.assertEqual(pay(), first)
            exchange.assert_called_once()

    @patch("counter.command")
    @patch("counter.context")
    def test_wait_does_not_follow_different_visit(self, context, command):
        context.return_value = dict(self.view, visit="another", stage="ready")
        self.assertEqual(counter.wait_opening(self.folder, self.generation, self.visit)["status"], "visit_changed")
        command.assert_not_called()
