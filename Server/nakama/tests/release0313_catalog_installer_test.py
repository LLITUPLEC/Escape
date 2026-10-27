"""Pure offline runtime-pin, scope, CAS and create-only catalog installer guards."""
from copy import deepcopy
import hashlib
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

REPO = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(REPO / "Server/nakama/tools"))
import install_release0313_catalogs as release
import storage_admin as admin
import merge_shop_catalog as merge
import install_shop_catalog as shop

HELPERS = (admin, merge, shop)
TOKEN = json.loads((REPO / "Server/nakama/data/migrations/shop-items-0.3.13.json").read_text(encoding="utf-8"))
PRICES = json.loads((REPO / "Server/nakama/data/duel_shop_catalog.json").read_text(encoding="utf-8"))
BOSS = json.loads((REPO / "Server/nakama/data/global_boss.config.json").read_text(encoding="utf-8"))
OLD = json.loads((REPO / "Server/nakama/data/duel_match3_item_defs.json").read_text(encoding="utf-8"))
OLD["items"].pop("global_boss_teleport"); OLD["version"] = 4


def row(collection, key, value):
    return {"collection": collection, "key": key, "user_id": admin.SYSTEM_USER_ID, "version": "v1", "value": deepcopy(value)}


class Console:
    def __init__(self):
        self.rows = {("duel_match3_item_defs", "catalog"): row("duel_match3_item_defs", "catalog", OLD)}
        self.writes = []
        self.race = None
        self.before = None

    def read(self, collection, key):
        return deepcopy(self.rows.get((collection, key)))

    def write(self, collection, key, value, version):
        identity = (collection, key)
        if self.before:
            self.before(identity)
        if self.race == identity:
            self.rows[identity] = row(collection, key, value)
            self.rows[identity]["version"] = "operator"
            self.rows[identity]["value"]["operator_preserved"] = True
        current = self.rows.get(identity)
        if version == "*" and current is not None or version != "*" and (current is None or current["version"] != version):
            raise admin.AdminError("CAS conflict")
        self.writes.append((collection, key, version))
        self.rows[identity] = row(collection, key, value)
        self.rows[identity]["version"] = "written" + str(len(self.writes))


class Tests(unittest.TestCase):
    def setUp(self):
        scratch = REPO / ".cache/qa/catalog-installer"
        scratch.mkdir(parents=True, exist_ok=True)
        self.temp = tempfile.TemporaryDirectory(dir=scratch)
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        (self.root / "runtime").mkdir()
        for name in ("duel_shop.lua", "duel_global_boss.lua", "duel_daily.lua"):
            (self.root / "runtime" / name).write_text("return {}", encoding="utf-8")
        self.files = release.runtime_tree(self.root / "runtime")
        self.sha = hashlib.sha256(json.dumps(self.files, sort_keys=True).encode()).hexdigest()
        self.manifest = {"sha256": self.sha, "files": self.files}
        (self.root / "deployed-runtime-manifest.json").write_text(json.dumps(self.manifest), encoding="utf-8")
        self.console = Console()
        self.guards = 0
        def guard():
            self.guards += 1
            release.verify_runtime(self.root, self.sha)
        self.guarded = release.GuardedConsole(self.console, guard)

    def test_exact_manifest_and_actual_runtime_tree_required(self):
        self.assertEqual(release.verify_runtime(self.root, self.sha), self.sha)
        with self.assertRaises(release.InstallError):
            release.verify_runtime(self.root, "f" * 64)
        (self.root / "runtime/duel_daily.lua").write_text("changed")
        with self.assertRaises(release.InstallError):
            release.verify_runtime(self.root, self.sha)

    def test_extra_or_missing_runtime_file_is_refused(self):
        extra = self.root / "runtime/extra.lua"; extra.write_text("unapproved")
        with self.assertRaises(release.InstallError):
            release.verify_runtime(self.root, self.sha)
        extra.unlink(); (self.root / "runtime/duel_shop.lua").unlink()
        with self.assertRaises(release.InstallError):
            release.verify_runtime(self.root, self.sha)

    def test_manifest_map_hash_and_path_traversal_are_refused(self):
        self.manifest["files"]["../outside.lua"] = "a" * 64
        (self.root / "deployed-runtime-manifest.json").write_text(json.dumps(self.manifest))
        with self.assertRaises(release.InstallError):
            release.verify_runtime(self.root, self.sha)
        self.manifest["files"].pop("../outside.lua"); self.manifest["files"]["duel_shop.lua"] = "a" * 64
        (self.root / "deployed-runtime-manifest.json").write_text(json.dumps(self.manifest))
        with self.assertRaises(release.InstallError):
            release.verify_runtime(self.root, self.sha)

    def test_read_only_default_has_no_storage_or_backup_writes(self):
        result = release.actions(self.guarded, TOKEN, PRICES, BOSS, HELPERS)
        self.assertEqual(result["items"]["added_ids"], ["global_boss_teleport"])
        self.assertEqual(result["shop"]["mode"], "planned_create")
        self.assertEqual(result["boss"]["mode"], "planned_create")
        self.assertEqual(self.console.writes, []); self.assertEqual(self.guards, 0)
        self.assertFalse((self.root / "backups").exists())

    def test_apply_backs_up_before_each_write_and_preserves_all72(self):
        backup = release.fresh_backup(self.root)
        def before(identity):
            name = {("duel_match3_item_defs", "catalog"): "items", ("duel_shop_catalog", "v1"): "shop", ("duel_global_boss", "config"): "boss"}[identity]
            self.assertTrue((backup / (name + "-before.json")).is_file())
        self.console.before = before
        result = release.actions(self.guarded, TOKEN, PRICES, BOSS, HELPERS, backup, True)
        self.assertEqual(result["items"]["result_count"], 73)
        current = self.console.rows[("duel_match3_item_defs", "catalog")]["value"]
        for key, definition in OLD["items"].items():
            self.assertEqual(current["items"][key], definition)
        self.assertEqual(json.loads((backup / "items-before.json").read_text())["value"], OLD)
        self.assertEqual(self.guards, 3)
        self.assertEqual([v[2] for v in self.console.writes], ["v1", "*", "*"])

    def test_existing_operator_shop_and_boss_are_never_overwritten(self):
        prices, boss = deepcopy(PRICES), deepcopy(BOSS)
        prices["offers"][0]["price"] = 17; boss["max_hp"] += 50000
        self.console.rows[(shop.COLLECTION, shop.KEY)] = row(shop.COLLECTION, shop.KEY, prices)
        self.console.rows[("duel_global_boss", "config")] = row("duel_global_boss", "config", boss)
        result = release.actions(self.guarded, TOKEN, PRICES, BOSS, HELPERS, release.fresh_backup(self.root), True)
        self.assertEqual(result["shop"]["mode"], "preserved_existing")
        self.assertEqual(result["boss"]["mode"], "preserved_existing")
        self.assertEqual(len(self.console.writes), 1)
        self.assertEqual(self.console.rows[(shop.COLLECTION, shop.KEY)]["value"], prices)
        self.assertEqual(self.console.rows[("duel_global_boss", "config")]["value"], boss)

    def test_second_apply_is_exactly_idempotent(self):
        release.actions(self.guarded, TOKEN, PRICES, BOSS, HELPERS, release.fresh_backup(self.root), True)
        result = release.actions(self.guarded, TOKEN, PRICES, BOSS, HELPERS, release.fresh_backup(self.root), True)
        self.assertEqual(len(self.console.writes), 3)
        self.assertEqual(result["items"]["mode"], "unchanged")
        self.assertEqual(result["shop"]["mode"], "preserved_existing")
        self.assertEqual(result["boss"]["mode"], "preserved_existing")

    def test_token_cas_race_fails_closed_with_original_backup(self):
        self.console.race = ("duel_match3_item_defs", "catalog")
        backup = release.fresh_backup(self.root)
        with self.assertRaises(admin.AdminError):
            release.actions(self.guarded, TOKEN, PRICES, BOSS, HELPERS, backup, True)
        self.assertEqual(self.console.writes, [])
        self.assertEqual(json.loads((backup / "items-before.json").read_text())["value"], OLD)
        self.assertEqual(self.console.rows[self.console.race]["version"], "operator")

    def test_boss_create_race_never_overwrites_new_admin_config(self):
        self.console.race = ("duel_global_boss", "config")
        with self.assertRaises(admin.AdminError):
            release.install_boss(self.guarded, BOSS, admin, release.fresh_backup(self.root) / "boss.json", True)
        self.assertEqual(self.console.writes, [])
        self.assertTrue(self.console.rows[self.console.race]["value"]["operator_preserved"])

    def test_changed_runtime_before_write_blocks_all_storage_mutation(self):
        (self.root / "runtime/duel_shop.lua").write_text("changed")
        with self.assertRaises(release.InstallError):
            release.actions(self.guarded, TOKEN, PRICES, BOSS, HELPERS, release.fresh_backup(self.root), True)
        self.assertEqual(self.console.writes, [])

    def test_row_identity_requires_explicit_consistent_system_ownership(self):
        value = self.console.rows[("duel_match3_item_defs", "catalog")]
        value["userId"] = "other-user"
        with self.assertRaises(release.InstallError):
            self.guarded.read("duel_match3_item_defs", "catalog")
        with self.assertRaises(release.InstallError):
            self.guarded.read("player_wallet", "v1")

    def test_proxy_rejects_boss_update_or_item_catalog_create(self):
        for collection, key, version in (("duel_global_boss", "config", "v1"), ("duel_shop_catalog", "v1", "v1"), ("duel_match3_item_defs", "catalog", "*")):
            with self.assertRaises(release.InstallError):
                self.guarded.write(collection, key, {}, version)
        self.assertEqual(self.console.writes, []); self.assertEqual(self.guards, 0)

    def test_backups_are_fresh_and_existing_backup_cannot_be_replaced(self):
        first, second = release.fresh_backup(self.root), release.fresh_backup(self.root)
        self.assertNotEqual(first, second); self.assertEqual(first.parent, self.root / "backups")
        saved = first / "items-before.json"; saved.write_text("preserved")
        with self.assertRaises(FileExistsError):
            release.actions(self.guarded, TOKEN, PRICES, BOSS, HELPERS, first, True)
        self.assertEqual(saved.read_text(), "preserved"); self.assertEqual(self.console.writes, [])

    def test_bundle_payload_hashes_and_no_extra_files(self):
        bundle = self.root / "release0313-catalogs-abcdef123456"; bundle.mkdir()
        hashes = {}
        for name in release.PAYLOADS:
            (bundle / name).write_text(name)
            hashes[name] = hashlib.sha256((bundle / name).read_bytes()).hexdigest()
        manifest = bundle / "bundle-manifest.json"
        manifest.write_text(json.dumps({"schema": 1, "bundle_id": bundle.name, "files": hashes}))
        release.verify_bundle(bundle)
        (bundle / "server.yml").write_text("must never be bundled")
        with self.assertRaises(release.InstallError):
            release.verify_bundle(bundle)
        (bundle / "server.yml").unlink(); (bundle / "storage_admin.py").write_text("tampered")
        with self.assertRaises(release.InstallError):
            release.verify_bundle(bundle)

    def test_json_duplicates_and_nan_are_rejected(self):
        path = self.root / "bad.json"
        for value in ('{"sha256":"a","sha256":"b"}', '{"value":NaN}'):
            path.write_text(value)
            with self.assertRaises(release.InstallError):
                release.document(path)


if __name__ == "__main__":
    unittest.main()
