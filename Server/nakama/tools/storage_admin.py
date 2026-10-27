#!/usr/bin/env python3
"""Administer Escape's known system-owned Storage through the Console API.

Python standard library only. Never uses the player API or adds an admin RPC.
See ../STORAGE_ADMIN.md for schemas, examples and SSH/server-local use.
"""

import argparse
import getpass
import json
import math
import os
from pathlib import Path
import sys
from urllib.error import HTTPError, URLError
from urllib.parse import quote, urlsplit
from urllib.request import HTTPRedirectHandler, Request, build_opener
import uuid


SYSTEM_USER_ID = "00000000-0000-0000-0000-000000000000"
RECORDS = {
    "items": ("duel_match3_item_defs", "catalog"),
    "achievements": ("duel_match3_achievement_defs", "catalog"),
    "bots": ("duel_match3_bot_defs", "catalog"),
    "bots-easy": ("duel_match3_bot_defs", "catalog_easy"),
    "bots-medium": ("duel_match3_bot_defs", "catalog_medium"),
    "bots-hard": ("duel_match3_bot_defs", "catalog_hard"),
    "contests": ("duel_match3_contest_goals", "config"),
    "aura": ("duel_match3_server_aura", "active"),
    "debug-preview": ("duel_match3_cheat_whitelist", "emails"),
    "global-boss": ("duel_global_boss", "config"),
    "shop": ("duel_shop_catalog", "v1"),
}
MAX_JSON_BYTES = 8 * 1024 * 1024


class AdminError(Exception):
    pass


class ApiError(AdminError):
    def __init__(self, status, version_conflict=False):
        self.status = status
        self.version_conflict = version_conflict
        # Do not echo response bodies: some Console endpoints contain secrets.
        detail = "Storage version conflict" if version_conflict else "Console API failure"
        super().__init__(f"{detail} (HTTP {status}); no automatic write retry was made")


class NoRedirects(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        raise AdminError("Console redirects are refused; use its final HTTPS URL")


def strict_json(text):
    def reject_constant(value):
        raise AdminError(f"Non-finite JSON number: {value}")
    return json.loads(text, parse_constant=reject_constant)


def read_json(path):
    path = Path(path)
    if path.stat().st_size > MAX_JSON_BYTES:
        raise AdminError("JSON file exceeds the 8 MiB limit")
    return strict_json(path.read_text(encoding="utf-8-sig"))


def write_json_new(path, value):
    """Never replace a previous export/backup; restrict new files on POSIX."""
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    with os.fdopen(fd, "w", encoding="utf-8", newline="\n") as output:
        json.dump(value, output, ensure_ascii=False, indent=2, allow_nan=False)
        output.write("\n")


def validate_value(alias, value):
    """Check the document's required shape; this is not a balance validator."""
    if not isinstance(value, dict):
        raise AdminError("Storage value must be a JSON object")
    if alias == "debug-preview":
        if set(value) != {"user_ids"} or not isinstance(value["user_ids"], list):
            raise AdminError("Debug preview requires exactly {\"user_ids\": [\"UUID\"]}; emails are unsupported")
        ids = value["user_ids"]
        for item in ids:
            try:
                parsed = uuid.UUID(item) if isinstance(item, str) else None
            except ValueError:
                parsed = None
            if parsed is None or str(parsed) != item.lower() or parsed.int == 0:
                raise AdminError("Each debug-preview entry must be a non-system canonical user UUID")
        if len(set(item.lower() for item in ids)) != len(ids):
            raise AdminError("Duplicate debug-preview user UUID")
    elif alias == "aura":
        if type(value.get("active")) is not bool:
            raise AdminError("Aura requires an explicit boolean active field")
        if "enabled" in value and type(value["enabled"]) is not bool:
            raise AdminError("Aura enabled must be boolean when supplied")
        for key in ("all_stats_pct", "stats_bonus_pct", "crit_pct", "hp_pct", "damage_pct",
                    "armor_pct", "healing_pct", "xp_bonus_pct", "xp_pct",
                    "mine_respawn_wait_pct", "mine_respawn_pct", "duration_hours",
                    "started_at_unix", "anchor_unix", "ends_at_unix", "ends_at"):
            if key in value and (type(value[key]) not in (int, float) or not math.isfinite(value[key])):
                raise AdminError(f"Aura {key} must be a finite JSON number")
    elif alias == "contests":
        race = value.get("race")
        if not isinstance(race, dict) or type(race.get("goal_mana")) is not int or race["goal_mana"] < 1:
            raise AdminError("Contests require race.goal_mana as a positive integer")
        for name in ("entry", "rewards"):
            if not isinstance(race.get(name), list) or not race[name]:
                raise AdminError(f"Contests require a nonempty race.{name} list")
            for row in race[name]:
                if (not isinstance(row, dict) or not isinstance(row.get("resource"), str)
                        or not row["resource"] or type(row.get("amount")) is not int or row["amount"] < 1):
                    raise AdminError(f"Each race.{name} row requires resource and a positive integer amount")
    elif alias in ("items", "bots", "bots-easy", "bots-medium", "bots-hard"):
        field = "items" if alias == "items" else "bots"
        entries = value.get(field)
        if (not isinstance(entries, dict) or not entries
                or any(not isinstance(row, dict) for row in entries.values())):
            raise AdminError(f"Catalog requires a nonempty {field} object of definitions")
    elif alias == "global-boss":
        for key in ("max_hp", "base_damage", "base_armor", "max_turns", "eligibility_min_damage"):
            if type(value.get(key)) is not int or value[key] < (0 if key == "base_armor" else 1):
                raise AdminError(f"Global boss {key} must be a valid nonnegative integer")
        if value["max_hp"] % 50000 or value["base_damage"] % 500:
            raise AdminError("Global boss HP must be a multiple of50000; damage a multiple of500")
        if not 0 <= value.get("base_crit", -1) <= 1 or value["max_turns"] > 60:
            raise AdminError("Global boss crit/turn limit is invalid")
        if not isinstance(value.get("rewards"), dict) or any(type(value["rewards"].get(k)) is not int or value["rewards"][k] < 0 for k in ("gold", "ore", "matter")):
            raise AdminError("Global boss rewards require nonnegative gold/ore/matter integers")
    elif alias == "shop":
        if value.get("schema") != 1 or not isinstance(value.get("offers"), list) or not value["offers"]:
            raise AdminError("Shop requires schema1 and a nonempty offers array; runtime performs exact price validation")
    elif alias == "achievements":
        chains = value.get("chains")
        if not isinstance(chains, (dict, list)) or not chains:
            raise AdminError("Achievement catalog requires nonempty chains")
        for chain in chains.values() if isinstance(chains, dict) else chains:
            if (not isinstance(chain, dict) or not chain.get("counter_key")
                    or not isinstance(chain.get("steps"), list) or not chain["steps"]):
                raise AdminError("Each achievement chain requires counter_key and nonempty steps")
    json.dumps(value, allow_nan=False)


class Console:
    def __init__(self, base, timeout=20):
        parsed = urlsplit(base)
        if (parsed.username or parsed.password or parsed.query or parsed.fragment
                or parsed.path not in ("", "/") or not parsed.hostname):
            raise AdminError("Console URL must contain only scheme, hostname and optional port")
        if parsed.scheme != "https" and not (
                parsed.scheme == "http" and parsed.hostname in ("127.0.0.1", "localhost", "::1")):
            raise AdminError("Use HTTPS, or HTTP through a loopback SSH tunnel")
        self.base = base.rstrip("/")
        self.timeout = timeout
        self.token = None
        self.opener = build_opener(NoRedirects())

    def call(self, method, path, payload=None):
        headers = {"Content-Type": "application/json"}
        if self.token:
            headers["Authorization"] = "Bearer " + self.token
        data = None if payload is None else json.dumps(payload, allow_nan=False).encode("utf-8")
        request = Request(self.base + path, data=data, headers=headers, method=method)
        try:
            with self.opener.open(request, timeout=self.timeout) as response:
                raw = response.read(MAX_JSON_BYTES + 1)
                if len(raw) > MAX_JSON_BYTES:
                    raise AdminError("Console response exceeds the 8 MiB limit")
                return strict_json(raw.decode("utf-8")) if raw else {}
        except HTTPError as error:
            version_conflict = False
            try:
                body = strict_json(error.read(8192).decode("utf-8"))
                version_conflict = body.get("message") == "Storage write rejected - version check failed."
            except (ValueError, UnicodeError, AttributeError, AdminError):
                pass
            error.close()
            raise ApiError(error.code, version_conflict) from None
        except (URLError, TimeoutError):
            raise AdminError("Console connection failed; check URL/TLS/SSH tunnel. No automatic write retry was made") from None

    def login(self, username, password):
        response = self.call("POST", "/v2/console/authenticate", {"username": username, "password": password})
        self.token = response.get("token")
        if not self.token:
            raise AdminError("Console authentication returned no token; check credentials/MFA")

    def close(self):
        if self.token:
            try:
                self.call("POST", "/v2/console/authenticate/logout", {"token": self.token})
            except AdminError:
                pass
            self.token = None

    @staticmethod
    def object_path(collection, key):
        return "/v2/console/storage/" + "/".join(quote(part, safe="") for part in (collection, key, SYSTEM_USER_ID))

    def read(self, collection, key):
        try:
            row = self.call("GET", self.object_path(collection, key))
        except ApiError as error:
            if error.status == 404:
                return None
            raise
        if isinstance(row.get("value"), str):
            row["value"] = strict_json(row["value"])
        return row

    def write(self, collection, key, value, version):
        if not version:
            raise AdminError("An explicit create-only or CAS version is required")
        return self.call("PUT", self.object_path(collection, key), {
            "collection": collection, "key": key, "user_id": SYSTEM_USER_ID,
            "value": json.dumps(value, ensure_ascii=False, allow_nan=False),
            "version": version, "permission_read": 0, "permission_write": 0,
        })


def self_test(client):
    """Only touches a unique neutral probe, which is removed even on failure."""
    nonce = str(uuid.uuid4())
    collection, key = "duel_match3_admin_probe", "storage-admin-" + nonce
    print(f"Probe {collection}/{key}: no gameplay configuration is changed")
    try:
        if client.read(collection, key) is not None:
            raise AdminError("Probe unexpectedly exists")
        created = client.write(collection, key, {"storage_admin_probe": nonce, "stage": 1}, "*")
        first_version = created["version"]
        row = client.read(collection, key)
        if row["value"].get("stage") != 1 or row.get("permission_read", 0) != 0 or row.get("permission_write", 0) != 0:
            raise AdminError("Probe create/read/permissions verification failed")
        print("PASS create-only, read and server-only permissions")
        try:
            client.write(collection, key, {"storage_admin_probe": nonce, "stage": 99}, "*")
        except ApiError as error:
            if not error.version_conflict:
                raise
        else:
            raise AdminError("Create-only unexpectedly overwrote a record")
        print("PASS duplicate create rejected")
        client.write(collection, key, {"storage_admin_probe": nonce, "stage": 2}, first_version)
        try:
            client.write(collection, key, {"storage_admin_probe": nonce, "stage": 99}, first_version)
        except ApiError as error:
            if not error.version_conflict:
                raise
        else:
            raise AdminError("Stale CAS unexpectedly overwrote a record")
        if client.read(collection, key)["value"].get("stage") != 2:
            raise AdminError("CAS update verification failed")
        print("PASS CAS update and stale version rejection")
    finally:
        row = client.read(collection, key)
        if row is not None:
            if row.get("value", {}).get("storage_admin_probe") != nonce:
                raise AdminError("Probe ownership marker changed; cleanup refused")
            client.call("DELETE", client.object_path(collection, key) + "/" + quote(row["version"], safe=""))
            if client.read(collection, key) is not None:
                raise AdminError("Probe cleanup verification failed")
        print("PASS temporary probe removed; no probe object remains")


def make_parser():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--url", default="https://console.prokrust-play.ru")
    parser.add_argument("--username", default="escape-admin")
    parser.add_argument("--config", type=Path, help="Read console credentials from JSON-formatted server.yml; never prints them")
    sub = parser.add_subparsers(dest="command", required=True)
    sub.add_parser("inventory", help="Read metadata for known global records; no values are printed")
    read = sub.add_parser("read", help="Read one record and its version")
    read.add_argument("record", choices=RECORDS)
    read.add_argument("--out", type=Path, help="Export only its JSON value to a NEW file; print metadata only")
    for name in ("validate", "create", "update"):
        command = sub.add_parser(name)
        command.add_argument("record", choices=RECORDS)
        command.add_argument("--file", type=Path, required=True, help="JSON value file, not a Storage envelope")
        if name == "update":
            command.add_argument("--version", required=True, help="Expected version from read; blind overwrite is forbidden")
            command.add_argument("--backup", type=Path, required=True, help="Save previous full record to a NEW file before writing")
    sub.add_parser("self-test", help="Create/update/delete a unique temporary probe; leaves gameplay config untouched")
    return parser


def main(argv=None):
    args = make_parser().parse_args(argv)
    value = None
    if args.command in ("validate", "create", "update"):
        value = read_json(args.file)
        validate_value(args.record, value)
        if args.command == "validate":
            print(f"PASS {args.record}: required JSON shape is valid (offline; balance was not evaluated)")
            return
        if args.command == "update" and (not args.version.strip() or args.version == "*"):
            raise AdminError("Update requires an existing version hash; use create for a new record")

    if args.config:
        settings = read_json(args.config).get("console", {})
        username, password = settings.get("username"), settings.get("password")
        if not username or not password:
            raise AdminError("Config must contain console.username and console.password")
    else:
        username = args.username
        password = getpass.getpass(f"Console password for {username}: ")
    client = Console(args.url)
    try:
        client.login(username, password)
        password = None
        if args.command == "inventory":
            for alias, (collection, key) in RECORDS.items():
                row = client.read(collection, key)
                if row is None:
                    print(f"MISSING {alias:14} {collection}/{key}")
                else:
                    print(f"PRESENT {alias:14} {collection}/{key} read={row.get('permission_read', 0)} write={row.get('permission_write', 0)} version={row['version']}")
        elif args.command == "self-test":
            self_test(client)
        else:
            collection, key = RECORDS[args.record]
            if args.command == "read":
                row = client.read(collection, key)
                if row is None:
                    raise AdminError("Requested record is absent")
                if args.out:
                    write_json_new(args.out, row["value"])
                    print(f"Exported value to {args.out}; version={row['version']} read={row.get('permission_read', 0)} write={row.get('permission_write', 0)}")
                else:
                    print(json.dumps(row, ensure_ascii=False, indent=2, allow_nan=False))
            else:
                version = "*"
                if args.command == "update":
                    previous = client.read(collection, key)
                    if previous is None or previous["version"] != args.version:
                        raise AdminError("Record is missing or changed since read; no write was made")
                    write_json_new(args.backup, previous)
                    version = args.version
                ack = client.write(collection, key, value, version)
                print(f"OK {args.command} {args.record}; version={ack['version']} owner={SYSTEM_USER_ID} read=0 write=0")
    finally:
        client.close()


if __name__ == "__main__":
    try:
        main()
    except (AdminError, OSError, ValueError, KeyError) as error:
        print(f"ERROR: {error}", file=sys.stderr)
        sys.exit(1)
