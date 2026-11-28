import asyncio
import json
import sys
import unittest
from pathlib import Path
from unittest.mock import patch

from mcp.server.fastmcp import FastMCP
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "mcp"))
import heat_control


class HeatControlTools(unittest.TestCase):
    def test_start_rejects_old_mod_before_dispatch_and_status_reads_fresh(self):
        server = FastMCP("heat-test")
        heat_control.register(server, Path("."))
        args = dict(mode="start", owner_id=4, vessel_id=9, min_c=45, max_c=50, seconds=60,
                    generation="a"*32, revision=2)
        def call(options):
            result = asyncio.run(server.call_tool("control_heat", options))
            return json.loads(result[0].text)
        view = dict(status="observed", generation=args["generation"], revision=2, phase="CLOSED")
        with patch("counter.context", return_value=view), patch("counter.exchange") as exchange:
            self.assertEqual(call(args)["required_protocol"], "heat_control_protocol")
            exchange.assert_not_called()
            view["heat_control_protocol"] = 1
            exchange.return_value={"status":"started"}
            self.assertEqual(call(args)["status"],"started")
            plan = exchange.call_args.args[2]
            self.assertEqual((plan["action"],plan["vessel_id"],plan["seconds"]),("heat_control",9,60))
            call({"mode":"status"})
            first=exchange.call_args.args[2]["command_id"]
            call({"mode":"status"})
            self.assertNotEqual(exchange.call_args.args[2]["command_id"],first)
