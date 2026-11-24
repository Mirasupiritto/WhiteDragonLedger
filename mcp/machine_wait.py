"""One read-only threshold watch on an open machine window."""
import asyncio
import math
from typing import Literal
from uuid import uuid4

import counter
from command_transport import exchange, read_command_receipt, submit_command
from scene import interrupted_shop_call


def wait(folder, owner_id, generation, revision, control, direction, threshold, timeout):
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

    return submit_command(folder, "watch", generation, request, read_receipt=receipt, wait_seconds=timeout + 8, transport=exchange)


def register(server, folder):
    @server.tool()
    async def wait_for_machine(owner_id: int, generation: str, revision: int, control: str, threshold: float,
                               direction: Literal["at_least", "at_most"]="at_least", timeout: float=10) -> dict:
        """Watch an open owned machine until its reading reaches the threshold or times out (0..60 real seconds).
        Use current_c for a heating plate with a vessel, or a visible progress control path from inspect_item_window.
        The final receipt gives the observed value and elapsed_ms. The machine keeps running."""
        return await asyncio.to_thread(wait, folder, owner_id, generation, revision, control, direction, threshold, timeout)
