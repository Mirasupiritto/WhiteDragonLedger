"""One local mailbox per load. Only the Unity game thread may confirm a transfer."""
import json
import os
import re
import tempfile
import time
from pathlib import Path
from uuid import uuid4

from ledger import read_head


def move_item(folder: Path, item_id, source, destination, generation, revision, command_id=None):
    command = command_id or uuid4().hex
    if not re.fullmatch("[a-f0-9]{32}", generation) or not re.fullmatch("[a-f0-9]{32}", command):
        raise ValueError("generation and command_id must be lowercase 32-digit hex values")
    if {source, destination} != {"inventory", "showcase"} or item_id < 1 or revision < 1:
        raise ValueError("Expected a positive item ID, revision and two different stock areas")
    request = dict(command_id=command, item_id=item_id, source=source, destination=destination,
                   generation=generation, revision=revision)
    def read_receipt():
        try:
            result = json.loads((folder / f"receipt-{command}.json").read_text(encoding="utf-8-sig"))
        except (FileNotFoundError, PermissionError, json.JSONDecodeError):
            return None
        if (result["generation"], result["expected_revision"], result["item_id"], result["from"],
            result["requested_area"], result["command_id"]) != (generation, revision, item_id, source, destination, command):
            raise ValueError("command_id already used for a different move")
        return result

    previous = read_receipt()
    if previous is not None:
        return previous
    head = read_head(folder)
    if head.get("generation") != generation or head.get("revision") != revision:
        return {"status": "stale_observation", "command_id": command}
    if head.get("move_protocol") != 1:
        return {"status": "reload_updated_mod", "command_id": command}
    if head["phase"] not in ("CLOSED", "MORNING"):
        return {"status": "shop_open", "command_id": command}
    inbox = folder / f"move-{generation}.json"
    fd, name = tempfile.mkstemp(dir=folder, prefix="posting-", suffix=".tmp")
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as stream:
            json.dump({**request, "expires_ms": int(time.time()*1000)+6000}, stream)
        try:
            # A fully written request becomes visible atomically, without replacing another move.
            os.link(name, inbox)
        except FileExistsError:
            try:
                pending = json.loads(inbox.read_text(encoding="utf-8"))
            except FileNotFoundError:
                pending = {}
            if pending and pending.get("command_id") != command:
                return {"status": "busy", "command_id": command}
            if pending and any(pending.get(k) != v for k,v in request.items()):
                raise ValueError("command_id already pending for a different move")
    finally:
        Path(name).unlink(missing_ok=True)
    deadline = time.monotonic() + 8
    while time.monotonic() < deadline:
        result = read_receipt()
        if result is not None:
            return result
        time.sleep(0.075)
    return {"status": "unconfirmed", "command_id": command,
            "detail": "Use the same command_id to check the receipt and query inventory before another move."}
