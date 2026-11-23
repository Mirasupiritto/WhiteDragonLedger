"""MCP tools available inside the pawnshop."""
from typing import Literal
from uuid import uuid4

from pydantic import BaseModel, Field
import trade_journal
import trade_tools
import shopdesk
import counter
import shop_day
from ledger import lookup, same_observation
from moves import move_item as submit_move, move_items as submit_batch


class SaleLine(BaseModel):
    item_id: int = Field(gt=0)
    min_price: int = Field(ge=0)


def register_pawnshop_tools(server, folder, database):
    trade_tools.register(server, folder, database)
    shop_day.register_day_tools(server, folder, database)

    @server.tool()
    def advance_dialogue(generation: str, revision: int, visit: str, page: str) -> dict:
        """Advance just the observed manual counter page. Get its page token from get_trade_context."""
        return counter.command(folder, "advance", generation, revision, visit=visit, page=page)

    @server.tool()
    def choose_dialogue(generation: str, revision: int, visit: str, page: str, choice_id: int) -> dict:
        """Select an enabled dialog_choices ID from the observed counter page."""
        return counter.command(folder, "choose_dialogue", generation, revision, visit=visit, page=page, choice_id=choice_id)

    @server.tool()
    def response_choice(generation: str, revision: int, page: str, choice_id: int, visit: str | None = None) -> dict:
        """Activate one visible response_choices item ID from get_trade_context. Pass its visit, including null."""
        return counter.command(folder, "response_choice", generation, revision, visit=visit, page=page, choice_id=choice_id)

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
                        area: Literal["inventory", "showcase", "front", "back"] | None = None,
                        owned: bool | None = None) -> dict:
        """Query the most recently published inventory by literal name substring, area or owner.
        The game measures load_scans for the current load and full_scans for this observer instance.
        A query reads local files and SQLite. IDs are scoped to generation."""
        account = lookup(folder, database, name, area, owned)
        if account["status"] == "observed":
            status = shop_day.shop_status(folder)
            if status["status"] == "observed":
                if not same_observation(account, status):
                    return {"status": "observation_changed"}
                account.update(calendar=status["calendar"], trash=shop_day.trash_contents(folder, database, status))
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
                   area: Literal["inventory", "showcase", "front", "back", "purifier"] | None = None,
                   exclude_contraband: bool = False, kinds: list[str] | None = None, exclude_kinds: list[str] | None = None) -> dict:
        """Pick stock by exact visible label, area, ownership and native item categories.
        Match any requested category or kind, then apply all exclusions. Use list_stock_tags to discover category names, codes and observed bottle labels.
        Visible variety restrictions group sellable matches by kind, one per kind for this visit.
        Includes purifier slots. Apply exclude_contraband when the visible tablet shows that restriction."""
        return shopdesk.allocation_stock(folder, database, dict(bottle_label=bottle_label, categories=categories,
            exclude_categories=exclude_categories, owned=owned, area=area, exclude_contraband=exclude_contraband, kinds=kinds, exclude_kinds=exclude_kinds))

    @server.tool()
    def get_stock_items(item_ids: list[int], generation: str) -> dict:
        """Read this load's stock IDs in request order, with current locations, labels and missing IDs."""
        return lookup(folder, database, stock_query=dict(item_ids=item_ids, generation=generation))

    @server.tool()
    def move_item(item_id: int, source: Literal["inventory", "showcase", "front", "back"],
                  destination: Literal["inventory", "showcase", "front", "back"], generation: str,
                  revision: int, command_id: str | None = None, visit: str | None = None) -> dict:
        """While closed, move owned stock between inventory and showcase. During trade pass the observed visit
        to stage items on the trade table (back), or return them (multiple owned items may be staged together
        during regular selling; guest goods require an empty table). Guest goods return to front and
        owned goods to inventory/showcase. Owned goods left on back when no customer is present can also be
        cleared to inventory/showcase with visit omitted. Placement alone does not confirm a trade. Reuse the command ID to retry."""
        return submit_move(folder, item_id, source, destination, generation, revision, command_id, visit=visit)

    @server.tool()
    def move_items(item_ids: list[int], source: Literal["inventory", "showcase", "front", "back"],
                   destination: Literal["inventory", "showcase"], generation: str, revision: int, visit: str | None = None) -> dict:
        """Move owned stock between inventory and showcase with the shutter closed, or collect owned front/back items to inventory.
        Pass the current visit during any arrived customer visit without an active order (including ready, barter, seller-only or dialogue), or omit it at an empty counter. Pass the generation and
        starting revision from query_inventory. Returns one receipt with each observed destination
        and game scan counters. Stops at the first unconfirmed or rejected item. The original
        arguments recover completed lines and can continue pending lines."""
        return submit_batch(folder, item_ids, source, destination, generation, revision, visit=visit)

    @server.tool()
    def query_purchase_lots(supplier: str | None = None, product: str | None = None, lot_id: str | None = None) -> dict:
        """Look up wholesale lots by seller, product or exact lot ID. Report paid, recovered and stock.
        Gross profit is shown only for fully sold lots with a confirmed payment."""
        return shopdesk.purchase_lots(folder, database, supplier, product, lot_id)

    @server.tool()
    def inspect_item_window(owner_id: int, generation: str, revision: int, open_if_closed: bool = False,
                            since_token: str | None = None) -> dict:
        """Inspect an owned inventory/showcase item, or a directly held owned back-counter item after closing.
        Slot items include save_id for matching night loot. Optionally open a closed window.
        Newspapers, exchange lists, slips and handnotes return window.document. Other windows return visible slots, text and controls. Pass the last token as since_token for changes only."""
        return counter.command(folder, "inspect_window", generation, revision, owner_id=owner_id,
                               open_if_closed=open_if_closed, since_token=since_token, read_id=uuid4().hex)

    @server.tool()
    def inspect_archive(generation: str, revision: int, mode: Literal["read", "open", "close"] = "read",
                        window_token: str | None = None) -> dict:
        """Read the fixed filing box, or explicitly open/close it before or after business.
        For changes only pass the previous window token. Store/retrieve with transfer_slot_item using the returned owner and control."""
        if mode == "close":
            if not window_token:
                raise ValueError("Use the current archive window token to close it")
            return counter.command(folder, "close_archive", generation, revision, window_token=window_token)
        return counter.command(folder, "inspect_archive", generation, revision, open_if_closed=mode=="open",
                               since_token=window_token, read_id=uuid4().hex)

    @server.tool()
    def transfer_slot_item(owner_id: int, control: str, window_token: str, item_id: int,
                           source: Literal["inventory", "showcase", "slot"], generation: str, revision: int) -> dict:
        """Move owned goods into a visible slot, or from that slot into inventory. Reports observed quantity and changed controls."""
        return counter.command(folder, "transfer_slot", generation, revision, owner_id=owner_id, control=control,
                               window_token=window_token, item_id=item_id, source=source)

    @server.tool()
    def transfer_slot_items(owner_id: int, control: str, window_token: str, item_ids: list[int],
                            source: Literal["inventory", "showcase", "slot"], generation: str, revision: int) -> dict:
        """Transfer one batch to/from the same visible storage control. Inspect each result for partial or skipped moves."""
        if not item_ids or any(i < 1 for i in item_ids) or len(set(item_ids)) != len(item_ids):
            raise ValueError("Provide a nonempty list of distinct positive item IDs")
        return counter.command(folder, "transfer_slots", generation, revision, owner_id=owner_id, control=control,
                               window_token=window_token, item_ids=item_ids, source=source)

    @server.tool()
    def press_machine_button(owner_id: int, control: str, label: str, window_token: str,
                             generation: str, revision: int) -> dict:
        """Press one visible enabled machinery button by observed label and token. May consume supplies or start production.
        Returns the native press result and changed controls if the window stays visible."""
        return counter.command(folder, "press_control", generation, revision, owner_id=owner_id, control=control,
                               window_token=window_token, label=label)

    @server.tool()
    def water_use(source_id: int, target_id: int | Literal["faucet", "drain"], generation: str, revision: int,
                  mode: Literal["preview", "apply"] = "preview", use_token: str | None = None) -> dict:
        """Preview a water interaction without using supplies. Sources include bottles, rations, tablets, portable purifiers and fresh strips.
        Use a bottle as source_id and target_id="faucet" to fill it from the tap, or "drain" to empty it.
        To perform it once, submit the same two IDs with mode=apply and the preview token. Recover the receipt with identical arguments.
        A seller's offered bottle permits testing only. Only the visible test panel supplies a water-quality result."""
        if source_id < 1 or (isinstance(target_id, int) and (target_id < 1 or target_id == source_id)):
            raise ValueError("Choose a source and a distinct water target from the current observation")
        if mode == "preview":
            return counter.command(folder, "water_use", generation, revision, source_id=source_id,
                                   target_id=target_id, mode=mode, read_id=uuid4().hex)
        if not use_token:
            raise ValueError("Use the token from the water_use preview")
        return counter.command(folder, "water_use", generation, revision, source_id=source_id,
                               target_id=target_id, mode=mode, use_token=use_token)

    @server.tool()
    def water_panel(generation: str, revision: int, mode: Literal["read", "match", "close", "notice_close"] = "read",
                    panel_token: str | None = None, bottle_id: int | None = None) -> dict:
        """Read an already open test-strip panel, including tested bottle, displayed result and unused strip count.
        Match an owned bottle label only with its observed ID and panel token, or close that panel by token. A visible single-button purifier notice can be acknowledged with notice_close."""
        if mode == "read":
            return counter.command(folder, "water_panel", generation, revision, mode=mode, read_id=uuid4().hex)
        if not panel_token or (mode == "match" and (bottle_id is None or bottle_id < 1)):
            raise ValueError("Use the observed panel_token and, for match, its bottle_id")
        return counter.command(folder, "water_panel", generation, revision, mode=mode,
                               panel_token=panel_token, bottle_id=bottle_id)

    @server.tool()
    def inspect_purifiers() -> dict:
        """Compare last night's and current bottle IDs and read battery, extraction readiness and the shared count of unused test strips."""
        return shopdesk.machines(folder)

    @server.tool()
    def take_purifier_bottle(machine_id: int, bottle_id: int, generation: str, revision: int,
                             command_id: str | None = None) -> dict:
        """While closed, retrieve the observed bottle from its purifier into inventory.
        Use IDs and revision from inspect_purifiers. A moved receipt confirms the actual
        destination, empty source slot and unused strip count. Does not test the water."""
        return submit_move(folder, bottle_id, "purifier", "inventory", generation, revision,
                           command_id, machine_id=machine_id)

    @server.tool()
    def put_purifier_bottle(machine_id: int, bottle_id: int, source: Literal["inventory", "showcase"],
                            generation: str, revision: int, command_id: str | None = None) -> dict:
        """Put one owned water bottle directly in an empty purifier. Confirm actual machine and slot bottle IDs."""
        return submit_move(folder, bottle_id, source, "purifier", generation, revision,
                           command_id, machine_id=machine_id)

    @server.tool()
    def get_buyer_context() -> dict:
        """Read the current customer's visit token, remaining budget and revision before submitting an order."""
        return shopdesk.strip_protocols(shopdesk.buyer(folder))

    @server.tool()
    def sell_order(lines: list[SaleLine], generation: str, revision: int, visit: str,
                   stop_same_kind_on_budget: bool = False, bundle: bool = False) -> dict:
        """Sell only the listed owned items, each with its own minimum. Before staging, auto-returns any
        non-order owned counter items (excluding nuclear_waste) to inventory and reports cleared_items.
        counter_unavailable and stale_order always attach reason and blocking_items (with guidance for nuclear_waste_on_counter).
        Set bundle=True to stage all listed
        items onto the trade table together and settle in one click (rolling all items back to their original
        areas if any item is declined, below minimum, duplicate variety, or the combined quote exceeds budget).
        With bundle=False (default), sells one item at a time and can opt in to stop_same_kind_on_budget.
        A staged item waits through dialogue and a closed tablet. Reuse identical arguments (including the original
        revision) to read sale_pending or the final receipt with actual received and remaining_budget."""
        return shopdesk.sell_order(folder, [line.model_dump() for line in lines],
                                  generation, revision, visit, stop_same_kind_on_budget,
                                  database=database, bundle=bundle)

    @server.tool()
    def get_counter() -> dict:
        """Read shop phase, waiting count, opening lines, and visible merchandise or the information dealer's receipt and price."""
        view = shopdesk.published(folder)
        if view["status"] != "observed" or view.get("counter_protocol") != 2:
            return counter.context(folder)
        day_info = {k: v for k, v in view["day_info"].items() if k in {"calendar", "trash", "sleep"}}
        result = {**{k:view[k] for k in ("status","slot","phase","generation","revision","day")}, **view["counter"], **day_info}
        result["trash"] = shop_day.trash_contents(folder, database, result)
        return shopdesk.strip_protocols(counter.with_barter(folder, result))

    @server.tool()
    def ring_customer(generation: str, revision: int, waiting: int) -> dict:
        """Ring once at an empty open counter. Wait for the greeting, stopping at choices."""
        return counter.ring(folder, generation, revision, waiting)

    @server.tool()
    def wait_for_opening(generation: str, visit: str) -> dict:
        """Continue waiting for this visit, turning finished ordinary pages and opening the trade tablet."""
        return counter.wait_opening(folder, generation, visit)

    @server.tool()
    def barter_offer(mode: Literal["select", "accept"], option: int, generation: str, revision: int,
                     visit: str, barter_token: str, payment_item_id: int | None = None) -> dict:
        """Select a numbered reward bundle from get_trade_context without exchanging anything.
        To accept, stage one food item with move_item, reread the quote, then supply its payment item ID."""
        if mode == "accept" and not payment_item_id:
            return {"status": "payment_item_required"}
        return counter.command(folder, "barter", generation, revision, visit=visit, mode=mode, option=option,
                               barter_token=barter_token, payment_item_id=payment_item_id)

    @server.tool()
    def buy_merchant_batch(generation: str, revision: int, visit: str, offer_token: str, max_total: int) -> dict:
        """Buy the observed entire offer at or below max_total. Reuse identical inputs to recover a receipt."""
        if max_total < 0:
            raise ValueError("Expected a non-negative batch price limit")
        return counter.command(folder, "buy", generation, revision, visit=visit,
                               offer_token=offer_token, max_total=max_total, database=database)

    @server.tool()
    def pay_info_receipt(generation: str, revision: int, visit: str, receipt_token: str, price: int) -> dict:
        """Buy information at the exact visible price. Returns actual paid and cash_after. Repeat identical inputs for the receipt."""
        if price < 0 or price > 2147483647:
            raise ValueError("Expected a game credit amount")
        return counter.command(folder, "pay_info", generation, revision, visit=visit,
                               receipt_token=receipt_token, price=price, database=database)

    @server.tool()
    def open_shop(generation: str, revision: int) -> dict:
        """Open the observed morning shutter once. Does not ring or close the shop."""
        return counter.command(folder, "open_shop", generation, revision)

    @server.tool()
    def dismiss_customer(generation: str, revision: int, visit: str) -> dict:
        """Dismiss this visit when reception.can_dismiss is true. Automatically returns non-order owned counter items (excluding nuclear_waste) to inventory as cleared_items without blocking dismissal. A newer inventory revision rechecks the same visit. After dismiss_unavailable (which always attaches reason and blocking_items), retry the original arguments when this visit can leave. A completed dismissal returns its original receipt."""
        return counter.command(folder, "dismiss", generation, revision, visit=visit)

    @server.tool()
    def set_auto_receive(enabled: bool, generation: str, revision: int, reception_token: str) -> dict:
        """Enable or disable automatic admission for this open day. Only an empty counter can take the next guest."""
        return counter.command(folder, "auto_receive", generation, revision, enabled=enabled, reception_token=reception_token)

    @server.tool()
    def close_shop(generation: str, revision: int, reception_token: str, confirm_remaining: int | None = None) -> dict:
        """Request closing. For early closing read waiting, then submit that exact count and the new reception token."""
        if confirm_remaining is not None and confirm_remaining < 0:
            raise ValueError("Expected the displayed remaining queue count")
        return counter.command(folder, "close_shop", generation, revision, reception_token=reception_token,
                               confirm_remaining=confirm_remaining)

    @server.tool()
    def wild_network(mode: Literal["read", "tab", "select", "buy", "close"] = "read",
                     generation: str | None = None, revision: int | None = None, panel_token: str | None = None,
                     target: str | None = None, cost: int | None = None, since_token: str | None = None) -> dict:
        """Read Joe's visible network catalog on entry and after changing tabs. Pass since_token for an unchanged check.
        On 0.19.1+, select and buy return selected details and a fresh top-level panel_token while the panel stays open.
        Chain using the receipt's generation, revision and panel_token. Buy requires selected_id and the quoted cost.
        The purchase receipt separates favor spent and cash paid."""
        if mode == "read":
            view = counter.context(folder)
            if view["status"] != "observed":
                return view
            if view.get("wild_network_protocol") != 1:
                return {"status":"reload_updated_mod"}
            if view["stage"] != "wild_network":
                return {"status":"panel_closed", "slot":view["slot"]}
            return counter.command(folder, "wild_network", view["generation"], view["revision"],
                                   mode=mode, since_token=since_token, read_id=uuid4().hex)
        if not generation or revision is None or not panel_token:
            raise ValueError("Use generation, revision and panel_token from the open panel")
        if mode in {"tab","select","buy"} and not target:
            raise ValueError("Use the visible tab or service ID")
        if mode == "buy" and (cost is None or cost < 0):
            raise ValueError("Use the exact displayed service cost")
        return counter.command(folder, "wild_network", generation, revision, mode=mode,
                               panel_token=panel_token, target=target, cost=cost)

    @server.tool()
    def get_phone_contacts() -> dict:
        """Read known phonebook and owned business-card contacts, services, answerability and visible call choices. Never dials."""
        return shopdesk.strip_protocols(counter.phone_context(folder))

    @server.tool()
    def phone_keypad(mode: Literal["open", "close", "dial"], generation: str, revision: int,
                     phone_token: str, digits: str = "") -> dict:
        """Open or close the phone, or enter 1–9 digits on the empty native keypad. Preserve leading zeros."""
        if mode == "dial" and not (digits.isascii() and digits.isdecimal() and 1 <= len(digits) <= 9):
            return {"status": "invalid_number"}
        return counter.command(folder, "phone_keypad", generation, revision, phone_token=phone_token, mode=mode, digits=digits)

    @server.tool()
    def dial_contact(number: int, generation: str, revision: int, phone_token: str) -> dict:
        """Dial exactly one observed contact. Starting the native call does not select a menu option."""
        return counter.command(folder, "dial", generation, revision, phone_token=phone_token, number=number)

    @server.tool()
    def choose_phone_service(choice_id: int, generation: str, revision: int, phone_token: str) -> dict:
        """Click one enabled choice from the current phone menu. May place an order or change service."""
        return counter.command(folder, "phone_choice", generation, revision, phone_token=phone_token, choice_id=choice_id)

    @server.tool()
    def advance_phone_dialogue(generation: str, revision: int, phone_token: str) -> dict:
        """Turn one finished ordinary phone page when can_advance is true. Never chooses a service."""
        return counter.command(folder, "phone_advance", generation, revision, phone_token=phone_token)
