"""Local command mailboxes and the receipts published by the game thread."""
import json
import os
import tempfile
import time
from pathlib import Path


def read_optional_json(path):
    try:
        return json.loads(path.read_text(encoding="utf-8-sig"))
    except (FileNotFoundError, PermissionError, json.JSONDecodeError):
        return None


def read_command_receipt(folder, command_id, generation):
    receipt = read_optional_json(folder / f"receipt-{command_id}.json")
    if receipt is not None and (receipt["command_id"] != command_id or receipt["generation"] != generation):
        raise ValueError("Receipt belongs to another command or load")
    return receipt


def exchange(folder, inbox, request, read_receipt, previous_command=None):
    command = request["command_id"]
    fd, name = tempfile.mkstemp(dir=folder, prefix="posting-", suffix=".tmp")
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as stream:
            json.dump({**request, "expires_ms": int(time.time()*1000)+6000}, stream)
        # Unity publishes the receipt just before removing the inbox. A batch may
        # reach this point while the preceding confirmed command still owns it.
        handoff_deadline = time.monotonic() + 0.5 if previous_command else None
        while True:
            try:
                os.link(name, inbox)
                break
            except FileExistsError:
                try:
                    pending = json.loads(inbox.read_text(encoding="utf-8"))
                except FileNotFoundError:
                    continue
                if pending.get("command_id") == command:
                    if any(pending.get(k) != v for k, v in request.items()):
                        raise ValueError("command_id already pending for a different request")
                    break
                if (previous_command and pending.get("command_id") == previous_command
                        and time.monotonic() < handoff_deadline):
                    time.sleep(0.025)
                    continue
                return {"status": "busy", "command_id": command}
    finally:
        Path(name).unlink(missing_ok=True)
    deadline = time.monotonic() + 8
    while time.monotonic() < deadline:
        result = read_receipt()
        if result is not None:
            return result
        time.sleep(0.075)
    return {"status": "unconfirmed", "command_id": command,
            "detail": "Check the receipt with the original arguments before another action."}
