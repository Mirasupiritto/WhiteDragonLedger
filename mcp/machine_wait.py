"""Compatibility shim; wait_for_machine now lives in workstation_tools."""
import asyncio
from typing import Literal

from command_transport import exchange
from workstation_tools import wait_machine


def wait(folder, owner_id, generation, revision, control, direction, threshold, timeout):
    return wait_machine(folder, owner_id, generation, revision, control, direction, threshold, timeout, transport=exchange)


def register(server, folder):
    @server.tool()
    async def wait_for_machine(owner_id: int, generation: str, revision: int, control: str, threshold: float,
                               direction: Literal["at_least", "at_most"]="at_least", timeout: float=10) -> dict:
        """Watch an open owned machine until its reading reaches the threshold or times out (0..60 real seconds).
        Use current_c for a heating plate with a vessel, or a visible progress control path from inspect_item_window.
        The final receipt gives the observed value and elapsed_ms. The machine keeps running."""
        return await asyncio.to_thread(wait, folder, owner_id, generation, revision, control, direction, threshold, timeout)
