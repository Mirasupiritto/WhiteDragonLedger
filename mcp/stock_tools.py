"""Stock query, inventory movement, sorting, purchase lot, and trade ticket tools."""
from typing import Literal

from ledger import lookup, same_observation
from moves import move_item as submit_move, move_items as submit_batch
import counter
import shop_day
import shopdesk
import trade_journal


def register(server, folder, database, *, section_cache=None):
    state = getattr(server, "state", None)
    cache = section_cache if section_cache is not None else (
        state.section_cache if state is not None else (getattr(server, "section_cache", None) or shop_day.SectionCache())
    )

    @server.tool()
    def get_shop_ledger(slot: int, ticket_id: str | None = None, inventory: bool = False,
                        name: str | None = None, area: str | None = None, owned: bool | None = None) -> dict:
        """Read unfinished trade tickets for this save slot. Pass a ticket ID for its original tool,
        arguments and latest receipt. inventory=true or name, area, owned filters read the last cached stock
        for this slot, including when the game is closed."""
        return trade_journal.ledger(folder, database, slot, ticket_id, inventory=inventory, name=name, area=area, owned=owned)

    @server.tool()
    def dismiss_trade_ticket(slot: int, ticket_id: str, version: int) -> dict:
        """Archive the reviewed ticket locally using its last observed version. Already submitted
        game actions may finish independently; this tool never places, cancels or repeats a trade."""
        return trade_journal.dismiss(folder, database, slot, ticket_id, version)

    @server.tool()
    def query_inventory(name: str | None = None,
                        area: Literal["inventory", "showcase", "front", "back", "hidden"] | None = None,
                        owned: bool | None = None,
                        verbose: bool = False) -> dict:
        """Query the most recently published inventory by literal name substring, area or owner.
        Items include native categories (the same as find_stock), size{w,h}, slot{x,y}, rotatable, estimated_value (reference estimate, before haggling/markup),
        actual water_ml, liquid_main and water-majority contains_water on water containers, and charge_remaining/liters_remaining on portable_water_purifier.
        Includes area capacity{w,h,free_cells,largest_free{w,h}} for inventory/showcase. IDs are scoped to generation.
        Unchanged calendar/trash sections are omitted after the first read unless verbose=True."""
        account = lookup(folder, database, name, area, owned)
        if account["status"] == "observed":
            status = shop_day.shop_status(folder)
            if status["status"] == "observed":
                if not same_observation(account, status):
                    return {"status": "observation_changed"}
                account.update(calendar=status["calendar"], trash=shop_day.trash_contents(folder, database, status))
                account = cache.filter(account.get("generation"), account, verbose=verbose)
            elif status["status"] in {"load_changed", "waiting_for_load"}:
                return {"status": "observation_changed"}
        return shopdesk.strip_protocols(account)

    @server.tool()
    def list_stock_tags(search: str | None = None, in_stock_only: bool = False) -> dict:
        """List native merchandise categories (code, localized label, in-stock flag) and observed bottle labels.
        Search by partial localized name, alias or code. Stock includes the four shop areas and tracked purifier slots."""
        return lookup(folder, database, stock_query=dict(list_tags=True, search=search, in_stock_only=in_stock_only))

    @server.tool()
    def find_stock(bottle_label: str | None = None, categories: list[str] | None = None,
                   exclude_categories: list[str] | None = None, owned: bool | None = None,
                   area: Literal["inventory", "showcase", "front", "back", "purifier", "hidden"] | None = None,
                   exclude_contraband: bool = False, kinds: list[str] | None = None, exclude_kinds: list[str] | None = None) -> dict:
        """Pick stock by exact visible label, area, ownership and native item categories.
        Match any requested category or kind, then apply all exclusions. Searching category WATER also matches empty water containers categorized as HOUSEHOLD_GOOD and filled water containers with at least 50% water.
        Each item includes size{w,h}, slot{x,y}, rotatable, estimated_value (reference estimate), and purifier charge_remaining/liters_remaining when applicable.
        Visible variety restrictions group sellable matches by kind, one per kind for this visit.
        Bag contents include parent_id, outer-to-inner container_path and access (unpack_first/container_locked). Unpack before sale.
        Includes purifier slots. Apply exclude_contraband when the visible tablet shows that restriction."""
        return shopdesk.allocation_stock(folder, database, dict(bottle_label=bottle_label, categories=categories,
            exclude_categories=exclude_categories, owned=owned, area=area, exclude_contraband=exclude_contraband, kinds=kinds, exclude_kinds=exclude_kinds))

    @server.tool()
    def get_stock_items(item_ids: list[int], generation: str) -> dict:
        """Read this load's stock IDs in request order, with current locations, size/slot/rotatable, labels, water/purifier details, estimated_value and missing IDs."""
        return lookup(folder, database, stock_query=dict(item_ids=item_ids, generation=generation))

    @server.tool()
    def move_item(item_id: int, source: str,
                  destination: str, generation: str,
                  revision: int, command_id: str | None = None, visit: str | None = None) -> dict:
        """Pass item_id, source, destination, generation and revision. Valid areas: "inventory", "showcase", "front", "back", "hidden", "trash", or "bay:<owner_id>" (an owned smuggler bay container). While closed or in the morning, move owned stock between inventory, showcase, smuggler bays, hidden (poster pulled), and trash (trash->inventory also supported before overnight clear; rejects IMPORTANT_TAG and hazardous waste). During trade pass the observed visit
        to stage items on the trade table (back) from inventory/showcase/bay:<owner_id>, or return them (multiple owned items may be staged together
        during regular selling; guest goods require an empty table). Guest goods return to front and
        owned goods to inventory/showcase/bay:<owner_id>. Owned goods left on back when no customer is present can also be
        cleared to inventory/showcase with visit omitted. Placement alone does not confirm a trade. Reuse the command ID to retry."""
        return submit_move(folder, item_id, source, destination, generation, revision, command_id, visit=visit)

    @server.tool()
    def move_items(item_ids: list[int], source: str,
                   destination: str, generation: str, revision: int, visit: str | None = None) -> dict:
        """Move owned stock between visible inventory, showcase, smuggler bays ("bay:<owner_id>"), hidden (morning/closed or open without customer, poster pulled), and trash (destination="trash", or source="trash"->inventory; rejects IMPORTANT_TAG and hazardous waste), including while open, stage owned goods onto the trade table (back) with visit (including dialogue choice_required/counter_request stages), or collect owned front/back items to inventory.
        Valid source areas: "inventory", "showcase", "front", "back", "hidden", "trash", or "bay:<owner_id>". Valid destination areas: "inventory", "showcase", "back", "hidden", "trash", or "bay:<owner_id>".
        Pass the current visit during any arrived customer visit without an active order (including ready, barter, seller-only or dialogue), or omit it at an empty counter (destination="back" always requires visit). Pass the generation and
        starting revision from query_inventory. When space is insufficient, returns no_space or showcase_full with item_size and target_capacity and skips that item into skipped_ids.
        Stops at the first unconfirmed or rejected item. The original arguments recover completed lines and can continue pending lines."""
        return submit_batch(folder, item_ids, source, destination, generation, revision, visit=visit)

    @server.tool()
    def sort_inventory(generation: str, revision: int) -> dict:
        """Compact the main inventory once using native big-first sorting. Compare before/after free_cells and largest_free.
        Returns external_sorter_active when the player uses a recognized Workshop sorter in auto mode."""
        return counter.command(folder, "sort_inventory", generation, revision)

    @server.tool()
    def toggle_poster(mode: Literal["up", "down"], generation: str, revision: int) -> dict:
        """Pull the store poster up or down to access or conceal the 2x2 hidden grid behind it.
        Refuses with reason="customer_present" when the shop is open and a customer is present."""
        return counter.command(folder, "toggle_poster", generation, revision, mode=mode)

    @server.tool()
    def query_purchase_lots(supplier: str | None = None, product: str | None = None, lot_id: str | None = None) -> dict:
        """Look up wholesale lots by seller, product or exact lot ID. Report paid, recovered and stock.
        Gross profit is shown only for fully sold lots with a confirmed payment."""
        return shopdesk.purchase_lots(folder, database, supplier, product, lot_id)
