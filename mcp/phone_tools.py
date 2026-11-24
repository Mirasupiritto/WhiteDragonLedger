"""Phone directory, keypad, dialing, and call menu tools."""
from typing import Literal

import counter
import shopdesk


def register(server, folder):
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
