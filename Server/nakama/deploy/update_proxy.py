"""Update only Escape's existing Caddy site, with validation and rollback."""
from datetime import datetime, timezone
import grp
import os
from pathlib import Path
import shutil
import subprocess

root = Path(__file__).resolve().parent
site = Path("/etc/caddy/sites/escape.caddy")
if not site.is_file():
    raise SystemExit("Escape route is missing; use enable_proxy.py for first installation.")
main = Path("/etc/caddy/Caddyfile")
if b"import /etc/caddy/sites/escape.caddy" not in main.read_bytes():
    raise SystemExit("Escape route is not imported; refusing to change shared routing.")
original = site.read_bytes()
replacement = (root / "Caddyfile").read_bytes()
stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")
backup = root / "backups" / ("escape.caddy." + stamp)
backup.parent.mkdir(mode=0o700, exist_ok=True)
shutil.copy2(site, backup)
try:
    site.write_bytes(replacement)
    site.chmod(0o640)
    os.chown(site, 0, grp.getgrnam("caddy").gr_gid)
    subprocess.run(["caddy", "validate", "--config", str(main), "--adapter", "caddyfile"], check=True)
    subprocess.run(["systemctl", "reload", "caddy"], check=True)
except BaseException:
    site.write_bytes(original)
    subprocess.run(["systemctl", "reload", "caddy"], check=False)
    raise
print("Escape API/dashboard route updated; other site files and shared routes were retained.")
