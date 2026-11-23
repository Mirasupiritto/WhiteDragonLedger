"""One local mailbox per load. Only the Unity game thread may confirm a transfer."""
import json
import re
from pathlib import Path
from uuid import NAMESPACE_URL, uuid4, uuid5

from command_transport import exchange
from receipt_store import replayed, read_command_receipt
from scene import interrupted_shop_call
from ledger import read_head


def move_item(folder: Path, item_id, source, destination, generation, revision, command_id=None, *, previous_command=None, machine_id=None, visit=None):
    changed = interrupted_shop_call(folder)
    if changed:
        return changed
    command = command_id or uuid4().hex
    if not re.fullmatch("[a-f0-9]{32}", generation) or not re.fullmatch("[a-f0-9]{32}", command):
        raise ValueError("generation and command_id must be lowercase 32-digit hex values")
    counter_move = source in {"front", "back"} or destination in {"front", "back"}
    collect = source in {"front", "back"} and destination == "inventory"
    idle_clear = (collect or (source, destination) == ("back", "showcase")) and visit is None
    if counter_move and not idle_clear:
        other = source if destination == "back" else destination
        if ((not collect and ("back" not in (source, destination) or other not in {"front","inventory","showcase"})) or machine_id is not None
                or not re.fullmatch("[a-f0-9]{32}", visit or "")):
            raise ValueError("Use the current visit and a route to or from the trade table (back)")
    elif idle_clear:
        if machine_id is not None:
            raise ValueError("Use the current visit and a route to or from the trade table (back)")
    elif visit is not None:
        raise ValueError("A visit is only used for trade table moves")
    elif machine_id is not None:
        if machine_id < 1 or (source, destination) not in {
            ("purifier", "inventory"), ("inventory", "purifier"), ("showcase", "purifier")
        }:
            raise ValueError("Expected a stock area and a purifier slot")
    elif {source, destination} != {"inventory", "showcase"}:
        raise ValueError("Expected two different stock areas")
    if item_id < 1 or revision < 1:
        raise ValueError("Expected a positive item ID, revision and two different stock areas")
    request = dict(command_id=command, item_id=item_id, source=source, destination=destination,
                   generation=generation, revision=revision)
    if counter_move and not idle_clear:
        request["visit"] = visit
    if machine_id is not None:
        request["machine_id"] = machine_id

    def read_receipt():
        result = read_command_receipt(folder, command, generation, "move")
        if result is None or result.get("reason") == "receipt_corrupt":
            return result
        if (result["expected_revision"], result["item_id"], result["from"],
            result["requested_area"], result.get("machine_id"), result.get("visit")) != (
                revision, item_id, source, destination, machine_id, visit):
            raise ValueError("command_id already used for a different move")
        return result

    previous = read_receipt()
    if previous is not None:
        return replayed(previous)
    head = read_head(folder)
    verify_location = (
        head.get("move_location_protocol") == 1
        and revision < head["revision"]
        and (
            {source, destination} == {"inventory", "showcase"}
            or (destination in {"inventory", "showcase"} and source in {"front", "back"})
            or (destination == "back" and source in {"inventory", "showcase", "front"} and visit is not None)
        )
    )
    if head.get("generation") != generation or (head.get("revision") != revision and not verify_location):
        return {"status": "stale_observation", "command_id": command}
    if destination == "purifier" and head.get("purifier_put_protocol") != 1:
        return {"status": "reload_updated_mod", "command_id": command}
    if head.get("move_protocol") != 1 or (machine_id is not None and head.get("purifier_take_protocol") != 1):
        return {"status": "reload_updated_mod", "command_id": command}
    if counter_move and head.get("counter_move_protocol") != 1:
        return {"status": "reload_updated_mod", "command_id": command}
    if counter_move and not idle_clear and not (verify_location and destination in {"inventory", "showcase"}) and head["phase"] != "OPEN":
        return {"status":"shop_closed", "command_id":command}
    if not counter_move and not verify_location and destination != "purifier" and head["phase"] not in ("CLOSED", "MORNING"):
        return {"status": "shop_open", "command_id": command}
    inbox = folder / f"move-{generation}.json"
    return exchange(folder, inbox, request, read_receipt, previous_command)


def move_items(folder: Path, item_ids, source, destination, generation, revision, visit=None):
    """Execute one ordered plan. An identical plan reuses its native receipts."""
    if source in {"front", "back"} and destination != "inventory":
        raise ValueError("A batch from the counter can only go to inventory")
    if not item_ids or any(item_id < 1 for item_id in item_ids):
        raise ValueError("Expected a nonempty list of positive item IDs")
    if len(set(item_ids)) != len(item_ids):
        raise ValueError("Each item must appear only once in a batch")

    identity = [generation, revision, source, destination, item_ids]
    if visit is not None:
        identity.append(visit)
    plan = json.dumps(identity, separators=(",", ":"))
    batch_id = uuid5(NAMESPACE_URL, "white-dragon-ledger/move-items/" + plan)
    result = dict(batch_id=batch_id.hex, generation=generation, expected_revision=revision,
                  revision=revision, source=source, destination=destination,
                  status="moved", moved_count=0, items=[])
    next_revision = revision
    previous_command = None
    for position, item_id in enumerate(item_ids):
        command = uuid5(batch_id, str(item_id)).hex
        receipt = move_item(folder, item_id, source, destination, generation, next_revision, command,
                            previous_command=previous_command, visit=visit)
        row = {"item_id": item_id, "status": receipt["status"], "from": source,
               "actual_area": receipt.get("actual_area")}
        for field in ("name", "kind", "quantity", "observed_quantity", "bottle_label", "revision",
                      "reason", "item_size", "target_capacity", "identity_changed"):
            if field in receipt:
                row[field] = receipt[field]
        if "identity_changed" in receipt:
            result.setdefault("identity_changed", []).extend(receipt["identity_changed"])
        result["items"].append(row)
        if "load_scans" in receipt:
            for field in ("load_scans", "full_scans", "phase", "slot", "revision"):
                result[field] = receipt[field]
        if receipt["status"] == "already_in_destination":
            # A no-op may confirm a newer location, but cannot authorize the next move at that revision.
            result["already_count"] = result.get("already_count", 0) + 1
            previous_command = command
            continue
        if receipt["status"] != "moved":
            result["status"] = "partial" if result["moved_count"] or receipt["status"] == "partial" else "stopped"
            result["items"].extend({"item_id": pending, "status": "not_attempted", "actual_area": None}
                                   for pending in item_ids[position+1:])
            break
        next_revision = result["revision"]
        result["moved_count"] += 1
        previous_command = command
    if result["status"] == "moved" and result["moved_count"] == 0 and result.get("already_count"):
        result["status"] = "already_in_destination"
    return result
