"""Isolated Nakama 3.37 recovery regression; disposable accounts, loopback only.

Copy beside multiclient_review.py as /opt/escape/recovery_probe.py. The runner
passes the exact disposable compose project/file and config. Never production.
"""
import argparse
import base64
from concurrent.futures import ThreadPoolExecutor
import importlib.util
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import subprocess
import time
from urllib.error import HTTPError
from urllib.parse import quote, urlsplit
from urllib.request import HTTPRedirectHandler, ProxyHandler, build_opener

IMAGE = "registry.heroiclabs.com/heroiclabs/nakama:3.37.0"
CANONICAL_ITEM_CATALOG_SHA256 = "6266d0eb19fcf79d749d2eeea4185fb53fc1d94d3b357d9b7178c4e9aae393f1"
COLLECTIONS = {"duel_match3_character", "duel_match3_progress", "duel_match3_journal", "duel_match3_stats",
 "duel_global_boss", "duel_global_boss_player", "duel_global_boss_attempt", "duel_mail"}


def loopback_url(value, port):
    parsed = urlsplit(value)
    if (parsed.scheme != "http" or parsed.hostname not in ("127.0.0.1", "localhost")
            or parsed.port != port or parsed.username or parsed.password
            or parsed.path not in ("", "/") or parsed.query or parsed.fragment):
        raise ValueError("Recovery probe requires exact isolated loopback HTTP ports")
    return "http://127.0.0.1:" + str(port)


def isolated_identity(project, compose_file, config, root):
    """Pure guard, also tested offline. Resolved filesystem checks follow below."""
    if not re.fullmatch(r"escape-review-[a-z0-9]{6,32}", project):
        raise ValueError("Explicit disposable escape-review project required")
    suffix = project.removeprefix("escape-review-")
    expected = PurePosixPath("/opt/escape/.multiclient-" + suffix)
    if (PurePosixPath(str(root)) != expected or PurePosixPath(str(compose_file)) != expected / "compose.yml"
            or PurePosixPath(str(config)) != expected / "secrets/server.yml"):
        raise ValueError("Compose/config must belong to the matching disposable directory")
    return expected


def validate_compose(value, project):
    services = value.get("services", {})
    if value.get("name") != project or set(services) != {"nakama", "postgres"}:
        raise ValueError("Unexpected disposable compose identity/services")
    n, p = services["nakama"], services["postgres"]
    if (n.get("image") != IMAGE or set(n.get("ports", [])) !=
            {"127.0.0.1:28350:7350", "127.0.0.1:28351:7351"}
            or set(n.get("volumes", [])) != {"./runtime:/nakama/data/modules:ro", "./secrets:/nakama/config:ro"}
            or p.get("volumes") != ["test-data:/var/lib/postgresql/data"] or p.get("ports")):
        raise ValueError("Recovery restart is restricted to isolated 3.37 ports/mounts")


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        raise HTTPError(req.full_url, code, "Redirect forbidden in isolated probe", headers, fp)


class IsolatedNakama:
    def __init__(self, args):
        args.url = loopback_url(args.url, 28350)
        args.console_url = loopback_url(args.console_url, 28351)
        isolated_identity(args.compose_project, args.compose_file, args.config, args.root)
        root = args.root.resolve(strict=True)
        if (root != args.root.absolute() or args.compose_file.resolve(strict=True) != root / "compose.yml"
                or args.config.resolve(strict=True) != root / "secrets/server.yml"
                or not (root / "runtime").is_dir() or (root / "runtime").resolve() != root / "runtime"):
            raise ValueError("Symlinked or mismatched disposable paths are forbidden")
        validate_compose(json.loads(args.compose_file.read_text()), args.compose_project)
        config = json.loads(args.config.read_text())
        addresses = config.get("database", {}).get("address", [])
        if len(addresses) != 1 or not addresses[0].endswith("@postgres:5432/escape_test"):
            raise ValueError("Disposable database address required")
        if args.item_catalog.resolve(strict=True) != root / "item-catalog.json":
            raise ValueError("Canonical item fixture must be inside the explicit disposable directory")
        catalog_bytes = args.item_catalog.read_bytes()
        if hashlib.sha256(catalog_bytes).hexdigest() != CANONICAL_ITEM_CATALOG_SHA256:
            raise ValueError("Item fixture differs from the pinned canonical repository catalog")
        self.catalog_fixture = json.loads(catalog_bytes.decode("utf-8-sig"))
        if not isinstance(self.catalog_fixture.get("items"), dict) or not self.catalog_fixture["items"]:
            raise ValueError("Canonical item fixture schema is invalid")
        self.args, self.restarts = args, 0
        self.command = ["docker", "compose", "-p", args.compose_project, "-f", str(args.compose_file)]
        self.verify_container()

    @staticmethod
    def command_json(command):
        result = subprocess.run(command, capture_output=True, text=True, timeout=30)
        if result.returncode:
            raise RuntimeError("Isolated container identity verification failed")
        return result.stdout

    def verify_container(self):
        cid = self.command_json(self.command + ["ps", "-q", "nakama"]).strip()
        if not re.fullmatch(r"[a-f0-9]{12,64}", cid):
            raise RuntimeError("Isolated Nakama container is not running")
        rows = json.loads(self.command_json(["docker", "inspect", cid]))
        if len(rows) != 1:
            raise RuntimeError("Unexpected isolated container count")
        row = rows[0]
        labels = row.get("Config", {}).get("Labels", {})
        ports = row.get("NetworkSettings", {}).get("Ports", {})
        if (labels.get("com.docker.compose.project") != self.args.compose_project
                or labels.get("com.docker.compose.service") != "nakama"
                or row.get("Config", {}).get("Image") != IMAGE
                or ports.get("7350/tcp") != [{"HostIp": "127.0.0.1", "HostPort": "28350"}]
                or ports.get("7351/tcp") != [{"HostIp": "127.0.0.1", "HostPort": "28351"}]):
            raise RuntimeError("Container labels/image/port bindings are not the explicit isolated target")
        return row.get("State", {}).get("StartedAt")

    def restart(self, helpers):
        before = self.verify_container()
        # SIGKILL deliberately bypasses graceful match_terminate: the next
        # process must recover the real bound Race receipt from PostgreSQL.
        for suffix in (["kill", "--signal", "SIGKILL", "nakama"], ["up", "-d", "--no-deps", "nakama"]):
            result = subprocess.run(self.command + suffix, capture_output=True, timeout=60)
            if result.returncode:
                raise RuntimeError("Isolated Nakama crash/restart failed")
        deadline = time.monotonic() + 90
        while time.monotonic() < deadline:
            try:
                status, _ = helpers.http(self.args.url, "GET", "/healthcheck")
                if status == 200:
                    break
            except (OSError, RuntimeError):
                pass
            time.sleep(1)
        else:
            raise RuntimeError("Isolated Nakama restart health deadline exceeded")
        if self.verify_container() == before:
            raise RuntimeError("Isolated Nakama process did not restart")
        self.restarts += 1


def load_helpers(path):
    if path.resolve() != Path("/opt/escape/multiclient_review.py"):
        raise ValueError("Use the explicitly installed isolated suite helper")
    spec = importlib.util.spec_from_file_location("escape_recovery_helpers", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    # No environment proxy or redirect can route loopback credentials elsewhere.
    module.urlopen = build_opener(ProxyHandler({}), NoRedirect()).open
    return module


class RecoveryProbe:
    def __init__(self, args, helpers, isolated):
        self.args, self.helpers, self.isolated = args, helpers, isolated
        self.suite = helpers.Suite(args)
        self.evidence, self.errors = [], []
        self.catalog = None
        self.seed_canonical_catalog()  # Before the first player/RPC can cache an empty catalog.

    def seed_canonical_catalog(self):
        # Root explicitly authorizes this one create-only SYSTEM fixture in the
        # verified fresh disposable DB. No production/existing DB data is read.
        path = "/storage/duel_match3_item_defs/catalog/00000000-0000-0000-0000-000000000000"
        status, _ = self.suite.console("GET", path)
        if status != 404:
            raise RuntimeError("Fresh disposable catalog already exists; refusing to overwrite it")
        status, _ = self.suite.console("PUT", path, {"value": json.dumps(self.isolated.catalog_fixture),
            "version": "*", "permission_read": 1, "permission_write": 0})
        if status != 200:
            raise RuntimeError("Create-only canonical catalog fixture failed")
        self.evidence.append({"case": "canonical_catalog_bootstrap", "source": "Pinned immutable repository JSON; create-only Console seed in verified disposable DB before first player RPC",
            "sha256": CANONICAL_ITEM_CATALOG_SHA256})

    def require(self, value, label):
        if not self.suite.check(value, label):
            raise RuntimeError("Recovery invariant failed")

    def read(self, player, collection, key):
        return self.suite.storage(collection, key, player["uid"])

    def write(self, player, collection, key, value):
        if collection not in COLLECTIONS or not any(p is player for p in self.suite.players):
            raise ValueError("Storage seeding restricted to accounts created by this probe")
        path = "/storage/" + quote(collection, safe="") + "/" + quote(key, safe="") + "/" + quote(player["uid"], safe="")
        status, row = self.suite.console("GET", path)
        if status not in (200, 404):
            raise RuntimeError("Temporary storage read failed")
        status, _ = self.suite.console("PUT", path, {"value": json.dumps(value),
            "version": row.get("version", "*") if status == 200 else "*",
            "permission_read": 0 if collection == "duel_match3_journal" else 1, "permission_write": 0})
        if status != 200:
            raise RuntimeError("Temporary storage seed failed")

    def rpc(self, player, name, payload=None):
        return self.suite.rpc(player, name, payload)

    def wait_rpc(self, player, name, predicate, seconds=25, payload=None):
        deadline = time.monotonic() + seconds
        while time.monotonic() < deadline:
            result = self.rpc(player, name, payload)
            if predicate(result):
                return result
            time.sleep(.5)
        raise RuntimeError("Recovery RPC observation deadline exceeded")

    @staticmethod
    def pending(profile):
        return sum(int(row.get("amount", 0)) for row in profile.get("pending_loot", []) or [] if isinstance(row, dict))

    @staticmethod
    def item_count(profile, item_id):
        return sum(int(n) for item, n in zip(profile.get("inventory_def_ids", []), profile.get("inventory_counts", [])) if item == item_id)

    def items(self, player):
        if self.catalog is None:
            result = self.rpc(player, "duel_match3_item_catalog_get")
            self.require(result.get("ok") is True and isinstance(result.get("items"), list), "live isolated item catalog is available")
            self.catalog = result["items"]
        equipment = [d for d in self.catalog if d.get("kind") == "equipment" and int(d.get("max_stack", 1)) == 1 and int(d.get("sale_price", 0)) > 0]
        recipes = [d for d in self.catalog if d.get("kind") == "recipe" and int(d.get("max_stack", 1)) == 1 and "_green_" in d.get("id", "")]
        self.require(bool(equipment) and bool(recipes), "live catalog provides real salable equipment and nonstackable green Mine recipe")
        return equipment[0], recipes[0]

    def seed_sheet(self, player, capacity, filler):
        self.write(player, "duel_match3_character", "sheet", {"equipment": [""] * 8,
            "inventory": [filler] * capacity, "inventory_counts": [1] * capacity,
            "inventory_extra_slots": capacity - 25, "inventory_pending_loot": [], "inventory_slot_receipts": {},
            "learned_recipes": [], "workshop_slots": [{"output_def_id": "", "ends_at": 0} for _ in range(8)]})

    def seed_intent(self, player, capacity, loot, amount):
        rid = "recovery-" + self.suite.run_id + "-" + str(capacity) + ":pve"
        now = int(time.time())
        plan = {"schema": 1, "run_id": rid[:-4], "bot_id": "mine_1", "difficulty": "easy", "floor": 1,
            "is_boss": False, "stat_mul": 1, "reward_xp": 5, "reward_gold": 7, "reward_ore": 2,
            "reward_matter": 0, "reward_key_id": "", "reward_key_amount": 0,
            "next_spawn_at": now + 60, "next_affix": "normal", "achievement_deltas": {},
            "blueprint_rarity": "green", "items": [{"def_id": loot, "amount": amount, "kind": "recipe"}]}
        self.write(player, "duel_match3_journal", "result:" + rid, {"schema": 1, "result_id": rid,
            "kind": "pve", "plan": plan, "status": "prepared", "reward_status": "pending", "created_at": now})
        self.write(player, "duel_match3_journal", "pending", {"entries": {rid: {"kind": "pve", "created_at": now}}})
        return rid

    def purchases(self):
        p = self.suite.player()
        self.suite.seed_money(p, matter=12)
        buy = lambda index, req: self.rpc(p, "duel_character_inventory_slot_buy", {"slot_index": index, "request_id": req, "price": 0, "inventory_extra_slots": 25})
        req = "buy-" + self.suite.run_id
        first, replay = buy(25, req), buy(25, req)
        self.require(first.get("ok") and replay.get("ok") and replay.get("inventory_capacity") == 26
            and replay.get("progression", {}).get("matter") == 9, "real slot purchase and idempotent replay charge exactly three matter once")
        with ThreadPoolExecutor(max_workers=2) as pool:
            results = list(pool.map(lambda suffix: buy(26, req + suffix), ("-a", "-b")))
        current = self.rpc(p, "duel_character_get")
        self.require(all(r.get("ok") for r in results) and current.get("inventory_capacity") == 27
            and current.get("progression", {}).get("matter") == 6, "concurrent absolute-slot RPCs unlock one cell and charge one three-matter payment")
        denied = buy(28, req + "-skip")
        self.require(not denied.get("ok") and self.rpc(p, "duel_player_resources_get").get("matter") == 6,
            "server rejects skipping a locked cell without spending matter")

    def pending_loot(self, capacity):
        p = self.suite.player()
        filler, loot = self.items(p)
        self.suite.seed_money(p, matter=9, gold=100, ore=10, xp=0)
        self.seed_sheet(p, capacity, filler["id"])
        rid = self.seed_intent(p, capacity, loot["id"], 3)
        current = self.wait_rpc(p, "duel_character_get", lambda r: self.pending(r) == 3)
        resources = self.rpc(p, "duel_player_resources_get")
        record = self.read(p, "duel_match3_journal", "result:" + rid)
        receipt = record.get("reward", {})
        self.require(current.get("ok") and current.get("inventory_capacity") == capacity and self.item_count(current, loot["id"]) == 0
            and resources.get("gold") == 107 and resources.get("ore") == 12 and resources.get("matter") == 9 and resources.get("blueprint_green") == 0,
            "full " + str(capacity) + "-cell inventory commits wallet but retains all three undelivered items")
        self.require(receipt.get("item_loot_pending_count") == 3 and len(receipt.get("pending_items", [])) == 1
            and not receipt.get("delivered_items") and receipt.get("reward_ingots", 0) == 0 and not receipt.get("reward_recipe_item_id"),
            "persisted authoritative reward receipt explicitly marks pending items rather than delivered rewards")
        if capacity == 25:
            bought = self.rpc(p, "duel_character_inventory_slot_buy", {"slot_index": 25, "request_id": "loot-buy-" + self.suite.run_id})
            self.require(bought.get("ok") and self.pending(bought) == 2 and self.item_count(bought, loot["id"]) == 1
                and bought.get("progression", {}).get("matter") == 6, "buying one cell atomically delivers exactly one queued nonstackable item")
            delivered = 1
        else:
            denied = self.rpc(p, "duel_character_inventory_slot_buy", {"slot_index": 50, "request_id": "limit-" + self.suite.run_id})
            self.require(not denied.get("ok") and self.pending(self.rpc(p, "duel_character_get")) == 3,
                "full second page cannot exceed fifty cells or discard its pending loot")
            delivered = 0
        for index in range(3 - delivered):
            sale = {"source": "inventory", "inv_index": index, "expected_def_id": filler["id"],
                "request_id": "sale-" + self.suite.run_id + "-" + str(capacity) + "-" + str(index)}
            result = self.rpc(p, "duel_character_item_sell", sale)
            delivered += 1
            self.require(result.get("ok") and self.item_count(result, loot["id"]) == delivered and self.pending(result) == 3 - delivered,
                "freeing a real cell automatically delivers one pending item without losing or duplicating loot (capacity " + str(capacity) + ")")
            replay = self.rpc(p, "duel_character_item_sell", sale)
            self.require(replay.get("ok") and replay.get("sale_replayed") and self.item_count(replay, loot["id"]) == delivered
                and self.pending(replay) == 3 - delivered and replay.get("progression", {}).get("gold") == result.get("progression", {}).get("gold"),
                "identical sale replay cannot sell the newly auto-delivered recipe or credit a second sale")
        final = self.rpc(p, "duel_character_get")
        self.require(self.pending(final) == 0 and self.item_count(final, loot["id"]) == 3
            and self.rpc(p, "duel_player_resources_get").get("blueprint_green") == 3,
            "repeated profile/resource RPCs retain exactly three delivered recipes and spendable blueprint count after queue drains")
        self.evidence.append({"case": "prepared_pve_plan", "capacity": capacity, "source": "Console-seeded frozen plan for newly created account; actual recovery RPC/batch/queue", "pending_before": 3, "delivered_final": 3})

    def login_console(self):
        c = self.suite.config["console"]
        status, response = self.helpers.http(self.args.console_url, "POST", "/v2/console/authenticate", {"username": c["username"], "password": c["password"]})
        if status != 200 or not response.get("token"):
            raise RuntimeError("Isolated Console reauthentication failed")
        self.suite.console_auth = "Bearer " + response["token"]

    def restart(self):
        self.isolated.restart(self.helpers)
        self.login_console()

    def outbox_restart(self):
        p = self.suite.player()
        self.suite.seed_money(p, gold=123, matter=9)
        rid = "recovery-" + self.suite.run_id + "-outbox:pve"
        stat = "pve.kill.mine_2"
        before = self.suite.achievement_count(p, stat)
        now = int(time.time())
        # Explicit persisted crash-window fixture, not a simulated real battle:
        # the wallet was already committed; only its achievement delta is pending.
        record = {"schema": 1, "result_id": rid, "kind": "pve", "status": "committed", "reward_status": "committed",
            "created_at": now, "committed_at": now, "plan": {"achievement_deltas": {stat: 1}},
            "reward": {"reward_gold": 7, "delivered_items": [], "pending_items": []}}
        self.write(p, "duel_match3_journal", "result:" + rid, record)
        self.write(p, "duel_match3_journal", "pending", {"entries": {rid: {"kind": "pve", "created_at": now}}})
        self.restart()
        self.wait_rpc(p, "duel_match3_stats_get", lambda _: self.suite.achievement_count(p, stat) == before + 1)
        resources = self.rpc(p, "duel_player_resources_get")
        self.require(resources.get("gold") == 123 and self.suite.achievement_count(p, stat) == before + 1,
            "real process restart delivers a persisted committed achievement outbox once without recrediting wallet")
        self.restart()
        self.rpc(p, "duel_match3_stats_get")
        self.require(self.suite.achievement_count(p, stat) == before + 1 and self.rpc(p, "duel_player_resources_get").get("gold") == 123,
            "second restart and repeated recovery cannot duplicate committed achievement delta or currency")
        self.evidence.append({"case": "committed_outbox_restart", "source": "Console-seeded persisted committed/pending-effects crash window; real Nakama SIGKILL/restart and recovery RPC", "delta_once": 1})

    def global_boss_restart(self):
        p=self.suite.player();self.rpc(p,'duel_character_get')
        self.suite.seed_sheet(p,['global_boss_teleport'],[3])
        sheet=self.read(p,'duel_match3_character','sheet');catalog=self.isolated.catalog_fixture['items']
        sheet['equipment']=[]
        for slot in ('Helmet','Shoulders','Chest','Gloves','Legs','Feet','WeaponLeft','WeaponRight'):
            found=sorted(i for i,d in catalog.items() if d.get('kind','equipment')=='equipment' and d.get('slot')==slot and d.get('quality')=='epic')
            sheet['equipment'].append(found[-1] if found else '')
        self.write(p,'duel_match3_character','sheet',sheet)
        request='boss-recovery-'+self.suite.run_id
        event_id=self.rpc(p,'duel_global_boss_get')['event_id']
        entered=self.rpc(p,'duel_global_boss_enter',{'request_id':request,'event_id':event_id});mid=entered.get('match_id')
        self.require(entered.get('ok') and mid,'real boss admission consumes one owned teleport')
        ws=self.suite.connect(p);joined=ws.command('match_join',{'match_id':mid});initial=ws.event(mid,10)
        self.require('match' in joined and initial.get('globalBoss'),'actualWebSocket joins its true sharedHP boss attempt')
        ws.match_data(mid,17);ws.match_data(mid,13,self.helpers.valid_swap(initial['board']))
        board=ws.board_action(mid,p['uid'])
        attempt=self.read(p,'duel_global_boss_attempt',entered['attempt_id'])
        self.require(attempt.get('sequence')==1 and attempt.get('player_actions')==1 and attempt.get('snapshot',{}).get('board'),
            'accepted real boss action persists exact board actor stats and monotonic sequence')
        before=self.rpc(p,'duel_global_boss_get');frozen=attempt['snapshot']
        self.restart()
        resumed=self.wait_rpc(p,'duel_global_boss_enter',lambda r:r.get('ok'),payload={'request_id':request,'event_id':event_id})
        self.require(resumed.get('resumed') and resumed.get('attempt_id')==entered['attempt_id'] and resumed.get('match_id')!=mid,
            'real Nakama SIGKILL resumes the same weekly boss attempt in a new actor without fresh admission')
        second=self.suite.connect(p);newmid=resumed['match_id'];second.command('match_join',{'match_id':newmid});restored=second.event(newmid,10)
        self.require(restored.get('board')==board.get('board') and restored.get('aHp')==frozen['stats'][p['uid']]['hp']
            and restored.get('aMana')==frozen['stats'][p['uid']]['mana'] and restored.get('globalBossMyDamage')==attempt.get('actual_damage',0),
            'restarted boss attempt preserves board HP mana and its exact prior contribution without a reset')
        after=self.rpc(p,'duel_global_boss_get')
        self.require(after.get('token_count')==2 and after.get('attempts_used')==1 and after.get('hp')==before.get('hp'),
            'boss crash recovery does not spend another token or attempt or repeat global damage')
        self.require(self.rpc(p,'duel_match3_forfeit',{'match_id':newmid}).get('terminal'),
            'recovered boss actor can settle normally through acknowledged surrender')
        second.event(newmid,11)
        self.evidence.append({'case':'actual_global_boss_process_loss','actual_ws_join':True,'snapshot_sequence':1,
            'crash':'SIGKILL only identified isolated Nakama; sharedHP and exact own snapshot persisted','tokens_before':3,'after_paid_and_recovered':2})

    def global_boss_mail_restart(self):
        # An explicit persisted death-before-fanout crash window, restricted to
        # this fresh isolated DB and two accounts created by this probe.
        owners=[self.suite.player(),self.suite.player()]
        current=self.rpc(owners[0],'duel_global_boss_get');system='00000000-0000-0000-0000-000000000000'
        source=self.suite.storage('duel_global_boss',current['event_id'],system)
        oldid='week-'+str(source['starts_at']-604800)
        e=json.loads(json.dumps(source));e.update(id=oldid,starts_at=source['starts_at']-604800,ends_at=source['starts_at'],
            status='dead',hp=0,defeated_at=source['starts_at']-1,mail_done=False,worker_match_id='missing-before-crash',worker_until=int(time.time())+2)
        e.pop('mail_cursor',None)
        def system_write(key,value):
            path='/storage/duel_global_boss/'+quote(key,safe='')+'/'+system
            code,row=self.suite.console('GET',path)
            self.require(code in (200,404),'old-week outbox fixture stays inside isolated system boss collection')
            code,_=self.suite.console('PUT',path,{'value':json.dumps(value),'version':row.get('version','*') if code==200 else '*','permission_read':0,'permission_write':0})
            if code!=200:raise RuntimeError('Isolated old-week outbox CAS failed')
        system_write(oldid,e)
        idx=self.suite.storage('duel_global_boss','index',system);idx.setdefault('pending',{})[oldid]=True;system_write('index',idx)
        for p in owners:self.write(p,'duel_global_boss_player',oldid,{'schema':1,'event_id':oldid,'attempts_used':1,'qualified':True,'total_damage':100,'qualifying_damage':100,'requests':{}})
        self.restart()
        # Console-only observation: no boss_get/mail_get/user request may act
        # as a hidden scheduler. All player sockets are offline.
        deadline=time.monotonic()+18;complete=False
        while time.monotonic()<deadline:
            completed=self.suite.storage('duel_global_boss',oldid,system)
            if completed.get('mail_done'):
                complete=True;break
            time.sleep(.5)
        self.require(complete,'startup automatically resumes previous-week dead boss outbox with every contributor offline and no player RPC')
        for p in owners:
            mail=self.read(p,'duel_mail','global_boss:'+oldid);proof=self.read(p,'duel_global_boss_player',oldid)
            self.require(mail.get('claimed') is False and len(mail.get('attachments',[]))==7 and proof.get('reward_mail_created'),
                'old-week offline mail and durable per-owner creation proof are committed together')
        self.restart()
        for p in owners:
            inbox=self.rpc(p,'duel_mail_get')
            self.require(inbox.get('unread_count')==1 and len(inbox.get('messages',[]))==1,
                'another actual restart cannot duplicate a previously delivered weekly boss chest')
        self.evidence.append({'case':'previous_week_boss_death_outbox_process_loss','source':'explicit Console persisted death-before-fanout fixture; actual SIGKILL/restart; no player RPC until Console sees mail_done',
            'offline_contributors':2,'chests_each':1,'extra_restart_no_duplicates':True})

    def global_boss_ranking_restart(self):
        owners=[self.suite.player()for _ in range(3)]
        current=self.rpc(owners[0],'duel_global_boss_get');event=current['event_id']
        system='00000000-0000-0000-0000-000000000000'
        for n,p in enumerate(owners,1):
            # Explicit crash window: protected damage already committed, native
            # derived projection not yet written. No damage-claim RPC exists.
            self.write(p,'duel_global_boss_player',event,{'schema':1,'event_id':event,'total_damage':n*1000,
                'qualifying_damage':n*1000,'qualified':True,'attempts_used':n,'requests':{}})
        path='/storage/duel_global_boss/'+quote(event,safe='')+'/'+system
        code,row=self.suite.console('GET',path);assert code==200
        value=json.loads(row['value']);value['revision']=value.get('revision',0)+1;value['hp']-=6000
        code,_=self.suite.console('PUT',path,{'value':json.dumps(value),'version':row['version'],'permission_read':0,'permission_write':0})
        self.require(code==200,'ranking crash fixture updates only this disposable shared event and three owned participants')
        self.restart()
        deadline=time.monotonic()+18;repaired=False
        while time.monotonic()<deadline:
            if all(self.read(p,'duel_global_boss_player',event).get('ranked_total')==n*1000 for n,p in enumerate(owners,1)):
                repaired=True;break
            time.sleep(.4)
        self.require(repaired,'startup repairs native boss ranking from committed totals without any ranking or player RPC')
        small=owners[0];page=self.rpc(small,'duel_global_boss_leaderboard_get',{'event_id':event,'limit':1})
        self.require(page.get('ok')and len(page.get('entries',[]))==1 and page.get('self_entry',{}).get('user_id')==small['uid']
            and page['self_entry'].get('damage')==1000 and page['self_entry'].get('rank',0)>=3,
            'real native cursor page returns own aggregate position even outside the first page')
        collected=[]
        for _ in range(10):
            collected.extend(page.get('entries')or[])
            if not page.get('next_cursor'):break
            page=self.rpc(small,'duel_global_boss_leaderboard_get',{'event_id':event,'limit':1,'cursor':page['next_cursor']})
        self.require(all(sum(r.get('user_id')==p['uid']for r in collected)==1 for p in owners),
            'native pagination has exactly one row per player across multiple weekly attempts')
        self.require(all(next(r for r in collected if r['user_id']==p['uid'])['damage']==n*1000
            and next(r for r in collected if r['user_id']==p['uid'])['attempts_used']==n for n,p in enumerate(owners,1)),
            'native projection preserves summed damage and current weekly attempt count')
        status,denied=self.helpers.http(self.args.url,'POST','/v2/leaderboard/'+quote('escape_boss_'+event,safe=''),
            {'record':{'score':'99999999'}},'Bearer '+small['token'])
        self.require(status in(400,403)and'authoritative'in str(denied.get('message','')).lower(),
            'direct native client score submission is rejected by the authoritative boss leaderboard')
        self.restart()
        again=self.rpc(small,'duel_global_boss_leaderboard_get',{'event_id':event,'limit':20})
        self.require(all(sum(r.get('user_id')==p['uid']and r.get('damage')==n*1000 for r in again.get('entries',[]))==1
            for n,p in enumerate(owners,1)), 'another actual restart cannot increment or duplicate repaired aggregate boss scores')
        self.evidence.append({'case':'boss_ranking_projection_process_loss','source':'explicit committed-source-before-native-write fixture; actual isolated SIGKILL/restart; Console-only observation until repair',
            'participants':3,'totals':[1000,2000,3000],'one_row_per_owner':True,'direct_native_write_blocked':True})

    def race_restart(self):
        p = self.suite.player()
        self.suite.seed_money(p, matter=20)
        entered = self.rpc(p, "duel_match3_race_enter")
        self.require(entered.get("ok") and entered.get("matter") == 18, "real paid Race entry debits exactly two matter")
        created = self.rpc(p, "duel_match3_race_create")
        mid = created.get("match_id")
        self.require(created.get("ok") and isinstance(mid, str) and bool(mid), "actual paid Race RPC creates a real bot match")
        ws = self.suite.connect(p)
        joined = ws.command("match_join", {"match_id": mid})
        board = ws.event(mid, 10)
        self.require("match" in joined and board.get("pvpRace") is True, "actual WebSocket owner joins the paid bot Race and receives authoritative Race board")
        active = self.read(p, "duel_race_entry", "active")
        run_id = active.get("run_id")
        receipt = self.read(p, "duel_race_entry", "run:" + str(run_id))
        self.require(active.get("status") == "bound" and active.get("match_id") == mid and receipt.get("status") == "bound"
            and receipt.get("match_id") == mid and not receipt.get("finish_intent"), "real bound Race receipt records the actual live WebSocket match ID before restart")
        self.restart()  # Keep the real WS open until the process is killed.
        result = self.wait_rpc(p, "duel_player_resources_get", lambda r: r.get("matter") == 20)
        after = self.read(p, "duel_race_entry", "run:" + str(run_id))
        self.require(result.get("matter") == 20 and after.get("status") == "refunded" and after.get("refund_reason") == "orphaned_match",
            "real Nakama process loss of a bound paid bot Race refunds its original two-matter receipt once")
        self.rpc(p, "duel_player_resources_get")
        self.restart()
        self.require(self.rpc(p, "duel_player_resources_get").get("matter") == 20
            and self.rpc(p, "duel_player_resources_get").get("matter") == 20,
            "repeated RPCs and another real restart cannot refund the same paid Race receipt twice")
        self.evidence.append({"case": "actual_paid_bot_race_process_loss", "actual_ws_join": True, "actual_bound_match_id": mid,
            "crash": "SIGKILL only explicitly identified isolated Nakama container; PostgreSQL retained", "matter_before": 20, "after_paid_entry": 18, "after_recovery_and_repeat_restart": 20})

    def run(self):
        for name, operation in [("purchase", self.purchases), ("pending25", lambda: self.pending_loot(25)),
                ("pending50", lambda: self.pending_loot(50)), ("outbox_restart", self.outbox_restart), ("race_restart", self.race_restart),
                ("global_boss_restart",self.global_boss_restart),("global_boss_ranking_restart",self.global_boss_ranking_restart),
                ("global_boss_mail_restart",self.global_boss_mail_restart)]:
            try:
                operation()
            except Exception as error:
                detail = str(error) if re.fullmatch(r"[a-z0-9_]+ HTTP [0-9]{3}", str(error)) else type(error).__name__
                self.errors.append({"case": name, "error_type": type(error).__name__, "safe_detail": detail})
                print("ERROR " + name + " " + detail + " (credentials/payloads omitted)", flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--url", default="http://127.0.0.1:28350")
    parser.add_argument("--console-url", default="http://127.0.0.1:28351")
    parser.add_argument("--config", type=Path, required=True)
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--item-catalog", type=Path, required=True)
    parser.add_argument("--compose-project", required=True)
    parser.add_argument("--compose-file", type=Path, required=True)
    parser.add_argument("--helpers", type=Path, default=Path("/opt/escape/multiclient_review.py"))
    parser.add_argument("--report", type=Path, required=True)
    args = parser.parse_args()
    isolated = IsolatedNakama(args)  # All guards precede helper auth/network.
    helpers = load_helpers(args.helpers)
    probe = RecoveryProbe(args, helpers, isolated)
    try:
        probe.run()
    finally:
        try:
            probe.login_console()
        except Exception:
            pass
        probe.suite.cleanup()
        report = {"status": "passed" if not probe.errors and not probe.suite.failures else "failed",
            "target": "isolated localhost Nakama3.37.0; fresh disposable PostgreSQL", "checks": probe.suite.checks,
            "failures": probe.suite.failures, "errors": probe.errors, "evidence": probe.evidence,
            "isolated_nakama_restarts": isolated.restarts, "physical_android_tested": False,
            "scope": "Real HTTP/WS/payment/refund and durable recovery; prepared/committed outcome crash windows are explicitly Console-seeded only for accounts created in this run"}
        args.report.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    return 0 if report["status"] == "passed" else 1


if __name__ == "__main__":
    raise SystemExit(main())
