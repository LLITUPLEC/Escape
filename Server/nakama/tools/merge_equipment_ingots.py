#!/usr/bin/env python3
"""Patch only eight known pre-28.10.2026 epic ingot prices, preserving all other fields.

Offline: --input exported-catalog.json --output NEW-plan.json
Console: --url explicit-loopback-or-https --config private-config.json
Read-only by default. --apply requires a NEW private backup and exact Storage CAS.
Unknown/custom target prices fail closed; already updated prices are idempotent.
"""
import argparse
from copy import deepcopy
import getpass
import json
from pathlib import Path
import sys

from storage_admin import AdminError, Console, SYSTEM_USER_ID, read_json, strict_json, write_json_new

COLLECTION, KEY = "duel_match3_item_defs", "catalog"
# ID suffix: slot, old ingots, new ingots. This migration has no version/release side effects.
PATCH = {
    "helmet": ("Helmet", 8, 110), "chest": ("Chest", 25, 270),
    "gloves": ("Gloves", 50, 350), "weapon_l": ("WeaponLeft", 125, 460),
    "weapon_r": ("WeaponRight", 150, 510), "legs": ("Legs", 180, 560),
    "shoulders": ("Shoulders", 220, 670), "feet": ("Feet", 270, 740),
}


def merge_catalog(current):
    if not isinstance(current, dict) or not isinstance(current.get("items"), dict) or not current["items"]:
        raise AdminError("Refusing to create or replace an absent item catalog")
    merged, changed = deepcopy(current), []
    for suffix, (slot, old, new) in PATCH.items():
        item_id = "eq_t1_epic_" + suffix
        item = merged["items"].get(item_id)
        if (not isinstance(item, dict) or item.get("kind") != "equipment" or type(item.get("tier")) is not int or item["tier"] != 1
                or item.get("quality") != "epic" or item.get("slot") != slot or item.get("craft_ingot_def") != "ingot_purple"
                or item.get("craft_recipe_id") != "recipe_drop_t1_purple_" + slot):
            raise AdminError("Target identity/material/recipe differs; no write planned: " + item_id)
        count = item.get("craft_ingot_n")
        if type(count) is not int or count not in (old, new):
            raise AdminError("Unknown/custom ingot price preserved; no write planned: " + item_id)
        if count == old:
            item["craft_ingot_n"] = new
            changed.append(item_id)
    return merged, sorted(changed)


def verify_row(row):
    if (not isinstance(row, dict) or row.get("collection") != COLLECTION or row.get("key") != KEY
            or row.get("user_id", row.get("userId")) != SYSTEM_USER_ID or not isinstance(row.get("version"), str)
            or not row["version"] or row["version"] == "*" or row.get("permission_read", 0) != 0 or row.get("permission_write", 0) != 0):
        raise AdminError("Expected existing system-owned private catalog and exact non-wildcard Storage version")


def merge_console(console, backup=None, apply=False):
    row = console.read(COLLECTION, KEY)
    verify_row(row)
    merged, changed = merge_catalog(row["value"])
    result = {"mode": "planned" if changed else "unchanged", "changed_ids": changed,
              "preserved_definitions": len(merged["items"]), "storage_version": row["version"],
              "catalog_version_preserved": merged.get("version")}
    if not changed or not apply:
        return result
    if backup is None:
        raise AdminError("Apply requires a NEW private backup path")
    write_json_new(backup, row)
    # One exact-version write, no wildcard, retries or full-catalog canonical replacement.
    console.write(COLLECTION, KEY, merged, row["version"])
    after = console.read(COLLECTION, KEY)
    verify_row(after)
    if after["value"] != merged:
        raise AdminError("Post-write verification differs; inspect private backup before any further write")
    result.update(mode="applied", storage_version=after["version"])
    return result


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--url")
    parser.add_argument("--config", type=Path)
    parser.add_argument("--username", default="escape-admin")
    parser.add_argument("--backup", type=Path)
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args(argv)
    if args.input:
        if args.apply or args.url:
            parser.error("Offline planning cannot perform Console writes")
        current = read_json(args.input)
        if isinstance(current, dict) and "value" in current:
            current = current["value"]
        if isinstance(current, str):
            current = strict_json(current)
        merged, changed = merge_catalog(current)
        if args.output:
            write_json_new(args.output, merged)
        print(json.dumps({"mode": "offline_plan", "changed_ids": changed, "output_written": bool(args.output)}, ensure_ascii=False))
        return
    if not args.url or args.output:
        parser.error("Choose --input for offline planning, or explicit --url for Console CAS")
    if args.apply and not args.backup:
        parser.error("--apply requires a NEW --backup path")
    config = read_json(args.config).get("console", {}) if args.config else {}
    username = config.get("username", args.username)
    password = config.get("password") if args.config else getpass.getpass("Console password: ")
    if not username or not password:
        raise AdminError("Console credentials are required")
    console = Console(args.url)
    try:
        console.login(username, password)
        print(json.dumps(merge_console(console, args.backup, args.apply), ensure_ascii=False))
    finally:
        console.close()


if __name__ == "__main__":
    try:
        main()
    except (AdminError, OSError, ValueError, KeyError, TypeError) as error:
        print("ERROR: " + str(error), file=sys.stderr)
        sys.exit(1)
