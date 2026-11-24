"""Shop calendar, nightly services and the current contents of the trash bin."""
import copy
import json
import time

import counter
from database import transaction
from ledger import read_head
from shopdesk import published
from scene import read_scene


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


def shop_status(folder):
    view = published(folder)
    if view["status"] != "observed":
        return view
    if "day_info" not in view:
        return {"status":"reload_updated_mod"}
    return {**{k:view[k] for k in ("status", "slot", "generation", "revision", "phase", "day")},
            "cash":view["counter"]["cash"], **view["day_info"]}


def trash_contents(folder, database, view):
    summary = view["trash"]
    if summary["status"] != "observed":
        return summary
    generation, version, day = view["generation"], summary["version"], view["day"]
    with transaction(database) as db:
        db.execute("CREATE TABLE IF NOT EXISTS trash_checkpoint (slot INTEGER PRIMARY KEY,generation TEXT,version INTEGER)")
        db.execute("CREATE TABLE IF NOT EXISTS trash_items (slot INTEGER,id INTEGER,name TEXT,kind TEXT,quantity INTEGER,PRIMARY KEY(slot,id))")
        slot = view["slot"]
        prior = db.execute("SELECT generation,version FROM trash_checkpoint WHERE slot=?", (slot,)).fetchone()
        if prior is None or (prior["generation"], prior["version"]) != (generation,version):
            head = read_head(folder)
            if (head.get("generation"),head.get("slot")) != (generation,slot):
                return {"status":"load_changed"}
            if prior and prior["generation"]==generation and prior["version"]>version:
                return {"status":"trash_changed"}
            data = json.loads((folder/f"trash-{generation}-{version}.json").read_text(encoding="utf-8-sig"))
            if (data["generation"],data["version"],data["day"]) != (generation,version,day):
                raise ValueError("Trash snapshot differs from desk receipt")
            db.execute("DELETE FROM trash_items WHERE slot=?", (slot,))
            db.executemany("INSERT INTO trash_items VALUES(?,?,?,?,?)",
                           ((slot,r["id"],r["name"],r["kind"],r["quantity"]) for r in data["items"]))
            db.execute("INSERT OR REPLACE INTO trash_checkpoint VALUES(?,?,?)",(slot,generation,version))
        rows = db.execute("SELECT id,name,kind,quantity FROM trash_items WHERE slot=? ORDER BY id",(slot,))
        return {**summary,"items":[dict(row) for row in rows]}


def register_day_tools(server, folder, database, section_cache=None):
    cache = section_cache if section_cache is not None else getattr(server, "section_cache", None)
    if cache is None:
        cache = SectionCache()
    server.section_cache = cache

    @server.tool()
    def get_shop_status(verbose: bool = False) -> dict:
        """Current cash, calendar events, rent, trash contents, night report, shop appeal, status summary (including injury), nuclear_waste counts and sleep costs inside the pawnshop.
        Includes health, addiction penalties, sentinel deadlines, weekly obligations and active alerts. Includes security evidence, contraband counts and last_save. verbose=True includes the wanted poster roster and base bounties.
        Unchanged calendar/trash/sleep/security/save sections are omitted after the first read on this connection unless verbose=True."""
        view = shop_status(folder)
        if view["status"] == "observed":
            view = dict(view)
            if not verbose:
                view.pop("wanted", None)
            view["trash"] = trash_contents(folder, database, view)
            view = cache.filter(view.get("generation"), view, verbose=verbose)
        return view

    @server.tool()
    def get_trash() -> dict:
        """Read the current bin from its own SQLite table. No items are discarded."""
        view = shop_status(folder)
        if view["status"] != "observed":
            return view
        return {**{k:view[k] for k in ("slot","generation","day")}, **trash_contents(folder, database, view)}

    @server.tool()
    def prepare_sleep(generation: str, revision: int, day: int, bill_token: str) -> dict:
        """Open the night bill from get_shop_status.sleep.bill and list services. Includes active health and payment alerts before sleep. Leaves the day running."""
        return counter.command(folder,"prepare_sleep",generation,revision,day=day,bill_token=bill_token)

    @server.tool()
    def night_service(generation: str, revision: int, day: int, bill_token: str, service_id: str,
                      cost: int, selected: bool) -> dict:
        """Set one observed night's service to the desired state at its displayed price. Returns the updated bill."""
        return counter.command(folder,"night_service",generation,revision,day=day,bill_token=bill_token,
                               service_id=service_id,cost=cost,selected=selected)

    @server.tool()
    def sleep(generation: str, revision: int, day: int, service_cost: int, bill_token: str | None = None) -> dict:
        """Confirm sleep with the day and exact nightly cost. Returns pre-sleep and newly observed morning alerts. Pass the reviewed bill_token to bind its service selection."""
        if day < 1 or service_cost < 0:
            raise ValueError("Use day and service_cost from get_shop_status")
        args = {"day": day, "service_cost": service_cost}
        if bill_token is not None:
            args["bill_token"] = bill_token
        result = counter.command(folder, "sleep", generation, revision, **args)
        if result["status"] != "sleep_submitted":
            return result
        deadline = time.monotonic() + 15
        for _ in range(200):
            scene = read_scene(folder)
            if scene.get("status") == "game_over":
                return {**result, **scene, "tool_status":"game_over"}
            view = shop_status(folder)
            if view.get("generation")==generation and view.get("day")==day+1 and view.get("alerts"):
                before = result.get("alerts", [])
                result = {**result, "alerts": before + [alert for alert in view["alerts"] if alert not in before]}
            report = view.get("night_report", {})
            if (view.get("slot") == result["slot"] and view.get("generation") == generation
                    and view.get("day") == day + 1 and view.get("phase") == "MORNING"
                    and report.get("day") == day + 1 and report.get("status") == "ready"):
                slept = {**result, "status": "slept", "day_after": view["day"], "calendar": view["calendar"],
                         "trash": trash_contents(folder, database, view)}
                for key in ("night_report", "appeal", "status_summary"):
                    if key in view:
                        slept[key] = view[key]
                return slept
            if view.get("generation") and view["generation"] != generation:
                return result
            if time.monotonic() >= deadline:
                break
            time.sleep(.15)
        if (view.get("slot") == result["slot"] and view.get("generation") == generation
                and view.get("day") == day + 1 and report.get("status") == "awaiting_report"):
            return {**result, "status": "awaiting_report", "day_after": view["day"], "night_report": report}
        return result
