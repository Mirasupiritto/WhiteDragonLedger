"""Shared per-server state: section cache, incremental update cursor, file cache, and heat tracking."""
import copy
import json
from pathlib import Path
import threading


class FileSnapshotCache:
    """Process-level JSON file cache keyed by (resolved_path, st_mtime_ns, st_size)."""

    def __init__(self, max_entries=64):
        self._lock = threading.Lock()
        self._entries = {}
        self._max_entries = max_entries

    def read_json(self, path: Path):
        resolved = path.resolve()
        stat = path.stat()
        stamp = (stat.st_mtime_ns, stat.st_size)
        with self._lock:
            cached = self._entries.get(resolved)
            if cached is not None and cached[0] == stamp:
                return copy.deepcopy(cached[1])
        data = json.loads(path.read_text(encoding="utf-8-sig"))
        with self._lock:
            if len(self._entries) >= self._max_entries and resolved not in self._entries:
                self._entries.pop(next(iter(self._entries)))
            self._entries[resolved] = (stamp, data)
        return copy.deepcopy(data)

    def clear(self):
        with self._lock:
            self._entries.clear()


snapshot_cache = FileSnapshotCache()


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
