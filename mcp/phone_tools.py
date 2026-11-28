"""Phone directory, keypad, dialing, and call menu tools."""
import time
from typing import Literal

from command_transport import poll
import counter
import shopdesk


def _settle_phone(folder, result, predicate, *, initial_connected=None):
    def settled():
        ctx = counter.phone_context(folder)
        if ctx.get("status") != "observed":
            return ctx
        return ctx if predicate(ctx) else None

    ctx, timed_out = poll(settled, timeout=2.0, interval=0.1, sleep=time.sleep)
    if not timed_out and isinstance(ctx, dict) and ctx.get("status") == "observed":
        for key in ("phone_token", "panel_open", "display", "status_text", "connected_to", "line", "can_advance", "choices"):
            if key in ctx:
                result[key] = ctx[key]
            elif key in result and not ctx.get("panel_open") and key in {"connected_to", "line", "can_advance", "choices"}:
                result.pop(key, None)
        if not ctx.get("panel_open") and "choices" not in ctx:
            result["choices"] = []
        contact_name = ctx.get("connected_to") or initial_connected
        if contact_name and isinstance(ctx.get("contacts"), list):
            match = next((c for c in ctx["contacts"] if c.get("name") == contact_name), None)
            if match is not None:
                result["contact_state"] = match.get("state")
                result["cooldown_days"] = match.get("cooldown_days")
    return result


def register(server, folder):
    @server.tool()
    def get_phone_contacts() -> dict:
        """Read known phonebook and owned business-card contacts, services, answerability and visible call choices. Never dials."""
        return shopdesk.strip_protocols(counter.phone_context(folder))

    @server.tool()
    def phone_keypad(mode: Literal["open", "close", "dial"], generation: str, revision: int,
                     phone_token: str, digits: str = "") -> dict:
        """Open or close the phone, or enter 1–9 digits on the empty native keypad. Preserve leading zeros."""
        if mode == "dial" and not (digits.isascii() and digits.isdecimal() and 1 <= len(digits) <= 9):
            return {"status": "invalid_number"}
        return counter.command(folder, "phone_keypad", generation, revision, phone_token=phone_token, mode=mode, digits=digits)

    @server.tool()
    def dial_contact(number: int, generation: str, revision: int, phone_token: str) -> dict:
        """Dial exactly one observed contact and wait briefly for display, line and choices to settle. Starting the native call does not select a menu option."""
        result = counter.command(folder, "dial", generation, revision, phone_token=phone_token, number=number)
        if result.get("status") != "connected":
            return result
        target_display = str(number)
        _settle_phone(
            folder, result,
            lambda ctx: (not ctx.get("panel_open") or (
                ctx.get("display") == target_display and
                bool(ctx.get("line") or ctx.get("choices") or ctx.get("can_advance"))
            )),
            initial_connected=result.get("connected_to"),
        )
        return result

    @server.tool()
    def choose_phone_service(choice_id: int, generation: str, revision: int, phone_token: str) -> dict:
        """Click one enabled choice from the current phone menu. Waits briefly for the phone state to settle after selection. May place an order or change service."""
        result = counter.command(folder, "phone_choice", generation, revision, phone_token=phone_token, choice_id=choice_id)
        if result.get("status") != "choice_submitted":
            return result
        initial_line = result.get("line")
        _settle_phone(
            folder, result,
            lambda ctx: not ctx.get("panel_open") or bool(ctx.get("choices")) or ctx.get("line") != initial_line,
            initial_connected=result.get("connected_to"),
        )
        label = result.get("selected_label") or ""
        if result.get("panel_open") and ("结束通话" in label or "結束通話" in label or "end" in label.lower()):
            result["next_tool"] = "phone_keypad"
            result["next_action"] = "mode=close"
        return result

    @server.tool()
    def advance_phone_dialogue(generation: str, revision: int, phone_token: str) -> dict:
        """Turn one finished ordinary phone page when can_advance is true and wait briefly for the next line, choices and phone_token. Never chooses a service."""
        before = counter.phone_context(folder)
        initial_line = before.get("line") if isinstance(before, dict) else None
        result = counter.command(folder, "phone_advance", generation, revision, phone_token=phone_token)
        if result.get("status") == "advanced":
            _settle_phone(
                folder, result,
                lambda ctx: (not ctx.get("panel_open") or bool(ctx.get("choices")) or
                             bool(ctx.get("can_advance")) or ctx.get("line") != initial_line),
                initial_connected=result.get("connected_to"),
            )
        elif result.get("status") == "phone_changed":
            ctx = counter.phone_context(folder)
            if isinstance(ctx, dict) and ctx.get("status") == "observed":
                for key in ("phone_token", "panel_open", "display", "status_text", "connected_to", "line", "can_advance", "choices"):
                    if key in ctx:
                        result[key] = ctx[key]
        return result
