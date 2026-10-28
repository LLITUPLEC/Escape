#!/usr/bin/env python3
"""CAS-update four unchanged0.3.13 shop offers; preserve operator edits and all other data.

Read-only by default. --apply requires a NEW backup file. No player/resource writes.
"""
import argparse
from copy import deepcopy
import getpass
import hashlib
import json
from pathlib import Path
import sys
from storage_admin import AdminError, Console, SYSTEM_USER_ID, read_json, write_json_new

COLLECTION, KEY = "duel_shop_catalog", "v1"
DEFAULT = Path(__file__).resolve().parents[3] / "Server/nakama/data/migrations/shop-price-0.3.14.json"
FINGERPRINTS = {
    "ingots_green": ("205d44474b400601ccc79b7e36550333e8d3090be9ccb7d7fd4492432a523513", "c3a20b97f7b0f2b7ac2651da984aa3442b5643065bf8064c5484cbb14a1bd58b"),
    "ingots_blue": ("372b9b8062732a83b21cc61ab3be7a5ccf051cb48fc141fe65bbc808799504f1", "7ac68ee2c0aa85a172eec88e7f624e39ed3561a90ee779e2a578d6733e0e8fda"),
    "ingots_purple": ("c19e3d0878b217356d88e1081309e3f2eddbedc8d7d4aafccdf7687283ccdea5", "16c7142f2068444697efae44bd98f53bc8b9b47471dbeba206d21d58dd669de9"),
    "craft_pack": ("db81a1c2c5c7ac0600b3bfc970e4d9992d89ad11f4f9594ba1036c95490869cb", "23262774e676031a0836f966cac3904f185c9b8bee62ea7365658660db84f424"),
}


def fingerprint(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, ensure_ascii=False, separators=(",", ":"), allow_nan=False).encode()).hexdigest()


def validate_manifest(manifest):
    if (not isinstance(manifest, dict) or type(manifest.get("schema")) is not int or manifest["schema"] != 1
            or manifest.get("migration") != "shop-0.3.14" or manifest.get("system_collection") != COLLECTION
            or manifest.get("system_key") != KEY or manifest.get("system_user_id") != SYSTEM_USER_ID
            or not isinstance(manifest.get("offers"), dict) or set(manifest["offers"]) != set(FINGERPRINTS)):
        raise AdminError("Expected the fixed shop-0.3.14 four-offer manifest")
    for offer_id, pair in manifest["offers"].items():
        if (not isinstance(pair, dict) or set(pair) != {"expected", "replacement"}
                or tuple(fingerprint(pair[k]) for k in ("expected", "replacement")) != FINGERPRINTS[offer_id]):
            raise AdminError("An expected or replacement offer differs from the fixed0.3.14 release")
    return manifest["offers"]


def merge_catalog(current, manifest):
    delta = validate_manifest(manifest)
    if not isinstance(current, dict) or current.get("schema") != 1 or not isinstance(current.get("offers"), list) or not current["offers"]:
        raise AdminError("Existing shop catalog is absent or malformed; refusing replacement")
    ids = [o.get("id") if isinstance(o, dict) else None for o in current["offers"]]
    if any(not isinstance(i, str) or not i for i in ids) or len(set(ids)) != len(ids):
        raise AdminError("Existing shop IDs must be nonempty and unique")
    merged = deepcopy(current)
    updated, preserved, already, missing = [], [], [], []
    for offer_id, pair in delta.items():
        if offer_id not in ids:
            missing.append(offer_id); continue
        index = ids.index(offer_id); before = current["offers"][index]
        if before == pair["replacement"]:
            already.append(offer_id)
        elif before == pair["expected"]:
            merged["offers"][index] = deepcopy(pair["replacement"]); updated.append(offer_id)
        else:
            preserved.append(offer_id)  # Any changed price, content, text or extra metadata is operator-owned.
    return merged, {"updated_offers": updated, "preserved_admin_offers": preserved,
        "already_current_offers": already, "preserved_missing_offers": missing}


def verify_row(row):
    owners = [row[k] for k in ("user_id", "userId") if k in row] if isinstance(row, dict) else []
    if (not isinstance(row, dict) or row.get("collection") != COLLECTION or row.get("key") != KEY
            or not owners or any(x != SYSTEM_USER_ID for x in owners) or not isinstance(row.get("value"), dict)
            or not isinstance(row.get("version"), str) or not row["version"] or row["version"] == "*"):
        raise AdminError("Expected existing explicitly system-owned shop catalog and exact CAS version")


def merge_console(console, manifest, backup=None, apply=False):
    row = console.read(COLLECTION, KEY); verify_row(row)
    merged, detail = merge_catalog(row["value"], manifest)
    result = dict(detail, mode="unchanged" if merged == row["value"] else "planned", storage_version=row["version"])
    if merged == row["value"] or not apply:
        return result
    if backup is None:
        raise AdminError("Apply requires a NEW backup path")
    write_json_new(backup, row)
    console.write(COLLECTION, KEY, merged, row["version"])
    after = console.read(COLLECTION, KEY); verify_row(after)
    if after["value"] != merged:
        raise AdminError("Post-write verification failed; inspect private backup before retrying")
    return dict(result, mode="applied", storage_version=after["version"])


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", type=Path, default=DEFAULT)
    parser.add_argument("--input", type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--url")
    parser.add_argument("--config", type=Path)
    parser.add_argument("--username", default="escape-admin")
    parser.add_argument("--backup", type=Path)
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args(argv); manifest = read_json(args.manifest); validate_manifest(manifest)
    if args.input:
        if args.apply or args.url:
            parser.error("Offline input cannot perform Console writes")
        current = read_json(args.input); current = current.get("value", current)
        merged, detail = merge_catalog(current, manifest)
        if args.output: write_json_new(args.output, merged)
        print(json.dumps(dict(detail, mode="offline_plan", output_written=bool(args.output)))); return
    if not args.url or args.output or args.apply and not args.backup:
        parser.error("Choose offline --input, or explicit --url; apply requires a NEW --backup")
    settings = read_json(args.config).get("console", {}) if args.config else {}
    username = settings.get("username", args.username)
    password = settings.get("password") if args.config else getpass.getpass("Console password: ")
    if not username or not password: raise AdminError("Console credentials are required")
    settings = None; console = Console(args.url)
    try:
        console.login(username, password); password = None
        print(json.dumps(merge_console(console, manifest, args.backup, args.apply), ensure_ascii=False))
    finally:
        password = None; console.close()


if __name__ == "__main__":
    try: main()
    except (AdminError, OSError, ValueError, KeyError, TypeError) as error:
        print("ERROR: " + str(error), file=sys.stderr); sys.exit(1)
