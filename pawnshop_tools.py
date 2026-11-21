"""MCP tools available inside the pawnshop."""
from typing import Literal
from uuid import uuid4

from pydantic import BaseModel, Field
import shopdesk
import counter
import shop_day
from ledger import lookup
from moves import move_item as submit_move, move_items as submit_batch


class SaleLine(BaseModel):
    item_id: int = Field(gt=0)
    min_price: int = Field(ge=0)


def register_pawnshop_tools(server, folder, database):
    shop_day.register_day_tools(server, folder, database)

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
            if status.get("generation") == account["generation"]:
                account.update(calendar=status["calendar"], trash=shop_day.trash_contents(folder, database, status))
        return account

    @server.tool()
    def move_item(item_id: int, source: Literal["inventory", "showcase"],
                  destination: Literal["inventory", "showcase"], generation: str,
                  revision: int, command_id: str | None = None) -> dict:
        """Move one owned item while the shutter is closed. Use the generation and revision from
        query_inventory. A moved receipt gives the confirmed location and game-side scan counts.
        Reuse the same command_id and original arguments when checking an uncertain response."""
        return submit_move(folder, item_id, source, destination, generation, revision, command_id)

    @server.tool()
    def move_items(item_ids: list[int], source: Literal["inventory", "showcase"],
                   destination: Literal["inventory", "showcase"], generation: str, revision: int) -> dict:
        """Move an ordered list of owned items with the shutter closed. Pass the generation and
        starting revision from query_inventory. Returns one receipt with each observed destination
        and game scan counters. Stops at the first unconfirmed or rejected item. The original
        arguments recover completed lines and can continue pending lines."""
        return submit_batch(folder, item_ids, source, destination, generation, revision)

    @server.tool()
    def query_purchase_lots(supplier: str | None = None, product: str | None = None, lot_id: str | None = None) -> dict:
        """Look up wholesale lots by seller, product or exact lot ID. Report paid, recovered and stock.
        Gross profit is shown only for fully sold lots with a confirmed payment."""
        return shopdesk.purchase_lots(folder, database, supplier, product, lot_id)

    @server.tool()
    def inspect_item_window(owner_id: int, generation: str, revision: int, open_if_closed: bool = False,
                            since_token: str | None = None) -> dict:
        """Inspect one owned inventory or showcase item's content window. Optionally open it while closed.
        First read returns visible slots, text and controls. Pass the last token as since_token for changes only."""
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
    def press_machine_button(owner_id: int, control: str, label: str, window_token: str,
                             generation: str, revision: int) -> dict:
        """Press one visible enabled machinery button by observed label and token. May consume supplies or start production.
        Returns the native press result and changed controls if the window stays visible."""
        return counter.command(folder, "press_control", generation, revision, owner_id=owner_id, control=control,
                               window_token=window_token, label=label)

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
        return shopdesk.buyer(folder)

    @server.tool()
    def sell_order(lines: list[SaleLine], generation: str, revision: int, visit: str,
                   stop_same_kind_on_budget: bool = False) -> dict:
        """Sell only the listed owned items, each with its own minimum. Opt in to skipping the same
        native item kind after a budget rejection has been returned. Other kinds stay in order.
        Reuse identical arguments to recover the receipt of a submitted order."""
        return shopdesk.sell_order(folder, [line.model_dump() for line in lines],
                                  generation, revision, visit, stop_same_kind_on_budget)

    @server.tool()
    def get_counter() -> dict:
        """Read shop phase, waiting count, opening lines, and visible merchandise or the information dealer's receipt and price."""
        view = shopdesk.published(folder)
        if view["status"] != "observed" or view.get("counter_protocol") != 2:
            return counter.context(folder)
        result = {**{k:view[k] for k in ("status","slot","phase","generation","revision","day")}, **view["counter"], **view["day_info"]}
        result["trash"] = shop_day.trash_contents(folder, database, result)
        return result

    @server.tool()
    def ring_customer(generation: str, revision: int, waiting: int) -> dict:
        """Ring once at an empty open counter. Wait for the greeting, stopping at choices."""
        return counter.ring(folder, generation, revision, waiting)

    @server.tool()
    def wait_for_opening(generation: str, visit: str) -> dict:
        """Continue waiting for this visit, turning finished ordinary pages and opening the trade tablet."""
        return counter.wait_opening(folder, generation, visit)

    @server.tool()
    def buy_merchant_batch(generation: str, revision: int, visit: str, offer_token: str, max_total: int) -> dict:
        """Buy the observed entire offer at or below max_total. Reuse identical inputs to recover a receipt."""
        if max_total < 0:
            raise ValueError("Expected a non-negative batch price limit")
        return counter.command(folder, "buy", generation, revision, visit=visit,
                               offer_token=offer_token, max_total=max_total)

    @server.tool()
    def pay_info_receipt(generation: str, revision: int, visit: str, receipt_token: str, price: int) -> dict:
        """Buy information at the exact visible price. Returns actual paid and cash_after. Repeat identical inputs for the receipt."""
        if price < 0 or price > 2147483647:
            raise ValueError("Expected a game credit amount")
        return counter.command(folder, "pay_info", generation, revision, visit=visit,
                               receipt_token=receipt_token, price=price)

    @server.tool()
    def open_shop(generation: str, revision: int) -> dict:
        """Open the observed morning shutter once. Does not ring or close the shop."""
        return counter.command(folder, "open_shop", generation, revision)

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
    def get_phone_contacts() -> dict:
        """Read known phonebook and owned business-card contacts, services, answerability and visible call choices. Never dials."""
        return counter.phone_context(folder)

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
