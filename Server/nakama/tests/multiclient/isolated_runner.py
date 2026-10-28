"""Run the suite in a disposable localhost-only Nakama+PostgreSQL project.

Run server-locally from /opt/escape. Copies Escape runtime, not player data.
The dedicated Compose name, directories, ports and fresh database isolate tests.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import secrets
import shutil
import socket
import subprocess
import time
from urllib.request import urlopen
import uuid


def capture_runtime_diagnostics(cmd, escape, scenario):
    # A Go fatal dump can exceed hundreds of lines. Preserve the complete local
    # test log privately; its beginning is essential for diagnosing the cause.
    logs = subprocess.run(cmd + ["logs", "--no-color", "nakama"], capture_output=True, text=True)
    path = escape / ("multiclient-isolated-" + scenario + "-runtime-errors.log")
    path.write_text(logs.stdout + logs.stderr, encoding="utf-8")
    containers = subprocess.run(cmd + ["ps", "-a", "-q", "nakama"], capture_output=True, text=True)
    states = []
    for container in containers.stdout.split():
        inspected = subprocess.run(["docker", "inspect", "--format", "{{json .State}}", container], capture_output=True, text=True)
        if inspected.returncode == 0:
            state = json.loads(inspected.stdout)
            states.append({key: state.get(key) for key in ("Status", "Running", "OOMKilled", "Dead", "ExitCode", "Error", "StartedAt", "FinishedAt")})
    (escape / ("multiclient-isolated-" + scenario + "-container-state.json")).write_text(json.dumps(states, indent=2), encoding="utf-8")
    print("Isolated runtime diagnostics saved in protected " + str(path), flush=True)


def suite_arguments(escape, root, project, scenario):
    if scenario == "recovery":
        return ["python3", str(escape / "recovery_probe.py"), "--url", "http://127.0.0.1:28350",
            "--console-url", "http://127.0.0.1:28351", "--config", str(root / "secrets/server.yml"),
            "--root", str(root), "--compose-project", project, "--compose-file", str(root / "compose.yml"),
            "--item-catalog", str(root / "item-catalog.json"),
            "--report", str(escape / "multiclient-isolated-recovery.json")]
    result = ["python3", str(escape / "multiclient_review.py"),
        "--url", "http://127.0.0.1:28350", "--console-url", "http://127.0.0.1:28351",
        "--config", str(root / "secrets/server.yml"), "--scenario", scenario,
        "--report", str(escape / ("multiclient-isolated-" + scenario + ".json"))]
    if scenario in ("all", "lifecycle", "smith", "daily", "consumables", "cosmetics", "guidance", "global-boss", "global-boss-practice", "equipment-sets", "shop"):
        result.extend(["--item-catalog", str(root / "item-catalog.json")])
    if scenario == "all":
        result.extend(["--catalog-migration-helper", str(root / "catalog-migration/merge_consumable_catalog.py"),
            "--consumable-manifest", str(root / "catalog-migration/consumable-item-defs.json"), "--fail-fast"])
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--scenario", default="arena")
    args = parser.parse_args()
    escape = Path("/opt/escape").resolve()
    if Path.cwd().resolve() != escape or not (escape / "runtime/duel_match3.lua").is_file():
        raise SystemExit("Run from the existing /opt/escape project only")
    if args.scenario in ("recovery", "all", "lifecycle", "smith", "daily", "consumables", "cosmetics", "guidance", "global-boss", "global-boss-practice", "equipment-sets", "shop") and not (escape / "recovery-item-catalog.json").is_file():
        raise SystemExit("Install canonical repository item defs as /opt/escape/recovery-item-catalog.json first")
    if args.scenario == "all" and not (escape / "multiclient-catalog-migration/merge_consumable_catalog.py").is_file():
        raise SystemExit("Install the isolated consumable migration fixture before --scenario all")
    available = int(next(line.split()[1] for line in Path("/proc/meminfo").read_text().splitlines()
        if line.startswith("MemAvailable:")))
    if available < 600000:
        raise SystemExit("Insufficient available memory for bounded isolated test")
    for port in (28350, 28351):
        with socket.socket() as sock:
            sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
            sock.bind(("127.0.0.1", port))
    recovery = args.scenario == "recovery"
    suffix = uuid.uuid4().hex[:10]
    project = ("escape-review-" if recovery else "escape-multiclient-") + suffix
    parent = escape if recovery else escape / "tests"
    parent.mkdir(mode=0o700, exist_ok=True)
    root = parent / (".multiclient-" + suffix if recovery else project)
    root.mkdir(mode=0o700)
    os.umask(0o077)
    shutil.copytree(escape / "runtime", root / "runtime")
    if recovery or args.scenario in ("all", "lifecycle", "smith", "daily", "consumables", "cosmetics", "guidance", "global-boss", "global-boss-practice", "equipment-sets", "shop"):
        shutil.copy2(escape / "recovery-item-catalog.json", root / "item-catalog.json")
    if args.scenario == "all":
        shutil.copytree(escape / "multiclient-catalog-migration", root / "catalog-migration")
    overlay = escape / "multiclient-runtime-overlay"
    if overlay.is_dir():
        for source in overlay.glob("*.lua"):
            shutil.copy2(source, root / "runtime" / source.name)
    manifest = {str(source.relative_to(root / "runtime")): hashlib.sha256(source.read_bytes()).hexdigest()
        for source in sorted((root / "runtime").rglob("*")) if source.is_file()}
    runtime_digest = hashlib.sha256(json.dumps(manifest, sort_keys=True).encode()).hexdigest()
    (escape / "multiclient-runtime-manifest.json").write_text(json.dumps(
        {"sha256": runtime_digest, "files": manifest}, indent=2), encoding="utf-8")
    print("Copied test runtime SHA256=" + runtime_digest + "; files=" + str(len(manifest)), flush=True)
    (root / "secrets").mkdir(mode=0o700)
    password = secrets.token_hex(32)
    config = {
        "name": "mt" + uuid.uuid4().hex[:10], "data_dir": "/nakama/data", "logger": {"level": "WARN"},
        "database": {"address": ["escape_test:" + password + "@postgres:5432/escape_test"],
            "max_open_conns": 8, "max_idle_conns": 2},
        "socket": {"server_key": secrets.token_hex(32), "max_message_size_bytes": 8192},
        "session": {"encryption_key": secrets.token_hex(32), "refresh_encryption_key": secrets.token_hex(32),
            "token_expiry_sec": 7200, "refresh_token_expiry_sec": 604800},
        "console": {"username": "test-admin", "password": secrets.token_urlsafe(32), "signing_key": secrets.token_hex(32)},
        "runtime": {"path": "/nakama/data/modules", "http_key": secrets.token_hex(32), "lua_min_count": 4, "lua_max_count": 16},
    }
    (root / "secrets/server.yml").write_text(json.dumps(config), encoding="utf-8")
    compose = {
        "name": project,
        "services": {
            "postgres": {"image": "postgres:16.15-alpine", "mem_limit": "128m", "cpus": 0.25,
                "command": ["postgres", "-c", "shared_buffers=16MB", "-c", "max_connections=30", "-c", "work_mem=2MB", "-c", "maintenance_work_mem=16MB"],
                "environment": {"POSTGRES_DB": "escape_test", "POSTGRES_USER": "escape_test", "POSTGRES_PASSWORD": password},
                "volumes": ["test-data:/var/lib/postgresql/data"],
                "healthcheck": {"test": ["CMD-SHELL", "pg_isready -h 127.0.0.1 -U escape_test -d escape_test"], "interval": "2s", "timeout": "2s", "retries": 30}},
            "nakama": {"image": "registry.heroiclabs.com/heroiclabs/nakama:3.37.0", "mem_limit": "384m", "cpus": 0.5,
                "entrypoint": ["/bin/sh", "-ec"], "command": ["/nakama/nakama migrate up --config /nakama/config/server.yml && exec /nakama/nakama --config /nakama/config/server.yml"],
                "depends_on": {"postgres": {"condition": "service_healthy"}},
                "volumes": ["./secrets:/nakama/config:ro", "./runtime:/nakama/data/modules:ro"],
                "ports": ["127.0.0.1:28350:7350", "127.0.0.1:28351:7351"],
                "read_only": True, "tmpfs": ["/tmp:rw,nosuid,noexec,size=16m"],
                "cap_drop": ["ALL"], "security_opt": ["no-new-privileges:true"]},
        }, "volumes": {"test-data": {}},
    }
    (root / "compose.yml").write_text(json.dumps(compose), encoding="utf-8")
    cmd = ["docker", "compose", "-p", project, "-f", str(root / "compose.yml")]
    try:
        subprocess.run(["docker", "run", "--rm", "--memory", "256m", "--cpus", "0.5",
            "-v", str(root / "runtime") + ":/nakama/data/modules:ro",
            "registry.heroiclabs.com/heroiclabs/nakama:3.37.0", "check", "--runtime.path", "/nakama/data/modules"],
            check=True, stdout=subprocess.DEVNULL)
        print("Nakama 3.37 runtime check passed for copied modules plus the pending local overlay", flush=True)
        subprocess.run(cmd + ["up", "-d"], check=True, stdout=subprocess.DEVNULL)
        deadline = time.monotonic() + 90
        while True:
            try:
                with urlopen("http://127.0.0.1:28350/healthcheck", timeout=2) as response:
                    if response.status == 200:
                        break
            except Exception:
                pass
            if time.monotonic() > deadline:
                capture_runtime_diagnostics(cmd, escape, args.scenario)
                raise RuntimeError("Isolated Nakama failed healthcheck")
            time.sleep(1)
        print("Isolated test backend healthy; fresh DB; Lua pool 4..16; 512MiB combined memory limits", flush=True)
        if recovery and not (escape / "recovery_probe.py").is_file():
            raise RuntimeError("Install recovery_probe.py beside isolated_runner.py before --scenario recovery")
        arguments = suite_arguments(escape, root, project, args.scenario)
        result = subprocess.run(arguments)
        if result.returncode:
            capture_runtime_diagnostics(cmd, escape, args.scenario)
        return result.returncode
    finally:
        owned_recovery = recovery and root.parent == escape and root.name == ".multiclient-" + suffix and project == "escape-review-" + suffix
        owned_legacy = not recovery and root.parent == escape / "tests" and root.name == project and project.startswith("escape-multiclient-")
        if owned_recovery or owned_legacy:
            subprocess.run(cmd + ["down", "--volumes", "--remove-orphans"], check=True, stdout=subprocess.DEVNULL)
            shutil.rmtree(root)
            print("Removed only disposable test containers, network, DB volume and server-local test credentials", flush=True)


if __name__ == "__main__":
    raise SystemExit(main())
