"""Offline checks for the append-only achievement catalog installer."""
from copy import deepcopy
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "tools"))
from append_achievement_catalog import AdminError, COLLECTION, KEY, SYSTEM_USER_ID, merge_catalog, merge_console


def chain(entry_id, category):
    return {"id": entry_id, "category": category, "counter_key": entry_id,
            "steps": [{"threshold_delta": 1, "rewards": [{"type": "gold", "value": 5}]}]}


OLD = {"schema_version": 1, "updated_at": 1, "operator_data": {"owner": True},
       "categories": [{"id": "obsession", "label_ru": "Подпись оператора"}],
       "chains": [chain("obs.old", "obsession"), chain("ops.custom", "obsession")]}
CANONICAL = {"schema_version": 1, "updated_at": 2,
             "categories": [{"id": "obsession", "label_ru": "Одержимость"},
                            {"id": "resources", "label_ru": "Ресурсы", "min_journal_version": 2}],
             "chains": [dict(chain("obs.old", "obsession"), title_ru="Другой заголовок"),
                        chain("obs.new", "obsession"),
                        dict(chain("res.new", "resources"), min_journal_version=2, threshold_mode="absolute_total"),
                        chain("res.extra", "resources")]}
CATEGORIES, CHAINS = ["resources"], ["obs.new", "res.new"]
SCRATCH = Path(__file__).resolve().parents[3] / ".cache/qa/achievement-catalog-append-test"


class FakeConsole:
    def __init__(self):
        self.row = {"collection": COLLECTION, "key": KEY, "user_id": SYSTEM_USER_ID, "version": "v7",
                    "permission_read": 0, "permission_write": 0, "value": deepcopy(OLD)}
        self.attempts = 0
        self.before_write = None
        self.corrupt_read_back = self.lose_ack = False

    def read(self, collection, key):
        assert (collection, key) == (COLLECTION, KEY)
        return deepcopy(self.row)

    def write(self, collection, key, value, version):
        assert (collection, key) == (COLLECTION, KEY)
        self.attempts += 1
        if self.before_write:
            self.before_write()
        if version != self.row["version"]:
            raise AdminError("CAS conflict")
        self.row.update(version="v8", value=deepcopy(value))
        if self.lose_ack:
            raise AdminError("lost acknowledgement")
        if self.corrupt_read_back:
            self.row["value"]["chains"][0]["steps"][0]["threshold_delta"] = 99
        return {"version": "v8"}


class CatalogAppendTest(unittest.TestCase):
    def setUp(self):
        SCRATCH.mkdir(parents=True, exist_ok=True)
        folder = tempfile.TemporaryDirectory(dir=SCRATCH)
        self.assertTrue(Path(folder.name).resolve().is_relative_to(SCRATCH.resolve()))
        self.addCleanup(folder.cleanup)
        self.backup = Path(folder.name) / "before.json"
        self.console = FakeConsole()

    def plan(self):
        return merge_console(self.console, CANONICAL, CATEGORIES, CHAINS)

    def apply(self, version="v7"):
        return merge_console(self.console, CANONICAL, CATEGORIES, CHAINS,
                             apply=True, backup=self.backup, expected_version=version)

    def test_append_exact_selection_preserves_all_original_records_and_metadata(self):
        merged, detail = merge_catalog(OLD, CANONICAL, CATEGORIES, CHAINS)
        self.assertEqual(merged["categories"][:1], OLD["categories"])
        self.assertEqual(merged["categories"][1], CANONICAL["categories"][1])
        self.assertEqual(merged["chains"][:2], OLD["chains"])
        self.assertEqual(merged["chains"][2:], CANONICAL["chains"][1:3])
        self.assertEqual({k: v for k, v in merged.items() if k not in ("categories", "chains")},
                         {k: v for k, v in OLD.items() if k not in ("categories", "chains")})
        self.assertEqual(detail["added_chains"], CHAINS)
        self.assertEqual(detail["added_categories"], CATEGORIES)
        self.assertEqual(len(OLD["chains"]), 2)

    def test_supports_legacy_object_storage_without_reformatting_old_chains(self):
        legacy = {"chains": {"obs.old": {k: v for k, v in OLD["chains"][0].items() if k != "id"}}}
        merged, _ = merge_catalog(legacy, CANONICAL, CATEGORIES, CHAINS)
        self.assertIsInstance(merged["chains"], dict)
        self.assertEqual(merged["chains"]["obs.old"], legacy["chains"]["obs.old"])
        self.assertEqual(merged["chains"]["res.new"], CANONICAL["chains"][2])

    def test_invalid_selection_or_missing_category_stops_before_writes(self):
        for categories, chains in [([], []), ([], ["res.new"]), ([], ["missing"]), (CATEGORIES, CHAINS * 2)]:
            with self.subTest(categories=categories, chains=chains), self.assertRaises(AdminError):
                merge_catalog(OLD, CANONICAL, categories, chains)
        self.assertEqual(self.console.attempts, 0)

    def test_existing_selected_id_with_manual_edit_fails_closed(self):
        merged, _ = merge_catalog(OLD, CANONICAL, CATEGORIES, CHAINS)
        for field, index in [("categories", 1), ("chains", 2)]:
            current = deepcopy(merged)
            current[field][index]["operator_override"] = True
            with self.subTest(field=field), self.assertRaisesRegex(AdminError, "differs from canonical"):
                merge_catalog(current, CANONICAL, CATEGORIES, CHAINS)

    def test_read_only_preflight_then_cas_with_complete_backup_and_idempotent_replay(self):
        initial = deepcopy(self.console.row)
        preflight = self.plan()
        self.assertEqual(preflight["mode"], "planned")
        self.assertEqual(self.console.attempts, 0)
        self.console.before_write = lambda: self.assertEqual(json.loads(self.backup.read_text(encoding="utf-8")), initial)
        applied = self.apply(preflight["storage_version"])
        self.assertEqual(applied["mode"], "applied")
        self.assertEqual(applied["storage_version"], "v8")
        self.assertEqual(self.apply("v8")["mode"], "unchanged")
        self.assertEqual(self.console.attempts, 1)

    def test_stale_preflight_and_existing_backup_reject_the_write(self):
        with self.assertRaisesRegex(AdminError, "changed since preflight"):
            self.apply("older")
        self.assertFalse(self.backup.exists())
        self.backup.write_text("keep", encoding="utf-8")
        with self.assertRaises(FileExistsError):
            self.apply()
        self.assertEqual(self.backup.read_text(encoding="utf-8"), "keep")
        self.assertEqual(self.console.attempts, 0)

    def test_concurrent_edit_preserves_original_envelope_and_does_not_retry(self):
        initial = deepcopy(self.console.row)
        self.console.before_write = lambda: self.console.row.update(version="raced")
        with self.assertRaisesRegex(AdminError, "CAS conflict"):
            self.apply()
        self.assertEqual(json.loads(self.backup.read_text(encoding="utf-8")), initial)
        self.assertEqual(self.console.attempts, 1)
        self.assertEqual(self.console.row["value"], initial["value"])

    def test_read_back_validates_preserved_content(self):
        self.console.corrupt_read_back = True
        with self.assertRaisesRegex(AdminError, "Read-back differs"):
            self.apply()
        self.assertTrue(self.backup.exists())
        self.assertEqual(self.console.attempts, 1)

    def test_lost_ack_fresh_preflight_finds_installed_selection(self):
        self.console.lose_ack = True
        with self.assertRaisesRegex(AdminError, "lost acknowledgement"):
            self.apply()
        self.assertEqual(self.plan()["mode"], "unchanged")
        self.assertEqual(self.apply("v8")["mode"], "unchanged")
        self.assertEqual(self.console.attempts, 1)

    def test_rejects_wrong_storage_identity_wildcard_or_public_permission(self):
        for patch in ({"userId": "other"}, {"collection": "other"}, {"key": "other"}, {"version": "*"}, {"permission_write": 1}):
            self.console = FakeConsole()
            self.console.row.update(patch)
            with self.subTest(patch=patch), self.assertRaises(AdminError):
                self.apply()
            self.assertEqual(self.console.attempts, 0)


if __name__ == "__main__":
    unittest.main(verbosity=2)
