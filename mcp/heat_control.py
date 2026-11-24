"""Background regulation for a single observed heating plate and vessel."""
from typing import Literal
from uuid import uuid4

import counter


def register(server, folder):
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
