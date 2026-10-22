"""Add only Escape's Caddy import, validate, and gracefully reload with rollback."""
import grp
from pathlib import Path
import re
import shutil
import subprocess

root = Path(__file__).resolve().parent
main = Path("/etc/caddy/Caddyfile")
site = Path("/etc/caddy/sites/escape.caddy")
original = main.read_bytes()
import_line = "import /etc/caddy/sites/escape.caddy"
if import_line.encode() in original or re.search(rb"(?m)^\s*prokrust-play\.ru\s*\{", original):
    raise SystemExit("Escape domain already configured; refusing to replace its route.")
if site.exists():
    raise SystemExit("Escape site file already exists; refusing to replace it.")
backup = root / "backups" / "Caddyfile.before-escape"
backup.parent.mkdir(mode=0o700, exist_ok=True)
if backup.exists():
    raise SystemExit("Existing proxy backup found; inspect before retrying.")
shutil.copy2(main, backup)
site.parent.mkdir(mode=0o755, exist_ok=True)
shutil.copyfile(root / "Caddyfile", site)
site.chmod(0o640)
import os
os.chown(site, 0, grp.getgrnam("caddy").gr_gid)
try:
    main.write_bytes(original + b"\n# Escape: isolated backend\n" + import_line.encode() + b"\n")
    subprocess.run(["caddy", "validate", "--config", str(main), "--adapter", "caddyfile"], check=True)
    subprocess.run(["systemctl", "reload", "caddy"], check=True)
except BaseException:
    main.write_bytes(original)
    site.unlink()
    subprocess.run(["systemctl", "reload", "caddy"], check=False)
    raise
print("Added only Escape's domain route; existing Caddy configuration was retained and backed up.")
