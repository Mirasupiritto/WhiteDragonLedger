import asyncio
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "mcp"))
import trade_tools
from trade_journal import ledger


class TradeTools(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory(dir=os.environ.get("LEDGER_TEST_TEMP"))
        self.addCleanup(temp.cleanup)
        self.folder = Path(temp.name)
        self.db = self.folder / "account.sqlite3"
        self.generation = "a"*32
        self.visit = "b"*32
        self.counter = dict(stage="ready", visit=self.visit, customer="星際客", cash=218,
                            lines=["我要買水", "但預算不多"], waiting=3, reception=dict(can_ring=False))
        self.bottle = dict(id=27,name="瓶裝水",kind="bottle",area="purifier",machine_id=23,
                           bottle_label="鐵鏽水",owned=True,quantity=1)
        self.publish(1, "MORNING", dict(op="baseline", items=[], purifier_stock=[self.bottle]))

    def write(self, name, data):
        (self.folder/name).write_text(json.dumps(data), encoding="utf-8")

    def publish(self, revision, phase, event, day=10):
        head = dict(status="observed", generation=self.generation, revision=revision, slot=4,
                    phase=phase, day=day, load_scans=1, desk_protocol=1)
        self.view = dict(head, counter_protocol=2, counter=self.counter,
                         customer=dict(remaining=114), available_strips=6)
        self.write("head.json",head)
        self.write(f"{self.generation}-{revision}.json", dict(event,phase=phase,day=day))
        self.write(f"desk-{self.generation}.json", self.view)
        self.write("scene.json",dict(status="ready",location="pawnshop",slot=4,session="test",revision=1))

    def test_two_variety_buyers_group_four_scraps_and_remember_sales(self):
        from shopdesk import allocation_stock
        scraps = [dict(id=i, name="“远古外星遗物”", kind="scrap_metal", area="showcase" if i==6 else "inventory",
                       owned=True, quantity=1) for i in [6,15,17,18]]
        other = dict(scraps[0], id=20, kind="souvenir", name=scraps[0]["name"])
        demand = dict(source="trade_panel", status="visible", restrictions=["种类多样"],
                      max_per_kind=1, sold_kinds=[], allocation_hint="種類多樣：每種只出一件")
        self.counter.update(customer="维护工人",demand=demand)
        self.publish(1,"OPEN",dict(op="baseline",items=[*scraps,other,dict(other,id=21,owned=False)]))
        def pick():
            return allocation_stock(self.folder,self.db,{})
        first = pick()
        self.assertEqual(first["load_scans"],1)
        self.assertEqual(first["visit"],self.visit)
        self.assertEqual(first["allocation_hint"],"種類多樣：每種只出一件")
        self.assertEqual(first["kind_groups"],[
            dict(kind="scrap_metal",item_ids=[6,15,17,18],count=4,recommended_count=1),
            dict(kind="souvenir",item_ids=[20],count=1,recommended_count=1)])
        self.assertEqual(first["items"][0]["kind"],"scrap_metal")
        demand["sold_kinds"]=["scrap_metal"]
        self.publish(2,"OPEN",dict(op="delta",remove=[6],upsert=[]))
        self.assertEqual(pick()["kind_groups"][0]["recommended_count"],0)
        self.counter["visit_variety"] = dict(demand, status="last_visible_for_visit", visit=self.visit)
        del self.counter["demand"]
        self.publish(3,"OPEN",dict(op="delta",remove=[],upsert=[]))
        after_sale = pick()
        self.assertEqual(after_sale["allocation_status"],"last_visible_for_visit")
        self.assertEqual(after_sale["load_scans"],1)
        self.assertEqual(after_sale["sold_kinds"],["scrap_metal"])
        self.assertEqual(after_sale["kind_groups"],[
            dict(kind="scrap_metal",item_ids=[15,17,18],count=3,recommended_count=0),
            dict(kind="souvenir",item_ids=[20],count=1,recommended_count=1)])
        self.counter.update(customer="空间站游客",visit="d"*32,demand=demand)
        demand["sold_kinds"]=[]
        self.publish(4,"OPEN",dict(op="delta",remove=[],upsert=[]))
        self.assertEqual(pick()["kind_groups"][0]["recommended_count"],1)
        self.counter["demand"]={"source":"trade_panel","status":"panel_updating"}
        self.publish(5,"OPEN",dict(op="delta",remove=[],upsert=[]))
        self.assertNotIn("kind_groups",pick())

    def test_checkpoint_replay_stock_transfer_and_offline(self):
        updates = trade_tools.Updates(self.folder,self.db)
        base = updates.report()
        self.publish(2,"OPEN",dict(op="delta",remove=[],upsert=[dict(self.bottle,area="inventory")],purifier_stock=[]))
        self.publish(3,"CLOSED",dict(op="delta",remove=[],upsert=[]))
        change = updates.report()
        self.assertEqual(change["load_scans"],1)
        self.assertEqual([row["phase"] for row in change["checkpoints"]], ["OPEN","CLOSED"])
        self.assertEqual([(row["id"],row["area"]) for row in change["upsert"]],[(27,"inventory")])
        self.assertEqual(trade_tools.Updates(self.folder,self.db).report(base["cursor"]), change)
        unchanged = updates.report()
        self.assertEqual(set(unchanged.keys()), {"status", "revision", "cursor"})
        self.assertEqual((unchanged["status"], unchanged["revision"]), ("unchanged", 3))
        old = self.generation
        self.generation = "c"*32
        self.publish(1,"MORNING",dict(op="baseline",items=[],purifier_stock=[]))
        self.assertEqual(updates.report()["reason"],"cursor_reset")
        self.assertEqual(ledger(self.folder,self.db,4,inventory=True)["generation"],self.generation)
        self.assertNotEqual(self.generation,old)
        self.publish(2,"OPEN",dict(op="delta",remove=[],upsert=[self.bottle]))
        updates.report()
        self.write("head.json",dict(status="waiting_for_load"))
        offline = ledger(self.folder,self.db,4,name="瓶",area="purifier",owned=True)
        self.assertEqual(offline["items"], [self.bottle])
        self.assertEqual(ledger(self.folder,self.db,3,owned=True)["inventory_status"],"never_observed")

    def test_readiness_demand_bottles_and_no_side_effects(self):
        self.view["phase"] = "OPEN"
        self.counter.update(quote_ready=True, offer=dict(items=[dict(id=27,bottle_label="水",capacity_ml=500,
                        last_strip=dict(tested="基礎水",purity="98%"))]))
        with patch("shopdesk.published",return_value=self.view):
            r = trade_tools.wait_event(self.folder,"goods_offered",0)
            self.assertEqual(r["customer_line"], "星際客｜我要買水｜剩餘 114")
            self.assertEqual(r["next_action"],"可以報價")
            self.assertEqual(r["seller_water"]["bottles"][0]["capacity_ml"],500)
            self.counter.update(stage="choice_required",quote_ready=False)
            self.assertEqual(trade_tools.wait_event(self.folder,"opening_finished",0)["reason"],"input_needed")
            self.counter.update(stage="speaking")
            self.assertEqual(trade_tools.wait_event(self.folder,"opening_finished",0)["reason"],"timeout")
            self.counter.update(stage="idle",visit=None,reception=dict(can_ring=True))
            self.assertEqual(trade_tools.wait_event(self.folder,"next_customer_available",0)["reason"],"next_customer_available")

    def test_inventory_waits_for_matching_calendar_revision(self):
        async def run():
            params = StdioServerParameters(command=sys.executable, args=[
                "-B", str(Path(__file__).resolve().parents[1] / "mcp" / "ledger_mcp.py"),
                "--journal", str(self.folder), "--database", str(self.db)])
            async with stdio_client(params) as streams, ClientSession(*streams) as session:
                await session.initialize()
                async def query():
                    result = await session.call_tool("query_inventory", {})
                    self.assertFalse(result.isError, result.content)
                    return json.loads(result.content[0].text)

                desk_file = f"desk-{self.generation}.json"
                yesterday = dict(self.view, day_info=dict(calendar=dict(day=10), trash=dict(status="unavailable")))
                self.publish(2,"MORNING",dict(op="delta",upsert=[],remove=[]),day=11)
                self.write(desk_file,yesterday)
                self.assertEqual(await query(),{"status":"observation_changed"})
                self.write(desk_file,dict(self.view, day=11, day_info=dict(calendar=dict(day=11), trash=dict(status="unavailable"))))
                current = await query()
                self.assertEqual((current["day"],current["calendar"]["day"],current["revision"],current["load_scans"]),
                                 (11,11,2,1))
        asyncio.run(run())

    def test_incremental_report_does_not_create_trade_tickets(self):
        import sqlite3
        from contextlib import closing
        report = trade_tools.Updates(self.folder,self.db).report()
        self.assertEqual(report["status"],"observed")
        with closing(sqlite3.connect(self.db)) as db:
            tables = {row[0] for row in db.execute("SELECT name FROM sqlite_master WHERE type='table'")}
        self.assertIn("book_cursors",tables)
        self.assertNotIn("trade_tickets",tables)
        self.assertEqual(ledger(self.folder,self.db,4)["open_tickets"],[])

    def test_stdio_counter_pages_and_night_bill(self):
        async def run():
            params = StdioServerParameters(command=sys.executable,args=["-B",str(Path(__file__).resolve().parents[1] / "mcp" / "ledger_mcp.py"),
                                       "--journal",str(self.folder),"--database",str(self.db)])
            async with stdio_client(params) as streams, ClientSession(*streams) as session:
                await session.initialize()
                async def call(name, **args):
                    result = await session.call_tool(name,args)
                    self.assertFalse(result.isError,result.content)
                    return json.loads(result.content[0].text)

                async def replay(tool, native, status, **args):
                    args.update(generation=self.generation, revision=self.view["revision"])
                    pending = asyncio.create_task(call(tool,**args))
                    inbox = self.folder/f"counter-{self.generation}.json"
                    for _ in range(400):
                        if inbox.exists() or pending.done():
                            break
                        await asyncio.sleep(.01)
                    if pending.done():
                        self.fail(await pending)
                    command = json.loads(inbox.read_text(encoding="utf-8"))
                    self.assertEqual(command["action"],native)
                    for field,value in args.items():
                        self.assertEqual(command[field],value)
                    receipt = dict(status=status,command_id=command["command_id"],generation=self.generation)
                    self.write(f"receipt-{command['command_id']}.json",receipt)
                    inbox.unlink()
                    first = await pending
                    self.assertEqual(first["status"],status)
                    su = first["shop_update"]
                    if su["status"] == "unchanged":
                        self.assertEqual(set(su.keys()), {"status", "revision", "cursor"})
                    else:
                        self.assertEqual(su["load_scans"],1)
                    again = await call(tool,**args)
                    self.assertEqual(again["command_id"],first["command_id"])
                    self.assertFalse(inbox.exists())

                await call("get_shop_updates")
                self.counter.update(dialogue_protocol=1,night_bill_protocol=1,page="manual",stage="advance_required")
                event=dict(op="delta",upsert=[],remove=[])
                self.publish(2,"OPEN",event)
                await replay("advance_dialogue","advance","advanced",visit=self.visit,page="manual")
                for tool,native,stage,key in [
                    ("choose_dialogue","choose_dialogue","choice_required","dialog_choices"),
                    ("response_choice","response_choice","response_required","response_choices")
                ]:
                    self.counter.update(page=stage,stage=stage)
                    self.counter[key]=[dict(id=7,text="visible",enabled=True)]
                    self.publish(2,"OPEN",event)
                    decision=await call("get_trade_context")
                    self.assertEqual(decision[key][0]["text"],"visible")
                    stale=await call(tool,generation=self.generation,revision=2,visit=self.visit,page="old",choice_id=7)
                    self.assertEqual(stale["status"],"page_changed")
                    await replay(tool,native,"choice_submitted",visit=self.visit,page=decision["page"],choice_id=7)
                    del self.counter[key]
                self.publish(3,"CLOSED",event)
                await replay("prepare_sleep","prepare_sleep","bill_open",day=10,bill_token="before")
                await replay("night_service","night_service","service_set",day=10,bill_token="opened",
                             service_id="clean",cost=7,selected=True)
                cig_v1 = dict(id=77, name="紅牌香菸", kind="red_cigarette", area="front", owned=False, quantity=1,
                              size={"w": 1, "h": 1}, slot={"x": 0, "y": 0}, rotatable=False, estimated_value=100)
                cig_v2 = dict(cig_v1, name="紅牌香菸（贗品）", estimated_value=50)
                self.publish(4, "OPEN", dict(op="delta", upsert=[cig_v1], remove=[]))
                await call("get_shop_updates")
                self.publish(5, "OPEN", dict(op="delta", upsert=[cig_v2], remove=[],
                             identity_changed=[dict(id=77, **{"from": "紅牌香菸", "to": "紅牌香菸（贗品）"})]))
                delta = await call("get_shop_updates")
                self.assertEqual(delta["mode"], "delta")
                self.assertEqual(delta["identity_changed"], [{"id": 77, "from": "紅牌香菸", "to": "紅牌香菸（贗品）"}])
                self.assertEqual(delta["upsert"][0]["estimated_value"], 50)
        asyncio.run(run())

    def test_stdio_rejects_old_native_trade_and_attaches_delta(self):
        async def run():
            params = StdioServerParameters(command=sys.executable,args=["-B",str(Path(__file__).resolve().parents[1] / "mcp" / "ledger_mcp.py"),
                                       "--journal",str(self.folder),"--database",str(self.db)])
            async with stdio_client(params) as streams, ClientSession(*streams) as session:
                await session.initialize()
                async def call(name,**args):
                    response = await session.call_tool(name,args)
                    self.assertFalse(response.isError,response.content)
                    return json.loads(response.content[0].text)
                await call("get_shop_updates")
                for name,args in [("trade_batch", dict(orders=[dict(item_id=27,side="buy",price_limit=4)])),
                                  ("confirm_trade", dict(item_id=27,side="sell",price=4))]:
                    result=await call(name,generation=self.generation,revision=1,visit=self.visit,**args)
                    self.assertEqual(result["status"],"reload_updated_mod")
                    self.assertEqual(result["shop_update"]["status"],"unchanged")
                    self.assertEqual(set(result["shop_update"].keys()),{"status","revision","cursor"})
                self.assertEqual(await call("get_shop_ledger",slot=4),dict(slot=4,status="observed",open_tickets=[]))
                self.assertFalse(list(self.folder.glob("sale-*.json")))
        asyncio.run(run())

    def test_strip_protocols_from_llm_responses(self):
        from shopdesk import strip_protocols
        nested = dict(
            status="observed", desk_protocol=1, counter_protocol=2,
            counter=dict(stage="ready", barter_protocol=1, dialogue_protocol=1, visit=self.visit),
            phone=dict(phone_token="p1", keypad_protocol=1),
        )
        cleaned = strip_protocols(nested)
        self.assertNotIn("desk_protocol", cleaned)
        self.assertNotIn("counter_protocol", cleaned)
        self.assertNotIn("barter_protocol", cleaned["counter"])
        self.assertNotIn("dialogue_protocol", cleaned["counter"])
        self.assertNotIn("keypad_protocol", cleaned["phone"])
        self.assertEqual(cleaned["counter"]["visit"], self.visit)
        self.assertEqual(cleaned["phone"]["phone_token"], "p1")

    def test_context_forwards_forced_choice_and_table_items(self):
        import trade_tools
        view = dict(
            status="observed", slot=4, generation=self.generation, revision=3, phase="OPEN",
            customer=dict(remaining=100),
            counter=dict(
                visit=self.visit, customer="企業廢料主管", stage="choice_required",
                lines=["Take our barrels."], demand={}, quote=None,
                page="p1", dialog_choices=[dict(id=0, text="1 barrel", enabled=True)],
                forced_choice=True, forced_choice_reason="必收核廢料（1/3/5 桶）",
                table_items=[dict(id=7, kind="nuclear_waste", area="back", owned=True, hazardous=True)],
            ),
        )
        ctx = trade_tools.context(view)
        self.assertTrue(ctx["forced_choice"])
        self.assertEqual(ctx["forced_choice_reason"], "必收核廢料（1/3/5 桶）")
        self.assertEqual(ctx["table_items"][0]["kind"], "nuclear_waste")
