"""Water usage, testing, stationary purifiers, and batch bottle fill-and-purify tools."""
from typing import Literal
from uuid import uuid4

import counter
from flow import Flow
from moves import move_item as submit_move
import shopdesk


def register(server, folder):
    @server.tool()
    def water_use(source_id: int, target_id: int | Literal["faucet", "drain"], generation: str, revision: int,
                  mode: Literal["preview", "apply", "apply_now"] = "preview", use_token: str | None = None) -> dict:
        """Preview or apply a water interaction. Sources include bottles, rations, tablets, portable purifiers and fresh strips.
        An empty drain or pour source returns source_empty before a token is issued. A receiving filter adds component losses and final vessel volume to plan.filter.
        Low-risk operations (filling from target_id="faucet", purifying with portable_water_purifier, or testing with water_test_strip) support mode="apply_now" in a single call without preview. High-risk operations (drain, bottle-to-bottle pour, tablet, ration) reject apply_now with preview_required and must use preview + apply.
        To fill a container from the tap, pass the water container as source_id and target_id="faucet"; pass target_id="drain" to empty it.
        Use a single water_test_strip or water_tablet as source_id and the bottle as target_id. For boxed supplies, inspect_item_window(owner_id=box id, open_if_closed=True) then transfer_slot_item(source="slot").
        purifier_used reports charge_remaining and liters_remaining; tablet_used explains effect_pending (chlorine reacts over time/overnight and resets strip exposure).
        For mode="apply", submit the same two IDs with the preview use_token."""
        if source_id < 1 or (isinstance(target_id, int) and (target_id < 1 or target_id == source_id)):
            raise ValueError("Choose a source and a distinct water target from the current observation")
        if mode == "preview":
            return counter.command(folder, "water_use", generation, revision, source_id=source_id,
                                   target_id=target_id, mode=mode, read_id=uuid4().hex)
        if mode == "apply_now":
            return counter.command(folder, "water_use", generation, revision, source_id=source_id,
                                   target_id=target_id, mode=mode)
        if not use_token:
            raise ValueError("Use the token from the water_use preview")
        return counter.command(folder, "water_use", generation, revision, source_id=source_id,
                               target_id=target_id, mode=mode, use_token=use_token)

    @server.tool()
    def fill_and_purify_bottles(bottle_ids: list[int], purifier_id: int, generation: str, revision: int) -> dict:
        """Fill each empty bottle from the faucet and immediately purify it with the given portable_water_purifier using low-risk apply_now.
        Returns per-bottle status/water_ml/bottle_label and final purifier charge_remaining and liters_remaining. Stops on notice, empty charge, or closed faucet."""
        if not bottle_ids or any(i < 1 for i in bottle_ids) or len(set(bottle_ids)) != len(bottle_ids):
            raise ValueError("Provide a nonempty list of distinct positive bottle IDs")
        if purifier_id < 1 or purifier_id in bottle_ids:
            raise ValueError("Provide a valid purifier_id distinct from bottle_ids")
        flow = Flow(folder, generation, revision)
        results = []
        charge_remaining = None
        liters_remaining = None
        overall = "completed"
        for idx, bottle_id in enumerate(bottle_ids):
            fill_res = flow.command("water_use", source_id=bottle_id, target_id="faucet", mode="apply_now")
            if fill_res.get("status") != "transferred":
                results.append({"bottle_id": bottle_id, "status": fill_res.get("status"),
                                "step": "fill", "reason": fill_res.get("reason")})
                overall = "partial" if any(r["status"] == "filled_and_purified" for r in results) else fill_res.get("status", "failed")
                results.extend({"bottle_id": pending, "status": "not_attempted"} for pending in bottle_ids[idx + 1:])
                break
            flow.sync_revision()
            purify_res = flow.command("water_use", source_id=purifier_id, target_id=bottle_id, mode="apply_now")
            if "charge_remaining" in purify_res:
                charge_remaining = purify_res["charge_remaining"]
            if "liters_remaining" in purify_res:
                liters_remaining = purify_res["liters_remaining"]
            if purify_res.get("status") != "purifier_used":
                row = {"bottle_id": bottle_id, "status": purify_res.get("status"),
                       "step": "purify", "fill_status": "transferred"}
                for k in ("reason", "notice", "panel_token"):
                    if k in purify_res:
                        row[k] = purify_res[k]
                results.append(row)
                overall = "partial" if any(r["status"] == "filled_and_purified" for r in results) else purify_res.get("status", "failed")
                results.extend({"bottle_id": pending, "status": "not_attempted"} for pending in bottle_ids[idx + 1:])
                break
            row = {
                "bottle_id": bottle_id,
                "status": "filled_and_purified",
                "fill_status": "transferred",
                "purify_status": "purifier_used",
                "charge_used": purify_res.get("charge_used"),
                "charge_remaining": charge_remaining,
                "liters_remaining": liters_remaining,
            }
            for k in ("water_ml", "bottle_label"):
                if k in purify_res:
                    row[k] = purify_res[k]
                elif k in fill_res:
                    row[k] = fill_res[k]
            results.append(row)
            flow.sync_revision()
        if charge_remaining is None:
            info = shopdesk.machines(folder)
            if info.get("status") == "observed":
                for p in info.get("portable_purifiers", []):
                    if p.get("id") == purifier_id:
                        charge_remaining = p.get("charge_remaining")
                        liters_remaining = p.get("liters_remaining")
                        break
        completed_count = sum(1 for r in results if r.get("status") == "filled_and_purified")
        reply = {
            "status": overall,
            "generation": generation,
            "revision": flow.rev,
            "purifier_id": purifier_id,
            "completed_count": completed_count,
            "failed_count": len(bottle_ids) - completed_count,
            "items": results,
        }
        if charge_remaining is not None:
            reply["charge_remaining"] = charge_remaining
        if liters_remaining is not None:
            reply["liters_remaining"] = liters_remaining
        return reply

    @server.tool()
    def water_panel(generation: str, revision: int, mode: Literal["read", "match", "close", "notice_close"] = "read",
                    panel_token: str | None = None, bottle_id: int | None = None) -> dict:
        """Read an already open test-strip panel, including tested bottle, displayed result and unused strip count.
        Match an owned bottle label only with its observed ID and panel token, or close that panel by token. A visible single-button purifier notice can be acknowledged with notice_close."""
        if mode == "read":
            return counter.command(folder, "water_panel", generation, revision, mode=mode, read_id=uuid4().hex)
        if not panel_token or (mode == "match" and (bottle_id is None or bottle_id < 1)):
            raise ValueError("Use the observed panel_token and, for match, its bottle_id")
        return counter.command(folder, "water_panel", generation, revision, mode=mode,
                               panel_token=panel_token, bottle_id=bottle_id)

    @server.tool()
    def inspect_purifiers() -> dict:
        """Compare last night's and current bottle IDs, list portable_purifiers (with charge_remaining and liters_remaining), and read battery, extraction readiness and unused test strips."""
        return shopdesk.machines(folder)

    @server.tool()
    def take_purifier_bottle(machine_id: int, bottle_id: int, generation: str, revision: int,
                             command_id: str | None = None) -> dict:
        """While closed, retrieve the observed bottle from its purifier into inventory.
        Use IDs and revision from inspect_purifiers. A moved receipt confirms the actual
        destination, empty source slot and unused strip count. Does not test the water."""
        return submit_move(folder, bottle_id, "purifier", "inventory", generation, revision,
                           command_id, machine_id=machine_id)

    @server.tool()
    def put_purifier_bottle(machine_id: int, bottle_id: int, source: Literal["inventory", "showcase"],
                            generation: str, revision: int, command_id: str | None = None) -> dict:
        """Put a bottle in the stationary purifier slot. For the portable purifier use water_use(source_id=purifier, target_id=bottle)."""
        return submit_move(folder, bottle_id, source, "purifier", generation, revision,
                           command_id, machine_id=machine_id)
