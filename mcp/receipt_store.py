"""Read a game receipt without creating, deleting or resubmitting a command."""
import json
import time


def read_optional_json(path):
    try:
        return json.loads(path.read_text(encoding="utf-8-sig"))
    except (FileNotFoundError, PermissionError, UnicodeDecodeError, json.JSONDecodeError):
        return None


def replayed(receipt):
    return {**receipt, "replayed": True}


def read_command_receipt(folder, command_id, generation, channel, *, legacy_ticket=False):
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
