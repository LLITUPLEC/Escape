#!/usr/bin/env python3
"""Append selected achievement definitions from a canonical JSON catalog.

Read-only by default. Apply needs the preflight Storage version and a new backup
path for the complete previous Storage record. Only the selected IDs are added.
"""
import argparse
from copy import deepcopy
import getpass
import json
from pathlib import Path
import sys

from storage_admin import AdminError, Console, RECORDS, SYSTEM_USER_ID, read_json, strict_json, validate_value, write_json_new

COLLECTION, KEY = RECORDS["achievements"]


def valid_id(value):
    return isinstance(value, str) and bool(value) and value == value.strip()


def index_entries(entries, field):
    if isinstance(entries, list):
        entries = ((None, row) for row in entries)
    elif isinstance(entries, dict):
        entries = entries.items()
    else:
        raise AdminError(f"Expected {field} to be an array or object")
    indexed = {}
    for key, row in entries:
        if not isinstance(row, dict):
            raise AdminError(f"Expected an object in {field}")
        entry_id = row.get("id")
        if entry_id in (None, ""):
            entry_id = key
        if not valid_id(entry_id) or entry_id in indexed:
            raise AdminError(f"Expected unique nonempty IDs in {field}")
        indexed[entry_id] = row
    return indexed


def selected_ids(ids, canonical, field):
    if any(not valid_id(entry_id) for entry_id in ids) or len(set(ids)) != len(ids):
        raise AdminError(f"Select unique nonempty IDs from {field}")
    missing = set(ids) - canonical.keys()
    if missing:
        raise AdminError(f"Missing canonical {field} IDs: {', '.join(sorted(missing))}")
    return set(ids)


def same_json(first, second):
    return json.dumps(first, sort_keys=True, allow_nan=False) == json.dumps(second, sort_keys=True, allow_nan=False)


def append_selected(target, current, source, selected, field):
    added, already = [], []
    for entry_id, row in source.items():
        if entry_id not in selected:
            continue
        entry = dict(row, id=entry_id)
        if entry_id in current:
            if not same_json(dict(current[entry_id], id=entry_id), entry):
                raise AdminError(f"Existing {field} ID differs from canonical: {entry_id}")
            already.append(entry_id)
            continue
        if isinstance(target, list):
            target.append(deepcopy(entry))
        else:
            if entry_id in target:
                raise AdminError(f"Existing {field} object key conflicts with ID: {entry_id}")
            target[entry_id] = deepcopy(entry)
        added.append(entry_id)
    return added, already


def merge_catalog(current, canonical, category_ids, chain_ids):
    validate_value("achievements", current)
    validate_value("achievements", canonical)
    current_categories = index_entries(current.get("categories", []), "categories")
    current_chains = index_entries(current["chains"], "chains")
    source_categories = index_entries(canonical.get("categories", []), "canonical categories")
    source_chains = index_entries(canonical["chains"], "canonical chains")
    categories = selected_ids(category_ids, source_categories, "category")
    chains = selected_ids(chain_ids, source_chains, "chain")
    if not categories and not chains:
        raise AdminError("Select at least one category or chain ID")

    # A legacy catalog can omit the category table and still label each chain.
    known_categories = current_categories.keys() | categories
    known_categories.update(row["category"] for row in current_chains.values() if valid_id(row.get("category")))
    for chain_id in chains:
        chain = source_chains[chain_id]
        category = chain.get("category")
        if not valid_id(category) or category not in known_categories:
            raise AdminError(f"Selected chain has no installed or selected category: {chain_id}")
        if not valid_id(chain.get("counter_key")):
            raise AdminError(f"Selected chain has no valid counter key: {chain_id}")
        for step in chain["steps"]:
            if not isinstance(step, dict) or type(step.get("threshold_delta")) is not int or step["threshold_delta"] <= 0:
                raise AdminError(f"Selected chain has an invalid threshold: {chain_id}")

    merged = deepcopy(current)
    added_categories, present_categories = [], []
    if categories:
        added_categories, present_categories = append_selected(
            merged.setdefault("categories", []), current_categories, source_categories, categories, "category"
        )
    added_chains, present_chains = append_selected(merged["chains"], current_chains, source_chains, chains, "chain")
    return merged, {
        "added_categories": added_categories,
        "added_chains": added_chains,
        "present_categories": present_categories,
        "present_chains": present_chains,
        "total_categories": len(current_categories) + len(added_categories),
        "total_chains": len(current_chains) + len(added_chains),
    }


def exact_version(version):
    return valid_id(version) and version != "*"


def verify_row(row):
    owners = [row[field] for field in ("user_id", "userId") if field in row] if isinstance(row, dict) else []
    if (not isinstance(row, dict) or row.get("collection") != COLLECTION or row.get("key") != KEY
            or not owners or any(owner != SYSTEM_USER_ID for owner in owners)
            or not exact_version(row.get("version")) or not isinstance(row.get("value"), (dict, str))):
        raise AdminError("Expected the existing system-owned achievement catalog and an exact Storage version")
    for field in ("permission_read", "permission_write", "permissionRead", "permissionWrite"):
        if field in row and (type(row[field]) is not int or row[field] != 0):
            raise AdminError("Expected private permissions on the achievement catalog")
    return strict_json(row["value"]) if isinstance(row["value"], str) else row["value"]


def merge_console(console, canonical, category_ids, chain_ids, *, expected_version=None, backup=None, apply=False):
    if apply and (not exact_version(expected_version) or backup is None):
        raise AdminError("Apply requires the exact preflight version and a new backup path")
    row = console.read(COLLECTION, KEY)
    current = verify_row(row)
    if apply and row["version"] != expected_version:
        raise AdminError("Storage version changed since preflight; run preflight again")
    merged, result = merge_catalog(current, canonical, category_ids, chain_ids)
    changed = bool(result["added_categories"] or result["added_chains"])
    result.update(mode="planned" if changed else "unchanged", storage_version=row["version"])
    if not apply or not changed:
        return result

    write_json_new(backup, row)
    ack = console.write(COLLECTION, KEY, merged, expected_version)
    after = console.read(COLLECTION, KEY)
    if not same_json(verify_row(after), merged) or after["version"] != ack.get("version"):
        raise AdminError("Read-back differs from the planned write; inspect the saved backup before any further write")
    return dict(result, mode="applied", storage_version=after["version"])


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    source = parser.add_mutually_exclusive_group(required=True)
    source.add_argument("--input", type=Path, help="Existing local catalog value or Console export")
    source.add_argument("--url", help="Explicit Console URL for live preflight and CAS")
    parser.add_argument("--canonical", type=Path, required=True)
    parser.add_argument("--category", action="append", default=[], help="Category ID to append (repeatable)")
    parser.add_argument("--chain", action="append", default=[], help="Chain ID to append (repeatable)")
    parser.add_argument("--output", type=Path, help="Write the offline merged catalog to a new path")
    parser.add_argument("--config", type=Path)
    parser.add_argument("--username", default="escape-admin")
    parser.add_argument("--apply", action="store_true")
    parser.add_argument("--expected-version", help="Exact Storage version returned by preflight")
    parser.add_argument("--backup", type=Path, help="New private path for the full previous Storage record")
    args = parser.parse_args(argv)
    if not args.category and not args.chain:
        parser.error("Select at least one --category or --chain")
    if args.apply and (not args.url or not exact_version(args.expected_version) or args.backup is None):
        parser.error("--apply requires --url, an exact --expected-version and a new --backup path")
    if not args.apply and (args.expected_version is not None or args.backup is not None):
        parser.error("--expected-version and --backup are apply options")
    if args.url and args.output:
        parser.error("--output is an offline option")

    canonical = read_json(args.canonical)
    if args.input:
        current = read_json(args.input)
        if isinstance(current, dict) and "chains" not in current and "value" in current:
            current = verify_row(current)
        merged, result = merge_catalog(current, canonical, args.category, args.chain)
        if args.output:
            write_json_new(args.output, merged)
        result.update(mode="offline_plan", output_written=args.output is not None)
    else:
        settings = read_json(args.config).get("console", {}) if args.config else {}
        username = settings.get("username", args.username)
        password = settings.get("password") if args.config else getpass.getpass(f"Console password for {username}: ")
        if not username or not password:
            raise AdminError("Console credentials are required")
        console = Console(args.url)
        try:
            console.login(username, password)
            result = merge_console(console, canonical, args.category, args.chain,
                                   expected_version=args.expected_version, backup=args.backup, apply=args.apply)
        finally:
            console.close()
    print(json.dumps(result, ensure_ascii=False))


if __name__ == "__main__":
    try:
        main()
    except (AdminError, OSError, ValueError, TypeError, KeyError, AttributeError) as error:
        print(f"ERROR: {error}", file=sys.stderr)
        sys.exit(1)
