#!/usr/bin/env python3
"""Server-local 0.3.13 catalog installation; read-only unless --apply is explicit.

No runtime, player, balance or other project writes. The exact deployed runtime
must match --expected-runtime-sha256 before Console login and every Storage write.
"""
import argparse
from datetime import datetime, timezone
import hashlib
import importlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import stat
import sys
import uuid

sys.dont_write_bytecode = True
ROOT = Path("/opt/escape")
PAYLOADS = frozenset({"storage_admin.py", "merge_shop_catalog.py", "install_shop_catalog.py",
    "install_release0313_catalogs.py", "shop-item-defs.json", "duel_shop_catalog.json", "global_boss.config.json"})
RECORDS = frozenset({("duel_match3_item_defs", "catalog"), ("duel_shop_catalog", "v1"), ("duel_global_boss", "config")})
SYSTEM_USER_ID = "00000000-0000-0000-0000-000000000000"
HEX64 = re.compile(r"[0-9a-f]{64}\Z")


class InstallError(Exception):
    pass


def document(path):
    path = Path(path)
    if path.is_symlink() or not path.is_file() or path.stat().st_size > 8 * 1024 * 1024:
        raise InstallError("Expected a regular bounded JSON file")
    def pairs(values):
        result = {}
        for key, value in values:
            if key in result:
                raise InstallError("Duplicate JSON property refused")
            result[key] = value
        return result
    def constant(_):
        raise InstallError("Non-finite JSON number refused")
    try:
        return json.loads(path.read_text(encoding="utf-8-sig"), object_pairs_hook=pairs, parse_constant=constant)
    except (ValueError, UnicodeError):
        raise InstallError("Invalid JSON document") from None


def verify_location(bundle, root=ROOT, check_private=True):
    bundle, root = Path(bundle).absolute(), Path(root).absolute()
    if (bundle.parent != root or not re.fullmatch(r"release0313-catalogs-[0-9a-f]{12}", bundle.name)
            or bundle.resolve() != bundle or root.resolve() != root or not bundle.is_dir()):
        raise InstallError("Run from the private /opt/escape/release0313-catalogs-<12hex> directory")
    if check_private and (os.name != "posix" or stat.S_IMODE(bundle.stat().st_mode) & 0o077):
        raise InstallError("Bundle directory must be private (mode0700)")
    if Path.cwd().resolve() != bundle:
        raise InstallError("Current directory must be the exact private bundle directory")


def verify_bundle(bundle):
    bundle = Path(bundle)
    manifest = document(bundle / "bundle-manifest.json")
    if (not isinstance(manifest, dict) or manifest.get("schema") != 1 or manifest.get("bundle_id") != bundle.name
            or not isinstance(manifest.get("files"), dict) or set(manifest["files"]) != PAYLOADS):
        raise InstallError("Unexpected release catalog bundle manifest")
    if {p.name for p in bundle.iterdir()} != PAYLOADS | {"bundle-manifest.json"}:
        raise InstallError("Unexpected bundle entry refused")
    for name, expected in manifest["files"].items():
        path = bundle / name
        if (not isinstance(expected, str) or not HEX64.fullmatch(expected) or path.is_symlink()
                or not path.is_file() or hashlib.sha256(path.read_bytes()).hexdigest() != expected):
            raise InstallError("Bundle payload hash mismatch")
    return manifest


def runtime_tree(path):
    path = Path(path)
    if path.is_symlink() or not path.is_dir() or path.resolve() != path.absolute():
        raise InstallError("Runtime must be a real directory")
    result = {}
    for file in sorted(path.rglob("*")):
        if file.is_symlink():
            raise InstallError("Runtime symlink refused")
        if file.is_file():
            result[file.relative_to(path).as_posix()] = hashlib.sha256(file.read_bytes()).hexdigest()
    return result


def verify_runtime(root, expected):
    if not isinstance(expected, str) or not HEX64.fullmatch(expected):
        raise InstallError("An exact lowercase64-character expected runtime SHA256 is required")
    root = Path(root)
    manifest = document(root / "deployed-runtime-manifest.json")
    files = manifest.get("files") if isinstance(manifest, dict) else None
    if not isinstance(files, dict) or not files or manifest.get("sha256") != expected:
        raise InstallError("Expected SHA does not match the deployed runtime manifest")
    for name, digest in files.items():
        parts = PurePosixPath(name)
        if (not name or parts.is_absolute() or ".." in parts.parts or "\\" in name or parts.as_posix() != name
                or not isinstance(digest, str) or not HEX64.fullmatch(digest)):
            raise InstallError("Invalid deployed runtime file map")
    actual = hashlib.sha256(json.dumps(files, sort_keys=True).encode()).hexdigest()
    if actual != expected or runtime_tree(root / "runtime") != files:
        raise InstallError("Actual runtime tree does not match the exact deployed manifest SHA")
    if not {"duel_shop.lua", "duel_global_boss.lua", "duel_daily.lua"}.issubset(files):
        raise InstallError("Required0.3.13 runtime modules are absent")
    return expected


def verify_row(row, collection, key):
    if row is None:
        return
    owners = [row[k] for k in ("user_id", "userId") if k in row] if isinstance(row, dict) else []
    if (not isinstance(row, dict) or row.get("collection") != collection or row.get("key") != key
            or not owners or any(v != SYSTEM_USER_ID for v in owners)
            or not isinstance(row.get("version"), str) or not row["version"] or row["version"] == "*"
            or not isinstance(row.get("value"), dict)):
        raise InstallError("Console row is not the exact explicitly system-owned Storage record")


class GuardedConsole:
    """Only three known Storage records; runtime pin rechecked at each write."""
    def __init__(self, console, before_write):
        self.console, self.before_write = console, before_write
        self.written = set()

    def read(self, collection, key):
        if (collection, key) not in RECORDS:
            raise InstallError("Storage record outside the narrow installer scope")
        row = self.console.read(collection, key)
        verify_row(row, collection, key)
        return row

    def write(self, collection, key, value, version):
        identity = (collection, key)
        if identity not in RECORDS or identity in self.written or not isinstance(version, str) or not version:
            raise InstallError("Unexpected or repeated Storage write refused")
        if identity != ("duel_match3_item_defs", "catalog") and version != "*":
            raise InstallError("Shop and boss config are create-only")
        if identity == ("duel_match3_item_defs", "catalog") and version == "*":
            raise InstallError("Existing item catalog requires an exact CAS version")
        self.before_write()
        self.written.add(identity)
        return self.console.write(collection, key, value, version)


def install_boss(console, config, admin, backup=None, apply=False):
    admin.validate_value("global-boss", config)
    row = console.read("duel_global_boss", "config")
    if row is not None:
        admin.validate_value("global-boss", row["value"])
        return {"mode": "preserved_existing", "storage_version": row["version"]}
    if not apply:
        return {"mode": "planned_create"}
    if backup is None:
        raise InstallError("A fresh backup path is required")
    admin.write_json_new(backup, {"collection": "duel_global_boss", "key": "config", "user_id": SYSTEM_USER_ID, "existed": False})
    console.write("duel_global_boss", "config", config, "*")
    after = console.read("duel_global_boss", "config")
    if after is None or after["value"] != config:
        raise InstallError("Boss config post-write verification failed")
    return {"mode": "created", "storage_version": after["version"]}


def actions(console, token, prices, boss, helpers, backup=None, apply=False):
    admin, merge, shop = helpers
    return {"items": merge.merge_console(console, token, backup / "items-before.json" if backup else None, apply),
        "shop": shop.install(console, prices, backup / "shop-before.json" if backup else None, apply),
        "boss": install_boss(console, boss, admin, backup / "boss-before.json" if backup else None, apply)}


def fresh_backup(root):
    root = Path(root)
    parent = root / "backups"
    if parent.exists() and (parent.is_symlink() or not parent.is_dir() or parent.resolve().parent != root.resolve()):
        raise InstallError("Unsafe backup parent")
    parent.mkdir(mode=0o700, exist_ok=True)
    name = "catalogs0313-" + datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ-") + uuid.uuid4().hex[:12]
    target = parent / name
    target.mkdir(mode=0o700, exist_ok=False)
    return target


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--expected-runtime-sha256", "--expected-runtimeSHA", dest="expected", required=True)
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args(argv)
    os.umask(0o077)
    bundle = Path(__file__).absolute().parent
    verify_location(bundle)
    verify_bundle(bundle)
    verify_runtime(ROOT, args.expected)
    # Helpers are imported only after verifying their exact bundle hashes.
    sys.path.insert(0, str(bundle))
    helpers = tuple(importlib.import_module(n) for n in ("storage_admin", "merge_shop_catalog", "install_shop_catalog"))
    admin = helpers[0]
    token, prices, boss = (document(bundle / n) for n in ("shop-item-defs.json", "duel_shop_catalog.json", "global_boss.config.json"))
    helpers[1].validate_manifest(token); helpers[2].validate(prices); admin.validate_value("global-boss", boss)
    settings = None
    try:
        settings = document(ROOT / "secrets/server.yml").get("console", {})
        username, password = settings.get("username"), settings.get("password")
        if not isinstance(username, str) or not username or not isinstance(password, str) or not password:
            raise InstallError("Console credentials unavailable in server-local config")
    finally:
        settings = None
    client = admin.Console("http://127.0.0.1:27351")
    try:
        client.login(username, password)
        username = password = None
        console = GuardedConsole(client, lambda: verify_runtime(ROOT, args.expected))
        plan = actions(console, token, prices, boss, helpers)
        if not args.apply:
            print(json.dumps({"mode": "read_only", "runtime_sha256": args.expected, "actions": plan}, ensure_ascii=False))
            return
        verify_runtime(ROOT, args.expected)
        backup = fresh_backup(ROOT)
        admin.write_json_new(backup / "preflight.json", {"runtime_sha256": args.expected, "bundle_id": bundle.name, "actions": plan})
        result = actions(console, token, prices, boss, helpers, backup, True)
        verify_runtime(ROOT, args.expected)
        summary = {"mode": "applied", "runtime_sha256": args.expected, "backup": str(backup), "actions": result}
        admin.write_json_new(backup / "result.json", summary)
        print(json.dumps(summary, ensure_ascii=False))
    finally:
        username = password = None
        client.close()


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        # No config contents, Console response bodies or credentials in output.
        print("ERROR: " + (str(error) if isinstance(error, InstallError) else "Catalog installation stopped; inspect private backups and server health before retrying"), file=sys.stderr)
        sys.exit(1)
