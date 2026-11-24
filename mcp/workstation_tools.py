"""Workstation windows, archive box, slot transfers, item menus/panels, heat control, and machine waits."""
import asyncio
import math
from typing import Literal
from uuid import uuid4

from command_transport import exchange, read_command_receipt, submit_command
import counter
from scene import interrupted_shop_call


def wait_machine(folder, owner_id, generation, revision, control, direction, threshold, timeout, *, transport=None):
    if owner_id < 1 or not control or not math.isfinite(threshold) or not 0 <= timeout <= 60:
        raise ValueError("Use a positive owner_id, a visible reading, a finite threshold and a timeout of 0..60 seconds")
    view = counter.context(folder)
    if view.get("status") != "observed":
        return view
    if view["generation"] != generation or view["revision"] != revision:
        return {"status": "stale_observation"}
    if view.get("machine_wait_protocol") != 1:
        return {"status": "reload_updated_mod", "need": "mod", "required_protocol": "machine_wait_protocol"}
    command = uuid4().hex
    request = dict(command_id=command, generation=generation, revision=revision, owner_id=owner_id,
                   control=control, direction=direction, threshold=threshold, timeout=timeout)

    def receipt():
        return read_command_receipt(folder, command, generation, "watch") or interrupted_shop_call(folder)

    return submit_command(folder, "watch", generation, request, read_receipt=receipt, wait_seconds=timeout + 8, transport=transport or exchange)


def register(server, folder):
    @server.tool()
    def item_menu(item_id: int, generation: str, revision: int, mode: Literal["read", "choose", "targets"] = "read",
                  menu_token: str | None = None, verb: str | None = None, target_id: int | None = None,
                  target_ids: list[int] | None = None) -> dict:
        """Inspect or choose an owned item's native right-click actions. First read returns the shared verb legend and enabled verbs.
        On subsequent reads pass the last menu_token, even across items, for added/removed verbs and changed labels (null resets to base).
        Choose a displayed enabled verb with its matching item and token. activate is also read/consume depending on the native label.
        An expired or already-used token returns status="stale_menu_token" with the item's fresh menu_token and enabled verbs so you can retry in one step; menu_changed means the item's available options changed.
        For use, mode=targets checks up to 64 candidate target_ids via native predicates without using the item. Choose with the matching target_id. Water operations return the water_use route. Using a chemical scanner returns scan{components[id,name,ul,ml,percent], total_ml, tested, label, effects} and auto-closes a tool-opened scanner panel (panel_closed=True). invoked confirms the native action, not an overnight effect."""
        args = dict(item_id=item_id, mode=mode, menu_token=menu_token)
        if mode == "targets":
            if not menu_token or not target_ids or len(target_ids)>64 or any(i<=0 for i in target_ids):
                raise ValueError("Use the source menu_token and 1 to 64 positive candidate target_ids")
            args.update(target_ids=target_ids, read_id=uuid4().hex)
        elif mode == "read":
            args["read_id"] = uuid4().hex
        else:
            if not verb or not menu_token:
                raise ValueError("Choose an enabled verb with the token from item_menu")
            args.update(verb=verb, target_id=target_id)
        return counter.command(folder, "item_menu", generation, revision, **args)

    @server.tool()
    def item_panel(generation: str, revision: int, mode: Literal["read","choose","focus","close"]="read",
                   panel_token: str | None=None, control: str | None=None,
                   value: str | bool | int | float | None=None, panel: str | None=None) -> dict:
        """Read the current item's native panel. Reuse panel_token for changes. Choose a visible enabled control by ID
        using the last token. For select_panel read the desired panel by name, then focus or close with its token. Buttons take no value, choices take a zero-based index, scroll takes 0..1, rename takes text.
        Applied actions return the updated panel under after. Dedicated panels specify the next tool or player action.
        A chemical_scanner view adds a live scan (components by volume, tested/label purity, effects); token rereads return scan only when it changes."""
        if mode=="read":
            return counter.command(folder, "item_panel", generation, revision, mode=mode,
                                   panel_token=panel_token, read_id=uuid4().hex, **({"panel":panel} if panel else {}))
        if not panel_token or (mode=="choose" and not control):
            raise ValueError("Use the current panel_token and an enabled control ID")
        return counter.command(folder, "item_panel", generation, revision, mode=mode,
                               panel_token=panel_token, control=control, value=value, **({"panel":panel} if panel else {}))

    @server.tool()
    def inspect_item_window(owner_id: int, generation: str, revision: int, open_if_closed: bool = False,
                            since_token: str | None = None) -> dict:
        """Inspect an owned inventory/showcase item, or a directly held owned back-counter item after closing.
        Pass the local ID of the item itself as owner_id (never 0, the area or save_id). Pass open_if_closed=True to open and read in one call. Slot items include save_id for matching night loot.
        Read a document by its own ID, including inside an open archive. window.document contains original pages, text, structured terms, or a reason (image_only_guide for image pages). Other windows return visible slots, text and controls (single-item slots report accepts="one_item"; chem_finisher reports recipe_id and button actions configure/output; evaporator reports battery energy, container liquid summary, and distillate_capture status). Pass the last token as since_token for changes only."""
        return counter.command(folder, "inspect_window", generation, revision, owner_id=owner_id,
                               open_if_closed=open_if_closed, since_token=since_token, read_id=uuid4().hex)

    @server.tool()
    def inspect_archive(generation: str, revision: int, mode: Literal["read", "open", "close"] = "read",
                        window_token: str | None = None) -> dict:
        """Read the fixed filing box, or explicitly open/close it before or after business.
        For changes only pass the previous window token. Store/retrieve with transfer_slot_item using the returned owner and control."""
        if mode == "close":
            if not window_token:
                raise ValueError("Use the current archive window token to close it")
            return counter.command(folder, "close_archive", generation, revision, window_token=window_token)
        return counter.command(folder, "inspect_archive", generation, revision, open_if_closed=mode=="open",
                               since_token=window_token, read_id=uuid4().hex)

    @server.tool()
    def transfer_slot_item(owner_id: int, control: str, window_token: str, item_id: int,
                           source: Literal["inventory", "showcase", "slot"], generation: str, revision: int) -> dict:
        """Move owned goods into a visible slot, or from that slot into inventory. Reports observed quantity and changed controls."""
        return counter.command(folder, "transfer_slot", generation, revision, owner_id=owner_id, control=control,
                               window_token=window_token, item_id=item_id, source=source)

    @server.tool()
    def transfer_slot_items(owner_id: int, control: str, window_token: str, item_ids: list[int],
                            source: Literal["inventory", "showcase", "slot"], generation: str, revision: int) -> dict:
        """Transfer one batch to/from the same visible storage control. Inspect each result for partial or skipped moves."""
        if not item_ids or any(i < 1 for i in item_ids) or len(set(item_ids)) != len(item_ids):
            raise ValueError("Provide a nonempty list of distinct positive item IDs")
        return counter.command(folder, "transfer_slots", generation, revision, owner_id=owner_id, control=control,
                               window_token=window_token, item_ids=item_ids, source=source)

    @server.tool()
    def adjust_machine_control(owner_id: int, control: str, values: list[float], window_token: str,
                               generation: str, revision: int) -> dict:
        """Set a visible native machinery slider with [value] or range with [low, high]. Use observed limits.
        Returns the native snapped values and changed window controls. Radio buttons and tabs use press_machine_button."""
        if len(values) not in (1,2):
            raise ValueError("Provide one slider value or two range endpoints")
        return counter.command(folder, "adjust_control", generation, revision, owner_id=owner_id, control=control,
                               values=values, window_token=window_token)

    @server.tool()
    def press_machine_button(owner_id: int, control: str, label: str, window_token: str,
                             generation: str, revision: int, tab_index: int | None=None, quantity: int | None=None) -> dict:
        """For a tabs control pass its zero-based tab_index and exact page caption as label. Press one visible enabled machinery button by observed label and token. May consume supplies or start production.
        For the chemical finisher output only, quantity (1..64) reports produced, purity, grade, and a stop reason. Selecting a recipe in the chemical_finisher panel exposes recipe_id on each button. Returns changed controls."""
        return counter.command(folder, "press_control", generation, revision, owner_id=owner_id, control=control,
                               window_token=window_token, label=label, **({"tab_index":tab_index} if tab_index is not None else {}),
                               **({"quantity":quantity} if quantity is not None else {}))

    @server.tool()
    def control_heat(mode: Literal["start", "status", "stop"], owner_id: int | None=None, vessel_id: int | None=None,
                     min_c: int | None=None, max_c: int | None=None, seconds: int | None=None,
                     generation: str | None=None, revision: int | None=None, run_id: str | None=None) -> dict:
        """Start one background heating run from an open plate window. Pass plate/bottle IDs, range (26..160 C),
        duration in real seconds from start, generation and revision. The start receipt returns immediately.
        Use status for a live sample, stop with the returned run_id. To repeat an identical finished run pass
        its run_id on the new start. Shop deltas report completion. Native heat steps may overshoot in low/off mode.
        The plate uses high heat to close large gaps. Summaries report peak_c, reached_range and in_range_seconds;
        a finished run that never entered the range adds warning="range_not_reached"."""
        if mode == "start":
            if None in (owner_id,vessel_id,min_c,max_c,seconds,generation,revision):
                raise ValueError("Start requires owner_id, vessel_id, min_c, max_c, seconds, generation and revision")
            if min(owner_id,vessel_id,seconds) < 1 or not 26 <= min_c <= max_c <= 160:
                raise ValueError("Supply positive IDs and seconds, and 26 <= min_c <= max_c <= 160")
            args = dict(owner_id=owner_id,vessel_id=vessel_id,min_c=min_c,max_c=max_c,seconds=seconds)
        else:
            if mode == "stop" and not run_id:
                raise ValueError("Stop requires the run_id from the start receipt")
            view = counter.context(folder)
            if view.get("status") != "observed":
                return view
            generation, revision = view["generation"], view["revision"]
            args = {"read_id": uuid4().hex} if mode == "status" else {}
        if run_id is not None:
            args["run_id"] = run_id
        return counter.command(folder,"heat_control",generation,revision,mode=mode,**args)

    @server.tool()
    async def wait_for_machine(owner_id: int, generation: str, revision: int, control: str, threshold: float,
                               direction: Literal["at_least", "at_most"]="at_least", timeout: float=10) -> dict:
        """Watch an open owned machine until its reading reaches the threshold or times out (0..60 real seconds).
        Use current_c for a heating plate with a vessel, or a visible progress control path from inspect_item_window.
        The final receipt gives the observed value and elapsed_ms. The machine keeps running."""
        return await asyncio.to_thread(wait_machine, folder, owner_id, generation, revision, control, direction, threshold, timeout)
