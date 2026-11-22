import argparse
import os
from pathlib import Path
from scene import register_scene_tools
from scene_server import SceneServer
from journey import register as register_journey_tools
from pawnshop_tools import register_pawnshop_tools


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--journal", type=Path, default=Path.home() /
        "AppData/LocalLow/Questing Goose Studio/Probably Stolen/ModData/white_dragon_ledger")
    parser.add_argument("--database", type=Path)
    args = parser.parse_args()
    database = args.database or Path(os.environ["LOCALAPPDATA"]) / "WhiteDragonLedger" / "account.sqlite3"
    server = SceneServer(args.journal)
    register_scene_tools(server, args.journal)
    register_journey_tools(server, args.journal)
    register_pawnshop_tools(server, args.journal, database)
    server.run()


if __name__ == "__main__":
    main()
