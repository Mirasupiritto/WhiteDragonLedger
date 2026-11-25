"""Facade registering all pawnshop-region MCP tools across domain modules."""
import counter
import counter_tools
from counter_tools import SaleLine
import phone_tools
import shop_day
import stock_tools
import trade_tools
import water_tools
import workstation_tools


__all__ = ["SaleLine", "register_pawnshop_tools"]


def register_pawnshop_tools(server, folder, database):
    state = getattr(server, "state", None)
    section_cache = state.section_cache if state is not None else (getattr(server, "section_cache", None) or shop_day.SectionCache())
    server.section_cache = section_cache

    trade_tools.register(server, folder, database)
    shop_day.register_day_tools(server, folder, database, section_cache=section_cache)
    counter_tools.register(server, folder, database, section_cache=section_cache)
    stock_tools.register(server, folder, database, section_cache=section_cache)
    workstation_tools.register(server, folder)
    water_tools.register(server, folder)
    phone_tools.register(server, folder)
