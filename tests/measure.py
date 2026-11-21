"""Run against a real stdio MCP. Keep the before/after receipts outside the repository."""
import argparse
import asyncio
import json
import sys
from pathlib import Path

from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client

ROOT = Path(__file__).resolve().parents[1]


async def measure(args):
    params = StdioServerParameters(command=sys.executable, args=["-B", str(ROOT / "ledger_mcp.py"),
        "--journal", str(args.journal), "--database", str(args.database)])
    async with stdio_client(params) as (reader, writer), ClientSession(reader, writer) as session:
        await session.initialize()

        async def query(**filters):
            response = await session.call_tool("query_inventory", filters)
            if response.isError:
                raise RuntimeError(response.content[0].text)
            result = json.loads(response.content[0].text)
            if result["status"] != "observed":
                raise RuntimeError("Waiting for a game load")
            if result["phase"] not in ("CLOSED", "MORNING"):
                raise RuntimeError("The loaded shop is open")
            return result

        if args.phase == "before":
            first = await query()
            owned = [r for r in first["items"] if r["owned"]]
            candidates = [r for r in owned if r["kind"] == "bottled_water"]
            sample = (candidates or owned or first["items"] or [dict(name="水", area="inventory")])[0]
            by_name = await query(name=sample["name"])
            by_area = await query(area=sample["area"], owned=True)
            assert by_name["items"] == [r for r in first["items"] if sample["name"] in r["name"]]
            assert by_area["items"] == [r for r in owned if r["area"] == sample["area"]]
            reads = [first, by_name, by_area]
            assert len({(r["generation"],r["revision"],r["full_scans"]) for r in reads}) == 1
            assert [r["load_scans"] for r in reads] == [1,1,1]
            assert [r["applied_records"] for r in reads[1:]] == [0,0]
            result = {"reads": [{k:r[k] for k in ("full_scans","load_scans","revision","applied_records")} for r in reads],
                      "baseline": first, "water_candidates": candidates}
            print(json.dumps({"slot": first["slot"], "phase": first["phase"], "reads": result["reads"],
                              "water_candidates": candidates}, ensure_ascii=False))
        else:
            before = json.loads((args.output / "before.json").read_text(encoding="utf-8"))["baseline"]
            after = await query()
            assert before["generation"] == after["generation"], "Save reloaded after measurement"
            assert after["revision"] > before["revision"], "No change observed"
            assert before["full_scans"] == after["full_scans"] and after["load_scans"] == 1
            old = {r["id"]:r for r in before["items"]}
            new = {r["id"]:r for r in after["items"]}
            assert new.keys() == old.keys(), "Item IDs changed"
            changes = [(old[i], new[i]) for i in old if old[i] != new[i]]
            assert len(changes) == 1, "Expected exactly one changed item"
            source, target = changes[0]
            assert source["owned"] and source["area"] != target["area"]
            assert {**source, "area": target["area"]} == target
            assert source["kind"] == "bottled_water", "Moved item does not match water filter"
            result = {"before":source, "after":target, "load_scans":after["load_scans"],
                      "full_scans":after["full_scans"], "revision":after["revision"]}
            print(json.dumps(result,ensure_ascii=False))
        (args.output / (args.phase + ".json")).write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding="utf-8")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("phase", choices=["before","after"])
    parser.add_argument("--journal", type=Path, required=True)
    parser.add_argument("--database", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    if any(p.resolve().is_relative_to(ROOT) for p in (args.output,args.database)):
        parser.error("Keep the reports and database outside this repository")
    args.output.mkdir(parents=True, exist_ok=True)
    asyncio.run(measure(args))
