"""Shared per-server state: section cache, incremental update cursor, and bounded heat tracking."""
import copy
import threading


class SectionCache:
    """Remember calendar/trash/sleep snapshots per generation to suppress unchanged repeats."""

    def __init__(self):
        self._seen = {}

    def filter(self, generation, payload, *, verbose=False):
        if not generation or payload.get("status") != "observed":
            return payload
        state = self._seen.setdefault(generation, {})
        if len(self._seen) > 4:
            for old in [k for k in self._seen if k != generation]:
                del self._seen[old]
        out = dict(payload)
        unchanged = []
        for key in ("calendar", "trash", "sleep", "security", "last_save", "health"):
            if key not in out:
                continue
            val = out[key]
            if verbose or (key == "health" and out.get("alerts")) or key not in state or state[key] != val:
                state[key] = copy.deepcopy(val)
            else:
                del out[key]
                unchanged.append(key)
        if unchanged and not verbose:
            out["unchanged_sections"] = unchanged
        return out


class ServerState:
    """Per-connection server state with generation-scoped heat deduplication."""

    def __init__(self):
        self.lock = threading.RLock()
        self.section_cache = SectionCache()
        self.updates = None
        self.last_emitted_items = {}
        self._heat_generation = None
        self._heat_seen = set()

    def is_finished_heat_seen(self, folder, generation, heat):
        if not isinstance(heat, dict) or heat.get("status") == "running":
            return False
        if generation != self._heat_generation:
            self._heat_seen.clear()
            self._heat_generation = generation
        key = (str(folder), generation, heat.get("run_id"), heat.get("status"))
        if key in self._heat_seen:
            return True
        self._heat_seen.add(key)
        return False

    def filter_finished_heat(self, folder, result):
        if not isinstance(result, dict):
            return result
        heat = result.get("heat")
        if self.is_finished_heat_seen(folder, result.get("generation"), heat):
            filtered = dict(result)
            filtered.pop("heat", None)
            return filtered
        return result
