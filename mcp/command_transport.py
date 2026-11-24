"""Local command mailboxes, receipt recovery, polling, and shared ID helpers."""
import json
import os
import re
import tempfile
import time
from pathlib import Path
from uuid import NAMESPACE_URL, uuid5


_HEX32_RE = re.compile(r"[0-9a-f]{32}")


def is_hex32(value):
    """Return True if value is a lowercase 32-digit hexadecimal string."""
    return isinstance(value, str) and bool(_HEX32_RE.fullmatch(value))


def deterministic_id(namespace, plan):
    """Build a deterministic 32-hex command ID from a namespace prefix and plan payload."""
    payload = plan if isinstance(plan, str) else json.dumps(plan, sort_keys=True)
    return uuid5(NAMESPACE_URL, f"{namespace}{payload}").hex


def read_optional_json(path):
    """Read a JSON file if present and valid; return None on transient or missing files."""
    try:
        return json.loads(path.read_text(encoding="utf-8-sig"))
    except (FileNotFoundError, PermissionError, UnicodeDecodeError, json.JSONDecodeError):
        return None


def replayed(receipt):
    """Mark a recovered command receipt as replayed."""
    return {**receipt, "replayed": True}


def read_command_receipt(folder, command_id, generation, channel, *, legacy_ticket=False):
    """Read a receipt without creating, deleting, or resubmitting a command."""
    path = folder / f"receipt-{command_id}.json"
    for attempt in range(2):
        try:
            data = json.loads(path.read_text(encoding="utf-8-sig"))
        except (FileNotFoundError, PermissionError):
            return None
        except (UnicodeDecodeError, json.JSONDecodeError):
            data = None
        if isinstance(data, dict) and isinstance(data.get("status"), str):
            if data.get("command_id") != command_id or data.get("generation", generation if legacy_ticket else None) != generation:
                raise ValueError("Receipt belongs to another command or load")
            return data
        if attempt == 0:
            time.sleep(0.05)  # A native write may still be in progress.
    copy = read_optional_json(folder / f"last-receipt-{channel}.json")
    if (isinstance(copy, dict) and isinstance(copy.get("status"), str)
            and (copy.get("command_id"), copy.get("generation")) == (command_id, generation)):
        return {**copy, "recovered_from": "channel_receipt"}
    return {"status": "outcome_unknown", "reason": "receipt_corrupt", "command_id": command_id,
            "generation": generation, "detail": "Check the ledger and item location before taking another action."}


def poll(check, *, timeout, interval=0.1, sleep=None):
    """Poll check() until it returns a non-None value or timeout expires.

    Returns (result, timed_out). Always evaluates check() at least once.
    """
    sleep_fn = sleep or time.sleep
    deadline = time.monotonic() + max(0.0, timeout)
    while True:
        result = check()
        if result is not None:
            return result, False
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            return None, True
        sleep_fn(min(interval, remaining))


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
    result, timed_out = poll(read_receipt, timeout=wait_seconds, interval=0.075)
    if not timed_out:
        return replayed(result) if joined else result
    return {"status": "unconfirmed", "command_id": command,
            "detail": "Check the receipt with the original arguments before another action."}


def submit_command(folder, channel, generation, request, *, read_receipt=None,
                   previous_command=None, wait_seconds=None, check_existing=False,
                   replay_transform=None, transport=None):
    """Check an optional existing receipt, then submit request to the channel mailbox."""
    reader = read_receipt or (lambda: read_command_receipt(folder, request["command_id"], generation, channel))
    if check_existing:
        previous = reader()
        if previous is not None:
            return replayed(replay_transform(previous) if replay_transform else previous)
    sender = transport or exchange
    inbox = folder / f"{channel}-{generation}.json"
    kwargs = {"wait_seconds": wait_seconds} if wait_seconds is not None else {}
    if previous_command is not None:
        return sender(folder, inbox, request, reader, previous_command, **kwargs)
    return sender(folder, inbox, request, reader, **kwargs)
