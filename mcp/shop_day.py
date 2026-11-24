"""Shop calendar, nightly services and the current contents of the trash bin."""
import json
import time

import counter
from command_transport import poll
from database import transaction
from ledger import read_head
from scene import read_scene
from server_state import SectionCache
from shopdesk import published


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
    state = getattr(server, "state", None)
    cache = section_cache if section_cache is not None else (state.section_cache if state is not None else getattr(server, "section_cache", None))
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
        last_view = {}

        def check_morning():
            nonlocal result, last_view
            scene = read_scene(folder)
            if scene.get("status") == "game_over":
                return {**result, **scene, "tool_status": "game_over"}
            view = shop_status(folder)
            last_view = view
            if view.get("generation") == generation and view.get("day") == day + 1 and view.get("alerts"):
                due_today = {alert["due_day"] for alert in view["alerts"] if alert.get("type") == "blackmail_due_today"}
                before = [alert for alert in result.get("alerts", []) if not
                          (alert.get("type") == "blackmail_due_tomorrow" and alert.get("due_day") in due_today)]
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
            return None

        slept, timed_out = poll(check_morning, timeout=15, interval=0.15, sleep=time.sleep)
        if not timed_out:
            return slept
        report = last_view.get("night_report", {})
        if (last_view.get("slot") == result["slot"] and last_view.get("generation") == generation
                and last_view.get("day") == day + 1 and report.get("status") == "awaiting_report"):
            return {**result, "status": "awaiting_report", "day_after": last_view["day"], "night_report": report}
        return result
