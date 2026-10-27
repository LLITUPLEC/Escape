"""Offline token migration and create-only editable shop catalog guarantees."""
from copy import deepcopy
import json
from pathlib import Path
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "Server/nakama/tools"))
import merge_shop_catalog as token
import install_shop_catalog as shop
from storage_admin import AdminError, SYSTEM_USER_ID

MANIFEST = json.loads((ROOT / "Server/nakama/data/migrations/shop-items-0.3.13.json").read_text(encoding="utf-8"))
CATALOG = json.loads((ROOT / "Server/nakama/data/duel_match3_item_defs.json").read_text(encoding="utf-8"))
PRICES = json.loads((ROOT / "Server/nakama/data/duel_shop_catalog.json").read_text(encoding="utf-8"))
OLD = deepcopy(CATALOG)
OLD["items"].pop("global_boss_teleport")
OLD["version"] = 4
TEMP = (ROOT / ".cache/qa/shop-catalog-tests").resolve()
assert TEMP.is_relative_to(ROOT.resolve())
TEMP.mkdir(parents=True, exist_ok=True)


class Console:
    def __init__(self, collection, key, value=None):
        self.collection, self.key = collection, key
        self.row = None if value is None else {"collection": collection, "key": key, "user_id": SYSTEM_USER_ID, "version": "v1", "value": deepcopy(value)}
        self.writes = 0
        self.lost_ack = self.race = False

    def read(self, collection, key):
        assert (collection, key) == (self.collection, self.key)
        return deepcopy(self.row)

    def write(self, collection, key, value, version):
        assert (collection, key) == (self.collection, self.key)
        if self.race:
            self.row = {"collection": collection, "key": key, "user_id": SYSTEM_USER_ID, "version": "operator", "value": deepcopy(PRICES)}
        if version == "*" and self.row is not None or version != "*" and (self.row is None or self.row["version"] != version):
            raise AdminError("CAS conflict")
        self.writes += 1
        self.row = {"collection": collection, "key": key, "user_id": SYSTEM_USER_ID, "version": "v2", "value": deepcopy(value)}
        if self.lost_ack:
            self.lost_ack = False
            raise AdminError("Lost acknowledgement")


def backup():
    folder = Path(tempfile.mkdtemp(dir=TEMP)).resolve()
    assert folder.is_relative_to(TEMP)
    return folder / "before.json"


class Tests(unittest.TestCase):
    def test_token_preserves_all72_definitions_and_operator_metadata(self):
        current = deepcopy(OLD)
        current["operator"] = {"keep": [1, 2]}
        merged, added = token.merge_catalog(current, MANIFEST)
        self.assertEqual(added, ["global_boss_teleport"])
        self.assertEqual(len(merged["items"]), 73)
        self.assertEqual(merged["operator"], current["operator"])
        for key, value in current["items"].items():
            self.assertEqual(merged["items"][key], value)
        self.assertEqual(len(current["items"]), 72)

    def test_conflicting_existing_token_is_never_overwritten(self):
        current = deepcopy(CATALOG)
        current["items"]["global_boss_teleport"]["sale_price"] = 30000
        with self.assertRaises(AdminError):
            token.merge_catalog(current, MANIFEST)
        self.assertEqual(current["items"]["global_boss_teleport"]["sale_price"], 30000)

    def test_manifest_cannot_introduce_recipes_or_altered_kind(self):
        bad = deepcopy(MANIFEST)
        bad["items"]["global_boss_teleport"]["kind"] = "recipe"
        with self.assertRaises(AdminError):
            token.validate_manifest(bad)
        bad = deepcopy(MANIFEST)
        bad["items"]["recipe_t11"] = bad["items"]["global_boss_teleport"]
        with self.assertRaises(AdminError):
            token.validate_manifest(bad)

    def test_token_CAS_backup_and_idempotent_replay(self):
        c = Console(token.COLLECTION, token.KEY, OLD)
        path = backup()
        result = token.merge_console(c, MANIFEST, path, True)
        self.assertEqual(result["mode"], "applied")
        self.assertEqual(json.loads(path.read_text())["value"], OLD)
        self.assertEqual(token.merge_console(c, MANIFEST)["mode"], "unchanged")
        self.assertEqual(c.writes, 1)

    def test_lost_token_ack_retry_preserves_committed_definition(self):
        c = Console(token.COLLECTION, token.KEY, OLD)
        c.lost_ack = True
        with self.assertRaises(AdminError):
            token.merge_console(c, MANIFEST, backup(), True)
        self.assertEqual(token.merge_console(c, MANIFEST)["mode"], "unchanged")
        self.assertEqual(c.writes, 1)

    def test_shop_default_create_dry_run_then_exact_create_only(self):
        c = Console(shop.COLLECTION, shop.KEY)
        self.assertEqual(shop.install(c, PRICES)["mode"], "planned_create")
        self.assertEqual(c.writes, 0)
        path = backup()
        self.assertEqual(shop.install(c, PRICES, path, True)["mode"], "created")
        self.assertEqual(c.row["value"], PRICES)
        self.assertFalse(json.loads(path.read_text())["existed"])

    def test_shop_preserves_existing_admin_prices(self):
        operator = deepcopy(PRICES)
        operator["offers"][0]["price"] = 17
        c = Console(shop.COLLECTION, shop.KEY, operator)
        self.assertEqual(shop.install(c, PRICES, backup(), True)["mode"], "preserved_existing")
        self.assertEqual(c.row["value"], operator)
        self.assertEqual(c.writes, 0)

    def test_shop_create_race_never_overwrites_operator_catalog(self):
        c = Console(shop.COLLECTION, shop.KEY)
        c.race = True
        with self.assertRaises(AdminError):
            shop.install(c, PRICES, backup(), True)
        self.assertEqual(c.row["version"], "operator")
        self.assertEqual(c.writes, 0)

    def test_resource_anchor_and_item_sale_values_do_not_mint_gold(self):
        offers = {x["id"]: x for x in PRICES["offers"]}
        self.assertEqual((offers["rub_resources_99"]["price"], offers["rub_resources_99"]["gold"], offers["rub_resources_99"]["ore"]), (99, 10000, 10000))
        self.assertEqual((offers["rub_matter_40"]["price"], offers["rub_matter_40"]["matter"]), (99, 40))
        self.assertEqual((offers["resources_10000"]["price"], offers["resources_10000"]["gold"], offers["resources_10000"]["ore"]), (40, 10000, 10000))
        self.assertEqual((offers["boss_teleport"]["currency"], offers["boss_teleport"]["price"]), ("gold", 30000))
        for offer in offers.values():
            if offer["currency"] != "matter":
                continue
            sale = offer["gold"] + sum(CATALOG["items"][x["def_id"]].get("sale_price", 0) * x["amount"] for x in offer.get("items", []))
            self.assertLessEqual(sale, offer["price"] * 250)


if __name__ == "__main__":
    unittest.main()
