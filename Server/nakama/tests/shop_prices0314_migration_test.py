"""Pure CAS/operator preservation guarantees for the four-offer0.3.14 price update."""
from copy import deepcopy
import json
from pathlib import Path
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "Server/nakama/tools"))
import merge_shop_prices0314 as migration
from storage_admin import AdminError, SYSTEM_USER_ID

MANIFEST = json.loads((ROOT / "Design/Release-0.3.14/shop-price-migration.json").read_text(encoding="utf-8"))
OLD = json.loads((ROOT / "Server/nakama/tests/fixtures/shop-catalog-0.3.13.json").read_text(encoding="utf-8"))
NEW = json.loads((ROOT / "Server/nakama/data/duel_shop_catalog.json").read_text(encoding="utf-8"))
DEFS = json.loads((ROOT / "Server/nakama/data/duel_match3_item_defs.json").read_text(encoding="utf-8"))["items"]


class Console:
    def __init__(self):
        self.row = {"collection": migration.COLLECTION, "key": migration.KEY, "user_id": SYSTEM_USER_ID, "version": "old-version", "value": deepcopy(OLD)}
        self.writes = 0; self.race = self.lost_ack = False
    def read(self, collection, key):
        assert (collection, key) == (migration.COLLECTION, migration.KEY)
        return deepcopy(self.row)
    def write(self, collection, key, value, version):
        assert (collection, key) == (migration.COLLECTION, migration.KEY)
        if self.race:
            self.row["version"] = "operator"
            self.row["value"]["operator"] = True
        if version != self.row["version"]:
            raise AdminError("CAS conflict")
        self.writes += 1; self.row["value"] = deepcopy(value); self.row["version"] = "new-version"
        if self.lost_ack:
            raise AdminError("Lost acknowledgement")


class Tests(unittest.TestCase):
    def setUp(self):
        scratch = ROOT / ".cache/qa/shop-prices"
        scratch.mkdir(parents=True, exist_ok=True)
        self.temp = tempfile.TemporaryDirectory(dir=scratch)
        self.addCleanup(self.temp.cleanup); self.backup = Path(self.temp.name) / "before.json"

    def test_only_four_expected_offers_change_and_metadata_is_preserved(self):
        current = deepcopy(OLD); current["operator_metadata"] = {"keep": [1, 2]}
        merged, report = migration.merge_catalog(current, MANIFEST)
        self.assertEqual(set(report["updated_offers"]), set(migration.FINGERPRINTS))
        self.assertEqual(merged["operator_metadata"], current["operator_metadata"])
        self.assertEqual([o["id"] for o in merged["offers"]], [o["id"] for o in current["offers"]])
        for before, after in zip(current["offers"], merged["offers"]):
            if before["id"] not in migration.FINGERPRINTS: self.assertEqual(before, after)
        expected = deepcopy(NEW); expected["operator_metadata"] = current["operator_metadata"]
        self.assertEqual(merged, expected)

    def test_admin_price_text_quantity_and_metadata_edits_are_preserved(self):
        for field, value in (("price", 17), ("description", "operator"), ("items", [{"def_id": "ingot_purple", "amount": 40}]), ("operator_note", "keep")):
            current = deepcopy(OLD); offer = next(o for o in current["offers"] if o["id"] == "ingots_purple"); offer[field] = value
            merged, report = migration.merge_catalog(current, MANIFEST)
            self.assertEqual(next(o for o in merged["offers"] if o["id"] == "ingots_purple"), offer)
            self.assertEqual(report["preserved_admin_offers"], ["ingots_purple"])
            self.assertEqual(len(report["updated_offers"]), 3)

    def test_removed_offer_is_not_restored(self):
        current = deepcopy(OLD); current["offers"] = [o for o in current["offers"] if o["id"] != "craft_pack"]
        merged, report = migration.merge_catalog(current, MANIFEST)
        self.assertEqual(report["preserved_missing_offers"], ["craft_pack"])
        self.assertNotIn("craft_pack", [o["id"] for o in merged["offers"]])

    def test_read_only_and_second_apply_write_nothing(self):
        console = Console(); result = migration.merge_console(console, MANIFEST)
        self.assertEqual(result["mode"], "planned"); self.assertEqual(console.writes, 0); self.assertFalse(self.backup.exists())
        migration.merge_console(console, MANIFEST, self.backup, True)
        self.assertEqual(json.loads(self.backup.read_text(encoding="utf-8"))["value"], OLD)
        self.assertEqual(migration.merge_console(console, MANIFEST, self.backup, True)["mode"], "unchanged")
        self.assertEqual(console.writes, 1)

    def test_missing_or_ambiguous_owner_row_is_refused(self):
        for bad in (None, {"userId": "foreign"}, {"version": "*"}, {"collection": "player_wallet"}):
            console = Console()
            if bad is None: console.row = None
            else: console.row.update(bad)
            with self.assertRaises(AdminError): migration.merge_console(console, MANIFEST, self.backup, True)
            self.assertEqual(console.writes, 0)

    def test_concurrent_operator_change_fails_closed_with_backup(self):
        console = Console(); console.race = True
        with self.assertRaises(AdminError): migration.merge_console(console, MANIFEST, self.backup, True)
        self.assertEqual(console.writes, 0); self.assertTrue(console.row["value"]["operator"])
        self.assertEqual(json.loads(self.backup.read_text(encoding="utf-8"))["value"], OLD)

    def test_lost_ack_retry_rereads_committed_catalog(self):
        console = Console(); console.lost_ack = True
        with self.assertRaises(AdminError): migration.merge_console(console, MANIFEST, self.backup, True)
        self.assertEqual(migration.merge_console(console, MANIFEST)["mode"], "unchanged"); self.assertEqual(console.writes, 1)

    def test_manifest_cannot_change_fixed_prices_or_introduce_recipes(self):
        for field, value in (("price", 1), ("items", [{"def_id": "recipe_gold_Helmet", "amount": 1}])):
            bad = deepcopy(MANIFEST); bad["offers"]["ingots_purple"]["replacement"][field] = value
            with self.assertRaises(AdminError): migration.validate_manifest(bad)

    def test_existing_backup_and_duplicate_ids_refuse_writes(self):
        self.backup.write_text("preserved"); console = Console()
        with self.assertRaises(FileExistsError): migration.merge_console(console, MANIFEST, self.backup, True)
        self.assertEqual(console.writes, 0); self.assertEqual(self.backup.read_text(), "preserved")
        current = deepcopy(OLD); current["offers"].append(deepcopy(current["offers"][0]))
        with self.assertRaises(AdminError): migration.merge_catalog(current, MANIFEST)

    def test_all_singles_and_bundles_resist_buy_sell_gold_arbitrage(self):
        offers = {o["id"]: o for o in NEW["offers"]}
        for offer in offers.values():
            if offer["currency"] != "matter": continue
            direct_gold = offer["gold"] + sum(DEFS[i["def_id"]].get("sale_price", 0) * i["amount"] for i in offer.get("items", []))
            self.assertLessEqual(direct_gold, offer["price"] * 250, offer["id"])
        for color, price in (("green", 3), ("blue", 4), ("purple", 5)):
            self.assertEqual((offers["ingots_" + color]["price"], offers["ingots_" + color]["items"][0]["amount"]), (price, 20))
        self.assertEqual(offers["craft_pack"]["price"], 30)
        self.assertEqual(offers["rub_resources_99"]["price"], 99)
        self.assertEqual(offers["boss_teleport"]["price"], 30000)


if __name__ == "__main__": unittest.main()
