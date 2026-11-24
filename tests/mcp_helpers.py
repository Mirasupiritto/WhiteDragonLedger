"""Shared MCP stdio client helpers and fake game state publisher for tests."""
import asyncio
from contextlib import asynccontextmanager
import json
from pathlib import Path
import sys

from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client


ROOT = Path(__file__).resolve().parents[1]
LEDGER_MCP = ROOT / "mcp" / "ledger_mcp.py"


def server_params(folder: Path, database: Path) -> StdioServerParameters:
    return StdioServerParameters(
        command=sys.executable,
        args=["-B", str(LEDGER_MCP), "--journal", str(folder), "--database", str(database)],
    )


@asynccontextmanager
async def open_mcp_session(folder: Path, database: Path, *, message_handler=None):
    params = server_params(folder, database)
    async with stdio_client(params) as streams, ClientSession(*streams, message_handler=message_handler) as client:
        await client.initialize()
        yield client


async def call_tool_json(client: ClientSession, name: str, arguments: dict | None = None) -> dict:
    result = await client.call_tool(name, arguments or {})
    return json.loads(result.content[0].text)


class ScriptedGame:
    """Minimal fake game publisher for MCP unit and integration tests."""

    def __init__(self, folder: Path, *, generation: str | None = None, slot: int = 4, session: str = "test"):
        self.folder = Path(folder)
        self.generation = generation or ("a" * 32)
        self.slot = slot
        self.session = session

    def write(self, name: str, data: dict) -> Path:
        path = self.folder / name
        tmp = self.folder / f"{name}.tmp"
        tmp.write_text(json.dumps(data), encoding="utf-8")
        tmp.replace(path)
        return path

    def publish_scene(self, status="ready", location="pawnshop", revision=1, **extra) -> dict:
        record = {
            "session": self.session,
            "revision": revision,
            "status": status,
            "location": location,
            "slot": self.slot,
            **extra,
        }
        self.write("scene.json", record)
        return record

    def publish_desk(self, revision=1, phase="OPEN", day=10, *, counter=None, event=None, **extra) -> dict:
        head = {
            "status": "observed",
            "generation": self.generation,
            "revision": revision,
            "slot": self.slot,
            "phase": phase,
            "day": day,
            "load_scans": 1,
            "desk_protocol": 1,
        }
        desk_counter = counter if counter is not None else {
            "stage": "idle",
            "cash": 100,
            "lines": [],
            "waiting": 0,
            "reception": {"can_ring": False},
        }
        view = {
            **head,
            "counter_protocol": 2,
            "counter": desk_counter,
            "customer": extra.pop("customer", None),
            "available_strips": extra.pop("available_strips", 0),
            **extra,
        }
        self.write("head.json", head)
        if event is not None:
            self.write(f"{self.generation}-{revision}.json", {**event, "phase": phase, "day": day})
        self.write(f"desk-{self.generation}.json", view)
        return view

    async def wait_inbox(self, channel: str, *, session_id: str | None = None, timeout: float = 5.0) -> dict:
        ident = session_id or self.generation
        inbox = self.folder / f"{channel}-{ident}.json"
        async with asyncio.timeout(timeout):
            while not inbox.exists():
                await asyncio.sleep(0.02)
        return json.loads(inbox.read_text(encoding="utf-8"))

    def write_receipt(self, command_id: str, receipt: dict, *, unlink_inbox: Path | None = None):
        self.write(f"receipt-{command_id}.json", receipt)
        if unlink_inbox is not None:
            unlink_inbox.unlink(missing_ok=True)
