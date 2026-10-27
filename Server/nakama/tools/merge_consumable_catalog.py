#!/usr/bin/env python3
"""Merge only the four 0.3.9 consumables; preserve every existing catalog entry.

Offline: --input exported-catalog.json --output NEW-plan.json
Server-local/tunnel: --url http://127.0.0.1:27351 --config secrets/server.yml
Read-only by default. --apply requires a NEW --backup path and uses exact CAS.
"""
import argparse
from copy import deepcopy
import getpass
import json
from pathlib import Path
import sys

from storage_admin import AdminError, Console, SYSTEM_USER_ID, read_json, strict_json, write_json_new

IDS = frozenset(("elixir_assault", "elixir_stone", "elixir_gambit", "scroll_pve_bounty"))
COLLECTION, KEY = "duel_match3_item_defs", "catalog"
DEFAULT_MANIFEST = Path(__file__).resolve().parents[1] / "data/migrations/consumables-0.3.9.json"


def validate_manifest(manifest):
    if (not isinstance(manifest, dict) or type(manifest.get("schema")) is not int or manifest.get("schema") != 1
            or manifest.get("migration") != "consumables-0.3.9" or manifest.get("catalog_version") != 4
            or manifest.get("system_collection") != COLLECTION or manifest.get("system_key") != KEY
            or manifest.get("system_user_id") != SYSTEM_USER_ID or set(manifest.get("items", {})) != IDS):
        raise AdminError("Expected the fixed schema1/catalog4 consumables-0.3.9 manifest and exactly four allowed IDs")
    for item_id, definition in manifest["items"].items():
        expected = {"kind": "consumable", "tier": 1, "quality": "epic" if item_id in ("elixir_gambit", "scroll_pve_bounty") else "rare",
                    "max_stack": 20, "sale_price": 0, "effect_id": item_id}
        if definition != expected or any(type(definition.get(k)) is not int for k in ("tier", "max_stack", "sale_price")):
            raise AdminError("The manifest's fixed consumable definition differs: " + item_id)
    return manifest["items"]


def merge_catalog(current, manifest):
    delta = validate_manifest(manifest)
    if not isinstance(current, dict) or not isinstance(current.get("items"), dict) or not current["items"]:
        raise AdminError("Refusing to replace/create an absent existing catalog; use the normal isolated seed for new deployments")
    if type(current.get("version")) is not int or current["version"] < 1:
        raise AdminError("The existing catalog version must be an integer")
    merged = deepcopy(current)
    added = []
    for item_id in sorted(IDS):
        if item_id in merged["items"]:
            if merged["items"][item_id] != delta[item_id]:
                raise AdminError("An allowed ID already exists with a different definition; refusing to overwrite: " + item_id)
        else:
            merged["items"][item_id] = deepcopy(delta[item_id])
            added.append(item_id)
    merged["version"] = max(4, current["version"])
    for item_id, definition in current["items"].items():
        if merged["items"][item_id] != definition:
            raise AdminError("An existing definition changed")
    return merged, added


def verify_row(row):
    if (not isinstance(row, dict) or row.get("collection") != COLLECTION or row.get("key") != KEY
            or row.get("user_id", row.get("userId")) != SYSTEM_USER_ID or not isinstance(row.get("version"),str) or not row.get("version")
            or row["version"] == "*"):
        raise AdminError("Expected the existing, explicitly system-owned catalog and its exact storage version")


def merge_console(console, manifest, backup=None, apply=False):
    row = console.read(COLLECTION, KEY)
    verify_row(row)
    current = row["value"]
    merged, added = merge_catalog(current, manifest)
    changed = merged != current
    result = {"mode": "unchanged" if not changed else "planned", "added_ids": added,
              "existing_definitions_preserved": len(current["items"]), "result_count": len(merged["items"]),
              "catalog_version": merged["version"], "storage_version": row["version"]}
    if not changed or not apply:
        return result
    if backup is None:
        raise AdminError("Apply requires a NEW backup path")
    # The full previous envelope includes its version. O_EXCL refuses replacement
    # of any older backup. A concurrent edit makes the subsequent CAS fail closed.
    write_json_new(backup, row)
    console.write(COLLECTION, KEY, merged, row["version"])
    after = console.read(COLLECTION, KEY)
    verify_row(after)
    if after["value"] != merged:
        raise AdminError("Post-write catalog verification failed; inspect the saved backup before any further operation")
    result.update(mode="applied", storage_version=after["version"])
    return result


def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--manifest", type=Path, default=DEFAULT_MANIFEST)
    p.add_argument("--input", type=Path, help="Offline exported value or complete storage envelope")
    p.add_argument("--output", type=Path, help="Write the merged offline value to a NEW file")
    p.add_argument("--url", help="Explicit Console URL; HTTPS or loopback HTTP")
    p.add_argument("--config", type=Path)
    p.add_argument("--username", default="escape-admin")
    p.add_argument("--backup", type=Path)
    p.add_argument("--apply", action="store_true")
    args = p.parse_args(argv)
    manifest = read_json(args.manifest)
    validate_manifest(manifest)
    if args.input:
        if args.apply or args.url:
            p.error("Offline input cannot perform Console writes")
        current = read_json(args.input)
        if isinstance(current, dict) and "value" in current:
            current = current["value"]
        if isinstance(current, str):
            current = strict_json(current)
        merged, added = merge_catalog(current, manifest)
        if args.output:
            write_json_new(args.output, merged)
        print(json.dumps({"mode": "offline_plan", "added_ids": added, "existing_preserved": len(current["items"]),
                          "result_count": len(merged["items"]), "output_written": bool(args.output)}, ensure_ascii=False))
        return
    if not args.url or args.output:
        p.error("Choose --input for offline planning, or provide an explicit --url for Console read/CAS")
    if args.apply and not args.backup:
        p.error("--apply requires a NEW --backup path")
    if args.config:
        settings = read_json(args.config).get("console", {})
        username, password = settings.get("username"), settings.get("password")
        if not username or not password:
            raise AdminError("Config must contain console credentials")
    else:
        username = args.username
        password = getpass.getpass("Console password: ")
    console = Console(args.url)
    try:
        console.login(username, password)
        password = None
        print(json.dumps(merge_console(console, manifest, args.backup, args.apply), ensure_ascii=False))
    finally:
        console.close()


if __name__ == "__main__":
    try:
        main()
    except (AdminError, OSError, ValueError, KeyError) as error:
        print("ERROR: " + str(error), file=sys.stderr)
        sys.exit(1)
