"""Bounded multi-client Escape integration tests; uses disposable accounts only."""
import argparse
import base64
from concurrent.futures import ThreadPoolExecutor
import hashlib
import json
from pathlib import Path
import re
import socket
import ssl
import struct
import time
from urllib.error import HTTPError
from urllib.parse import urlencode, urlsplit, quote
from urllib.request import Request, urlopen
import uuid
import select
import sys
class JsonWebSocket:
    """Small RFC 6455 client; one deadline bounds handshake and match traffic."""
    MAX_MESSAGE = 2 * 1024 * 1024

    def __init__(self, base_url, session_token, timeout=15):
        self.deadline = time.monotonic() + timeout
        self.buffer = bytearray()
        url = urlsplit(base_url)
        secure = url.scheme == "https"
        port = url.port or (443 if secure else 80)
        self.sock = socket.create_connection((url.hostname, port), timeout=self.remaining())
        try:
            if secure:
                self.sock.settimeout(self.remaining())
                self.sock = ssl.create_default_context().wrap_socket(self.sock, server_hostname=url.hostname)
            key = base64.b64encode(uuid.uuid4().bytes).decode()
            path = "/ws?" + urlencode({"lang": "en", "status": "true", "token": session_token, "format": "json"})
            headers = (f"GET {path} HTTP/1.1\r\nHost: {url.netloc}\r\nUpgrade: websocket\r\n"
                       f"Connection: Upgrade\r\nSec-WebSocket-Key: {key}\r\nSec-WebSocket-Version: 13\r\n\r\n")
            self.sock.sendall(headers.encode())
            response = bytearray()
            while b"\r\n\r\n" not in response:
                if len(response) >= 16384:
                    raise RuntimeError("WebSocket upgrade headers are too large")
                self.sock.settimeout(self.remaining())
                part = self.sock.recv(4096)
                if not part:
                    raise RuntimeError("WebSocket closed during upgrade")
                response.extend(part)
            header, extra = bytes(response).split(b"\r\n\r\n", 1)
            expected = base64.b64encode(hashlib.sha1((key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11").encode()).digest())
            fields = {}
            for line in header.split(b"\r\n")[1:]:
                name, separator, value = line.partition(b":")
                if separator:
                    fields[name.strip().lower()] = value.strip()
            if not header.startswith(b"HTTP/1.1 101 ") or fields.get(b"sec-websocket-accept") != expected:
                raise RuntimeError("Authenticated WebSocket upgrade failed")
            # The first data frame may arrive in the same TCP read as the headers.
            self.buffer.extend(extra)
        except BaseException:
            self.sock.close()
            raise

    def remaining(self):
        remaining = self.deadline - time.monotonic()
        if remaining <= 0:
            raise TimeoutError("WebSocket match smoke exceeded 15 seconds")
        return remaining

    def read_exact(self, size):
        while len(self.buffer) < size:
            self.sock.settimeout(self.remaining())
            data = self.sock.recv(max(4096, size - len(self.buffer)))
            if not data:
                raise RuntimeError("WebSocket closed before match completed")
            self.buffer.extend(data)
        result = bytes(self.buffer[:size])
        del self.buffer[:size]
        return result

    def send_frame(self, opcode, payload=b""):
        size = len(payload)
        header = bytes([0x80 | opcode])
        if size < 126:
            header += bytes([0x80 | size])
        elif size <= 65535:
            header += b"\xfe" + struct.pack("!H", size)
        else:
            header += b"\xff" + struct.pack("!Q", size)
        mask = uuid.uuid4().bytes[:4]
        masked = bytes(value ^ mask[index % 4] for index, value in enumerate(payload))
        self.sock.settimeout(self.remaining())
        self.sock.sendall(header + mask + masked)

    def send_json(self, message):
        self.send_frame(1, json.dumps(message, separators=(",", ":")).encode())

    def read_json(self):
        fragments = bytearray()
        in_message = False
        while True:
            first, second = self.read_exact(2)
            final, opcode = bool(first & 0x80), first & 0x0f
            if first & 0x70 or second & 0x80:
                raise RuntimeError("Invalid server WebSocket frame")
            size = second & 0x7f
            if size == 126:
                size = struct.unpack("!H", self.read_exact(2))[0]
            elif size == 127:
                size = struct.unpack("!Q", self.read_exact(8))[0]
            if size > self.MAX_MESSAGE or len(fragments) + size > self.MAX_MESSAGE:
                raise RuntimeError("WebSocket message is too large")
            if opcode >= 8 and (not final or size > 125):
                raise RuntimeError("Invalid WebSocket control frame")
            payload = self.read_exact(size)
            if opcode == 9:
                self.send_frame(10, payload)
                continue
            if opcode == 10:
                continue
            if opcode == 8:
                raise RuntimeError("WebSocket closed before game-over")
            if opcode == 1 and not in_message:
                in_message = True
            elif opcode != 0 or not in_message:
                raise RuntimeError("Unexpected WebSocket data frame")
            fragments.extend(payload)
            if final:
                return json.loads(fragments.decode())

    def wait_match_data(self, match_id, opcode):
        while True:
            self.remaining()
            message = self.read_json()
            if "error" in message:
                raise RuntimeError("Nakama rejected WebSocket match operation")
            data = message.get("match_data", {})
            if data.get("match_id") == match_id and str(data.get("op_code")) == str(opcode):
                return json.loads(base64.b64decode(data.get("data", ""), validate=True))

    def close(self):
        try:
            self.send_frame(8, struct.pack("!H", 1000))
        except (OSError, TimeoutError):
            pass
        finally:
            self.sock.close()


class SocketClient(JsonWebSocket):
    def __init__(self, base_url, token):
        super().__init__(base_url, token, timeout=20)
        self.pending = []

    def await_message(self, predicate, timeout=20):
        self.deadline = time.monotonic() + timeout
        while True:
            for i, message in enumerate(self.pending):
                if predicate(message):
                    return self.pending.pop(i)
            message = self.read_json()
            if predicate(message):
                return message
            self.pending.append(message)

    def command(self, kind, payload):
        cid = uuid.uuid4().hex
        self.deadline = time.monotonic() + 20
        self.send_json({"cid": cid, kind: payload})
        return self.await_message(lambda m: m.get("cid") == cid)

    def match_data(self, match_id, opcode, payload=None):
        self.deadline = time.monotonic() + 20
        data = "" if payload is None else base64.b64encode(json.dumps(payload).encode()).decode()
        self.send_json({"match_data_send": {"match_id": match_id, "op_code": str(opcode), "data": data}})

    def event(self, match_id, opcode, timeout=20):
        result = self.await_message(lambda m: m.get("match_data", {}).get("match_id") == match_id
            and str(m["match_data"].get("op_code")) == str(opcode), timeout)
        return json.loads(base64.b64decode(result["match_data"]["data"]))

    def board_action(self, match_id, actor_uid):
        def is_action(message):
            data = message.get("match_data", {})
            if data.get("match_id") != match_id or str(data.get("op_code")) != "10":
                return False
            payload = json.loads(base64.b64decode(data["data"]))
            return payload.get("actionType") == 1 and payload.get("actionActorUserId") == actor_uid
        result = self.await_message(is_action)
        return json.loads(base64.b64decode(result["match_data"]["data"]))


def http(base, method, path, payload=None, auth=None):
    headers = {"Content-Type": "application/json"}
    if auth:
        headers["Authorization"] = auth
    body = None if payload is None else json.dumps(payload).encode()
    for attempt in range(5):
        try:
            with urlopen(Request(base + path, data=body, headers=headers, method=method), timeout=20) as r:
                data = r.read()
                return r.status, json.loads(data) if data else {}
        except HTTPError as e:
            try:
                error = json.loads(e.read())
            except (ValueError, UnicodeDecodeError):
                error = {}
            if e.code == 429 and attempt < 4:
                # Respect the shared server create bucket instead of treating a
                # correctly enforced ten-second refill as a game regression.
                retry = re.search(r"retry_after_seconds=(\d+)", str(error.get("message", "")))
                delay = int(retry.group(1)) if retry else 2
                time.sleep(min(15, max(1, delay)))
                continue
            return e.code, error


class Suite:
    def __init__(self, args):
        self.args = args
        self.config = json.loads(args.config.read_text())
        basic = base64.b64encode((self.config["socket"]["server_key"] + ":").encode()).decode()
        self.basic = "Basic " + basic
        status, login = http(args.console_url, "POST", "/v2/console/authenticate", {
            "username": self.config["console"]["username"], "password": self.config["console"]["password"]})
        if status != 200 or not login.get("token"):
            raise RuntimeError("Console authentication failed: HTTP " + str(status))
        self.console_auth = "Bearer " + login["token"]
        self.players = []
        self.sockets = []
        self.tournament_ids = set()
        self.checks = []
        self.failures = []
        self.errors = []
        self.run_id = uuid.uuid4().hex[:12]

    def console(self, method, path, payload=None):
        return http(self.args.console_url, method, "/v2/console" + path, payload, self.console_auth)

    def check(self, condition, text):
        self.checks.append({"passed": bool(condition), "check": text})
        print(("PASS " if condition else "FAIL ") + text, flush=True)
        if not condition:
            self.failures.append(text)
        return bool(condition)

    def player(self):
        index = len(self.players)
        username = "multi_" + self.run_id + "_" + str(index)
        status, session = http(self.args.url, "POST", "/v2/account/authenticate/device?create=true&username=" + username,
            {"id": "escape-multi-" + uuid.uuid4().hex}, self.basic)
        if status != 200:
            raise RuntimeError("Temporary account create failed: HTTP " + str(status))
        claims = json.loads(base64.urlsafe_b64decode(session["token"].split(".")[1] + "=="))
        p = {"token": session["token"], "uid": claims["uid"], "epoch": 0, "username": username}
        self.players.append(p)  # Always track before performing any further operation.
        claim = self.rpc(p, "duel_session_claim", {})
        p["epoch"] = claim["session_epoch"]
        self.rpc(p, "duel_match3_stats_get", {})
        self.rpc(p, "duel_player_resources_get", {})
        return p

    def rpc(self, p, name, payload=None):
        data = {"session_epoch": p["epoch"]}
        data.update(payload or {})
        status, result = http(self.args.url, "POST", "/v2/rpc/" + name, json.dumps(data), "Bearer " + p["token"])
        if status != 200:
            raise RuntimeError(name + " HTTP " + str(status))
        return json.loads(result.get("payload", "{}"))

    def connect(self, p):
        ws = SocketClient(self.args.url, p["token"])
        self.sockets.append(ws)
        return ws

    def pair(self, a, b, rules="classic"):
        wa, wb = self.connect(a), self.connect(b)
        group = self.run_id + uuid.uuid4().hex[:8]
        query = "+properties.mode:match3 +properties.rules:" + rules + " +properties.test_group:" + group
        for ws in (wa, wb):
            result = ws.command("matchmaker_add", {"min_count": 2, "max_count": 2, "query": query,
                "string_properties": {"mode": "match3", "rules": rules, "test_group": group}, "count_multiple": 2})
            if "matchmaker_ticket" not in result:
                raise RuntimeError("Private test matchmaker ticket rejected")
        ma = wa.await_message(lambda m: "matchmaker_matched" in m, timeout=25)["matchmaker_matched"]
        mb = wb.await_message(lambda m: "matchmaker_matched" in m, timeout=25)["matchmaker_matched"]
        mid = ma.get("match_id", "")
        self.check(bool(mid) and mid == mb.get("match_id"), rules + ": both clients matched to the same private match")
        for ws in (wa, wb):
            result = ws.command("match_join", {"match_id": mid})
            if "match" not in result:
                raise RuntimeError("Joining private test match failed")
        ba, bb = wa.event(mid, 10), wb.event(mid, 10)
        self.check(ba["board"] == bb["board"] and ba["activeUserId"] == bb["activeUserId"],
            rules + ": both clients receive identical initial authoritative board")
        return mid, wa, wb, ba

    def stats(self, p):
        return self.rpc(p, "duel_match3_stats_get")

    def isolated_only(self, scenario):
        url = urlsplit(self.args.url)
        if url.hostname not in ("127.0.0.1", "localhost") or url.port != 28350:
            raise RuntimeError(scenario + " requires the isolated localhost test backend on port 28350")

    def storage(self, collection, key, uid):
        status, row = self.console("GET", "/storage/" + quote(collection, safe="") + "/"
            + quote(key, safe="") + "/" + quote(uid, safe=""))
        if status == 404:
            return {}
        if status != 200:
            raise RuntimeError("Test-owned storage inspect failed: HTTP " + str(status))
        return json.loads(row["value"])

    def achievement_count(self, p, name):
        value = self.storage("duel_match3_stats", "summary", p["uid"])
        return value.get("achievement_stats", {}).get(name, 0)

    def finish(self, mid, loser, winner, loser_p, winner_p):
        before_l, before_w = self.stats(loser_p), self.stats(winner_p)
        loser.match_data(mid, 12)
        # Repeat in the same match-loop batch exercises the ended-state guard.
        loser.match_data(mid, 12)
        result = winner.event(mid, 11)
        self.check(result.get("winnerUserId") == winner_p["uid"], "forfeit reports the remaining test participant as winner")
        time.sleep(0.3)
        after_l, after_w = self.stats(loser_p), self.stats(winner_p)
        self.check(after_l.get("losses", 0) == before_l.get("losses", 0) + 1
            and after_w.get("wins", 0) == before_w.get("wins", 0) + 1
            and after_l.get("played", 0) == before_l.get("played", 0) + 1
            and after_w.get("played", 0) == before_w.get("played", 0) + 1,
            "repeated forfeit records exactly one win/loss per participant")
        return result

    def pvp(self):
        a, b = self.player(), self.player()
        mid, wa, wb, board = self.pair(a, b)
        actor_p, actor_ws, peer_p, peer_ws = (a, wa, b, wb) if board["activeUserId"] == a["uid"] else (b, wb, a, wa)
        peer_ws.match_data(mid, 13, {"actionType": 1, "fromX": 0, "fromY": 0, "toX": 1, "toY": 0})
        reject = peer_ws.event(mid, 14)
        self.check(reject.get("reason") == "not_your_turn", "inactive client cannot make a combat move")
        # ACK starts the server turn clock, just like the Unity animation-completed signal.
        actor_ws.match_data(mid, 17)
        actor_ws.event(mid, 10)
        peer_ws.event(mid, 10)
        move = valid_swap(board["board"])
        actor_ws.match_data(mid, 13, move)
        moved_a, moved_b = actor_ws.event(mid, 10), peer_ws.event(mid, 10)
        self.check(moved_a["board"] == moved_b["board"] and moved_a.get("actionType") == 1,
            "a valid combat move is resolved and synchronized to both clients")
        peer_ws.close()
        gone = actor_ws.event(mid, 18)
        self.check(gone.get("disconnectedUserId") == peer_p["uid"] and gone.get("reconnectGraceSeconds") == 300,
            "one disconnected client receives a five-minute reconnect window")
        restored = self.connect(peer_p)
        join = restored.command("match_join", {"match_id": mid})
        self.check("match" in join, "disconnected test client can rejoin the same match")
        restored_board = restored.event(mid, 10)
        actor_ws.match_data(mid, 16)
        current = actor_ws.event(mid, 10)
        self.check(restored_board["board"] == current["board"] and restored_board["activeUserId"] == current["activeUserId"],
            "rejoined client receives a full board with the current turn")
        self.finish(mid, restored, actor_ws, peer_p, actor_p)

    def double_disconnect(self):
        a, b = self.player(), self.player()
        mid, wa, wb, initial = self.pair(a, b)
        wa.close()
        wb.close()
        time.sleep(0.6)
        restored = self.connect(a)
        result = restored.command("match_join", {"match_id": mid})
        self.check("match" in result, "both disconnected clients retain their match during reconnect grace")
        if "match" in result:
            restored_b = self.connect(b)
            result_b = restored_b.command("match_join", {"match_id": mid})
            self.check("match" in result_b, "second disconnected client can also rejoin the preserved match")
            if "match" in result_b:
                ba, bb = restored.event(mid, 10), restored_b.event(mid, 10)
                self.check(ba["board"] == bb["board"] == initial["board"]
                    and ba["activeUserId"] == bb["activeUserId"] == initial["activeUserId"],
                    "both rejoined clients receive the preserved board and turn")
                self.finish(mid, restored, restored_b, a, b)

    def overlap_reconnect(self):
        a, b = self.player(), self.player()
        mid, wa, wb, initial = self.pair(a, b)
        # Replace the active participant so the new connection must perform a real move.
        if initial["activeUserId"] != a["uid"]:
            a, b, wa, wb = b, a, wb, wa
        replacement = self.connect(a)
        joined = replacement.command("match_join", {"match_id": mid})
        self.check("match" in joined, "same-account replacement socket can join before old socket closes")
        snapshot = replacement.event(mid, 10, timeout=5)
        self.check(snapshot["board"] == initial["board"] and snapshot["activeUserId"] == a["uid"],
            "replacement socket receives its initial authoritative snapshot without requesting it")
        before = [self.stats(p) for p in (a, b)]
        try:
            wa.match_data(mid, 13, valid_swap(snapshot["board"]))
            wa.match_data(mid, 12)
        except (OSError, RuntimeError):
            # Closing the superseded connection is also a valid stale-session policy.
            pass
        time.sleep(0.3)
        replacement.match_data(mid, 16)
        untouched = replacement.event(mid, 10)
        after = [self.stats(p) for p in (a, b)]
        self.check(untouched["board"] == snapshot["board"] and untouched["activeUserId"] == a["uid"]
            and all(after[i].get("played", 0) == before[i].get("played", 0) for i in range(2)),
            "superseded socket cannot move or forfeit the replacement connection's match")
        wa.close()
        time.sleep(0.4)
        replacement.match_data(mid, 16)
        try:
            replacement.event(mid, 10, timeout=3)
            replacement_preserved = True
        except TimeoutError:
            replacement_preserved = False
        self.check(replacement_preserved, "old socket departure preserves the replacement socket presence")
        # Observe authoritative presence by console, without reading any other match.
        status, state = self.console("GET", "/match/" + quote(mid, safe="") + "/state")
        if status == 200:
            value = json.loads(state["state"])
            self.check(a["uid"] in value.get("presences", {}), "authoritative match still tracks replacement user after overlap reconnect")
        replacement.match_data(mid, 17)
        replacement.event(mid, 10)
        wb.event(mid, 10)
        replacement.match_data(mid, 13, valid_swap(untouched["board"]))
        moved_a, moved_b = replacement.board_action(mid, a["uid"]), wb.board_action(mid, a["uid"])
        self.check(moved_a["board"] == moved_b["board"] and moved_a.get("actionType") == 1,
            "replacement socket can make an authoritative combat move after the old socket leaves")
        self.finish(mid, wb, replacement, b, a)

    def race(self):
        a, b = self.player(), self.player()
        for p in (a, b):
            self.seed_money(p, gold=20000, ore=20000, matter=100)
            entry = self.rpc(p, "duel_match3_race_enter")
            self.check(entry.get("ok") is True and entry.get("matter") == 98,
                "Race entry cost deducts exactly two matter for a disposable account")
        mid, wa, wb, board = self.pair(a, b, "race")
        self.check(board.get("pvpRace") is True and board.get("raceGoalMana", 0) > 0,
            "Race queue selects authoritative Race rules and configured mana goal")
        cancellations = [self.rpc(p, "duel_match3_race_cancel") for p in (a, b)]
        self.check(all(v.get("ok") is False and v.get("err") == "race_entry_in_use" for v in cancellations),
            "a started human Race binds both entry receipts and rejects cancellation")
        self.finish(mid, wa, wb, a, b)
        balances = [self.rpc(p, "duel_player_resources_get") for p in (a, b)]
        self.check(balances[0].get("matter") == 98 and balances[1].get("matter") == 108,
            "paid human Race retains both fees and grants the winner exactly one ten-matter reward")

    def natural(self):
        for rules in ("classic", "pro", "race"):
            a, b = self.player(), self.player()
            if rules == "race":
                for p in (a, b):
                    self.seed_money(p, gold=20000, ore=20000, matter=100)
                    self.rpc(p, "duel_match3_race_enter")
            before = [self.stats(p) for p in (a, b)]
            mid, wa, wb, board = self.pair(a, b, rules)
            by_uid = {a["uid"]: (a, wa), b["uid"]: (b, wb)}
            game_over = None
            for step in range(150):
                actor_p, actor_ws = by_uid[board["activeUserId"]]
                previous_last_turn = board.get("raceLastTurnUserId")
                if board.get("turnEndsAtUnixMs", 0) == 0:
                    actor_ws.match_data(mid, 17)
                    ba, bb = wa.event(mid, 10), wb.event(mid, 10)
                    if ba["board"] != bb["board"]:
                        raise RuntimeError("Authoritative boards diverged after ACK")
                    board = ba
                move = valid_swap(board["board"], race=rules == "race")
                actor_ws.match_data(mid, 13, move)
                ba, bb = wa.event(mid, 10), wb.event(mid, 10)
                if ba["board"] != bb["board"] or ba["activeUserId"] != bb["activeUserId"]:
                    raise RuntimeError("Authoritative boards diverged after move")
                board = ba
                ended = board.get("aHp", 1) <= 0 or board.get("bHp", 1) <= 0
                if rules == "race":
                    goal = board.get("raceGoalMana", 200)
                    ended = ((not board.get("raceLastTurnUserId") and max(board.get("aMana", 0), board.get("bMana", 0)) >= goal)
                        or (previous_last_turn == actor_p["uid"] and not board.get("extraTurn")))
                if ended:
                    game_over = wa.event(mid, 11)
                    other_result = wb.event(mid, 11)
                    self.check(game_over == other_result, rules + ": both clients receive the same natural result")
                    break
                if (step + 1) % 20 == 0:
                    print("EVIDENCE natural " + rules + " moves=" + str(step + 1)
                        + " hp=" + str(board.get("aHp")) + "/" + str(board.get("bHp"))
                        + " mana=" + str(board.get("aMana")) + "/" + str(board.get("bMana")), flush=True)
                time.sleep(0.2)
            self.check(game_over is not None, rules + ": natural victory reached within 150 valid synchronized moves")
            if game_over is not None:
                after = [self.stats(p) for p in (a, b)]
                self.check(all(after[i].get("played", 0) == before[i].get("played", 0) + 1 for i in range(2)),
                    rules + ": natural finish records exactly one played match per client")
                print("EVIDENCE natural " + rules + " finished moves=" + str(step + 1)
                    + " rewardXp=" + str(game_over.get("rewardXp", 0))
                    + " rewardGold=" + str(game_over.get("rewardGold", 0))
                    + " rewardOre=" + str(game_over.get("rewardOre", 0)), flush=True)
            else:
                self.finish(mid, wa, wb, a, b)
            wa.close()
            wb.close()

    def race_unpaid(self):
        self.isolated_only("Unpaid Race regression")
        a, b = self.player(), self.player()
        before = [self.rpc(p, "duel_player_resources_get") for p in (a, b)]
        created = self.rpc(a, "duel_match3_race_create")
        self.check(created.get("ok") is False and created.get("err") == "race_entry_required"
            and not created.get("match_id"), "unpaid Race bot creation is rejected before a match starts")
        # No match is played: both queue tickets must be rejected at the before hook.
        group = self.run_id + uuid.uuid4().hex[:8]
        query = "+properties.mode:match3 +properties.rules:race +properties.test_group:" + group
        for p in (a, b):
            ws = self.connect(p)
            result = ws.command("matchmaker_add", {"min_count": 2, "max_count": 2, "query": query,
                "string_properties": {"mode": "match3", "rules": "race", "test_group": group}, "count_multiple": 2})
            ticket = result.get("matchmaker_ticket", {}).get("ticket")
            self.check("error" in result and not ticket, "unpaid Race matchmaking ticket is rejected")
            if ticket:
                ws.command("matchmaker_remove", {"ticket": ticket})
        after = [self.rpc(p, "duel_player_resources_get") for p in (a, b)]
        self.check(all(after[i].get("matter", 0) == before[i].get("matter", 0) for i in range(2)),
            "denied unpaid Race grants no spendable resources")

    def race_receipts(self):
        self.isolated_only("Race entry receipt regression")
        p = self.player()
        self.seed_money(p, matter=100)
        first = self.rpc(p, "duel_match3_race_enter")
        repeated = self.rpc(p, "duel_match3_race_enter")
        self.check(first.get("ok") is True and repeated.get("ok") is True
            and first.get("matter") == repeated.get("matter") == 98,
            "repeated pending Race entry charges the configured two matter only once")
        cancelled = self.rpc(p, "duel_match3_race_cancel")
        repeated_cancel = self.rpc(p, "duel_match3_race_cancel")
        self.check(cancelled.get("matter") == repeated_cancel.get("matter") == 100,
            "pending Race cancellation refunds its exact entry once")
        denied = self.rpc(p, "duel_match3_race_create")
        self.check(denied.get("ok") is False and not denied.get("match_id"),
            "a cancelled entry cannot start a Race bot match")
        self.rpc(p, "duel_match3_race_enter")
        created = self.rpc(p, "duel_match3_race_create")
        self.check(created.get("ok") is True and bool(created.get("match_id")),
            "a paid Race entry can start a bot match")
        mid = created.get("match_id")
        if not mid:
            return
        ws = self.connect(p)
        joined = ws.command("match_join", {"match_id": mid})
        self.check("match" in joined, "paid bot Race accepts its owner and sends a Race board")
        board = ws.event(mid, 10)
        self.check(board.get("pvpRace") is True, "paid bot match uses authoritative Race rules")
        rejected_cancel = self.rpc(p, "duel_match3_race_cancel")
        rejected_reuse = self.rpc(p, "duel_match3_race_create")
        resources = self.rpc(p, "duel_player_resources_get")
        self.check(rejected_cancel.get("ok") is False and rejected_cancel.get("err") == "race_entry_in_use"
            and resources.get("matter") == 98, "a claimed Race entry cannot be refunded while its match is active")
        self.check(rejected_reuse.get("ok") is False and not rejected_reuse.get("match_id"),
            "one paid entry cannot create a second active Race match")
        ws.match_data(mid, 12)
        ws.match_data(mid, 12)
        ws.event(mid, 11)
        after = self.rpc(p, "duel_match3_race_create")
        self.check(after.get("ok") is False and not after.get("match_id"),
            "a settled Race entry cannot be reused after match completion")

    def seed_money(self, p, **money):
        path = "/storage/duel_match3_progress/profile/" + p["uid"]
        status, row = self.console("GET", path)
        value = json.loads(row["value"]) if status == 200 else {}
        value.update(money)
        payload = {"value": json.dumps(value), "version": row.get("version", "*") if status == 200 else "*",
            "permission_read": 1, "permission_write": 0}
        status, _ = self.console("PUT", path, payload)
        if status != 200:
            raise RuntimeError("Test-only resource seed failed: HTTP " + str(status))

    def arena(self):
        self.isolated_only("Arena regression")
        participants = [self.player() for _ in range(8)]
        tier = {"arena_kind": "gold", "bet_tier": "2400"}
        initial = self.rpc(participants[0], "duel_arena_queue_poll", tier)
        if initial.get("queue_count", 0) > 0:
            raise RuntimeError("Selected public arena queue is occupied; refusing to mix test and real players")
        for p in participants:
            self.seed_money(p, gold=5000)
        with ThreadPoolExecutor(max_workers=8) as pool:
            joined_results = list(pool.map(lambda p: self.rpc(p, "duel_arena_queue_join", tier), participants))
        for joined in joined_results:
            self.check(joined.get("ok") is True, "one temporary participant enters gold-2400 arena")
        polls = [self.rpc(p, "duel_arena_queue_poll", tier) for p in participants]
        tids = {v.get("tournament", {}).get("id") for v in polls if v.get("tournament", {}).get("active")}
        tids.discard("")
        self.tournament_ids.update(tids)
        assembled = len(tids) == 1 and all(v.get("tournament", {}).get("id") in tids for v in polls)
        self.check(assembled, "eight independently authenticated clients enter one shared eight-player tournament")
        if not assembled:
            counts = [v.get("queue_count", 0) for v in polls]
            print("EVIDENCE arena visible queue counts=" + json.dumps(counts) + "; active tournaments=" + str(len(tids)), flush=True)
            # Cancel every test-only queue entry repeatedly; each RPC may use a different Lua VM.
            for _ in range(4):
                for p in participants:
                    self.rpc(p, "duel_arena_queue_leave", tier)
            return
        tid = next(iter(tids))
        roster = set(p["uid"] for p in participants)
        tournament = polls[-1]["tournament"]
        rows = tournament.get("qf", [])
        self.check(len(rows) == 4 and {r[side] for r in rows for side in ("uid_a", "uid_b")} == roster,
            "the tournament bracket contains exactly the eight temporary accounts")
        if not all(r[side] in roster for r in rows for side in ("uid_a", "uid_b")):
            raise RuntimeError("Unexpected participant found; stopping without modifying that match")
        by_uid = {p["uid"]: p for p in participants}
        sockets = {uid: self.connect(p) for uid, p in by_uid.items()}
        after_entry = [self.rpc(p, "duel_player_resources_get") for p in participants]
        self.check(all(value.get("gold") == 2600 for value in after_entry),
            "eight simultaneous arena joins charge every participant exactly one entry fee")
        # This participant will lose its quarterfinal; the eventual champion places no bets.
        bettor = by_uid[rows[0]["uid_a"]]
        bet_stat = "slaughter.arena_bets_placed"
        before_bets = self.achievement_count(bettor, bet_stat)
        repeated_bet = {"slot": 2, "side": "a"}
        with ThreadPoolExecutor(max_workers=4) as pool:
            responses = list(pool.map(lambda _: self.rpc(bettor, "duel_arena_place_bet", repeated_bet), range(4)))
        self.check(all(v.get("ok") is True for v in responses),
            "four concurrent deliveries of one legal arena prediction are accepted idempotently")
        changes = [self.rpc(bettor, "duel_arena_place_bet", value) for value in (
            {"slot": 2, "side": "b"}, {"slot": 2, "clear": True}, {"slot": 2, "side": "b"})]
        self.check(all(v.get("ok") is True for v in changes)
            and self.achievement_count(bettor, bet_stat) == before_bets + 1,
            "repeating, replacing, clearing and restoring one prediction counts its achievement exactly once")
        invalid_bets = [self.rpc(bettor, "duel_arena_place_bet", value) for value in (
            {"slot": 1, "side": "a"}, {"slot": 2, "side": "invalid"}, {"slot": 1.5, "side": "a"},
            {"slot": 0, "side": "a"}, {"slot": 999, "side": "b"})]
        self.check(all(v.get("ok") is False and v.get("err") in ("own_pair", "bad_side", "bad_slot") for v in invalid_bets)
            and self.achievement_count(bettor, bet_stat) == before_bets + 1,
            "own-pair, invalid-side and noninteger/out-of-range predictions are rejected without achievements")
        survivor = participants[0]
        for round_name, field in (("qf", "qf"), ("sf", "sf"), ("final", "final_pairs")):
            deadline = time.monotonic() + 35
            while True:
                polled = self.rpc(survivor, "duel_arena_queue_poll", tier)["tournament"]
                if polled.get("phase") == round_name:
                    break
                if time.monotonic() >= deadline:
                    self.check(False, round_name + ": countdown advances to next arena round")
                    return
                time.sleep(1)
            pairs = polled.get(field, [])
            self.check(bool(pairs), round_name + ": bracket provides fight matches")
            fights = []
            for row in pairs:
                uid_a, uid_b, mid = row["uid_a"], row["uid_b"], row["match_id"]
                if uid_a not in roster or uid_b not in roster:
                    raise RuntimeError("Refusing to operate on a non-test arena participant")
                for uid in (uid_a, uid_b):
                    result = sockets[uid].command("match_join", {"match_id": mid})
                    if "match" not in result:
                        raise RuntimeError("Arena match join failed")
                sockets[uid_a].event(mid, 10)
                sockets[uid_b].event(mid, 10)
                fights.append((mid, sockets[uid_a], sockets[uid_b], by_uid[uid_a], by_uid[uid_b]))
            # All different pairs finish together, reaching separate authoritative callbacks/VMs.
            with ThreadPoolExecutor(max_workers=len(fights)) as pool:
                list(pool.map(lambda fight: self.finish(*fight), fights))
            winners = [fight[4] for fight in fights]
            status, stored = self.console("GET", "/storage/arena_tournament/" + tid
                + "/00000000-0000-0000-0000-000000000000")
            if status != 200:
                raise RuntimeError("Completed test tournament state unavailable: HTTP " + str(status))
            saved = json.loads(stored["value"])
            if round_name == "final":
                persisted = (saved.get("phase") == "done" and saved.get("reward_receipt", {}).get("user_id") == winners[0]["uid"]
                    and len(saved.get("final", [])) == 1 and saved["final"][0].get("status") == "done")
            else:
                # Completed QF/SF rows are deliberately compacted after the next roster is committed.
                next_round = "sf" if round_name == "qf" else "final"
                next_pairs = saved.get(next_round, [])
                next_roster = {row[side] for row in next_pairs for side in ("uid_a", "uid_b")}
                persisted = (len(next_pairs) * 2 == len(winners)
                    and next_roster == {p["uid"] for p in winners}
                    and (saved.get("phase") == next_round
                        or (saved.get("phase") == "countdown" and saved.get("next_round") == next_round)))
            self.check(persisted, round_name + ": simultaneous fight results preserve every winner in the committed next stage")
            survivor = winners[0]
        final_resources = self.rpc(survivor, "duel_player_resources_get")
        # No bets: gold 5000 - entry 2400 + final 12000 = 14600.
        self.check(final_resources.get("gold") == 14600, "gold tournament grants its final prize exactly once")
        for _ in range(3):
            self.rpc(survivor, "duel_arena_queue_poll", tier)
        repeated_resources = self.rpc(survivor, "duel_player_resources_get")
        self.check(repeated_resources.get("gold") == 14600,
            "repeated completed-tournament polls cannot repeat its final prize")

    def cleanup(self):
        for ws in self.sockets:
            try:
                ws.close()
            except Exception:
                pass
        # Explicitly remove only IDs that were created by this run.
        for p in self.players:
            try:
                status, export = self.console("GET", "/account/" + p["uid"] + "/export")
                if status == 200:
                    for record in export.get("leaderboard_records", []) or []:
                        if not isinstance(record, dict):
                            continue
                        leaderboard = record.get("leaderboard_id")
                        if leaderboard:
                            self.console("DELETE", "/leaderboard/" + quote(leaderboard, safe="") + "/owner/" + p["uid"])
            except Exception as error:
                print("EVIDENCE test leaderboard export cleanup " + type(error).__name__, flush=True)
            try:
                status, _ = self.console("DELETE", "/account/" + p["uid"] + "?record_deletion=false")
                if status not in (200, 404):
                    time.sleep(0.5)
                    status, _ = self.console("DELETE", "/account/" + p["uid"] + "?record_deletion=false")
                if status not in (200, 404):
                    print("EVIDENCE temporary account deletion HTTP=" + str(status), flush=True)
                self.check(status in (200, 404), "temporary account and its user-owned storage removed")
            except Exception:
                self.check(False, "temporary account cleanup completed")
        for tid in self.tournament_ids:
            self.console("DELETE", "/storage/arena_tournament/" + tid + "/00000000-0000-0000-0000-000000000000")


def valid_swap(board, race=False):
    def line_score(values):
        matched = set()
        for y in range(6):
            for x in range(6):
                v = values[y * 6 + x]
                if (x < 4 and v == values[y * 6 + x + 1] == values[y * 6 + x + 2]) or (
                    y < 4 and v == values[(y + 1) * 6 + x] == values[(y + 2) * 6 + x]):
                    if x < 4 and v == values[y * 6 + x + 1] == values[y * 6 + x + 2]:
                        matched.update((y * 6 + x, y * 6 + x + 1, y * 6 + x + 2))
                    if y < 4 and v == values[(y + 1) * 6 + x] == values[(y + 2) * 6 + x]:
                        matched.update((y * 6 + x, (y + 1) * 6 + x, (y + 2) * 6 + x))
        weights = {1: 12, 2: 7, 3: 3, 4: 1, 5: 1} if race else {1: 1, 2: 1, 3: 1, 4: 100, 5: 1}
        return sum(weights.get(values[i], 0) for i in matched)
    best = None
    best_score = 0
    for y in range(6):
        for x in range(6):
            for tx, ty in ((x + 1, y), (x, y + 1)):
                if tx >= 6 or ty >= 6:
                    continue
                values = list(board)
                i, j = y * 6 + x, ty * 6 + tx
                values[i], values[j] = values[j], values[i]
                score = line_score(values)
                if score > best_score:
                    best_score = score
                    best = {"actionType": 1, "fromX": x, "fromY": y, "toX": tx, "toY": ty}
    if best:
        return best
    raise RuntimeError("Server returned board with no valid swaps")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--url", default="https://prokrust-play.ru")
    parser.add_argument("--console-url", default="http://127.0.0.1:27351")
    parser.add_argument("--config", type=Path, default=Path("secrets/server.yml"))
    parser.add_argument("--scenario", choices=("probe", "pvp", "double-disconnect", "overlap", "race", "arena", "natural", "race-unpaid", "race-receipts", "all"), default="pvp")
    parser.add_argument("--report", type=Path)
    args = parser.parse_args()
    target = urlsplit(args.url)
    if args.scenario in ("arena", "race-unpaid", "race-receipts", "all") and (
            target.hostname not in ("127.0.0.1", "localhost") or target.port != 28350):
        parser.error("This scenario requires the isolated localhost test backend on port 28350")
    suite = Suite(args)
    try:
        if args.scenario == "probe":
            status, tournaments = suite.console("GET", "/storage?collection=arena_tournament")
            suite.check(status == 200, "arena storage can be inspected without reading player identities")
            print("Arena persisted object count=" + str(tournaments.get("total_count", 0)), flush=True)
        else:
            scenarios = {"pvp": suite.pvp, "double-disconnect": suite.double_disconnect,
                "overlap": suite.overlap_reconnect, "race": suite.race, "arena": suite.arena, "natural": suite.natural,
                "race-unpaid": suite.race_unpaid, "race-receipts": suite.race_receipts}
            for name, fn in scenarios.items():
                if args.scenario not in (name, "all"):
                    continue
                print("SCENARIO " + name, flush=True)
                try:
                    fn()
                except Exception as error:
                    # Do not print requests, tokens, console credentials or a traceback containing URLs.
                    detail = name + ": " + type(error).__name__ + ": " + str(error)
                    suite.errors.append(detail)
                    print("ERROR " + detail, flush=True)
    finally:
        suite.cleanup()
        print("RESULT passed=" + str(sum(c["passed"] for c in suite.checks)) + " failed=" + str(len(suite.failures))
            + " errors=" + str(len(suite.errors)), flush=True)
        if args.report:
            args.report.write_text(json.dumps({"checks": suite.checks, "errors": suite.errors}, indent=2), encoding="utf-8")
    return 1 if suite.failures or suite.errors else 0


if __name__ == "__main__":
    sys.exit(main())
