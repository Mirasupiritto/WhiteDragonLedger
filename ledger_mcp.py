import argparse
import os
from pathlib import Path
from typing import Literal

from mcp.server.fastmcp import FastMCP
from ledger import lookup


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

    server.run()


if __name__ == "__main__":
    main()
