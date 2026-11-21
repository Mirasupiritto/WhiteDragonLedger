import argparse
import os
from pathlib import Path
from typing import Literal

from mcp.server.fastmcp import FastMCP
from ledger import lookup
from moves import move_item as submit_move, move_items as submit_batch


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--journal", type=Path, default=Path.home() /
        "AppData/LocalLow/Questing Goose Studio/Probably Stolen/ModData/white_dragon_ledger")
    parser.add_argument("--database", type=Path)
    args = parser.parse_args()
    database = args.database or Path(os.environ["LOCALAPPDATA"]) / "WhiteDragonLedger" / "account.sqlite3"
    server = FastMCP("白龍店帳")

    @server.tool()
    def query_inventory(name: str | None = None,
                        area: Literal["inventory", "showcase", "front", "back"] | None = None,
                        owned: bool | None = None) -> dict:
        """Query the most recently published inventory by literal name substring, area or owner.
        The game measures load_scans for the current load and full_scans for this observer instance.
        A query reads local files and SQLite. IDs are scoped to generation."""
        return lookup(args.journal, database, name, area, owned)

    @server.tool()
    def move_item(item_id: int, source: Literal["inventory", "showcase"],
                  destination: Literal["inventory", "showcase"], generation: str,
                  revision: int, command_id: str | None = None) -> dict:
        """Move one owned item while the shutter is closed. Use the generation and revision from
        query_inventory. A moved receipt gives the confirmed location and game-side scan counts.
        Reuse the same command_id and original arguments when checking an uncertain response."""
        return submit_move(args.journal, item_id, source, destination, generation, revision, command_id)

    @server.tool()
    def move_items(item_ids: list[int], source: Literal["inventory", "showcase"],
                   destination: Literal["inventory", "showcase"], generation: str, revision: int) -> dict:
        """Move an ordered list of owned items with the shutter closed. Pass the generation and
        starting revision from query_inventory. Returns one receipt with each observed destination
        and game scan counters. Stops at the first unconfirmed or rejected item. The original
        arguments recover completed lines and can continue pending lines."""
        return submit_batch(args.journal, item_ids, source, destination, generation, revision)

    server.run()


if __name__ == "__main__":
    main()
