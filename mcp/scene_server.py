"""Expose all ledger tools permanently and gate actions by the current game scene."""
import json
import sqlite3

from mcp.server.fastmcp import FastMCP
from mcp.server.lowlevel.server import NotificationOptions
from mcp.server.stdio import stdio_server

from journey import ACTION_REGIONS, REGIONS
from scene import read_scene, shop_visit, tool_region


# Tools that can change shop state; their replies carry the shop_update delta.
MUTATING_TOOLS = frozenset({
    "report_client",
    "move_item", "move_items", "sell_order", "trade_batch", "confirm_trade", "buy_merchant_batch",
    "barter_offer", "pay_info_receipt", "open_shop", "close_shop", "ring_customer", "dismiss_customer",
    "control_heat", "next_customer", "finish_customer", "fill_and_purify_bottles", "sort_inventory",
    "item_menu", "item_panel", "reopen_trade_tablet", "request_supplier_quote",
    "advance_dialogue", "choose_dialogue", "response_choice", "prepare_sleep", "night_service",
    "set_auto_receive", "sleep", "transfer_slot_item", "transfer_slot_items", "press_machine_button",
    "adjust_machine_control", "put_purifier_bottle", "take_purifier_bottle", "water_use", "water_panel",
    "dial_contact", "phone_keypad", "choose_phone_service", "advance_phone_dialogue",
    "inspect_archive", "inspect_item_window", "wait_for_opening", "chem_finish",
})
MODE_MUTATING_TOOLS = frozenset({"wild_network", "bargain"})


def _slim_shop_update(tool_name, result, update, arguments=None, last_emitted=None):
    if not isinstance(update, dict):
        return update
    if update.get("mode") == "baseline" and update.get("reason") == "cursor_reset":
        if last_emitted is not None:
            last_emitted.clear()
        fields = update.get("fields") or {}
        return {
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

    if not upsert and not remove and not fields and not checkpoints and not id_changes:
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
        self._last_emitted_items = {}

    async def list_tools(self):
        self.client_session = self.get_context().session
        return await super().list_tools()

    def available(self, name, region):
        if name in self.global_tools:
            return True
        if name in REGIONS:
            return REGIONS[name] == region
        return region == "pawnshop" or (region == "offline" and name in self.ledger_tools)

    async def call_tool(self, name, arguments):
        scene = read_scene(self.folder)
        if scene.get("status") == "game_over":
            if name == "get_scene" or name in self.ledger_tools:
                return await super().call_tool(name, arguments)
            return {**scene, "tool_status": "game_over"}
        if name in ACTION_REGIONS:
            # A departure can finish before the client receives its reply. The command
            # layer can recover an existing receipt across regions, then gates new work.
            return await super().call_tool(name, arguments)
        if name not in self.global_tools:
            region = tool_region(scene)
            if not self.available(name, region):
                return {**scene, "tool_status": "unavailable_in_scene"}
            if name in REGIONS or region == "offline":
                return await super().call_tool(name, arguments)
            token = shop_visit.set((scene["session"], scene["revision"]))
            try:
                mutating = name in MUTATING_TOOLS or (name in MODE_MUTATING_TOOLS and arguments.get("mode", "read") != "read")
                if mutating and (self.updates.cursor is not None or name == "report_client"):
                    tool = self._tool_manager.get_tool(name)
                    result = await tool.run(arguments, context=self.get_context())
                    if name == "report_client" and result.get("status") != "arrest_finished" and (arguments.get("mode", "read") == "read" or self.updates.cursor is None):
                        return tool.fn_metadata.convert_result(result)
                    try:
                        result["shop_update"] = _slim_shop_update(name, result, self.updates.report(), arguments, self._last_emitted_items)
                    except (OSError, sqlite3.Error, json.JSONDecodeError) as error:
                        # The action may already have settled. Keep its receipt even when the next read fails.
                        result["shop_update"] = {"status":"update_unavailable", "detail":str(error)}
                    return tool.fn_metadata.convert_result(result)
                return await super().call_tool(name, arguments)
            finally:
                shop_visit.reset(token)
        return await super().call_tool(name, arguments)

    async def run_stdio_async(self):
        options = self._mcp_server.create_initialization_options(NotificationOptions())
        async with stdio_server() as streams:
            await self._mcp_server.run(*streams, options)
