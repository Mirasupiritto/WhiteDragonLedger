import argparse
import os
from pathlib import Path
from scene import register_scene_tools
from scene_server import SceneServer
from journey import register as register_journey_tools
from pawnshop_tools import register_pawnshop_tools
from chem_workflow import register as register_chem_tools


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--journal", type=Path, default=Path.home() /
        "AppData/LocalLow/Questing Goose Studio/Probably Stolen/ModData/white_dragon_ledger")
    parser.add_argument("--database", type=Path)
    parser.add_argument("--item-details-compat", action="store_true", default=None)
    parser.add_argument("--appraisal-assist", action="store_true", default=None)
    args = parser.parse_args()
    database = args.database or Path(os.environ["LOCALAPPDATA"]) / "WhiteDragonLedger" / "account.sqlite3"
    compat = True if args.item_details_compat else None
    assist = True if args.appraisal_assist else None
    server = SceneServer(args.journal)
    register_scene_tools(server, args.journal, item_details_compat=compat)
    register_journey_tools(server, args.journal)
    register_pawnshop_tools(server, args.journal, database, item_details_compat=compat, appraisal_assist=assist)
    register_chem_tools(server, args.journal, database)
    server.run()


if __name__ == "__main__":
    main()
