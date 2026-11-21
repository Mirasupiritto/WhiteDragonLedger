import argparse
import os
from pathlib import Path
from typing import Literal

from mcp.server.fastmcp import FastMCP
from pydantic import BaseModel, Field
import shopdesk
import counter
from ledger import lookup
from moves import move_item as submit_move, move_items as submit_batch


class SaleLine(BaseModel):
    item_id: int = Field(gt=0)
    min_price: int = Field(ge=0)


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

    @server.tool()
    def query_purchase_lots(supplier: str | None = None, product: str | None = None, lot_id: str | None = None) -> dict:
        """Look up wholesale lots by seller, product or exact lot ID. Report paid, recovered and stock.
        Gross profit is shown only for fully sold lots with a confirmed payment."""
        return shopdesk.purchase_lots(args.journal, database, supplier, product, lot_id)

    @server.tool()
    def inspect_purifiers() -> dict:
        """Compare last night's and current bottle IDs and read battery, extraction readiness and the shared count of unused test strips."""
        return shopdesk.machines(args.journal)

    @server.tool()
    def take_purifier_bottle(machine_id: int, bottle_id: int, generation: str, revision: int,
                             command_id: str | None = None) -> dict:
        """While closed, retrieve the observed bottle from its purifier into inventory.
        Use IDs and revision from inspect_purifiers. A moved receipt confirms the actual
        destination, empty source slot and unused strip count. Does not test the water."""
        return submit_move(args.journal, bottle_id, "purifier", "inventory", generation, revision,
                           command_id, machine_id=machine_id)

    @server.tool()
    def get_buyer_context() -> dict:
        """Read the current customer's visit token, remaining budget and revision before submitting an order."""
        return shopdesk.buyer(args.journal)

    @server.tool()
    def sell_order(lines: list[SaleLine], generation: str, revision: int, visit: str,
                   stop_same_kind_on_budget: bool = False) -> dict:
        """Sell only the listed owned items, each with its own minimum. Opt in to skipping the same
        native item kind after a budget rejection has been returned. Other kinds stay in order.
        Reuse identical arguments to recover the receipt of a submitted order."""
        return shopdesk.sell_order(args.journal, [line.model_dump() for line in lines],
                                  generation, revision, visit, stop_same_kind_on_budget)

    @server.tool()
    def get_counter() -> dict:
        """Read shop phase, waiting count, opening lines, and the merchant's visible batch and total."""
        return counter.context(args.journal)

    @server.tool()
    def ring_customer(generation: str, revision: int, waiting: int) -> dict:
        """Ring once at an empty open counter. Wait for the greeting, stopping at choices."""
        return counter.ring(args.journal, generation, revision, waiting)

    @server.tool()
    def wait_for_opening(generation: str, visit: str) -> dict:
        """Continue waiting for this visit, turning finished ordinary pages and opening the trade tablet."""
        return counter.wait_opening(args.journal, generation, visit)

    @server.tool()
    def buy_merchant_batch(generation: str, revision: int, visit: str, offer_token: str, max_total: int) -> dict:
        """Buy the observed entire offer at or below max_total. Reuse identical inputs to recover a receipt."""
        if max_total < 0:
            raise ValueError("Expected a non-negative batch price limit")
        return counter.command(args.journal, "buy", generation, revision, visit=visit,
                               offer_token=offer_token, max_total=max_total)

    server.run()


if __name__ == "__main__":
    main()
