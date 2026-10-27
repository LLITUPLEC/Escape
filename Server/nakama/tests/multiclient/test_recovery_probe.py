"""Offline safety/orchestration tests. No Docker, HTTP, auth or real account calls."""
import importlib.util
import json
import hashlib
from pathlib import Path
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch


def load(name):
    path = Path(__file__).with_name(name + ".py")
    spec = importlib.util.spec_from_file_location(name, path)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


probe, runner = load("recovery_probe"), load("isolated_runner")
PROJECT = "escape-review-0123456789"
ROOT = "/opt/escape/.multiclient-0123456789"


def compose():
    return {"name": PROJECT, "services": {"nakama": {"image": probe.IMAGE,
        "ports": ["127.0.0.1:28350:7350", "127.0.0.1:28351:7351"],
        "volumes": ["./runtime:/nakama/data/modules:ro", "./secrets:/nakama/config:ro"]},
        "postgres": {"volumes": ["test-data:/var/lib/postgresql/data"]}}}


def inspector():
    return {"Config": {"Image": probe.IMAGE, "Labels": {"com.docker.compose.project": PROJECT, "com.docker.compose.service": "nakama"}},
        "NetworkSettings": {"Ports": {"7350/tcp": [{"HostIp": "127.0.0.1", "HostPort": "28350"}],
            "7351/tcp": [{"HostIp": "127.0.0.1", "HostPort": "28351"}]}}, "State": {"StartedAt": "new-process"}}


class RecoveryProbeSafetyTests(unittest.TestCase):
    def test_loopback_normalization(self):
        self.assertEqual(probe.loopback_url("http://localhost:28350", 28350), "http://127.0.0.1:28350")

    def test_production_proxy_credentials_and_redirect_like_urls_rejected(self):
        for value in ["https://prokrust-play.ru", "http://127.0.0.1:27350", "http://127.0.0.1:28350@prokrust-play.ru",
                "http://127.0.0.1:28350/other", "http://127.0.0.1:28350?url=https://production", "http://0.0.0.0:28350"]:
            with self.subTest(url=value), self.assertRaises(ValueError):
                probe.loopback_url(value, 28350)

    def test_matching_explicit_disposable_identity(self):
        self.assertEqual(str(probe.isolated_identity(PROJECT, ROOT + "/compose.yml", ROOT + "/secrets/server.yml", ROOT)), ROOT)

    def test_production_old_project_path_traversal_and_mismatched_root_rejected(self):
        for project, root in [("escape", ROOT), ("escape-multiclient-0123456789", ROOT), (PROJECT, "/opt/escape"),
                (PROJECT, ROOT + "/../runtime"), (PROJECT, "/opt/escape/.multiclient-abcdefghij")]:
            with self.subTest(project=project, root=root), self.assertRaises(ValueError):
                probe.isolated_identity(project, root + "/compose.yml", root + "/secrets/server.yml", root)
        with self.assertRaises(ValueError):
            probe.isolated_identity(PROJECT, ROOT + "/compose.yml", "/opt/escape/secrets/server.yml", ROOT)

    def test_only_337_loopback_private_mounts(self):
        probe.validate_compose(compose(), PROJECT)
        for field, value in [("image", "production/nakama"), ("ports", ["7350:7350"]), ("volumes", ["/opt/escape/runtime:/nakama/data/modules:ro"])]:
            data = compose(); data["services"]["nakama"][field] = value
            with self.subTest(field=field), self.assertRaises(ValueError): probe.validate_compose(data, PROJECT)
        data = compose(); data["services"]["postgres"]["ports"] = ["5432:5432"]
        with self.assertRaises(ValueError): probe.validate_compose(data, PROJECT)

    def isolated(self):
        instance = probe.IsolatedNakama.__new__(probe.IsolatedNakama)
        instance.args = SimpleNamespace(compose_project=PROJECT, url="http://127.0.0.1:28350")
        instance.command = ["docker", "compose", "-p", PROJECT, "-f", ROOT + "/compose.yml"]
        instance.restarts = 0
        return instance

    def test_live_container_labels_and_ports_match_explicit_target(self):
        instance = self.isolated(); data = inspector()
        instance.command_json = Mock(side_effect=["a" * 64, json.dumps([data])])
        self.assertEqual(instance.verify_container(), "new-process")
        for modify in [lambda d: d["Config"]["Labels"].update({"com.docker.compose.project": "escape"}),
                lambda d: d["NetworkSettings"]["Ports"]["7350/tcp"][0].update({"HostIp": "0.0.0.0"})]:
            data = inspector(); modify(data)
            instance.command_json = Mock(side_effect=["a" * 64, json.dumps([data])])
            with self.assertRaises(RuntimeError): instance.verify_container()

    def test_restart_kills_only_named_isolated_nakama_retains_postgres(self):
        instance = self.isolated(); instance.verify_container = Mock(side_effect=["old", "new"])
        helpers = SimpleNamespace(http=Mock(return_value=(200, {})))
        with patch.object(probe.subprocess, "run", return_value=SimpleNamespace(returncode=0)) as run:
            instance.restart(helpers)
        self.assertEqual(run.call_args_list[0].args[0], instance.command + ["kill", "--signal", "SIGKILL", "nakama"])
        self.assertEqual(run.call_args_list[1].args[0], instance.command + ["up", "-d", "--no-deps", "nakama"])
        self.assertEqual(instance.restarts, 1)
        self.assertTrue(all("postgres" not in call.args[0] and "down" not in call.args[0] for call in run.call_args_list))

    def test_restart_failure_stops_before_followup_command(self):
        instance = self.isolated(); instance.verify_container = Mock(return_value="old")
        with patch.object(probe.subprocess, "run", return_value=SimpleNamespace(returncode=1)) as run:
            with self.assertRaises(RuntimeError): instance.restart(SimpleNamespace(http=Mock()))
        self.assertEqual(run.call_count, 1)

    def test_restart_must_change_real_process_start_time(self):
        instance = self.isolated(); instance.verify_container = Mock(return_value="same")
        with patch.object(probe.subprocess, "run", return_value=SimpleNamespace(returncode=0)):
            with self.assertRaises(RuntimeError): instance.restart(SimpleNamespace(http=Mock(return_value=(200, {}))))
        self.assertEqual(instance.restarts, 0)

    def test_seeding_rejects_non_owned_uid_before_console_call(self):
        instance = probe.RecoveryProbe.__new__(probe.RecoveryProbe)
        owned = {"uid": "temporary"}; instance.suite = SimpleNamespace(players=[owned], console=Mock())
        with self.assertRaises(ValueError): instance.write({"uid": "other"}, "duel_match3_progress", "profile", {})
        with self.assertRaises(ValueError): instance.write(owned, "system", "catalog", {})
        instance.suite.console.assert_not_called()

    def test_catalog_fixture_pin_is_the_current_public_repository_json(self):
        catalog = Path(__file__).resolve().parents[2] / "data/duel_match3_item_defs.json"
        self.assertEqual(hashlib.sha256(catalog.read_bytes()).hexdigest(), probe.CANONICAL_ITEM_CATALOG_SHA256)

    def test_catalog_bootstrap_is_create_only_before_any_rpc(self):
        instance = probe.RecoveryProbe.__new__(probe.RecoveryProbe)
        instance.isolated = SimpleNamespace(catalog_fixture={"items": {"fixture": {"kind": "recipe"}}})
        instance.evidence = []
        instance.suite = SimpleNamespace(console=Mock(side_effect=[(404, {}), (200, {})]), rpc=Mock())
        instance.seed_canonical_catalog()
        self.assertEqual(instance.suite.console.call_args_list[0].args[0], "GET")
        call = instance.suite.console.call_args_list[1]
        self.assertEqual(call.args[0], "PUT"); self.assertEqual(call.args[2]["version"], "*")
        self.assertTrue(call.args[1].endswith("/00000000-0000-0000-0000-000000000000"))
        instance.suite.rpc.assert_not_called()

    def test_catalog_bootstrap_never_overwrites_an_existing_record(self):
        instance = probe.RecoveryProbe.__new__(probe.RecoveryProbe)
        instance.suite = SimpleNamespace(console=Mock(return_value=(200, {})))
        with self.assertRaises(RuntimeError): instance.seed_canonical_catalog()
        self.assertEqual(instance.suite.console.call_count, 1)

    def test_existing_runner_scenarios_keep_original_suite_args(self):
        for scenario in ("all", "pvp", "race"):
            args = runner.suite_arguments(Path("/opt/escape"), Path("/opt/escape/tests/temporary"), "temporary", scenario)
            self.assertTrue(args[1].endswith("multiclient_review.py")); self.assertEqual(args[args.index("--scenario") + 1], scenario)
            self.assertNotIn("--compose-project", args)

    def test_recovery_runner_passes_explicit_restart_authorization(self):
        args = runner.suite_arguments(Path("/opt/escape"), Path(ROOT), PROJECT, "recovery")
        self.assertTrue(args[1].endswith("recovery_probe.py")); self.assertNotIn("--scenario", args)
        self.assertEqual(args[args.index("--compose-project") + 1], PROJECT)
        self.assertEqual(Path(args[args.index("--root") + 1]), Path(ROOT))
        self.assertEqual(Path(args[args.index("--compose-file") + 1]), Path(ROOT) / "compose.yml")
        self.assertEqual(Path(args[args.index("--item-catalog") + 1]), Path(ROOT) / "item-catalog.json")


if __name__ == "__main__": unittest.main()
