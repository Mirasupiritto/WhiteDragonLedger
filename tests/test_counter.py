import sys
import unittest
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "mcp"))
import counter


class CounterFlow(unittest.TestCase):
    @patch("counter.time.sleep")
    @patch("counter.command")
    @patch("counter.context")
    def test_barter_opening_returns_both_sides_of_all_options(self, context, command, sleep):
        context.side_effect = [dict(self.view, stage="speaking"), dict(self.view, stage="barter")]
        offers = [{"give": [{"category": "FOOD", "quantity": 1}], "receive": [{"kind": k, "quantity": n}]}
                  for k, n in [("blood_bag", 2), ("scrap_metal", 3), ("scratcher", 2)]]
        command.return_value = {"status": "observed", "revision": 4, "barter": {"options": offers}}
        result = counter.wait_opening(self.folder, self.generation, self.visit)
        self.assertEqual((result["revision"], result["barter"]["options"]), (4, offers))
        self.assertEqual(command.call_count, 1)
        self.assertEqual(command.call_args.kwargs["mode"], "read")

    @patch("counter.exchange")
    @patch("counter.context")
    def test_ring_refusal_can_recover_at_same_revision_without_double_call(self, context, exchange):
        saved = {}
        def reply(folder, inbox, request, receipt):
            status = "customer_departing" if not saved else "called"
            saved[request["command_id"]] = {"status": status}
            return saved[request["command_id"]]
        exchange.side_effect = reply
        context.return_value = dict(self.view, visit=None, stage="idle", waiting=2, reception={"can_ring": True})
        with patch("counter.read_command_receipt", side_effect=lambda folder, token, gen, channel: saved.get(token)):
            def ring():
                return counter.command(self.folder, "ring", self.generation, 2, waiting=2)["status"]
            self.assertEqual(ring(), "customer_departing")
            self.assertEqual(ring(), "called")
            context.return_value = dict(self.view, stage="arriving")
            self.assertEqual(ring(), "called")
        self.assertEqual(exchange.call_count, 2)

    @patch("counter.exchange")
    @patch("counter.context")
    def test_sort_inventory_at_closed_shop(self, context, exchange):
        context.return_value = dict(self.view, phase="CLOSED", visit=None)
        self.assertEqual(counter.command(self.folder, "sort_inventory", self.generation, 2)["status"], "reload_updated_mod")
        exchange.assert_not_called()
        context.return_value["inventory_sort_protocol"] = 1
        exchange.return_value = {"status": "sorted", "after": {"largest_free": {"w": 4, "h": 1}}}
        self.assertEqual(counter.command(self.folder, "sort_inventory", self.generation, 2), exchange.return_value)
        exchange.assert_called_once()

    @patch("counter.read_command_receipt")
    @patch("counter.exchange")
    @patch("counter.context")
    def test_sort_after_player_changes_setting_at_same_revision(self, context, exchange, read):
        saved = {}
        statuses = iter(("external_sorter_active", "sorted"))
        def reply(folder, inbox, request, receipt):
            saved[request["command_id"]] = {"status": next(statuses)}
            return saved[request["command_id"]]
        read.side_effect = lambda folder, token, generation, channel: saved.get(token)
        exchange.side_effect = reply
        context.return_value = dict(self.view, inventory_sort_protocol=1)
        def sort():
            return counter.command(self.folder, "sort_inventory", self.generation, 2)["status"]
        self.assertEqual([sort(), sort(), sort()], ["external_sorter_active", "sorted", "sorted"])
        self.assertEqual(exchange.call_count, 2)

    @patch("counter.exchange")
    @patch("counter.context")
    def test_item_menu_before_opening_and_protocol_gate(self, context, exchange):
        context.return_value = dict(self.view, phase="MORNING")
        args = dict(item_id=5, mode="read")
        self.assertEqual(counter.command(self.folder, "item_menu", self.generation, 2, **args)["status"], "reload_updated_mod")
        exchange.assert_not_called()
        context.return_value["item_menu_protocol"] = 1
        exchange.return_value = {"status": "observed", "enabled": ["activate"]}
        self.assertEqual(counter.command(self.folder, "item_menu", self.generation, 2, **args), exchange.return_value)

    @patch("counter.exchange")
    @patch("counter.context")
    def test_new_item_tools_require_new_game_protocol(self, context, exchange):
        context.return_value = dict(self.view, phase="MORNING", item_menu_protocol=1)
        for action, args in [("item_menu", {"mode": "targets"}), ("item_panel", {"mode": "read"}),
                             ("adjust_control", {"values": [1]}), ("press_control", {"tab_index": 1})]:
            with self.subTest(action=action):
                self.assertEqual(counter.command(self.folder, action, self.generation, 2, **args)["status"], "reload_updated_mod")
        exchange.assert_not_called()
        context.return_value["item_panel_protocol"] = 1
        exchange.return_value = {"status": "unchanged"}
        self.assertEqual(counter.command(self.folder, "item_panel", self.generation, 2, mode="read"), exchange.return_value)

    def test_finished_heat_shown_once_in_counter_receipts(self):
        import tempfile
        with tempfile.TemporaryDirectory() as tmp:
            database = Path(tmp) / "ledger.sqlite3"
            self.check_heat_receipts(database)

    def check_heat_receipts(self, database):
        running = {"generation": self.generation, "heat": {"run_id": "r1", "status": "running"}}
        self.assertIn("heat", counter.filter_finished_heat(database, running))
        self.assertIn("heat", counter.filter_finished_heat(database, running))
        finished = {"generation": self.generation, "heat": {"run_id": "r1", "status": "finished"}}
        first = counter.filter_finished_heat(database, finished)
        second = counter.filter_finished_heat(database, finished)
        self.assertEqual(first["heat"]["status"], "finished")
        self.assertNotIn("heat", second)

    def setUp(self):
        self.generation = "b" * 32
        self.visit = "a" * 32
        self.folder = Path(".")
        self.view = dict(status="observed", phase="OPEN", generation=self.generation,
                         revision=2, visit=self.visit)

    @patch("counter.read_command_receipt")
    @patch("counter.exchange")
    @patch("counter.context")
    def test_report_after_interception_needs_the_new_token(self, context, exchange, read):
        # Day 14 playtest: revision stayed at 53 from the interception until the guest left.
        context.return_value = dict(self.view, revision=53, report_protocol=1)
        first = f"{self.generation}/53/{self.visit}/True/0"
        intercepted = counter.deterministic_id("white-dragon/counter/", dict(
            action="report_client", generation=self.generation, revision=53,
            mode="report", visit=self.visit, report_token=first))
        read.side_effect = lambda folder, token, *_: (
            {"status": "report_intercepted", "command_id": token} if token == intercepted else None)
        again = counter.command(self.folder, "report_client", self.generation, 53,
                                mode="report", visit=self.visit, report_token=first)
        self.assertEqual((again["status"], again.get("replayed")), ("report_intercepted", True))
        exchange.assert_not_called()
        exchange.return_value = {"status": "reported"}
        fresh = counter.command(self.folder, "report_client", self.generation, 53,
                                mode="report", visit=self.visit, report_token=f"{self.generation}/53/{self.visit}/True/1")
        self.assertEqual(fresh["status"], "reported")
        exchange.assert_called_once()

    @patch("counter.exchange")
    @patch("counter.context")
    def test_network_load_gate_and_closed_shop_phone_visit(self, context, exchange):
        context.return_value = dict(self.view, phase="CLOSED")
        args = dict(mode="buy",target="referral",panel_token="panel",cost=12)
        self.assertEqual(counter.command(self.folder,"wild_network",self.generation,2,**args)["status"],"reload_updated_mod")
        exchange.assert_not_called()
        context.return_value["wild_network_protocol"]=1
        exchange.return_value={"status":"purchased","cash_paid":7,"favor_spent":5}
        result=counter.command(self.folder,"wild_network",self.generation,2,**args)
        self.assertEqual(result["cash_paid"],7)
        self.assertEqual(exchange.call_args.args[2]["target"],"referral")

    @patch("counter.exchange")
    @patch("counter.context")
    def test_dismiss_rechecks_original_visit_and_recovers_receipt(self, context, exchange):
        import json
        import os
        import tempfile
        view = dict(self.view, visit=self.visit, revision=16, dismiss_recheck_protocol=1,
                    reception=dict(can_dismiss=True))
        context.return_value = view
        with tempfile.TemporaryDirectory(dir=os.environ.get("LEDGER_TEST_TEMP")) as tmp:
            folder = Path(tmp)
            def send(folder, inbox, request, receipt):
                self.assertEqual((request["revision"],request["visit"]),(14,self.visit))
                result=dict(status="dismissed", generation=self.generation, command_id=request["command_id"],
                            visit=self.visit, customer="维护工人", waiting=6)
                (folder/f"receipt-{request['command_id']}.json").write_text(json.dumps(result),encoding="utf8")
                return result
            exchange.side_effect=send
            def dismiss():
                return counter.command(folder,"dismiss",self.generation,14,visit=self.visit)
            view["visit"]="c"*32
            self.assertEqual(dismiss()["status"],"customer_replaced")
            view["visit"]=None
            self.assertEqual(dismiss()["status"],"customer_left")
            view["visit"]=self.visit
            view["reception"]["can_dismiss"]=False
            self.assertEqual(dismiss()["status"],"dismiss_unavailable")
            exchange.assert_not_called()
            view["reception"]["can_dismiss"]=True
            result=dismiss()
            self.assertEqual(result["waiting"],6)
            view["visit"]="c"*32
            self.assertEqual(dismiss(), dict(result, replayed=True))
            exchange.assert_called_once()

    @patch("counter.exchange")
    @patch("counter.context")
    def test_retry_persisted_native_refusal_with_original_revision(self, context, exchange):
        import json
        import os
        import tempfile
        for terminal in ("dismissed", "dismiss_intercepted", "outcome_unknown"):
            with self.subTest(terminal=terminal), tempfile.TemporaryDirectory(dir=os.environ.get("LEDGER_TEST_TEMP")) as tmp:
                view = dict(self.view, revision=22, dismiss_recheck_protocol=1, reception=dict(can_dismiss=True))
                context.return_value = view
                attempts = []
                outcomes = iter(["dismiss_unavailable", "dismiss_unavailable", terminal])
                def dispatch(folder, inbox, plan, read):
                    self.assertEqual((plan["revision"],plan["visit"]),(20,self.visit))
                    token = plan["command_id"]
                    attempts.append(token)
                    record = dict(status=next(outcomes), generation=self.generation, command_id=token)
                    (folder/f"receipt-{token}.json").write_text(json.dumps(record),encoding="utf-8")
                    return read()
                exchange.side_effect = dispatch
                def dismiss():
                    return counter.command(Path(tmp),"dismiss",self.generation,20,visit=self.visit)
                self.assertEqual(dismiss()["status"],"dismiss_unavailable")
                view["reception"]["can_dismiss"] = False
                self.assertEqual(dismiss()["status"],"dismiss_unavailable")
                view["visit"] = "c"*32
                view["reception"]["can_dismiss"] = True
                self.assertEqual(dismiss()["status"],"customer_replaced")
                self.assertEqual(len(attempts),1)
                view["visit"] = self.visit
                self.assertEqual(dismiss()["status"],"dismiss_unavailable")
                receipt = dismiss()
                self.assertEqual(receipt["status"],terminal)
                view["visit"] = "d"*32
                self.assertEqual(dismiss(), dict(receipt, replayed=True))
                self.assertEqual(len(set(attempts)),3)
                self.assertEqual(len(attempts),3)

    @patch("counter.published")
    def test_legacy_native_ready_is_not_an_offer(self, published):
        published.return_value = dict(self.view, slot=4, counter={"stage": "ready", "offer_token": "old"})
        self.assertEqual(counter.context(self.folder), {"status": "reload_updated_mod", "slot": 4})

    @patch("counter.exchange")
    @patch("counter.context")
    def test_water_use_replays_original_receipt_without_another_dispatch(self, context, exchange):
        import json
        import tempfile
        context.return_value = dict(self.view, phase="MORNING")
        args = dict(source_id=11, target_id=12, mode="apply", use_token="preview")
        with tempfile.TemporaryDirectory() as name:
            folder = Path(name)
            def apply():
                return counter.command(folder, "water_use", self.generation, 2, **args)
            self.assertEqual(apply()["status"], "reload_updated_mod")
            exchange.assert_not_called()
            context.return_value["water_use_protocol"] = 1
            def dispatch(folder, inbox, plan, read):
                token = plan["command_id"]
                result = dict(status="sample_taken", command_id=token, generation=self.generation,
                              source_id=11, target_id=12, available_strips=5)
                (folder / f"receipt-{token}.json").write_text(json.dumps(result), encoding="utf-8")
                return read()
            exchange.side_effect = dispatch
            first = apply()
            self.assertEqual(first["available_strips"], 5)
            self.assertEqual(apply(), dict(first, replayed=True))
            exchange.assert_called_once()

    @patch("counter.exchange")
    @patch("counter.context")
    def test_water_panel_in_morning_or_during_trade(self, context, exchange):
        for phase in ("MORNING", "OPEN", "CLOSED"):
            context.return_value = dict(self.view, phase=phase, water_panel_protocol=1)
            exchange.return_value = {"status": "panel_closed"}
            result = counter.command(self.folder, "water_panel", self.generation, 2, mode="read")
            self.assertEqual(result["status"], "panel_closed")
        self.assertEqual(exchange.call_count, 3)
        context.return_value = self.view
        result = counter.command(self.folder, "water_panel", self.generation, 2, mode="read")
        self.assertEqual(result["status"], "reload_updated_mod")
        self.assertEqual(exchange.call_count, 3)

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
        command.side_effect = [dict(status="called", visit=self.visit, reception_token="rt1"), dict(status="tablet_requested")]
        offer = dict(total=9, items=[dict(id=12, quantity=1)])
        context.side_effect = [dict(self.view, stage="tablet_closed"), dict(self.view, stage="ready", offer=offer)]
        result = counter.ring(self.folder, self.generation, 2, 3)
        self.assertEqual(result["status"], "called")
        self.assertEqual(result["opening_status"], "observed")
        self.assertEqual(result["reception_token"], "rt1")
        self.assertEqual(result["offer"], offer)
        self.assertEqual([call.args[1] for call in command.call_args_list], ["ring", "tablet"])

    @patch("counter.command")
    def test_ring_blocked_returns_uniform_bell_receipt_shape(self, command):
        blocked = dict(status="occupied", visit=self.visit, waiting=2, reception_token="rt2")
        command.return_value = blocked
        result = counter.ring(self.folder, self.generation, 2, 2)
        self.assertEqual(result["status"], "occupied")
        self.assertEqual(result["bell_receipt"], blocked)
        self.assertEqual(result["reception_token"], "rt2")

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
            self.assertEqual(pay(), dict(first, replayed=True))
            exchange.assert_called_once()

    @patch("counter.command")
    @patch("counter.context")
    def test_wait_does_not_follow_different_visit(self, context, command):
        context.return_value = dict(self.view, visit="another", stage="ready")
        self.assertEqual(counter.wait_opening(self.folder, self.generation, self.visit)["status"], "customer_replaced")
        context.return_value = dict(self.view, visit=None, stage="idle")
        self.assertEqual(counter.wait_opening(self.folder, self.generation, self.visit)["status"], "customer_left")
        command.assert_not_called()

    @patch("counter.time.sleep")
    @patch("counter.context")
    def test_choice_required_waits_for_dialog_choices_before_returning(self, context, sleep):
        empty = dict(self.view, stage="choice_required", dialog_choices=[])
        populated = dict(self.view, stage="choice_required", dialog_choices=[dict(id=0, text="Yes", enabled=True)])
        context.side_effect = [empty, empty, populated]
        result = counter.wait_opening(self.folder, self.generation, self.visit)
        self.assertEqual(result["stage"], "choice_required")
        self.assertEqual(result["dialog_choices"], [dict(id=0, text="Yes", enabled=True)])

    @patch("counter.exchange")
    @patch("counter.context")
    def test_dismiss_unavailable_always_attaches_reason_and_blocking_items(self, context, exchange):
        view = dict(self.view, reception=dict(can_dismiss=False))
        context.return_value = view
        plain = counter.command(self.folder, "dismiss", self.generation, 2, visit=self.visit)
        self.assertEqual((plain["status"], plain["reason"], plain["blocking_items"]),
                         ("dismiss_unavailable", "dismiss_unavailable", []))

        blocker = [dict(id=5, kind="mug", area="back")]
        view["reception"] = dict(can_dismiss=False, reason="order_in_progress", blocking_items=blocker, token="rt_block")
        detailed = counter.command(self.folder, "dismiss", self.generation, 2, visit=self.visit)
        self.assertEqual((detailed["status"], detailed["reason"], detailed["blocking_items"], detailed["reception_token"]),
                         ("dismiss_unavailable", "order_in_progress", blocker, "rt_block"))
        exchange.assert_not_called()

    def test_new_water_and_customer_workflow_tools(self):
        import asyncio
        import json
        import os
        import tempfile
        from mcp.server.fastmcp import FastMCP
        from pawnshop_tools import register_pawnshop_tools

        with tempfile.TemporaryDirectory(dir=os.environ.get("LEDGER_TEST_TEMP")) as tmp:
            folder = Path(tmp)
            server = FastMCP("replay")
            register_pawnshop_tools(server, folder, folder / "ledger.sqlite")

            def call(name, **args):
                res = asyncio.run(server.call_tool(name, args))
                return json.loads(res[0].text)

            with patch("counter.command", return_value={"status": "transferred", "water_ml": 500}) as cmd:
                out = call("water_use", source_id=10, target_id="faucet", generation=self.generation,
                           revision=2, mode="apply_now")
            self.assertEqual(out["status"], "transferred")
            self.assertEqual(cmd.call_args.kwargs["mode"], "apply_now")

            seq = [
                {"status": "transferred", "revision": 3, "water_ml": 500, "bottle_label": "dirty"},
                {"status": "purifier_used", "revision": 4, "water_ml": 500, "bottle_label": "clean",
                 "charge_used": 5, "charge_remaining": 15, "liters_remaining": 1.5},
                {"status": "transferred", "revision": 5, "water_ml": 500, "bottle_label": "dirty"},
                {"status": "purifier_used", "revision": 6, "water_ml": 500, "bottle_label": "clean",
                 "charge_used": 5, "charge_remaining": 10, "liters_remaining": 1.0},
            ]
            with patch("counter.command", side_effect=seq), patch("counter.context", return_value={"status": "waiting_for_load"}):
                batch = call("fill_and_purify_bottles", bottle_ids=[10, 11], purifier_id=20,
                             generation=self.generation, revision=2)
            self.assertEqual((batch["status"], batch["completed_count"], batch["failed_count"],
                              batch["charge_remaining"], batch["liters_remaining"], batch["revision"]),
                             ("completed", 2, 0, 10, 1.0, 6))
            self.assertEqual(batch["items"][1]["status"], "filled_and_purified")

            with (patch("counter.context", return_value=dict(self.view, stage="idle", visit=None, waiting=2)),
                  patch("counter.ring", return_value={"status": "called", "visit": self.visit, "stage": "ready", "reception_token": "rt"})):
                nxt = call("next_customer")
            self.assertEqual((nxt["status"], nxt["visit"], nxt["reception_token"]), ("called", self.visit, "rt"))

            with (patch("counter.context", return_value=dict(self.view, stage="speaking", visit=self.visit)),
                  patch("counter.wait_opening", return_value=dict(self.view, stage="ready", reception={"token": "rt2"}))):
                occupied = call("next_customer")
            self.assertEqual((occupied["status"], occupied["already_at_counter"], occupied["reception_token"]),
                             ("already_present", True, "rt2"))

            with patch("counter.command", return_value={"status": "dismissed", "waiting": 1, "reception_token": "rt3"}):
                fin = call("finish_customer", generation=self.generation, revision=6, visit=self.visit)
            self.assertEqual((fin["status"], fin["reception_token"], fin["next_tool"]), ("dismissed", "rt3", "next_customer"))

            transient = {"status": "dismiss_unavailable", "reason": "client_not_dismissable"}
            ready = dict(self.view, status="observed", generation=self.generation, visit=self.visit, reception={"can_dismiss": True})
            with (patch("counter.command", side_effect=[transient, {"status": "dismissed"}]) as dismiss,
                  patch("counter.context", return_value=ready)):
                self.assertEqual(call("finish_customer", generation=self.generation, revision=6, visit=self.visit)["status"], "dismissed")
                self.assertEqual(dismiss.call_args_list[0], dismiss.call_args_list[1])
            with (patch("counter.command", return_value=transient) as dismiss,
                  patch("counter.context", return_value=dict(ready, visit="someone_else"))):
                self.assertEqual(call("finish_customer", generation=self.generation, revision=6, visit=self.visit)["status"], "customer_replaced")
                dismiss.assert_called_once()

            leaving = dict(self.view, stage="customer_departing", visit=None, next_action="上一位客人正在離場，等候即可。")
            with patch("counter.context", return_value=leaving), patch("counter.ring") as ring:
                self.assertEqual(call("next_customer",timeout=0)["status"], "customer_departing")
                ring.assert_not_called()
            with (patch("counter.context", side_effect=[leaving, dict(leaving, stage="idle", waiting=1)]),
                  patch("counter.ring", return_value={"status":"called","visit":self.visit}) as ring):
                self.assertEqual(call("next_customer")["status"], "called")
                ring.assert_called_once()
            cooldown = dict(self.view, stage="idle", visit=None, waiting=6,
                            reception={"waiting": 6, "can_ring": False, "can_close_shop": False, "can_dismiss": False})
            ringable = dict(cooldown, reception={"waiting": 6, "can_ring": True, "can_close_shop": True, "can_dismiss": False})
            with (patch("counter.context", side_effect=[leaving, cooldown, ringable]),
                  patch("counter.ring", return_value={"status": "called", "visit": self.visit}) as ring):
                self.assertEqual(call("next_customer")["status"], "called")
                ring.assert_called_once()
            empty_cooldown = dict(cooldown, waiting=0, reception={"waiting": 0, "can_ring": False})
            with (patch("counter.context", return_value=empty_cooldown),
                  patch("counter.ring", return_value={"status": "bell_unavailable"}) as ring):
                self.assertEqual(call("next_customer")["status"], "bell_unavailable")
                ring.assert_called_once()
            heated = dict(leaving, heat={"run_id":"344261ba","status":"stopped"})
            with patch("counter.context",return_value=heated), patch("counter.ring",return_value=heated):
                self.assertIn("heat", call("next_customer", timeout=0))
                self.assertNotIn("heat", call("ring_customer", generation=self.generation, revision=6, waiting=1))
                server = FastMCP("reconnected")
                register_pawnshop_tools(server, folder, folder / "ledger.sqlite")
                with patch("shopdesk.published",return_value={"status":"waiting_for_load"}):
                    self.assertNotIn("heat", call("get_counter"))
            incoming = dict(self.view, visit="new", customer="新客", stage="arriving")
            with patch("counter.context", return_value=incoming), patch("counter.command", return_value={"status": "dismissed", "visit": self.visit}):
                handoff = call("finish_customer", generation=self.generation, revision=6, visit=self.visit)
                self.assertEqual((handoff["visit"], handoff["next_customer"]["visit"], handoff["next_stage"]), (self.visit, "new", "arriving"))
            with (patch("counter.context", side_effect=[dict(self.view, stage="idle", visit=None, waiting=1), incoming]),
                  patch("counter.ring", return_value={"status": "occupied"}) as ring,
                  patch("counter.wait_opening", return_value=dict(incoming, stage="ready"))):
                self.assertEqual(call("next_customer")["visit"], "new")
                ring.assert_called_once()

            closed_buyer = dict(self.view, stage="not_selling", client_role="buyer", demand={"status": "panel_closed", "interests": []})
            open_buyer = dict(self.view, stage="not_selling", client_role="buyer", demand={"status": "observed", "interests": ["MATERIAL"]})
            with (patch("counter.context", side_effect=[closed_buyer, closed_buyer, closed_buyer, open_buyer]),
                  patch("counter.command", return_value={"status": "tablet_requested"}) as cmd,
                  patch("counter.time.monotonic", side_effect=[0.0, 0.0, 0.2, 2.1, 2.3]),
                  patch("counter.time.sleep")):
                opened = counter.wait_opening(self.folder, self.generation, self.visit, timeout=5)
            self.assertEqual(opened["demand"]["status"], "observed")
            self.assertEqual(opened["tablet_attempts"], 2)
            self.assertEqual(cmd.call_count, 2)
            self.assertEqual(cmd.call_args.args[1], "tablet")
            self.assertIn("read_id", cmd.call_args.kwargs)

            with (patch("counter.context", return_value=dict(self.view, stage="speaking")),
                  patch("counter.time.sleep")):
                timed_out = counter.wait_opening(self.folder, self.generation, self.visit, timeout=0)
            self.assertEqual(timed_out["status"], "opening_timeout")

            services = [{"id": f"svc_{i}", "title": f"Svc {i}", "tab": "water" if i < 5 else "security",
                         "cost": 10, "unlocked": False, "ready": i % 2 == 0} for i in range(10)]
            net_panel = {"status": "observed", "panel": {"services": services, "tabs": [{"id": "0", "label": "All"}],
                         "cash": 50, "favor": 5, "panel_token": "pt1"}}
            with (patch("counter.context", return_value=dict(self.view, wild_network_protocol=1, stage="wild_network", slot=4)),
                  patch("counter.command", return_value=net_panel)):
                compact_net = call("wild_network", mode="read")
                full_net = call("wild_network", mode="read", verbose=True)
            self.assertNotIn("services", compact_net["panel"])
            self.assertEqual(compact_net["panel"]["service_summary"]["total_count"], 10)
            self.assertEqual(compact_net["panel"]["service_summary"]["ready_count"], 5)
            self.assertEqual(len(full_net["panel"]["services"]), 10)
