"""Local command mailboxes and the receipts published by the game thread."""
import json
import os
import tempfile
import time
from pathlib import Path


from receipt_store import replayed


def exchange(folder, inbox, request, read_receipt, previous_command=None, *, wait_seconds=8):
    command = request["command_id"]
    joined = False
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
                except (UnicodeDecodeError, json.JSONDecodeError, PermissionError):
                    return {"status": "busy", "reason": "inbox_unreadable", "command_id": command}
                if not isinstance(pending, dict):
                    return {"status": "busy", "reason": "inbox_unreadable", "command_id": command}
                if pending.get("command_id") == command:
                    joined = True
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
    deadline = time.monotonic() + wait_seconds
    while time.monotonic() < deadline:
        result = read_receipt()
        if result is not None:
            return replayed(result) if joined else result
        time.sleep(0.075)
    return {"status": "unconfirmed", "command_id": command,
            "detail": "Check the receipt with the original arguments before another action."}
