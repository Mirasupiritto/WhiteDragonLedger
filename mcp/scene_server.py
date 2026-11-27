"""Expose all ledger tools permanently and gate actions by the current game scene."""
import asyncio
import json
import sqlite3

from mcp.server.fastmcp import FastMCP
from mcp.server.fastmcp.exceptions import ToolError
from mcp.server.lowlevel.server import NotificationOptions
from mcp.server.stdio import stdio_server

from journey import ACTION_REGIONS, REGIONS
from receipt_store import read_optional_json
from scene import read_scene, shop_visit, tool_region
from server_state import ServerState


# Tools that can change shop state; their replies carry the shop_update delta.
# Values are either True (always mutating) or a predicate(arguments) -> bool.
TOOL_MUTATION_RULES = {
    "report_client": True,
    "move_item": True,
    "move_items": True,
    "sell_order": True,
    "trade_batch": True,
    "confirm_trade": True,
    "submit_appraisal": True,
    "buy_merchant_batch": True,
    "barter_offer": True,
    "pay_info_receipt": True,
    "open_shop": True,
    "close_shop": True,
    "ring_customer": True,
    "dismiss_customer": True,
    "control_heat": lambda args: args.get("mode", "status") != "status",
    "next_customer": True,
    "finish_customer": True,
    "fill_and_purify_bottles": True,
    "sort_inventory": True,
    "toggle_poster": True,
    "item_menu": lambda args: args.get("mode", "read") not in {"read", "targets"},
    "item_panel": lambda args: args.get("mode", "read") != "read",
    "reopen_trade_tablet": True,
    "request_supplier_quote": True,
    "advance_dialogue": True,
    "choose_dialogue": True,
    "response_choice": True,
    "prepare_sleep": True,
    "night_service": True,
    "set_auto_receive": True,
    "sleep": True,
    "transfer_slot_item": True,
    "transfer_slot_items": True,
    "press_machine_button": True,
    "adjust_machine_control": True,
    "put_purifier_bottle": True,
    "take_purifier_bottle": True,
    "water_use": lambda args: args.get("mode") != "preview",
    "water_panel": lambda args: args.get("mode", "read") != "read",
    "dial_contact": True,
    "phone_keypad": True,
    "choose_phone_service": True,
    "advance_phone_dialogue": True,
    "inspect_archive": lambda args: args.get("mode", "read") != "read",
    "inspect_item_window": lambda args: bool(args.get("open_if_closed", False)),
    "wait_for_opening": True,
    "chem_finish": True,
    "wild_network": lambda args: args.get("mode", "read") != "read",
    "bargain": lambda args: args.get("mode", "read") != "read",
}


def is_mutating_tool(name, arguments):
    rule = TOOL_MUTATION_RULES.get(name, False)
    if callable(rule):
        return bool(rule(arguments or {}))
    return bool(rule)


MUTATING_TOOLS = frozenset(name for name, rule in TOOL_MUTATION_RULES.items() if rule is True)


def attach_arrivals(folder, previous, update):
    if not previous or update.get("mode") != "delta":
        return update
    generation, rev, _ = previous.split(":", 2)
    arrivals = []
    outside = 0
    for number in range(int(rev)+1, update["revision"]+1):
        record = read_optional_json(folder / f"{generation}-{number}.json") or {}
        arrivals.extend(record.get("arrivals", []))
        outside += record.get("contraband_outside_bay", 0)
    if arrivals:
        update["arrivals"] = arrivals
    if outside:
        update["contraband_outside_bay"] = outside
    return update


def _slim_shop_update(tool_name, result, update, arguments=None, last_emitted=None):
    if not isinstance(update, dict):
        return update
    if update.get("mode") == "baseline" and update.get("reason") == "cursor_reset":
        if last_emitted is not None:
            last_emitted.clear()
        fields = update.get("fields") or {}
        summary = {
            "status": update["status"],
            "mode": "baseline_summary",
            "reason": "cursor_reset",
            "slot": update.get("slot"),
            "generation": update.get("generation"),
            "revision": update.get("revision"),
            "cursor": update.get("cursor"),
            "item_count": len(update.get("items") or []),
            "cash": fields.get("cash"),
            "day": fields.get("day"),
            "phase": fields.get("phase"),
            "next_tool": "get_shop_updates",
        }
        if "heat" in fields:
            summary["heat"] = fields["heat"]
        return summary
    if update.get("mode") != "delta" or not isinstance(result, dict):
        if last_emitted is not None and update.get("mode") == "baseline":
            last_emitted.clear()
            for row in update.get("items") or []:
                if isinstance(row, dict) and row.get("id") is not None:
                    last_emitted[row["id"]] = json.dumps(row, sort_keys=True, ensure_ascii=False)
        return update

    receipt_ids = set()
    if tool_name in {"buy_merchant_batch", "trade_batch", "confirm_trade"}:
        for row in result.get("items") or []:
            if isinstance(row, dict):
                item_id = row.get("id") if "id" in row else row.get("item_id")
                if item_id is not None:
                    receipt_ids.add(item_id)

    prev_emitted = dict(last_emitted) if last_emitted is not None else {}
    upsert = []
    for row in update.get("upsert", []):
        if not isinstance(row, dict):
            upsert.append(row)
            continue
        rid = row.get("id")
        if rid in receipt_ids:
            continue
        encoded = json.dumps(row, sort_keys=True, ensure_ascii=False)
        if rid is not None and prev_emitted.get(rid) == encoded:
            continue
        upsert.append(row)

    if last_emitted is not None:
        last_emitted.clear()
        for row in result.get("items") or []:
            if isinstance(row, dict) and row.get("id") is not None:
                last_emitted[row["id"]] = json.dumps(row, sort_keys=True, ensure_ascii=False)
        for row in upsert:
            if isinstance(row, dict) and row.get("id") is not None:
                last_emitted[row["id"]] = json.dumps(row, sort_keys=True, ensure_ascii=False)

    remove = update.get("remove", [])
    fields = update.get("fields", {})
    checkpoints = update.get("checkpoints")
    id_changes = update.get("identity_changed")
    offer_summarized = isinstance(result.get("offer"), dict) and result["offer"].get("summary_only") is True
    verbose = bool((arguments or {}).get("verbose"))

    if not upsert and not remove and not fields and not checkpoints and not id_changes and not update.get("arrivals"):
        return {"status": "unchanged", "revision": update["revision"], "cursor": update["cursor"]}
    if offer_summarized and not verbose and upsert:
        slimmed = dict(update)
        slimmed.pop("upsert", None)
        slimmed["upsert_summary"] = {"count": len(upsert)}
        return slimmed
    return dict(update, upsert=upsert)


_slim_purchase_update = _slim_shop_update


class SceneServer(FastMCP):
    def __init__(self, folder):
        super().__init__("白龍店帳")
        self.folder = folder
        self.global_tools = {"get_scene", "wait_for_scene"}
        self.ledger_tools = {"get_shop_ledger", "dismiss_trade_ticket"}
        self.client_session = None
        self.state = ServerState()
        self.section_cache = self.state.section_cache
        self._last_emitted_items = self.state.last_emitted_items
        self._tool_lock = asyncio.Lock()

    async def list_tools(self):
        self.client_session = self.get_context().session
        return await super().list_tools()

    def available(self, name, region):
        if name in self.global_tools:
            return True
        if name in REGIONS:
            return REGIONS[name] == region
        return region == "pawnshop" or (region == "offline" and name in self.ledger_tools)

    async def _invoke_tool(self, name, arguments, *, convert_result=True):
        tool = self._tool_manager.get_tool(name)
        if tool is None:
            return await super().call_tool(name, arguments)
        if tool.is_async:
            return await tool.run(arguments, context=self.get_context(), convert_result=convert_result)

        async def run_in_thread(*args, **kwargs):
            return await asyncio.to_thread(tool.fn, *args, **kwargs)

        try:
            ctx_arg = {tool.context_kwarg: self.get_context()} if tool.context_kwarg is not None else None
            result = await tool.fn_metadata.call_fn_with_arg_validation(
                run_in_thread, True, arguments, ctx_arg
            )
            if convert_result:
                result = tool.fn_metadata.convert_result(result)
            return result
        except Exception as e:
            raise ToolError(f"Error executing tool {tool.name}: {e}") from e

    def _collect_shop_update(self, name, result, arguments):
        with self.state.lock:
            previous = self.updates.cursor
            update = attach_arrivals(self.folder, previous, self.updates.report())
            return _slim_shop_update(name, result, update, arguments, self._last_emitted_items)

    def _run_get_shop_updates(self, arguments):
        with self.state.lock:
            args = arguments or {}
            previous = args.get("since") or self.updates.cursor
            result = self.updates.report(**args)
            return attach_arrivals(self.folder, previous, result)

    async def call_tool(self, name, arguments):
        if name in self.global_tools:
            if name == "wait_for_scene":
                scene = await asyncio.to_thread(read_scene, self.folder)
                if scene.get("status") == "game_over":
                    return {**scene, "tool_status": "game_over"}
            return await self._invoke_tool(name, arguments)
        async with self._tool_lock:
            return await self._call_locked(name, arguments)

    async def _call_locked(self, name, arguments):
        scene = await asyncio.to_thread(read_scene, self.folder)
        if scene.get("status") == "game_over":
            if name in self.ledger_tools:
                return await self._invoke_tool(name, arguments)
            return {**scene, "tool_status": "game_over"}
        if name in ACTION_REGIONS:
            # A departure can finish before the client receives its reply. The command
            # layer can recover an existing receipt across regions, then gates new work.
            return await self._invoke_tool(name, arguments)
        region = tool_region(scene)
        if not self.available(name, region):
            return {**scene, "tool_status": "unavailable_in_scene"}
        if name in REGIONS or region == "offline":
            return await self._invoke_tool(name, arguments)
        token = shop_visit.set((scene["session"], scene["revision"]))
        try:
            if name == "get_shop_updates":
                tool = self._tool_manager.get_tool(name)
                result = await asyncio.to_thread(self._run_get_shop_updates, arguments)
                return tool.fn_metadata.convert_result(result)
            mutating = is_mutating_tool(name, arguments)
            if mutating and (self.updates.cursor is not None or name == "report_client"):
                tool = self._tool_manager.get_tool(name)
                result = await self._invoke_tool(name, arguments, convert_result=False)
                if name == "report_client" and result.get("status") != "arrest_finished" and (arguments.get("mode", "read") == "read" or self.updates.cursor is None):
                    return tool.fn_metadata.convert_result(result)
                try:
                    result["shop_update"] = await asyncio.to_thread(self._collect_shop_update, name, result, arguments)
                except (OSError, sqlite3.Error, json.JSONDecodeError) as error:
                    # The action may already have settled. Keep its receipt even when the next read fails.
                    result["shop_update"] = {"status": "update_unavailable", "detail": str(error)}
                return tool.fn_metadata.convert_result(result)
            return await self._invoke_tool(name, arguments)
        finally:
            shop_visit.reset(token)

    async def run_stdio_async(self):
        options = self._mcp_server.create_initialization_options(NotificationOptions())
        async with stdio_server() as streams:
            await self._mcp_server.run(*streams, options)
