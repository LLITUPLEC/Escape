"""Initialize only a new isolated deployment; never rotate existing credentials."""
import argparse
import ipaddress
import json
import os
from pathlib import Path
import secrets

parser = argparse.ArgumentParser()
parser.add_argument("--client-key-file", type=Path, required=True)
parser.add_argument("--catalog-dir", type=Path)
parser.add_argument("--caddy-proxy-ip", required=True,
                    help="Exact gateway IP of escape_default, as seen by host Caddy requests")
args = parser.parse_args()
try:
    caddy_proxy_ip = str(ipaddress.ip_address(args.caddy_proxy_ip))
except ValueError:
    parser.error("--caddy-proxy-ip must be one exact IP address, not a CIDR or hostname")
root = Path(__file__).resolve().parent
os.umask(0o077)
config_dir = root / "secrets"
config_dir.mkdir(mode=0o700, exist_ok=True)
config_file = config_dir / "server.yml"
env_file = root / ".env"
if config_file.exists() or env_file.exists():
    raise SystemExit("Existing configuration found; refusing to replace credentials.")
client_key = args.client_key_file.read_text(encoding="utf-8-sig").strip()
if not client_key:
    raise SystemExit("Client key is empty")
database_password = secrets.token_hex(32)
config = {
    "name": "escape",
    "data_dir": "/nakama/data",
    "logger": {"level": "INFO"},
    "database": {
        "address": [f"escape:{database_password}@postgres:5432/escape"],
        "max_open_conns": 16,
        "max_idle_conns": 4,
    },
    "socket": {"server_key": client_key, "max_message_size_bytes": 8192},
    "session": {
        "encryption_key": secrets.token_hex(32),
        "refresh_encryption_key": secrets.token_hex(32),
        "token_expiry_sec": 7200,
        "refresh_token_expiry_sec": 604800,
    },
    "console": {
        "username": "escape-admin",
        "password": secrets.token_urlsafe(32),
        "signing_key": secrets.token_hex(32),
    },
    "runtime": {
        "path": "/nakama/data/modules",
        "http_key": secrets.token_hex(32),
        "lua_min_count": 4,
        "lua_max_count": 16,
    },
}
# Only static design data is seeded, never player profiles or example inventories.
# Keep catalogs system-owned and create-only so a redeploy preserves live edits.
data_dir = args.catalog_dir or root.parent / "data"
catalogs = [
    ("duel_match3_item_defs", "catalog", "duel_match3_item_defs.json"),
    ("duel_match3_bot_defs", "catalog_easy", "duel_match3_bots_catalog_easy.json"),
    ("duel_match3_bot_defs", "catalog_medium", "duel_match3_bots_catalog_medium.json"),
    ("duel_match3_bot_defs", "catalog_hard", "duel_match3_bots_catalog_hard.json"),
    ("duel_match3_achievement_defs", "catalog", "achievement_catalog.example.json"),
    ("duel_match3_contest_goals", "config", "contest_goals.example.json"),
]
seed = ['local nk = require("nakama")', 'nk.run_once(function(ctx)']
for collection, key, filename in catalogs:
    value = json.loads((data_dir / filename).read_text(encoding="utf-8-sig"))
    encoded = json.dumps(value, ensure_ascii=False, separators=(",", ":"))
    if "]=]" in encoded:
        raise SystemExit("Catalog contains unsupported Lua delimiter")
    seed.extend([
        "do",
        f'  local id = {{ collection = "{collection}", key = "{key}", '
        'user_id = "00000000-0000-0000-0000-000000000000" }',
        "  local rows = nk.storage_read({ id })",
        "  if #rows == 0 then",
        f"    id.value = nk.json_decode([=[{encoded}]=])",
        "    id.version = '*'",
        "    id.permission_read = 0",
        "    id.permission_write = 0",
        "    nk.storage_write({ id })",
        "  end",
        "end",
    ])
seed.append("end)")
runtime = root / "runtime"
runtime.mkdir(exist_ok=True)
(runtime / "zz_escape_seed.lua").write_text("\n".join(seed) + "\n", encoding="utf-8")
# Catalog validation must complete before credentials are persisted, so a bad
# input file can be corrected and initialization safely retried.
config_file.write_text(json.dumps(config, indent=2) + "\n", encoding="utf-8")
env_file.write_text(f"POSTGRES_PASSWORD={database_password}\nCADDY_PROXY_IP={caddy_proxy_ip}\n", encoding="utf-8")
print("Created isolated configuration and create-only static catalog bootstrap; secrets were not printed.")
