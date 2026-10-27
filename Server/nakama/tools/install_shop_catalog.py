#!/usr/bin/env python3
"""Create the system-owned shop price catalog once; never overwrite admin edits.

Read-only by default. --apply requires a new backup file and create-only CAS.
Prices remain editable through Storage duel_shop_catalog/v1 after installation.
"""
import argparse
import getpass
import json
from pathlib import Path
import sys
from storage_admin import AdminError, Console, SYSTEM_USER_ID, read_json, write_json_new

COLLECTION, KEY = "duel_shop_catalog", "v1"
DEFAULT = Path(__file__).resolve().parents[1] / "data/duel_shop_catalog.json"
ITEMS = {"ingot_green", "ingot_blue", "ingot_purple", "tesseract", "global_boss_teleport", "elixir_assault", "elixir_stone", "elixir_gambit", "scroll_pve_bounty"}


def validate(value):
    if not isinstance(value, dict) or value.get("schema") != 1 or not isinstance(value.get("offers"), list) or not 1 <= len(value["offers"]) <= 64:
        raise AdminError("Expected schema1 and one to64 shop offers")
    seen = set()
    for offer in value["offers"]:
        if not isinstance(offer, dict) or not isinstance(offer.get("id"), str) or not offer["id"] or offer["id"] in seen:
            raise AdminError("Shop IDs must be nonempty and unique")
        seen.add(offer["id"])
        if offer.get("currency") not in ("matter", "gold", "rub") or type(offer.get("price")) is not int or not 1 <= offer["price"] <= 1_000_000_000:
            raise AdminError("Shop currency or price is invalid")
        if offer.get("tab") not in ("bundles", "resources", "supplies", "rub") or (offer["tab"] == "rub") != (offer["currency"] == "rub"):
            raise AdminError("Shop tab and currency disagree")
        if not isinstance(offer.get("title"), str) or not offer["title"]:
            raise AdminError("Shop title is required")
        for name in ("gold", "ore", "matter", "energy"):
            if type(offer.get(name, 0)) is not int or not 0 <= offer.get(name, 0) <= 1_000_000_000:
                raise AdminError("Shop reward must be a bounded nonnegative integer")
        if offer["currency"] != "rub" and offer.get("matter", 0):
            raise AdminError("Only disabled real-money offers may grant matter")
        for item in offer.get("items", []):
            if item.get("def_id") not in ITEMS or type(item.get("amount")) is not int or not 1 <= item["amount"] <= 1_000_000:
                raise AdminError("Shop item is outside the fixed recipe-free allowlist")
    return value


def install(console, catalog, backup=None, apply=False):
    validate(catalog)
    row = console.read(COLLECTION, KEY)
    if row is not None:
        if row.get("collection") != COLLECTION or row.get("key") != KEY or row.get("user_id", row.get("userId")) != SYSTEM_USER_ID:
            raise AdminError("Expected the explicitly system-owned shop catalog")
        validate(row["value"])
        return {"mode": "preserved_existing", "offers": len(row["value"]["offers"]), "storage_version": row["version"]}
    if not apply:
        return {"mode": "planned_create", "offers": len(catalog["offers"])}
    if backup is None:
        raise AdminError("Apply requires a NEW backup path")
    write_json_new(backup, {"collection": COLLECTION, "key": KEY, "user_id": SYSTEM_USER_ID, "existed": False})
    console.write(COLLECTION, KEY, catalog, "*")
    after = console.read(COLLECTION, KEY)
    if after is None or after["value"] != catalog:
        raise AdminError("Shop catalog post-write verification failed")
    return {"mode": "created", "offers": len(catalog["offers"]), "storage_version": after["version"]}


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--catalog", type=Path, default=DEFAULT)
    p.add_argument("--url", required=True)
    p.add_argument("--config", type=Path)
    p.add_argument("--username", default="escape-admin")
    p.add_argument("--apply", action="store_true")
    p.add_argument("--backup", type=Path)
    a = p.parse_args()
    if a.apply and not a.backup:
        p.error("--apply requires --backup")
    data = read_json(a.config).get("console", {}) if a.config else {}
    username = data.get("username", a.username)
    password = data.get("password") if a.config else getpass.getpass("Console password: ")
    if not username or not password:
        raise AdminError("Console credentials are required")
    console = Console(a.url)
    try:
        console.login(username, password)
        print(json.dumps(install(console, read_json(a.catalog), a.backup, a.apply), ensure_ascii=False))
    finally:
        console.close()


if __name__ == "__main__":
    try:
        main()
    except (AdminError, ValueError, OSError, KeyError, TypeError) as error:
        print("ERROR: " + str(error), file=sys.stderr)
        sys.exit(1)
