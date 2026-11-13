"""Real Storage startup migration and Mine economy checks in the isolated runner."""
from copy import deepcopy
import json
from pathlib import Path
import re
import subprocess
import sys
import time
from types import SimpleNamespace
from urllib.request import HTTPRedirectHandler, ProxyHandler, build_opener
import uuid

import multiclient_review as helpers


TIERS = ("easy", "medium", "hard")
OWNER = "00000000-0000-0000-0000-000000000000"
PROGRESS = "duel_match3_progress"
ENTRY = "duel_pve_entry"


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def check(condition, description):
    if not condition:
        raise RuntimeError(description)


def safe_compose(stage, root, project):
    check(re.fullmatch(r"escape-multiclient-[a-f0-9]{10}", project), "Expected disposable project name")
    check(root == stage / "tests" / project and root == root.resolve(strict=True), "Expected disposable project path")
    config = root / "secrets/server.yml"
    compose = root / "compose.yml"
    check(config.resolve() == config and compose.resolve() == compose, "Mismatched disposable configuration")
    values = json.loads(compose.read_text())
    check(values.get("name") == project and set(values["services"]) == {"nakama", "postgres"},
          "Mismatched compose services")
    nakama, postgres = (values["services"][name] for name in ("nakama", "postgres"))
    check(set(nakama["ports"]) == {"127.0.0.1:28350:7350", "127.0.0.1:28351:7351"}
          and set(nakama["volumes"]) == {"./secrets:/nakama/config:ro", "./runtime:/nakama/data/modules:ro"}
          and postgres["volumes"] == ["./postgres-data:/var/lib/postgresql/data"] and not postgres.get("ports"),
          "Mismatched local ports or volumes")
    database = json.loads(config.read_text())["database"]["address"]
    check(len(database) == 1 and database[0].endswith("@postgres:5432/escape_test"), "Expected disposable database")
    command = ["docker", "compose", "-p", project, "-f", str(compose)]
    container = subprocess.check_output(command + ["ps", "-q", "nakama"], text=True).strip()
    check(re.fullmatch(r"[a-f0-9]{12,64}", container), "Isolated Nakama is not running")
    inspect = json.loads(subprocess.check_output(["docker", "inspect", container], text=True))[0]
    labels = inspect["Config"]["Labels"]
    check(labels["com.docker.compose.project"] == project
          and labels["com.docker.compose.project.config_files"] == str(compose), "Wrong container identity")
    return config, command


def restart(command, args):
    subprocess.run(command + ["restart", "nakama"], check=True, capture_output=True, timeout=45)
    deadline = time.monotonic() + 60
    while time.monotonic() < deadline:
        try:
            if helpers.http(args.url, "GET", "/healthcheck")[0] == 200:
                return helpers.Suite(args)
        except OSError:
            pass
        time.sleep(1)
    raise RuntimeError("Isolated Nakama did not restart")


def old_catalog(catalog):
    previous = deepcopy(catalog)
    previous["version"] = 1
    for bot in previous["bots"].values():
        floor = bot["floor"]
        bot["cost_attack"] = [{"resource": "energy", "amount": 80 if floor == 12 else 15 if floor <= 7 else 25}]
        bot["cost_banish"] = [{"resource": "energy", "amount": 40 if floor == 12 else 5 if floor <= 7 else 15}]
        if floor == 12:
            bot["cost_attack"].append({"resource": "matter", "amount": 5})
            bot["cost_banish"].append({"resource": "matter", "amount": 3})
    return previous


def catalog_path(tier):
    return f"/storage/duel_match3_bot_defs/catalog_{tier}/{OWNER}"


def read_row(suite, path):
    status, row = suite.console("GET", path)
    check(status in (200, 404), "Unexpected isolated Storage read status")
    return row if status == 200 else {}


def value(row):
    raw = row["value"]
    return json.loads(raw) if isinstance(raw, str) else raw


def write_row(suite, path, data, version, permission_read=0):
    status, _ = suite.console("PUT", path, {"value": json.dumps(data, ensure_ascii=False), "version": version,
                                            "permission_read": permission_read, "permission_write": 0})
    check(status == 200, "Isolated catalog fixture write rejected")


def record(suite, condition, description):
    check(suite.check(condition, description), description)


def verify_rpc_catalog(suite, player, tier, expected):
    payload = suite.rpc(player, "duel_match3_pve_catalog_get")
    shown = {bot["id"]: bot for bot in payload["bots"]}
    fields = ("cost_attack", "cost_banish", "reward_matter_min", "reward_matter_max", "reward_gold", "reward_ore")
    return payload["ok"] and payload["mine_difficulty"] == tier and set(shown) == set(expected["bots"]) and all(
        all(shown[bot][field] == row[field] for field in fields) for bot, row in expected["bots"].items())


def main():
    root, project, output = Path(sys.argv[1]), sys.argv[2], Path(sys.argv[3])
    stage = Path(__file__).resolve().parent
    check(stage.parent == Path("/opt/escape/tests") and re.fullmatch(r"escape-release0324-[a-f0-9]{12}", stage.name)
          and output == stage / "mine-costs-native.json", "Unexpected validation stage or report path")
    config, command = safe_compose(stage, root, project)
    helpers.urlopen = build_opener(ProxyHandler({}), NoRedirect()).open
    args = SimpleNamespace(config=config, url="http://127.0.0.1:28350", console_url="http://127.0.0.1:28351")
    suite = helpers.Suite(args)
    expected = {tier: json.loads((stage / f"duel_match3_bots_catalog_{tier}.json").read_text(encoding="utf-8-sig"))
                for tier in TIERS}
    check(all(catalog["version"] == 2 and len(catalog["bots"]) == 12 for catalog in expected.values()),
          "Expected 36 v2 catalog fixture entries")
    try:
        for tier, catalog in expected.items():
            path = catalog_path(tier)
            row = read_row(suite, path)
            check(not row or value(row) == old_catalog(catalog), "Unexpected initial seed catalog")
            if not row:
                write_row(suite, path, old_catalog(catalog), "*")

        player = suite.player()
        wallet_path = f"/storage/{PROGRESS}/profile/{player['uid']}"
        suite.seed_money(player, energy=100, matter=10, energy_updated_at=int(time.time()),
                         mine={"current_difficulty": "easy", "selected_floor": 12,
                               "unlocked": dict.fromkeys(TIERS, 12), "floor_states": {}})
        record(suite, verify_rpc_catalog(suite, player, "easy", old_catalog(expected["easy"])),
               "v1 Storage prices remain readable before startup migration")
        launch = suite.rpc(player, "duel_match3_pve_create",
                           {"bot_id": "mine_12", "floor": 12, "difficulty": "easy", "request_id": uuid.uuid4().hex})
        active = suite.storage(ENTRY, "active", player["uid"])
        wallet = value(read_row(suite, wallet_path))
        record(suite, launch.get("ok") and active["status"] == "bound"
               and active["costs"] == old_catalog(expected["easy"])["bots"]["mine_12"]["cost_attack"]
               and wallet["energy"] == 20 and wallet["matter"] == 5, "Native v1 battle reserves 80 energy and 5 matter")

        checks = suite.checks
        suite = restart(command, args)
        suite.players.append(player)
        suite.checks = checks
        versions = {}
        for tier, catalog in expected.items():
            row = read_row(suite, catalog_path(tier))
            record(suite, value(row) == catalog and row.get("permission_read", 0) == 0
                   and row.get("permission_write", 0) == 0, f"Startup migrates {tier} to exact v2 catalog")
            versions[tier] = row["version"]

        wallet = suite.rpc(player, "duel_player_resources_get")
        refunded = suite.storage(ENTRY, "launch:" + active["token"], player["uid"])
        record(suite, refunded["status"] == "refunded" and refunded["costs"] == active["costs"]
               and wallet["energy"] == 100 and wallet["matter"] == 10,
               "Recovered v1 match refunds the original frozen price after v2 startup")
        for tier, catalog in expected.items():
            row = read_row(suite, wallet_path)
            wallet = value(row)
            wallet["mine"]["current_difficulty"] = tier
            write_row(suite, wallet_path, wallet, row["version"], permission_read=1)
            record(suite, verify_rpc_catalog(suite, player, tier, catalog), f"RPC exposes all 12 updated {tier} quotes and rewards")

        changed = suite.rpc(player, "duel_mine_affix_reroll", {"floor": 12, "difficulty": "hard"})
        wallet = value(read_row(suite, wallet_path))
        record(suite, changed.get("ok") and changed["reroll_cost"] == [{"resource": "energy", "amount": 20}]
               and wallet["energy"] == 80 and wallet["matter"] == 10, "Native hard floor-12 banish costs exactly 20 energy")
        started = suite.rpc(player, "duel_match3_pve_create",
                            {"bot_id": "mine_12", "floor": 12, "difficulty": "hard", "request_id": uuid.uuid4().hex})
        entry = suite.storage(ENTRY, "active", player["uid"])
        wallet = value(read_row(suite, wallet_path))
        record(suite, started.get("ok") and entry["costs"] == [{"resource": "energy", "amount": 65}]
               and wallet["energy"] == 15 and wallet["matter"] == 10, "Native hard floor-12 attack costs exactly 65 energy")

        suite = restart(command, args)
        suite.players.append(player)
        suite.checks = checks
        record(suite, all(read_row(suite, catalog_path(tier))["version"] == version
                          for tier, version in versions.items()), "A second startup does not rewrite v2 catalogs")
        report = {"passed": True, "checks": checks}
    except Exception as error:
        report = {"passed": False, "checks": suite.checks, "error": str(error)}
        raise
    finally:
        if "report" in locals():
            output.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
