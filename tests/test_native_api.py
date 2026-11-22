import json
from hashlib import sha256
from pathlib import Path
from tempfile import TemporaryDirectory
import unittest

from native_api_mcp import NativeIndex


class NativeIndexTests(unittest.TestCase):
    def setUp(self):
        temporary = TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        folder = Path(temporary.name)
        self.assembly = folder / "Assembly-CSharp.dll"
        self.assembly.write_bytes(b"indexed assembly")
        self.file = folder / "index.json"
        self.method = "System.Void Store::Open(System.Int32)"
        self.overload = "System.Void Store::Open()"
        self.field = "System.Boolean Store::IsOpen"
        self.file.write_text(json.dumps({
            "schema": 1,
            "source": {
                "name": "Assembly-CSharp",
                "path": str(self.assembly),
                "sha256": sha256(self.assembly.read_bytes()).hexdigest(),
            },
            "symbols": [
                {"id": "Store", "name": "Store", "kind": "type"},
                {"id": self.overload, "name": "Open", "kind": "method", "owner": "Store"},
                {"id": self.method, "name": "Open", "kind": "method", "owner": "Store"},
                {"id": self.field, "name": "IsOpen", "kind": "field", "owner": "Store"},
            ],
            "references": [
                {"source": self.method, "target": self.field, "assembly": "Assembly-CSharp", "operation": "write_field"},
                {"source": self.overload, "target": self.method, "assembly": "Assembly-CSharp", "operation": "call"},
                {"source": self.overload, "target": self.method, "assembly": "External", "operation": "call"},
            ],
        }), encoding="utf-8")
        self.index = NativeIndex(self.file)

    def test_overloads_and_pagination(self):
        results = self.index.search("store::open", "method", 1, 0)
        self.assertEqual(results["total"], 2)
        self.assertEqual(results["next_offset"], 1)
        remaining = self.index.search("store.open", "method", 1, 1)
        ids = {item["id"] for item in results["items"] + remaining["items"]}
        self.assertEqual(ids, {self.method, self.overload})
        self.assertIsNone(remaining["next_offset"])

    def test_filter_declared_members(self):
        result = self.index.get("Store", "field", 20, 0)
        self.assertEqual([item["id"] for item in result["members"]["items"]], [self.field])

    def test_ignores_external_homonym_for_incoming(self):
        calls = self.index.references(self.method, "incoming", 20, 0)
        self.assertEqual(calls["total"], 1)
        self.assertEqual(calls["items"][0]["source"], self.overload)
        self.assertEqual(self.index.references(self.overload, "outgoing", 20, 0)["total"], 2)

    def test_field_writes(self):
        accesses = self.index.references(self.field, "incoming", 20, 0)
        self.assertEqual(accesses["items"][0]["operation"], "write_field")
        self.assertEqual(self.index.references(self.method, "outgoing", 20, 0), accesses)

    def test_rejects_changed_game_assembly(self):
        self.assembly.write_bytes(b"updated assembly")
        with self.assertRaisesRegex(ValueError, "Assembly-CSharp.dll changed"):
            NativeIndex(self.file)


if __name__ == "__main__":
    unittest.main()
