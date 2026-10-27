"""Offline only: fixed catalog migration, CAS, backup and replay guarantees."""
from copy import deepcopy
import json
from pathlib import Path
import sys
import tempfile
from contextlib import contextmanager
import shutil
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "tools"))
from merge_consumable_catalog import (AdminError, COLLECTION, KEY, SYSTEM_USER_ID,
                                     IDS, merge_catalog, merge_console, validate_manifest)

ROOT = Path(__file__).resolve().parents[3]
MANIFEST = json.loads((ROOT / "Server/nakama/data/migrations/consumables-0.3.9.json").read_text(encoding="utf-8"))
CANONICAL = json.loads((ROOT / "Server/nakama/data/duel_match3_item_defs.json").read_text(encoding="utf-8"))
# Keep the historical 0.3.9 migration fixture at its original72 definitions.
# The independent shop migration suite covers the subsequent token addition.
CANONICAL["items"].pop("global_boss_teleport", None)
CANONICAL["version"] = 4
OLD = deepcopy(CANONICAL)
for item_id in IDS:
    del OLD["items"][item_id]
OLD["version"] = 3


TEMP_ROOT = (ROOT / ".cache/qa/consumable-catalog-tests").resolve()
assert TEMP_ROOT.is_relative_to(ROOT.resolve())
TEMP_ROOT.mkdir(parents=True, exist_ok=True)

@contextmanager
def scratch():
    folder = Path(tempfile.mkdtemp(dir=TEMP_ROOT)).resolve()
    assert folder.is_relative_to(TEMP_ROOT)
    try:
        yield folder
    finally:
        # The resolved generated target is checked before recursive cleanup.
        assert folder.is_relative_to(TEMP_ROOT) and folder != TEMP_ROOT
        shutil.rmtree(folder)


class FakeConsole:
    def __init__(self):
        self.row = {"collection": COLLECTION, "key": KEY, "user_id": SYSTEM_USER_ID,
                    "version": "v7", "value": deepcopy(OLD), "permission_read": 0, "permission_write": 0}
        self.writes = 0
        self.race = self.lost_ack = False

    def read(self, collection, key):
        assert (collection, key) == (COLLECTION, KEY)
        return deepcopy(self.row)

    def write(self, collection, key, value, version):
        assert (collection, key) == (COLLECTION, KEY)
        if self.race:
            self.row["version"] = "concurrent"
        if version != self.row["version"]:
            raise AdminError("CAS conflict")
        self.writes += 1
        self.row.update(value=deepcopy(value), version="v8")
        if self.lost_ack:
            self.lost_ack = False
            raise AdminError("lost acknowledgement")
        return {"version": "v8"}


class MigrationTests(unittest.TestCase):
    def test_preserves_all_68_definitions_and_metadata(self):
        before = deepcopy(OLD)
        before["custom_operator_metadata"] = {"keep": [1, 2]}
        merged, added = merge_catalog(before, MANIFEST)
        self.assertEqual(len(added), 4)
        self.assertEqual(len(merged["items"]), 72)
        for key, value in OLD["items"].items():
            self.assertEqual(merged["items"][key], value)
        self.assertEqual(merged["custom_operator_metadata"], {"keep": [1, 2]})
        self.assertEqual(len(before["items"]), 68)

    def test_second_merge_is_exactly_idempotent(self):
        one, _ = merge_catalog(OLD, MANIFEST)
        two, added = merge_catalog(one, MANIFEST)
        self.assertEqual(one, two)
        self.assertEqual(added, [])

    def test_unrecognized_ids_are_rejected(self):
        delta = deepcopy(MANIFEST)
        delta["items"]["extra_attack"] = delta["items"]["elixir_assault"]
        with self.assertRaises(AdminError):
            merge_catalog(OLD, delta)

    def test_modified_existing_consumable_is_never_overwritten(self):
        current = deepcopy(CANONICAL)
        current["items"]["elixir_assault"]["sale_price"] = 99
        with self.assertRaises(AdminError):
            merge_catalog(current, MANIFEST)
        self.assertEqual(current["items"]["elixir_assault"]["sale_price"], 99)

    def test_fixed_schema_and_numeric_types(self):
        for field, value in (("max_stack", 999), ("sale_price", False), ("tier", True), ("kind", "equipment")):
            delta = deepcopy(MANIFEST)
            delta["items"]["elixir_assault"][field] = value
            with self.assertRaises(AdminError):
                validate_manifest(delta)

    def test_missing_catalog_and_bad_versions_are_rejected(self):
        for current in ({}, {"items": {}}, {"version": True, "items": OLD["items"]}):
            with self.assertRaises(AdminError):
                merge_catalog(current, MANIFEST)

    def test_read_only_plan_writes_nothing(self):
        console = FakeConsole()
        result = merge_console(console, MANIFEST)
        self.assertEqual(result["mode"], "planned")
        self.assertEqual(console.writes, 0)
        self.assertEqual(console.row["value"], OLD)

    def test_backup_precedes_cas_and_second_apply_writes_nothing(self):
        console = FakeConsole()
        with scratch() as folder:
            backup = Path(folder) / "catalog.before.json"
            result = merge_console(console, MANIFEST, backup, True)
            self.assertEqual(result["mode"], "applied")
            previous = json.loads(backup.read_text(encoding="utf-8"))
            self.assertEqual(previous["version"], "v7")
            self.assertEqual(previous["value"], OLD)
            self.assertEqual(merge_console(console, MANIFEST, backup, True)["mode"], "unchanged")
            self.assertEqual(console.writes, 1)

    def test_existing_backup_refuses_the_write(self):
        console = FakeConsole()
        with scratch() as folder:
            backup = Path(folder) / "keep.json"
            backup.write_text("keep", encoding="utf-8")
            with self.assertRaises(FileExistsError):
                merge_console(console, MANIFEST, backup, True)
            self.assertEqual(console.writes, 0)
            self.assertEqual(backup.read_text(), "keep")

    def test_concurrent_edit_fails_closed_with_backup_retained(self):
        console = FakeConsole()
        console.race = True
        with scratch() as folder:
            backup = Path(folder) / "before.json"
            with self.assertRaises(AdminError):
                merge_console(console, MANIFEST, backup, True)
            self.assertTrue(backup.exists())
            self.assertEqual(console.writes, 0)
            self.assertEqual(console.row["value"], OLD)

    def test_lost_ack_retry_is_safe_without_another_write(self):
        console = FakeConsole()
        console.lost_ack = True
        with scratch() as folder:
            backup = Path(folder) / "before.json"
            with self.assertRaises(AdminError):
                merge_console(console, MANIFEST, backup, True)
            self.assertEqual(merge_console(console, MANIFEST, backup, True)["mode"], "unchanged")
            self.assertEqual(console.writes, 1)

    def test_human_owned_or_ambiguous_storage_row_cannot_be_migrated(self):
        for field, value in (("user_id", "player-id"), ("collection", "player-sheet"), ("key", "other"), ("version", "*")):
            console = FakeConsole()
            console.row[field] = value
            with self.assertRaises(AdminError):
                merge_console(console, MANIFEST, apply=True)
            self.assertEqual(console.writes, 0)


if __name__ == "__main__":
    unittest.main(verbosity=2)
