"""One local mailbox per load. Only the Unity game thread may confirm a transfer."""
import json
import os
import re
import tempfile
import time
from pathlib import Path
from uuid import NAMESPACE_URL, uuid4, uuid5

from ledger import read_head


def move_item(folder: Path, item_id, source, destination, generation, revision, command_id=None, *, previous_command=None, machine_id=None):
    command = command_id or uuid4().hex
    if not re.fullmatch("[a-f0-9]{32}", generation) or not re.fullmatch("[a-f0-9]{32}", command):
        raise ValueError("generation and command_id must be lowercase 32-digit hex values")
    if machine_id is not None:
        if machine_id < 1 or (source, destination) != ("purifier", "inventory"):
            raise ValueError("Expected a purifier ID and inventory destination")
    elif {source, destination} != {"inventory", "showcase"}:
        raise ValueError("Expected two different stock areas")
    if item_id < 1 or revision < 1:
        raise ValueError("Expected a positive item ID, revision and two different stock areas")
    request = dict(command_id=command, item_id=item_id, source=source, destination=destination,
                   generation=generation, revision=revision)
    if machine_id is not None:
        request["machine_id"] = machine_id

    def read_receipt():
        try:
            result = json.loads((folder / f"receipt-{command}.json").read_text(encoding="utf-8-sig"))
        except (FileNotFoundError, PermissionError, json.JSONDecodeError):
            return None
        if (result["generation"], result["expected_revision"], result["item_id"], result["from"],
            result["requested_area"], result["command_id"], result.get("machine_id")) != (
                generation, revision, item_id, source, destination, command, machine_id):
            raise ValueError("command_id already used for a different move")
        return result

    previous = read_receipt()
    if previous is not None:
        return previous
    head = read_head(folder)
    if head.get("generation") != generation or head.get("revision") != revision:
        return {"status": "stale_observation", "command_id": command}
    if head.get("move_protocol") != 1 or (machine_id is not None and head.get("purifier_take_protocol") != 1):
        return {"status": "reload_updated_mod", "command_id": command}
    if head["phase"] not in ("CLOSED", "MORNING"):
        return {"status": "shop_open", "command_id": command}
    inbox = folder / f"move-{generation}.json"
    return exchange(folder, inbox, request, read_receipt, previous_command)


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
                        raise ValueError("command_id already pending for a different move")
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


def move_items(folder: Path, item_ids, source, destination, generation, revision):
    """Execute one ordered plan. An identical plan reuses its native receipts."""
    if not item_ids or any(item_id < 1 for item_id in item_ids):
        raise ValueError("Expected a nonempty list of positive item IDs")
    if len(set(item_ids)) != len(item_ids):
        raise ValueError("Each item must appear only once in a batch")

    plan = json.dumps([generation, revision, source, destination, item_ids], separators=(",", ":"))
    batch_id = uuid5(NAMESPACE_URL, "white-dragon-ledger/move-items/" + plan)
    result = dict(batch_id=batch_id.hex, generation=generation, expected_revision=revision,
                  revision=revision, source=source, destination=destination,
                  status="moved", moved_count=0, items=[])
    previous_command = None
    for position, item_id in enumerate(item_ids):
        command = uuid5(batch_id, str(item_id)).hex
        receipt = move_item(folder, item_id, source, destination, generation, result["revision"], command,
                            previous_command=previous_command)
        row = {"item_id": item_id, "status": receipt["status"], "from": source,
               "actual_area": receipt.get("actual_area")}
        for field in ("name", "kind", "quantity", "revision"):
            if field in receipt:
                row[field] = receipt[field]
        result["items"].append(row)
        if "load_scans" in receipt:
            for field in ("load_scans", "full_scans", "phase", "slot", "revision"):
                result[field] = receipt[field]
        if receipt["status"] != "moved":
            result["status"] = "partial" if result["moved_count"] else "stopped"
            result["items"].extend({"item_id": pending, "status": "not_attempted", "actual_area": None}
                                   for pending in item_ids[position+1:])
            break
        result["moved_count"] += 1
        previous_command = command
    return result
