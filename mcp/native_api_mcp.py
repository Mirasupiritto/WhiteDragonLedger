"""Read-only developer MCP for the installed Probably Stolen assembly."""

import argparse
from collections import defaultdict
from hashlib import sha256
import json
from pathlib import Path
from typing import Annotated, Any, Literal

from mcp.server.fastmcp import FastMCP
from pydantic import Field


Kind = Literal["type", "method", "field", "property"]
Limit = Annotated[int, Field(ge=1, le=50)]
Offset = Annotated[int, Field(ge=0)]


def page(items, limit, offset):
    next_offset = offset + limit
    return {
        "items": items[offset:next_offset],
        "total": len(items),
        "next_offset": next_offset if next_offset < len(items) else None,
    }


class NativeIndex:
    def __init__(self, file):
        with file.open(encoding="utf-8") as stream:
            data = json.load(stream)
        if data.get("schema") != 1:
            raise ValueError("Unsupported native API index schema. Rebuild the index.")

        self.source = data["source"]
        installed = Path(self.source["path"])
        with installed.open("rb") as stream:
            if sha256(stream.read()).hexdigest() != self.source["sha256"]:
                raise ValueError("Assembly-CSharp.dll changed. Rebuild the native API index.")

        self.symbols = {row["id"]: row for row in data["symbols"]}
        self.members = defaultdict(list)
        self.incoming = defaultdict(list)
        self.outgoing = defaultdict(list)
        self.search_rows = []
        for symbol in self.symbols.values():
            owner = symbol.get("owner")
            if owner is not None:
                self.members[owner].append(symbol)
            name = symbol["name"].casefold()
            qualified = f"{owner}.{name}".casefold() if owner else symbol["id"].casefold()
            signature = symbol["id"].casefold().replace("::", ".")
            self.search_rows.append((name, qualified, signature, symbol))

        for edge in data["references"]:
            self.outgoing[edge["source"]].append(edge)
            target = edge["target"]
            if edge["assembly"] == self.source["name"] and target in self.symbols:
                self.incoming[target].append(edge)

        self.reference_count = len(data["references"])

    def search(self, query, kind, limit, offset):
        normalized = query.casefold().replace("::", ".").strip()
        terms = normalized.split()
        if not terms:
            raise ValueError("Enter a type name, member name, or part of a signature.")

        matches = []
        for name, qualified, signature, symbol in self.search_rows:
            if kind is not None and symbol["kind"] != kind:
                continue
            if all(term in signature for term in terms):
                rank = (
                    normalized not in (name, qualified, signature),
                    not (name.startswith(normalized) or qualified.startswith(normalized)),
                    len(qualified),
                    signature,
                )
                matches.append((rank, symbol))
        matches.sort(key=lambda pair: pair[0])
        return page([symbol for _, symbol in matches], limit, offset)

    def get(self, symbol_id, kind, limit, offset):
        symbol = self.symbols.get(symbol_id)
        if symbol is None:
            raise ValueError("Unknown symbol ID. Use the exact id returned by search_native_api.")

        result = {"symbol": symbol}
        if symbol["kind"] == "type":
            members = self.members[symbol_id]
            if kind is not None:
                members = [member for member in members if member["kind"] == kind]
            members = sorted(members, key=lambda member: (member["name"], member["id"]))
            result["members"] = page(members, limit, offset)
        return result

    def references(self, symbol_id, direction, limit, offset):
        symbol = self.symbols.get(symbol_id)
        if symbol is None or symbol["kind"] not in ("method", "field"):
            raise ValueError("Use the exact id of a method or field returned by search_native_api.")
        if direction == "outgoing" and symbol["kind"] == "field":
            raise ValueError("Field references are incoming. Use direction='incoming'.")
        edges = self.incoming[symbol_id] if direction == "incoming" else self.outgoing[symbol_id]
        return page(edges, limit, offset)


def create_server(index):
    server = FastMCP(
        "white-dragon-native-api",
        instructions=(
            "Read-only developer index of the installed Probably Stolen Assembly-CSharp. "
            "Search by native type or member, then use its exact id for details or IL references. "
            "Use this before designing a custom bridge action."
        ),
    )

    @server.tool()
    def native_api_info() -> dict[str, Any]:
        """Show the indexed assembly identity, Steam build, and index size."""
        return {
            **{key: value for key, value in index.source.items() if key != "path"},
            "symbols": len(index.symbols),
            "references": index.reference_count,
        }

    @server.tool()
    def search_native_api(
        query: str, kind: Kind | None = None, limit: Limit = 20, offset: Offset = 0
    ) -> dict[str, Any]:
        """Find types, methods, fields, and properties by case-insensitive keywords or signature."""
        return index.search(query, kind, limit, offset)

    @server.tool()
    def get_native_symbol(
        symbol_id: str, kind: Kind | None = None, limit: Limit = 20, offset: Offset = 0
    ) -> dict[str, Any]:
        """Get a symbol by exact id. For a type, also list its declared members, filtered by kind."""
        return index.get(symbol_id, kind, limit, offset)

    @server.tool()
    def find_native_references(
        symbol_id: str, direction: Literal["incoming", "outgoing"] = "incoming",
        limit: Limit = 20, offset: Offset = 0
    ) -> dict[str, Any]:
        """Find direct IL calls, constructors, method pointers, and field reads/writes/addresses.

        Incoming lists game methods referencing this method or field. Outgoing lists references
        in this method, including dependency signatures. Use a property's getter or setter id.
        """
        return index.references(symbol_id, direction, limit, offset)

    return server


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--index", type=Path, required=True)
    args = parser.parse_args()
    try:
        index = NativeIndex(args.index)
    except (OSError, ValueError) as error:
        parser.exit(1, f"Native API index: {error}\n")
    create_server(index).run(transport="stdio")


if __name__ == "__main__":
    main()
