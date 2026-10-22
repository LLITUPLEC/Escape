"""Bind proxy trust to this deployment's actual Docker bridge gateway."""
from datetime import datetime, timezone
import ipaddress
import json
from pathlib import Path
import shutil
import subprocess

root = Path(__file__).resolve().parent
env_path = root / ".env"
if not env_path.is_file():
    raise SystemExit("Existing Escape .env is required")
network = json.loads(subprocess.check_output(["docker", "network", "inspect", "escape_default"]))[0]
gateways = [c["Gateway"] for c in network["IPAM"]["Config"] if c.get("Gateway")]
if len(gateways) != 1:
    raise SystemExit("Expected one unambiguous Escape network gateway")
gateway = str(ipaddress.ip_address(gateways[0]))
original = env_path.read_text()
lines = [line for line in original.splitlines() if not line.startswith("CADDY_PROXY_IP=")]
updated = "\n".join(lines + ["CADDY_PROXY_IP=" + gateway]) + "\n"
if updated != original:
    backup = root / "backups" / ("env.before-gateway-" + datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ"))
    backup.parent.mkdir(mode=0o700, exist_ok=True)
    shutil.copy2(env_path, backup)
    backup.chmod(0o600)
    env_path.write_text(updated)
    env_path.chmod(0o600)
print("Configured the verified Escape bridge gateway; no credentials were printed.")
