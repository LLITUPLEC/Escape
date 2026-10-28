"""Bounded multi-client Escape integration tests; uses disposable accounts only."""
import argparse
import base64
from concurrent.futures import ThreadPoolExecutor
import hashlib
import json
import math
from pathlib import Path
import re
import socket
import ssl
import struct
import subprocess
import time
from urllib.error import HTTPError
from urllib.parse import urlencode, urlsplit, quote
from urllib.request import Request, urlopen
import uuid
import select
import sys
import traceback
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
        self.arena_ui_snapshots = []
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
        device_id = "escape-multi-" + uuid.uuid4().hex
        status, session = http(self.args.url, "POST", "/v2/account/authenticate/device?create=true&username=" + username,
            {"id": device_id}, self.basic)
        if status != 200:
            raise RuntimeError("Temporary account create failed: HTTP " + str(status))
        claims = json.loads(base64.urlsafe_b64decode(session["token"].split(".")[1] + "=="))
        p = {"token": session["token"], "uid": claims["uid"], "epoch": 0, "username": username, "device_id": device_id}
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
        parsed = json.loads(result.get("payload", "{}"))
        if name in ("duel_character_get", "duel_character_loot_deliver", "duel_consumables_use", "duel_consumables_get") and parsed.get("ok") is False:
            print("EVIDENCE RPC " + name + " err=" + str(parsed.get("err")), flush=True)
        return parsed

    def connect(self, p):
        ws = SocketClient(self.args.url, p["token"])
        self.sockets.append(ws)
        return ws

    def pair(self, a, b, rules="classic"):
        mid, wa, wb = self.pending_pair(a, b, rules)
        for ws in (wa, wb):
            result = ws.command("match_join", {"match_id": mid})
            if "match" not in result:
                raise RuntimeError("Joining private test match failed")
        ba, bb = wa.event(mid, 10), wb.event(mid, 10)
        self.check(ba["board"] == bb["board"] and ba["activeUserId"] == bb["activeUserId"],
            rules + ": both clients receive identical initial authoritative board")
        self.check(ba.get("roundNumber") == bb.get("roundNumber") == 1,
            rules + ": fresh authoritative battle starts at round one for both clients")
        return mid, wa, wb, ba

    def pending_pair(self, a, b, rules="classic"):
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
        return mid, wa, wb

    def stats(self, p):
        return self.rpc(p, "duel_match3_stats_get")

    def isolated_only(self, scenario):
        url = urlsplit(self.args.url)
        console = urlsplit(self.args.console_url)
        if (url.hostname not in ("127.0.0.1", "localhost") or url.port != 28350
                or console.hostname not in ("127.0.0.1", "localhost") or console.port != 28351):
            raise RuntimeError(scenario + " requires isolated localhost ports 28350 and 28351")

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
        counts_match = (after_l.get("losses", 0) == before_l.get("losses", 0) + 1
            and after_w.get("wins", 0) == before_w.get("wins", 0) + 1
            and after_l.get("played", 0) == before_l.get("played", 0) + 1
            and after_w.get("played", 0) == before_w.get("played", 0) + 1)
        self.check(counts_match, "repeated forfeit records exactly one win/loss per participant")
        if not counts_match:
            print("EVIDENCE result counters " + json.dumps({name: {key: source.get(key, 0) for key in ("played", "wins", "losses")}
                for name, source in (("before_loser", before_l), ("after_loser", after_l), ("before_winner", before_w), ("after_winner", after_w))}), flush=True)
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
        self.check(restored_board.get("roundNumber") == current.get("roundNumber") == moved_a.get("roundNumber"),
            "snapshot and reconnect preserve the authoritative round counter")
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

    def lifecycle(self):
        self.isolated_only("Authoritative surrender and missing-admission regression")
        a, b, outsider = self.player(), self.player(), self.player()
        heartbeat_socket = self.connect(a)
        began = time.monotonic()
        heartbeat_wire = heartbeat_socket.command("rpc", {"id": "duel_online_ping_and_count", "payload": "{}"})
        heartbeat = json.loads(heartbeat_wire.get("rpc", {}).get("payload", "{}"))
        self.check(heartbeat.get("ok") is True and heartbeat.get("session_epoch") == a["epoch"]
            and isinstance(heartbeat.get("count"), int) and time.monotonic() - began < 3,
            "actual authenticated WebSocket RPC heartbeat accepts empty JSON payload and returns the account epoch within client timeout")
        for p in (a, b):
            self.seed_money(p, matter=100)
            self.check(self.rpc(p, "duel_match3_race_enter").get("ok"), "lifecycle fixture pays one real Race entry")
        mid, wa, wb, board = self.pair(a, b, "race")
        before = [self.stats(p) for p in (a, b)]
        rejected = self.rpc(outsider, "duel_match3_forfeit", {"match_id": mid})
        self.check(rejected.get("err") == "not_participant", "outsider cannot surrender another two-client match")
        stale = self.rpc(a, "duel_match3_forfeit", {"match_id": mid, "session_epoch": a["epoch"] - 1})
        self.check(stale.get("err") == "session_stale", "surrender RPC rejects a stale session before signaling the match")
        with ThreadPoolExecutor(max_workers=4) as pool:
            replies = list(pool.map(lambda _: self.rpc(a, "duel_match3_forfeit", {"match_id": mid}), range(4)))
        self.check(all(r.get("ok") and r.get("accepted") and r.get("terminal") and r.get("winner_user_id") == b["uid"] for r in replies),
            "four concurrent acknowledged Race surrenders freeze one winner after authoritative settlement")
        over_a, over_b = wa.event(mid, 11), wb.event(mid, 11)
        self.check(over_a == over_b and over_a.get("winnerUserId") == b["uid"],
            "both Race clients receive the identical terminal packet without the opponent finishing manually")
        self.check(all(self.race_settlement(p).get("status") == "settled" for p in (a, b)),
            "both paid Race reservations are settled immediately after one surrender")
        after = [self.stats(p) for p in (a, b)]
        balances = [self.rpc(p, "duel_player_resources_get").get("matter") for p in (a, b)]
        self.check(balances == [98, 102] and all(after[i].get("played", 0) == before[i].get("played", 0) + 1 for i in range(2)),
            "concurrent surrender charges both stakes once and records exactly one win and loss")
        time.sleep(.4)
        replay = self.rpc(b, "duel_match3_forfeit", {"match_id": mid})
        self.check(replay.get("terminal") and replay.get("winner_user_id") == b["uid"],
            "lost-ack retry after authoritative match deletion reads the bounded own terminal receipt")
        for p in (a, b):
            self.check(self.rpc(p, "duel_match3_race_enter").get("ok"), "both former Race participants can pay and queue again immediately")
        mid2, next_a, next_b, _ = self.pair(a, b, "race")
        self.check(mid2 != mid and self.rpc(b, "duel_match3_forfeit", {"match_id": mid2}).get("terminal"),
            "a second actual Race starts and ends without the old race_entry_in_use lock")
        next_a.event(mid2, 11); next_b.event(mid2, 11)
        for rules in ("classic", "pro"):
            x, y = self.player(), self.player()
            base = [self.stats(p) for p in (x, y)]
            pending, wx, wy = self.pending_pair(x, y, rules)
            wy.close()  # Matched in queue, then network lost before match_join.
            self.check("match" in wx.command("match_join", {"match_id": pending}), rules + ": remaining client joins its reserved admission")
            started = time.monotonic()
            cancelled = wx.event(pending, 11, timeout=27)
            elapsed = time.monotonic() - started
            self.check(cancelled.get("matchCancelled") is True and cancelled.get("isDraw") is True
                and not cancelled.get("winnerUserId") and cancelled.get("endReason") == "opponent_not_ready" and elapsed <= 26,
                rules + ": absent second participant cancels within bounded admission and notifies the waiting client")
            final = [self.stats(p) for p in (x, y)]
            self.check(all(final[i].get("played", 0) == base[i].get("played", 0) for i in range(2)),
                rules + ": failed admission grants neither a played result nor a free win")
            retry, new_x, new_y, _ = self.pair(x, y, rules)
            self.check(self.rpc(x, "duel_match3_forfeit", {"match_id": retry}).get("terminal"),
                rules + ": cancelled admission leaves both players free to requeue into a real match")
            new_x.event(retry, 11); new_y.event(retry, 11)
        self.pve_never_join()
        self.tournament_disconnect(never_join=False)
        self.tournament_disconnect(never_join=True)

    def pve_never_join(self):
        self.isolated_only("Paid never-joined PvE lifecycle regression")
        p = self.player(); self.seed_money(p, energy=100)
        self.seed_sheet(p, ["elixir_assault"], [1])
        self.check(self.rpc(p, "duel_consumables_use", self.consumable_use_request(p, 0, "elixir_assault",
            "nojoin-" + self.run_id)).get("ok"), "never-joined PvE fixture arms one real owned elixir")
        before = self.stats(p)
        first = self.rpc(p, "duel_match3_pve_create", {"bot_id": "mine_1"})
        if not first.get("ok") or not first.get("match_id"): raise RuntimeError("Paid no-join mine launch failed")
        entry = self.storage("duel_pve_entry", "active", p["uid"])
        self.check(entry.get("status") == "bound" and self.storage("duel_match3_progress", "profile", p["uid"]).get("energy") == 85,
            "a created but never joined mine match binds one exact fifteen-energy payment")
        state_path = "/match/" + quote(first["match_id"], safe="") + "/state"
        initial_status, initial_state = self.console("GET", state_path)
        self.check(initial_status == 200 and json.loads(initial_state.get("state", "{}")).get("match_id") == first["match_id"],
            "Console observes this specific fresh mine match before testing its admission expiry")
        deadline = time.monotonic() + 27
        status = 200
        match_gone = False
        while time.monotonic() < deadline:
            status, state_response = self.console("GET", state_path)
            # Nakama Console maps ErrMatchNotFound to InvalidArgument (HTTP400),
            # unlike the player API. The successful initial observation above
            # rules out accidentally treating a missing route/invalid ID as expiry.
            match_gone = status == 404 or (status == 400 and state_response.get("code") == 3
                and str(state_response.get("message", "")).startswith("Match not found"))
            if match_gone: break
            time.sleep(.5)
        print("EVIDENCE never-joined mine terminal Console HTTP=" + str(status), flush=True)
        self.check(match_gone, "never-joined paid mine match terminates within bounded admission")
        after = self.stats(p)
        energy = self.rpc(p, "duel_player_resources_get").get("energy")
        armed = self.rpc(p, "duel_consumables_get").get("consumables", {}).get("armed_item_id")
        self.check(after.get("played", 0) == before.get("played", 0) and after.get("losses", 0) == before.get("losses", 0)
            and energy == 100 and armed == "elixir_assault",
            "abandoned admission refunds original energy once, keeps armed elixir and records no phantom loss")
        second = self.rpc(p, "duel_match3_pve_create", {"bot_id": "mine_1"})
        self.check(second.get("ok") and second.get("match_id") != first["match_id"]
            and self.rpc(p, "duel_player_resources_get").get("energy") == 85,
            "new mine entry recovers old reservation and charges once rather than twice")
        ws = self.connect(p); mid = second["match_id"]
        if "match" not in ws.command("match_join", {"match_id": mid}): raise RuntimeError("Recovered mine launch join failed")
        board = ws.event(mid, 10)
        self.check(board.get("aConsumableId") == "elixir_assault"
            and self.rpc(p, "duel_consumables_get").get("consumables", {}).get("armed_item_id") == "",
            "only the subsequently joined mine battle consumes and applies the preserved elixir")
        self.check(board.get("roundNumber") == 1, "fresh mine admission starts the authoritative first round")
        self.check(self.rpc(p, "duel_match3_forfeit", {"match_id": mid}).get("terminal"),
            "subsequently joined mine can surrender through the same confirmed terminal contract")
        ws.event(mid, 11)

    def tournament_disconnect(self, never_join):
        self.isolated_only("Temporary gold bracket with a real human versus bot admission")
        active, absent, quitter = self.player(), self.player(), self.player()
        tier = {"arena_kind": "gold", "bet_tier": "600"}
        for p in (active, absent, quitter):
            self.seed_tournament_access(p, gold=5000)
            if not self.rpc(p, "duel_arena_queue_join", tier).get("ok"):
                raise RuntimeError("Isolated gold lifecycle entry rejected")
        system = "00000000-0000-0000-0000-000000000000"
        registry_path = "/storage/arena_queue/state/" + system
        status, row = self.console("GET", registry_path)
        registry = json.loads(row["value"])
        queue = registry["queues"]["gold:600"]
        while len(queue["entries"]) < 8:
            index = len(queue["entries"])
            queue["entries"].append({"uid": "zz-bot-lifecycle-" + uuid.uuid4().hex, "display": "Стенд " + str(index), "bot": True, "bet_tier": "600"})
        status, _ = self.console("PUT", registry_path, {"value": json.dumps(registry), "version": row["version"], "permission_read": 0, "permission_write": 0})
        if status != 200: raise RuntimeError("Isolated gold fixture CAS failed")
        initial = self.rpc(active, "duel_arena_queue_poll", tier)["tournament"]
        tid = initial["id"]; self.tournament_ids.add(tid)
        path = "/storage/arena_tournament/" + tid + "/" + system
        status, row = self.console("GET", path); tournament = json.loads(row["value"])
        bots = [uid for uid, part in tournament["parts"].items() if part.get("bot")]
        slots = [(active["uid"], quitter["uid"]), (absent["uid"], bots[0]), (bots[1], bots[2]), (bots[3], bots[4])]
        tournament["qf"] = [{"uid_a": a, "uid_b": b, "display_a": tournament["parts"][a]["display"],
            "display_b": tournament["parts"][b]["display"], "bot_a": tournament["parts"][a].get("bot", False),
            "bot_b": tournament["parts"][b].get("bot", False), "status": "pending"} for a, b in slots]
        tournament["countdown_until"] = int(time.time()) - 1
        status, _ = self.console("PUT", path, {"value": json.dumps(tournament), "version": row["version"], "permission_read": 0, "permission_write": 0})
        if status != 200: raise RuntimeError("Isolated bracket shape CAS failed")
        polls = {p["uid"]: self.rpc(p, "duel_arena_queue_poll", tier) for p in (active, absent, quitter)}
        qf = polls[active["uid"]]["tournament"].get("qf", [])
        missing_pair = next(r for r in qf if absent["uid"] in (r["uid_a"], r["uid_b"]))
        self.check(missing_pair.get("hp_known_a") is True and missing_pair.get("hp_known_b") is True
            and missing_pair.get("hp_max_a") == missing_pair.get("hp_max_b") == 150,
            "gold bot pair publishes known initial HP before the human has ever joined")
        for p in (active, quitter):
            ws = self.connect(p); mid = polls[p["uid"]]["tournament"]["join_match_id"]
            if "match" not in ws.command("match_join", {"match_id": mid}): raise RuntimeError("Gold human pair admission failed")
            p["lifecycle_socket"], p["lifecycle_match"] = ws, mid
        active["lifecycle_socket"].event(active["lifecycle_match"], 10)
        quitter["lifecycle_socket"].event(quitter["lifecycle_match"], 10)
        self.check(self.rpc(quitter, "duel_match3_forfeit", {"match_id": quitter["lifecycle_match"]}).get("terminal"),
            "one real two-human gold pair completes while the separate bot pair is missing")
        active["lifecycle_socket"].event(active["lifecycle_match"], 11)
        quitter["lifecycle_socket"].event(quitter["lifecycle_match"], 11)
        if not never_join:
            gone = self.connect(absent); mid = polls[absent["uid"]]["tournament"]["join_match_id"]
            if "match" not in gone.command("match_join", {"match_id": mid}): raise RuntimeError("Gold bot pair admission failed")
            gone.event(mid, 10); gone.close()
        began = time.monotonic(); final = None
        while time.monotonic() - began < 28:
            final = self.rpc(active, "duel_arena_queue_poll", tier)["tournament"]
            if final.get("phase") == "countdown" and final.get("next_round") == "sf": break
            time.sleep(.5)
        elapsed = time.monotonic() - began
        finished = next(r for r in final.get("qf", []) if absent["uid"] in (r["uid_a"], r["uid_b"]))
        self.check(final.get("phase") == "countdown" and final.get("next_round") == "sf" and finished.get("status") == "done"
            and finished.get("winner_uid") == bots[0] and elapsed <= 27,
            ("never-joined" if never_join else "disconnected") + " gold bot opponent resolves promptly and releases every waiting semifinalist")
        self.check(finished.get("hp_known_a") and finished.get("hp_known_b") and finished.get("hp_max_a") == finished.get("hp_max_b") == 150,
            "technical gold result retains authoritative HP and maxima in completed bracket history")

    def race(self):
        self.isolated_only("Race economy regression")
        a, b = self.player(), self.player()
        info = self.rpc(a, "duel_match3_race_info")
        self.check(info.get("ok") is True and resource_totals(info.get("entry")) == {"matter": 2}
            and resource_totals(info.get("rewards")).get("matter") == 4
            and info.get("bot_entry_refund") is True,
            "public Race policy reports entry two, human prize four and bot stake refund")
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
        result = self.finish(mid, wa, wb, a, b)
        balances = [self.rpc(p, "duel_player_resources_get") for p in (a, b)]
        self.check(balances[0].get("matter") == 98 and balances[1].get("matter") == 102
            and result.get("rewardMatter") == 4,
            "paid human Race retains both two-matter fees and pays the winner four matter exactly once")
        loser_receipt, winner_receipt = self.race_settlement(a), self.race_settlement(b)
        self.check(loser_receipt.get("reward", {}).get("reward_matter") == 0
            and winner_receipt.get("reward", {}).get("reward_matter") == 4
            and loser_receipt.get("reward", {}).get("reward_xp") == 10
            and winner_receipt.get("reward", {}).get("reward_xp") == 200,
            "human Race receipts preserve the existing loss ten XP and win two hundred XP")
        for p in (a, b):
            self.rpc(p, "duel_match3_race_cancel")
            self.rpc(p, "duel_match3_race_cancel")
        repeated = [self.rpc(p, "duel_player_resources_get") for p in (a, b)]
        self.check([v.get("matter") for v in repeated] == [98, 102],
            "settled human receipts cannot be refunded by repeated cancellation")

    def natural(self):
        self.isolated_only("Natural combat and Race regression")
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
                if rules == "race" and game_over.get("winnerUserId") in by_uid:
                    balances = [self.rpc(p, "duel_player_resources_get") for p in (a, b)]
                    self.check(all(value.get("matter") == (102 if p["uid"] == game_over["winnerUserId"] else 98)
                        for p, value in zip((a, b), balances)) and game_over.get("rewardMatter") == 4,
                        "natural human Race uses the same two-entry/four-prize policy")
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
        before_stats = self.stats(p)
        before_profile = self.storage("duel_match3_progress", "profile", p["uid"])
        ws.match_data(mid, 12)
        ws.match_data(mid, 12)
        result = ws.event(mid, 11)
        self.check(result.get("winnerUserId") == created.get("bot_user_id")
            and result.get("entryRefund") is True and result.get("rewardMatter") == 2
            and result.get("rewardXp") == 0,
            "bot Race forfeit reports the human stake refund without an extra victory prize or loss XP")
        refunded = self.rpc(p, "duel_player_resources_get")
        receipt = self.race_settlement(p)
        self.check(refunded.get("matter") == 100
            and resource_totals(receipt.get("costs")) == {"matter": 2}
            and receipt.get("reward", {}).get("reward_matter") == 2,
            "bot Race loss returns its actual paid two matter exactly once")
        for _ in range(3):
            self.rpc(p, "duel_match3_race_cancel")
        final_resources = self.rpc(p, "duel_player_resources_get")
        final_stats = self.stats(p)
        final_profile = self.storage("duel_match3_progress", "profile", p["uid"])
        self.check(final_resources.get("matter") == 100
            and final_stats.get("played", 0) == before_stats.get("played", 0) + 1
            and final_stats.get("losses", 0) == before_stats.get("losses", 0) + 1
            and final_profile.get("xp", 0) == before_profile.get("xp", 0),
            "duplicate bot forfeit and repeated cancellation preserve one loss, unchanged XP and one refund")
        after = self.rpc(p, "duel_match3_race_create")
        self.check(after.get("ok") is False and not after.get("match_id"),
            "a settled Race entry cannot be reused after match completion")

    def race_settlement(self, p):
        active = self.storage("duel_race_entry", "active", p["uid"])
        run_id = active.get("run_id")
        self.check(active.get("status") == "settled" and bool(run_id),
            "test-owned Race entry reaches durable settled state")
        receipt = self.storage("duel_race_entry", "run:" + run_id, p["uid"]) if run_id else {}
        self.check(receipt.get("status") == "settled" and receipt.get("receipt_id") == active.get("receipt_id"),
            "per-run Race settlement uses the same immutable paid receipt")
        return receipt

    def race_legacy_refund(self):
        self.isolated_only("Legacy paid Race refund regression")
        p = self.player()
        costs = [{"resource": "matter", "amount": 7}, {"resource": "gold", "amount": 11},
            {"resource": "ore", "amount": 13}]
        # Only this run's fresh account: model a receipt paid under an older price,
        # with the wallet already debited, then exercise the real migration path.
        self.seed_money(p, matter=93, gold=989, ore=987, race_entry_pending=True, race_entry_costs=costs)
        entered = self.rpc(p, "duel_match3_race_enter")
        self.check(entered.get("ok") is True and entered.get("spent") == 0
            and entered.get("matter") == 93 and entered.get("gold") == 989 and entered.get("ore") == 987
            and resource_totals(entered.get("entry")) == resource_totals(costs),
            "legacy paid receipt migrates its actual multi-resource stake without charging today's entry")
        created = self.rpc(p, "duel_match3_race_create")
        if not self.check(created.get("ok") is True and bool(created.get("match_id")),
                "migrated paid receipt starts a bot Race"):
            return
        mid = created["match_id"]
        ws = self.connect(p)
        if "match" not in ws.command("match_join", {"match_id": mid}):
            raise RuntimeError("Legacy paid bot Race join failed")
        ws.event(mid, 10)
        before_stats = self.stats(p)
        ws.match_data(mid, 12)
        ws.match_data(mid, 12)
        result = ws.event(mid, 11)
        self.check(result.get("entryRefund") is True and result.get("rewardMatter") == 7
            and result.get("rewardGold") == 11 and result.get("rewardOre") == 13
            and result.get("rewardXp") == 0,
            "bot loss refund payload uses actual receipt seven/eleven/thirteen without extra four matter")
        receipt = self.race_settlement(p)
        self.check(resource_totals(receipt.get("costs")) == resource_totals(costs)
            and receipt.get("reward", {}).get("reward_matter") == 7
            and receipt.get("reward", {}).get("reward_gold") == 11
            and receipt.get("reward", {}).get("reward_ore") == 13,
            "durable settled reward retains the actual paid multi-resource stake")
        for _ in range(3):
            self.rpc(p, "duel_match3_race_cancel")
        resources = self.rpc(p, "duel_player_resources_get")
        stats = self.stats(p)
        self.check(resources.get("matter") == 100 and resources.get("gold") == 1000 and resources.get("ore") == 1000
            and stats.get("played", 0) == before_stats.get("played", 0) + 1
            and stats.get("losses", 0) == before_stats.get("losses", 0) + 1,
            "legacy bot receipt refunds each resource once despite duplicate forfeit and repeated cancellation")

    def race_policy(self):
        self.isolated_only("Race 0.3.3 policy integration")
        self.race()
        self.race_receipts()
        self.race_legacy_refund()

    def friends(self):
        self.isolated_only("Friend invitation notification and match-kind regression")
        a, b = self.player(), self.player()
        for owner, peer in ((a, b), (b, a)):
            status, _ = http(self.args.url, "POST", "/v2/friend?" + urlencode({"ids": peer["uid"]}), {}, "Bearer " + owner["token"])
            if status != 200: raise RuntimeError("Test-owned friendship failed: HTTP " + str(status))
            self.seed_money(owner, matter=100)
        wa, wb = self.connect(a), self.connect(b)
        def notification(ws, code, iid):
            def find(message):
                for item in message.get("notifications", {}).get("notifications", []):
                    body = json.loads(item.get("content", "{}"))
                    if item.get("code") == code and body.get("invite_id") == iid: return body
                return None
            return find(ws.await_message(lambda message: find(message) is not None))
        invalid = self.rpc(a, "duel_friends_race_invite", {"target_user_id": b["uid"], "kind": "pro"})
        self.check(invalid.get("err") == "invalid_invite_kind", "friend invite rejects unknown match kind before creating a proposal")
        for kind in ("duel", "race"):
            before = [self.rpc(p, "duel_player_resources_get").get("matter") for p in (a, b)]
            proposal = self.rpc(a, "duel_friends_race_invite", {"target_user_id": b["uid"], "kind": kind})
            if not proposal.get("ok"): raise RuntimeError("Friend " + kind + " invitation rejected: " + str(proposal.get("err")))
            iid = proposal["invite_id"]
            incoming = notification(wb, 10020, iid)
            self.check(incoming.get("kind") == kind and incoming.get("from_user_id") == a["uid"], kind + ": recipient receives actual incoming invitation over its live socket")
            rejected = self.rpc(a, "duel_friends_race_respond", {"invite_id": iid, "accept": True})
            self.check(rejected.get("err") == "not_invitee", kind + ": sender cannot accept the recipient's proposal")
            ready = self.rpc(b, "duel_friends_race_respond", {"invite_id": iid, "accept": True, "kind": "race" if kind == "duel" else "duel"})
            if not ready.get("ok"): raise RuntimeError("Friend accept failed: " + str(ready.get("err")))
            mid = ready["match_id"]
            updates = [notification(ws, 10021, iid) for ws in (wa, wb)]
            self.check(ready.get("kind") == kind and all(n.get("kind") == kind and n.get("match_id") == mid for n in updates), kind + ": both ready notifications preserve the stored server match kind despite forged response kind")
            for ws in (wa, wb):
                if "match" not in ws.command("match_join", {"match_id": mid}): raise RuntimeError("Friend match join rejected")
            boards = [ws.event(mid, 10) for ws in (wa, wb)]
            self.check(all(board.get("pvpRace") == (kind == "race") and board.get("roundNumber") == 1 for board in boards), kind + ": both friends join the correct real rules at round one")
            after = [self.rpc(p, "duel_player_resources_get").get("matter") for p in (a, b)]
            fee = 2 if kind == "race" else 0
            self.check(after == [amount - fee for amount in before], kind + ": admission charges exactly the intended fee and never charges Race resources for duel")
            self.finish(mid, wa, wb, a, b)
            for ws in (wa, wb): ws.command("match_leave", {"match_id": mid})
        proposal = self.rpc(a, "duel_friends_race_invite", {"target_user_id": b["uid"], "kind": "duel"})
        iid = proposal["invite_id"]; notification(wb, 10020, iid)
        self.rpc(b, "duel_friends_race_respond", {"invite_id": iid, "accept": False})
        declined = notification(wa, 10021, iid)
        self.check(declined.get("status") == "declined" and declined.get("kind") == "duel", "declining a duel sends the matching sender notification")

    def cosmetics(self):
        self.isolated_only("Account gender and cosmetics regression")
        female, legacy = self.player(), self.player()

        def raw_rpc(player, name, payload=None):
            status, result = http(self.args.url, "POST", "/v2/rpc/" + name, json.dumps(payload or {}), "Bearer " + player["token"])
            if status != 200:
                raise RuntimeError(name + " HTTP " + str(status))
            return json.loads(result.get("payload", "{}"))

        fresh = raw_rpc(female, "duel_account_gender_get")
        self.check(fresh.get("ok") and fresh.get("can_choose") is True, "real AuthenticateDevice after-hook opens only a new account's registration choice")
        pending = raw_rpc(female, "duel_player_cosmetics_get")
        self.check(pending.get("err") == "registration_incomplete", "pending registration cannot persist cosmetics")
        chosen = raw_rpc(female, "duel_account_gender_register", {"gender": "female"})
        self.check(chosen.get("ok") and chosen.get("gender") == "female" and chosen.get("can_choose"), "new account stages its female registration choice on the server")
        password = uuid.uuid4().hex
        status, _ = http(self.args.url, "POST", "/v2/account/link/email", {"email": "invalid", "password": password}, "Bearer " + female["token"])
        retry = raw_rpc(female, "duel_account_gender_register", {"gender": "male"})
        self.check(status != 200 and retry.get("ok") and retry.get("can_choose"), "invalid email registration does not permanently freeze its pending gender")
        raw_rpc(female, "duel_account_gender_register", {"gender": "female"})
        email = "multi-" + self.run_id + "@example.test"
        status, _ = http(self.args.url, "POST", "/v2/account/link/email", {"email": email, "password": password}, "Bearer " + female["token"])
        self.check(status == 200, "temporary account registers by linking email through the real API")
        gender = raw_rpc(female, "duel_account_gender_get")
        self.check(gender.get("gender") == "female" and gender.get("can_choose") is False, "successful LinkEmail after-hook permanently finalizes the staged female identity")
        changed = raw_rpc(female, "duel_account_gender_register", {"gender": "male"})
        self.check(changed.get("err") == "account_gender_immutable", "registered gender cannot be changed by an authenticated RPC")
        first = raw_rpc(female, "duel_player_cosmetics_get")
        themes = {theme["id"]: theme for theme in first.get("themes", [])}
        self.check(first.get("ok") and len(themes) == 10 and themes.get("dawnwarden", {}).get("unlocked")
                   and sum(bool(theme.get("unlocked")) for theme in themes.values()) == 1,
                   "server returns ten themes and only the starter is initially unlocked")
        locked = raw_rpc(female, "duel_player_cosmetics_select", {"theme_id": "phoenix"})
        forged = raw_rpc(female, "duel_player_cosmetics_select", {"theme_id": "phoenix", "unlocked": True, "gender": "male"})
        self.check(locked.get("err") == "appearance_locked" and forged.get("err") == "bad_payload", "client claims cannot unlock a locked appearance or supply gender/progress")
        status, _ = http(self.args.url, "PUT", "/v2/storage", {"objects": [{"collection": "duel_player_cosmetics", "key": "appearance_v1",
            "value": json.dumps({"schema_version": 1, "selected_theme": "phoenix", "unlocked": {"phoenix": True}}),
            "permission_read": 1, "permission_write": 1}]}, "Bearer " + female["token"])
        self.check(status != 200 and raw_rpc(female, "duel_player_cosmetics_get").get("selected_theme") == "default",
                   "real storage request guard blocks direct cosmetic writes including a forged first create")
        status, _ = http(self.args.url, "PUT", "/v2/storage", {"objects": [{"collection": "duel_account_identity", "key": "gender_v1",
            "value": json.dumps({"version": 1, "gender": "male", "finalized": True}), "permission_read": 1, "permission_write": 1}]}, "Bearer " + female["token"])
        self.check(status != 200 and raw_rpc(female, "duel_account_gender_get").get("gender") == "female", "real storage request guard blocks direct gender replacement")

        path = "/storage/duel_match3_stats/summary/" + female["uid"]
        status, row = self.console("GET", path)
        if status not in (200, 404):
            raise RuntimeError("Temporary cosmetic progress source read failed")
        stats = json.loads(row["value"]) if status == 200 else {}
        stats["achievement_claimed"] = list(set(stats.get("achievement_claimed", []) + ["dnn.win_1hp:0"]))
        stats.setdefault("modes", {}).setdefault("duel", {})["wins"] = 25
        status, _ = self.console("PUT", path, {"value": json.dumps(stats), "version": row.get("version", "*"), "permission_read": 1, "permission_write": 0})
        if status != 200:
            raise RuntimeError("Isolated cosmetic achievement fixture failed")
        earned = raw_rpc(female, "duel_player_cosmetics_get")
        earned_themes = {theme["id"]: theme for theme in earned.get("themes", [])}
        self.check(earned_themes.get("phoenix", {}).get("unlocked") and all(rule.get("met") for rule in earned_themes.get("phoenix", {}).get("requirements", [])),
                   "pre-existing durable achievement and PvP progress retroactively unlock Phoenix")
        self.check(earned.get("unlock_reward_matter_granted") == 5 and earned.get("passive_hp_bonus_percent") == .5
            and earned_themes.get("phoenix", {}).get("reward_granted") is True,
            "first prestige unlock atomically grants five matter and permanent half-percent HP")
        mail = self.rpc(female, "duel_mail_get")
        notices = [m for m in mail.get("messages", []) if m.get("kind") == "appearance_unlock"]
        self.check(len(notices) == 1 and notices[0].get("id") == "appearance_unlock:phoenix"
            and notices[0].get("avatar_id") == "phoenix-female" and notices[0].get("action") == "hero" and not notices[0].get("attachments"),
            "real first appearance unlock creates one named informational letter with its paired avatar and Hero action")
        opened = self.rpc(female, "duel_mail_claim", {"message_id": "appearance_unlock:phoenix"})
        opened_again = self.rpc(female, "duel_mail_claim", {"message_id": "appearance_unlock:phoenix"})
        self.check(opened.get("ok") and opened.get("informational") and opened_again.get("already_claimed")
            and self.rpc(female, "duel_player_resources_get").get("matter") == 5,
            "reading or replaying appearance letter never issues a second unlock reward")
        with ThreadPoolExecutor(max_workers=2) as pool:
            selections = list(pool.map(lambda theme: raw_rpc(female, "duel_player_cosmetics_select", {"theme_id": theme}), ["phoenix", "dawnwarden"]))
        self.check(all(value.get("ok") for value in selections), "independent concurrent cosmetic selections commit through storage versions")
        selected = raw_rpc(female, "duel_player_cosmetics_select", {"theme_id": "phoenix"})
        self.check(selected.get("ok") and selected.get("selected_theme") == "phoenix" and selected.get("gender") == "female", "unlocked female appearance selection is saved authoritatively")
        public = raw_rpc(legacy, "duel_public_appearance_get", {"user_id": female["uid"]})
        self.check(public == {"ok": True, "user_id": female["uid"], "avatar_id": "phoenix-female", "body_id": "phoenix-female"},
            "public opponent appearance RPC exposes exactly selected paired render IDs and no private metadata")
        status, session = http(self.args.url, "POST", "/v2/account/authenticate/email?create=false", {"email": email, "password": password, "vars": {"gender": "male"}}, self.basic)
        if status != 200:
            raise RuntimeError("Temporary account email reauthentication failed")
        female["token"] = session["token"]
        restored = raw_rpc(female, "duel_player_cosmetics_get")
        self.check(restored.get("selected_theme") == "phoenix" and restored.get("gender") == "female", "email reauthentication restores selection and cannot change gender through token vars")
        self.check(self.rpc(female, "duel_player_resources_get").get("matter") == 5
            and restored.get("unlock_reward_matter_granted") == 0,
            "concurrent selection, refresh and reauthentication never repay the five-matter unlock")
        notices = [m for m in self.rpc(female, "duel_mail_get").get("messages", []) if m.get("kind") == "appearance_unlock"]
        self.check(len(notices) == 1 and notices[0].get("claimed") and self.rpc(female, "duel_mail_get").get("unread_count") == 0,
            "appearance polling and email reconnect never duplicate or reopen a read unlock letter")

        # Admin-only deletion represents an account that predates this feature.
        status, _ = self.console("DELETE", "/storage/duel_account_identity/gender_v1/" + legacy["uid"])
        if status not in (200, 404):
            raise RuntimeError("Isolated legacy account fixture failed")
        migrated = raw_rpc(legacy, "duel_account_gender_get")
        legacy_choice = raw_rpc(legacy, "duel_account_gender_register", {"gender": "female"})
        self.check(migrated.get("gender") == "male" and migrated.get("can_choose") is False and legacy_choice.get("err") == "account_gender_immutable",
                   "pre-upgrade account without a gender row defaults permanently to male")
        other = raw_rpc(legacy, "duel_player_cosmetics_get")
        other_themes = {theme["id"]: theme for theme in other.get("themes", [])}
        self.check(other.get("selected_theme") == "default" and not other_themes.get("phoenix", {}).get("unlocked"), "unlocks and selection never cross accounts")
        baseline = self.rpc(legacy, "duel_character_get")
        base_hp = baseline.get("stats", {}).get("hp", 0)
        self.seed_money(legacy, matter=7)
        path = "/storage/duel_player_cosmetics/appearance_v1/" + legacy["uid"]
        status, record = self.console("GET", path)
        value = json.loads(record["value"])
        value["unlocked"] = {theme: True for theme in other_themes}
        value.pop("unlock_reward_receipts", None)
        status, _ = self.console("PUT", path, {"value": json.dumps(value), "version": record["version"], "permission_read": 0, "permission_write": 0})
        if status != 200:
            raise RuntimeError("Isolated pre-reward cosmetics fixture failed")
        def reconcile(index):
            return self.rpc(legacy, "duel_character_get") if index % 2 == 0 else raw_rpc(legacy, "duel_player_cosmetics_get")
        with ThreadPoolExecutor(max_workers=6) as pool:
            reconciled = list(pool.map(reconcile, range(6)))
        self.check(all(response.get("ok") for response in reconciled)
            and all(response.get("stats", {}).get("hp") == int(base_hp * 1.045)
                and response.get("progression", {}).get("matter") == 52 for response in reconciled[::2]),
            "concurrent first character reads retroactively grant all nine rewards and return fresh wallet plus four-point-five-percent HP")
        print("EVIDENCE cosmetic character snapshots " + json.dumps({"baseline_hp": base_hp,
            "characters": [{"ok": r.get("ok"), "err": r.get("err"), "hp": r.get("stats", {}).get("hp"),
                "matter": r.get("progression", {}).get("matter")} for r in reconciled[::2]]}), flush=True)
        after_rewards = raw_rpc(legacy, "duel_player_cosmetics_get")
        self.check(after_rewards.get("unlocked_prestige_count") == 9 and after_rewards.get("rewarded_matter_total") == 45
            and after_rewards.get("passive_hp_bonus_percent") == 4.5 and self.rpc(legacy, "duel_player_resources_get").get("matter") == 52,
            "nine old server unlocks award exactly forty-five matter once without losing the existing balance")
        for theme in ("phoenix", "dawnwarden", "default", "phoenix"):
            raw_rpc(legacy, "duel_player_cosmetics_select", {"theme_id": theme})
        current = self.rpc(legacy, "duel_character_get")
        self.check(current.get("stats", {}).get("hp") == int(base_hp * 1.045) and current.get("progression", {}).get("matter") == 52,
            "equipping and restoring appearances neither farm matter nor remove the collection HP bonus")
        forged = raw_rpc(legacy, "duel_player_cosmetics_get", {"rewarded_matter_total": 999, "passive_hp_bonus_percent": 100})
        self.check(forged.get("err") == "bad_payload" and self.rpc(female, "duel_player_resources_get").get("matter") == 5,
            "client reward/HP claims are rejected and retroactive receipts remain isolated to their account")
        self.cosmetic_ui_snapshots = [{"name": "female-locked", "response": first}, {"name": "female-phoenix-earned", "response": restored},
            {"name": "male-legacy", "response": other}, {"name": "male-all-rewards", "response": after_rewards}]

    def daily(self):
        self.isolated_only("Daily rewards regression")
        p, other = self.player(), self.player()
        def get(player, payload=None):
            status, result = http(self.args.url, "POST", "/v2/rpc/duel_daily_get", json.dumps(payload or {}), "Bearer " + player["token"])
            if status != 200:
                raise RuntimeError("Daily snapshot HTTP " + str(status))
            return json.loads(result.get("payload", "{}"))
        first = get(p)
        day = first.get("day")
        self.check(first.get("ok") and first.get("login", {}).get("can_claim") and len(first.get("quests", [])) == 3
            and first.get("reset_at", 0) > first.get("now", 0),
            "daily snapshot publishes server Moscow day, reset and three authoritative quests")
        sources = {item["def_id"] for reward in first.get("login", {}).get("rewards", []) for item in reward.get("items", [])}
        self.check({"elixir_assault", "elixir_stone", "elixir_gambit", "scroll_pve_bounty"}.issubset(sources),
            "all four consumables have reachable server-defined login-cycle sources without admin grants")
        self.seed_money(p, gold=11, ore=13, matter=7)
        forged = get(p, {"mine_wins": 999, "user_id": other["uid"]})
        invalid = self.rpc(p, "duel_daily_claim", {"kind": "login", "day": day, "reward": {"matter": 999}})
        stale = self.rpc(p, "duel_daily_claim", {"kind": "login", "day": day - 1})
        incomplete = self.rpc(p, "duel_daily_claim", {"kind": "quest", "day": day, "quest_id": "mine"})
        completion = self.rpc(p, "duel_daily_claim", {"kind": "completion", "day": day})
        self.check(forged.get("err") == "bad_payload" and invalid.get("err") == "bad_payload"
            and stale.get("err") == "day_changed" and incomplete.get("err") == "quest_incomplete"
            and completion.get("err") == "quests_unclaimed",
            "daily rejects client progress, reward/user claims, stale day and incomplete/unclaimed objectives")
        with ThreadPoolExecutor(max_workers=4) as pool:
            replies = list(pool.map(lambda _: self.rpc(p, "duel_daily_claim", {"kind": "login", "day": day}), range(4)))
        balance = self.rpc(p, "duel_player_resources_get")
        self.check(all(r.get("ok") for r in replies) and balance.get("gold") == 161 and balance.get("ore") == 113
            and get(p).get("login", {}).get("total_claimed") == 1,
            "four concurrent daily logins commit only one 150-gold/100-ore wallet award")
        login_response = next((r for r in replies if r.get("receipt")), None)
        self.check(get(other).get("login", {}).get("can_claim") and self.rpc(other, "duel_player_resources_get").get("gold") == 0,
            "daily login and reward receipts are isolated between temporary accounts")
        path = "/storage/duel_match3_stats/summary/" + p["uid"]
        status, row = self.console("GET", path)
        summary = json.loads(row["value"]) if status == 200 else {}
        summary["daily"] = {"day": day, "mine_wins": 999, "ability_uses": 999, "pvp_played": 999}
        status, _ = self.console("PUT", path, {"value": json.dumps(summary), "version": row.get("version", "*") if status == 200 else "*",
            "permission_read": 1, "permission_write": 0})
        if status != 200:
            raise RuntimeError("Isolated daily durable-stat fixture failed")
        catalog = self.storage("duel_match3_item_defs", "catalog", "00000000-0000-0000-0000-000000000000")["items"]
        filler = next(key for key, value in catalog.items()
            if value.get("kind") not in ("recipe", "material", "tesseract", "consumable") and value.get("max_stack", 1) == 1)
        self.seed_sheet(p, [filler] * 25, [1] * 25)
        snapshot = get(p)
        self.check(all(q.get("ready") for q in snapshot.get("quests", [])) and not snapshot.get("completion_ready"),
            "trusted durable daily counters unlock three quests but completion still requires their claim receipts")
        for quest in snapshot["quests"]:
            with ThreadPoolExecutor(max_workers=2) as pool:
                claims = list(pool.map(lambda _: self.rpc(p, "duel_daily_claim", {"kind": "quest", "day": day, "quest_id": quest["id"]}), range(2)))
            self.check(all(r.get("ok") for r in claims), "daily concurrent " + quest["id"] + " claims reconcile one durable receipt")
        with ThreadPoolExecutor(max_workers=2) as pool:
            claims = list(pool.map(lambda _: self.rpc(p, "duel_daily_claim", {"kind": "completion", "day": day}), range(2)))
        completion_response = next((r for r in claims if r.get("receipt")), None)
        sheet = self.storage("duel_match3_character", "sheet", p["uid"])
        pending = {r["def_id"]: r["amount"] for r in sheet.get("inventory_pending_loot", [])}
        balance = self.rpc(p, "duel_player_resources_get")
        self.check(all(r.get("ok") for r in claims) and pending == {"ingot_green": 2, "scroll_pve_bounty": 1}
            and balance.get("gold") == 411 and balance.get("ore") == 238 and balance.get("matter") == 7,
            "completed daily set awards exact currencies and retains one ingot/scroll delivery at full capacity")
        final = get(p)
        self.check(final.get("completion_claimed") and all(q.get("claimed") for q in final.get("quests", [])),
            "daily final snapshot cannot be manually farmed through refresh or repeated claim")
        item_player = self.player()
        self.seed_sheet(item_player, [filler] * 25, [1] * 25)
        path = "/storage/duel_daily/v1/" + item_player["uid"]
        status, _ = self.console("PUT", path, {"value": json.dumps({"schema": 1, "total_claimed": 1, "last_login_day": day - 1,
            "quest_day": -1, "quest_claimed": {}}), "version": "*", "permission_read": 0, "permission_write": 0})
        if status != 200:
            raise RuntimeError("Isolated prior-day daily login fixture failed")
        item_response = self.rpc(item_player, "duel_daily_claim", {"kind": "login", "day": day})
        self.check(item_response.get("ok") and item_response.get("receipt", {}).get("pending_items") == [{"def_id": "ingot_green", "amount": 3}],
            "second login-cycle item award survives a full bag using actual pending receipt JSON")
        self.check(isinstance(login_response, dict) and isinstance(completion_response, dict),
            "actual currency and full-bag completion claim payloads are captured for native Unity JSON validation")
        self.daily_ui_snapshots = [{"name": "daily-fresh", "response": first}, {"name": "daily-completed", "response": final},
            {"name": "daily-login-currency", "response": login_response}, {"name": "daily-login-item-fullbag", "response": item_response},
            {"name": "daily-completion-fullbag", "response": completion_response}]

    def consumable_use_request(self, player, index, item_id, request_id, **extra):
        # Quote once for a NEW intent. The caller keeps this exact dictionary
        # for concurrent/ambiguous retries instead of silently advancing seq.
        status = self.rpc(player, "duel_consumables_get").get("consumables", {})
        return {"inv_index": index, "expected_def_id": item_id, "request_id": request_id,
            "use_seq": status.get("next_use_seq", 0), **extra}

    def consumables(self):
        self.isolated_only("Consumable integration regression")
        a, b = self.player(), self.player()
        self.seed_sheet(a, ["elixir_assault", "elixir_stone", "scroll_pve_bounty"], [2, 1, 2])
        use = self.consumable_use_request(a, 0, "elixir_assault", "assault-" + self.run_id,
            damage_multiplier=999, armor_multiplier=999)
        with ThreadPoolExecutor(max_workers=3) as pool:
            activated = list(pool.map(lambda _: self.rpc(a, "duel_consumables_use", use), range(3)))
        sheet = self.storage("duel_match3_character", "sheet", a["uid"])
        self.check(all(r.get("ok") for r in activated) and sheet.get("inventory_counts", [0])[0] == 1
            and activated[0].get("consumables", {}).get("armed_item_id") == "elixir_assault",
            "concurrent and repeated consumable use spends exactly one owned elixir and arms its fixed server effect")
        denied = self.rpc(a, "duel_consumables_use", self.consumable_use_request(a, 1, "elixir_stone", "overarm-" + self.run_id))
        self.check(denied.get("err") == "consumable_already_armed"
            and self.storage("duel_match3_character", "sheet", a["uid"])["inventory_counts"][1] == 1
            and self.rpc(b, "duel_consumables_get").get("consumables", {}).get("armed_item_id") == "",
            "second armed elixir is rejected without spending inventory and cannot affect another account")
        mid, wa, wb, board = self.pair(a, b, "classic")
        self.check(not board.get("aConsumableId") and not board.get("bConsumableId")
            and self.rpc(a, "duel_consumables_get").get("consumables", {}).get("armed_item_id") == "elixir_assault",
            "classic match neither applies nor consumes the elixir reserved for PvE or Pro")
        self.finish(mid, wa, wb, a, b)
        self.seed_sheet(b, ["elixir_stone", "elixir_gambit"], [1, 1])
        self.check(self.rpc(b, "duel_consumables_use", self.consumable_use_request(b, 0, "elixir_stone", "stone-" + self.run_id)).get("ok"),
            "second Pro participant independently arms its owned Stone elixir")
        mid, wa, wb, board = self.pair(a, b, "pro")
        sides = {uid: side for uid, side in zip(sorted((a["uid"], b["uid"])), ("a", "b"))}
        for p, item, damage, armor in ((a, "elixir_assault", 1.2, .85), (b, "elixir_stone", .9, 1.2)):
            side = sides[p["uid"]]
            self.check(board.get(side + "ConsumableId") == item and abs(board.get(side + "DamageMultiplier", 0) - damage) < .0001
                and abs(board.get(side + "ArmorMultiplier", 0) - armor) < .0001,
                "real Pro BOARD_SYNC exposes fixed " + item + " multipliers rather than forged payload values")
        self.check(all(self.rpc(p, "duel_consumables_get").get("consumables", {}).get("armed_item_id") == "" for p in (a, b)),
            "real Pro start atomically claims both armed elixirs into immutable match receipts")
        wa.close()
        replacement = self.connect(a)
        replacement.command("match_join", {"match_id": mid})
        restored = replacement.event(mid, 10)
        self.check(restored.get(sides[a["uid"]] + "ConsumableId") == "elixir_assault"
            and self.storage("duel_match3_character", "sheet", a["uid"])["inventory_counts"][0] == 1,
            "Pro reconnect restores the frozen elixir without consuming another inventory item")
        self.finish(mid, replacement, wb, a, b)
        gambit = self.rpc(b, "duel_consumables_use", self.consumable_use_request(b, 1, "elixir_gambit", "gambit-" + self.run_id))
        self.check(gambit.get("ok"), "new elixir can be armed after the previous eligible battle consumed its binding")
        mid, wa, wb, board = self.pair(a, b, "pro")
        side = sides[b["uid"]]
        self.check(board.get(side + "ConsumableId") == "elixir_gambit"
            and abs(board.get(side + "ConsumableCritAdd", 0) - .1) < .0001
            and abs(board.get(side + "HealingMultiplier", 0) - .5) < .0001,
            "real Gambit Pro binding adds ten crit percentage points and halves healing")
        self.finish(mid, wa, wb, a, b)
        scroll = self.consumable_use_request(a, 2, "scroll_pve_bounty", "bounty-" + self.run_id,
            ends_at=99999999999, reward_multiplier=999)
        bounty = self.rpc(a, "duel_consumables_use", scroll)
        replay = self.rpc(a, "duel_consumables_use", scroll)
        status = bounty.get("consumables", {})
        blocked = self.rpc(a, "duel_consumables_use", self.consumable_use_request(a, 2, "scroll_pve_bounty", "bounty-again-" + self.run_id))
        self.check(bounty.get("ok") and replay.get("ok") and status.get("bounty_active")
            and status.get("bounty_ends_at", 0) - status.get("bounty_started_at", 0) == 3600
            and blocked.get("err") == "bounty_already_active"
            and self.storage("duel_match3_character", "sheet", a["uid"])["inventory_counts"][2] == 1,
            "bounty activation uses one server hour, rejects extension/forged multiplier and spends only one scroll on replay")
        self.seed_money(a, xp=486974, energy=100, gold=0, ore=0)
        catalog = self.storage("duel_match3_item_defs", "catalog", "00000000-0000-0000-0000-000000000000")["items"]
        weapon = max(((item_id, definition) for item_id, definition in catalog.items() if definition.get("slot") == "WeaponLeft"),
            key=lambda pair: pair[1].get("damage", 0))[0]
        path = "/storage/duel_match3_character/sheet/" + a["uid"]
        code, row = self.console("GET", path)
        value = json.loads(row["value"])
        value["equipment"] = [""] * 8
        value["equipment"][6] = weapon
        code, _ = self.console("PUT", path, {"value": json.dumps(value), "version": row["version"], "permission_read": 1, "permission_write": 0})
        if code != 200:
            raise RuntimeError("Isolated canonical PvE gear fixture failed")
        created = self.rpc(a, "duel_match3_pve_create", {"bot_id": "mine_1"})
        mid = created.get("match_id")
        if not created.get("ok") or not mid:
            raise RuntimeError("Real bounty PvE fixture could not start: " + str(created.get("err")))
        ws = self.connect(a)
        ws.command("match_join", {"match_id": mid})
        board = ws.event(mid, 10)
        print("EVIDENCE bounty PvE initial " + json.dumps({key: board.get(key) for key in ("aHp", "bHp", "aBaseDamage", "bBaseArmor", "bountyEndsAt")}), flush=True)
        self.check(board.get("bountyEndsAt") == status.get("bounty_ends_at"),
            "actual mine battle binds the authoritative active bounty timer")
        result = None
        moves = acks = bot_frames = 0
        deadline = time.monotonic() + 110
        for _ in range(160):
            if board.get("aHp", 1) <= 0 or board.get("bHp", 1) <= 0:
                result = ws.event(mid, 11)
                break
            if time.monotonic() > deadline:
                break
            if board.get("turnEndsAtUnixMs", 0) == 0:
                acks += 1
                ws.match_data(mid, 17)
                board = ws.event(mid, 10)
                continue
            if board.get("activeUserId") != a["uid"]:
                bot_frames += 1
                board = ws.event(mid, 10)
                continue
            ws.match_data(mid, 13, valid_swap(board["board"]))
            moves += 1
            board = ws.board_action(mid, a["uid"])
        print("EVIDENCE bounty PvE progress " + json.dumps({"human_moves": moves, "acks": acks, "bot_frames": bot_frames,
            "aHp": board.get("aHp"), "bHp": board.get("bHp"), "owner_active": board.get("activeUserId") == a["uid"]}), flush=True)
        self.check(result is not None and result.get("winnerUserId") == a["uid"],
            "real synchronized mine combat reaches a natural victory with the bounty active")
        if result is not None and result.get("winnerUserId") == a["uid"]:
            response, listing = self.console("GET", "/storage?collection=duel_match3_journal&user_id=" + a["uid"] + "&limit=100")
            plans = []
            if response == 200:
                for row in listing.get("objects", []):
                    saved = self.storage("duel_match3_journal", row["key"], a["uid"])
                    if saved.get("kind") == "pve" and saved.get("status") == "complete":
                        plans.append(saved)
            self.check(len(plans) == 1 and plans[0].get("plan", {}).get("currency_gold_multiplier", 0) >= 1.5
                and plans[0].get("plan", {}).get("currency_ore_multiplier", 0) >= 1.5,
                "natural PvE award freezes bounty currency multipliers in its durable committed reward plan")
            balance = self.rpc(a, "duel_player_resources_get")
            self.check(balance.get("gold") == result.get("rewardGold") and balance.get("ore") == result.get("rewardOre"),
                "actual bounty PvE currencies match the single committed game-over award")
        self.consumable_ui_snapshots = [{"name": "elixir-armed", "response": activated[0]}, {"name": "bounty-active", "response": bounty}]

    def shop(self):
        self.isolated_only('Server priced shop concurrent RPC regression')
        p=self.player();self.rpc(p,'duel_character_get');self.seed_money(p,matter=100,gold=100000,ore=0)
        offer=self.rpc(p,'duel_shop_get')
        self.check(offer.get('ok') and len(offer.get('offers',[]))==22 and offer.get('next_sequence')==1,
            'shop returns22 server-priced offers and fresh monotonic purchase sequence')
        payload={'offer_id':'resources_2500','request_id':'shop-same-'+self.run_id,'purchase_seq':offer['next_sequence'],'catalog_version':offer['catalog_version']}
        before=self.rpc(p,'duel_player_resources_get')
        with ThreadPoolExecutor(max_workers=4)as pool:replies=list(pool.map(lambda _:self.rpc(p,'duel_shop_buy',payload),range(4)))
        after=self.rpc(p,'duel_player_resources_get')
        self.check(all(r.get('ok') for r in replies) and after['matter']==before['matter']-10
            and after['gold']==before['gold']+2500 and after['ore']==before['ore']+2500,
            'four simultaneous identical shop purchases charge once and grant one exact bundle')
        for n in range(5):
            state=self.rpc(p,'duel_shop_get')
            purchase=self.rpc(p,'duel_shop_buy',{'offer_id':'resources_2500','request_id':'shop-next-'+str(n)+'-'+self.run_id,
                'purchase_seq':state['next_sequence'],'catalog_version':state['catalog_version']})
            if not purchase.get('ok'):raise RuntimeError('Shop sequential purchase '+str(purchase.get('err')))
        wallet=self.rpc(p,'duel_player_resources_get');old=self.rpc(p,'duel_shop_buy',payload)
        self.check(old.get('err')=='request_compacted' and self.rpc(p,'duel_player_resources_get')['matter']==wallet['matter'],
            'shop keeps only four receipts while compacted old purchase can never recharge or regrant')
        state=self.rpc(p,'duel_shop_get')
        rub=self.rpc(p,'duel_shop_buy',{'offer_id':'rub_resources_99','request_id':'shop-rub-'+self.run_id,
            'purchase_seq':state['next_sequence'],'catalog_version':state['catalog_version']})
        self.check(rub.get('err')=='payment_not_connected' and self.rpc(p,'duel_shop_get')['next_sequence']==state['next_sequence'],
            'unconnected real-money payment cannot charge wallet or advance purchase sequence')
        self.seed_sheet(p,['eq_t1_epic_helmet']*25,[1]*25)
        token=self.rpc(p,'duel_shop_buy',{'offer_id':'boss_teleport','request_id':'shop-teleport-'+self.run_id,
            'purchase_seq':state['next_sequence'],'catalog_version':state['catalog_version']})
        after_token=self.rpc(p,'duel_player_resources_get')
        self.check(token.get('ok') and after_token['gold']==wallet['gold']-30000
            and any(i.get('def_id')=='global_boss_teleport' and i.get('amount')==1 for i in token.get('receipt',{}).get('pending_items',[])),
            'teleport costs exactly30000gold and full inventory queues its paid delivery safely')
        forged=self.rpc(p,'duel_shop_buy',{'offer_id':'boss_teleport','request_id':'shop-forged-'+self.run_id,
            'purchase_seq':state['next_sequence']+1,'catalog_version':state['catalog_version'],'price':1})
        self.check(forged.get('err')=='bad_payload','shop rejects client prices and grant amounts')

    def global_boss(self):
        self.isolated_only('Shared weekly boss, mail and shop regression')
        system='00000000-0000-0000-0000-000000000000'
        def put(collection,key,uid,value):
            path='/storage/'+quote(collection,safe='')+'/'+quote(key,safe='')+'/'+uid
            code,row=self.console('GET',path)
            code,_=self.console('PUT',path,{'value':json.dumps(value),'version':row.get('version','*') if code==200 else '*','permission_read':0,'permission_write':0})
            if code!=200:raise RuntimeError('Isolated boss fixture CAS failed')
        def raw(p,name,payload=None):
            status,result=http(self.args.url,'POST','/v2/rpc/'+name,json.dumps(payload or {}),'Bearer '+p['token'])
            if status!=200:raise RuntimeError(name+' HTTP '+str(status))
            return json.loads(result.get('payload','{}'))
        def launch(p,request_id):
            entered=self.rpc(p,'duel_global_boss_enter',{'request_id':request_id,'event_id':event})
            if not entered.get('ok'):raise RuntimeError('Boss launch '+str(entered.get('err')))
            ws=self.connect(p);joined=ws.command('match_join',{'match_id':entered['match_id']})
            if 'match' not in joined:raise RuntimeError('Boss owned match join rejected')
            return entered,ws,ws.event(entered['match_id'],10)
        def move(p,entry,ws,board):
            mid=entry['match_id']
            chosen={'actionType':4} if board.get('aMana',0)>=30 and board.get('aPetardCd',0)==0 else valid_swap(board['board'])
            sequence=board.get('bossActionSequence',0)
            ws.match_data(mid,17)
            if board.get('turnPaused'):
                def running_clock(message):
                    md=message.get('match_data',{})
                    if md.get('match_id')!=mid or str(md.get('op_code'))!='10':return False
                    value=json.loads(base64.b64decode(md.get('data','')))
                    return value.get('bossActionSequence')==sequence and not value.get('turnPaused') and value.get('turnEndsAtUnixMs',0)>0 and not value.get('actionActorUserId')
                ws.await_message(running_clock,timeout=8)
            ws.match_data(mid,13,chosen)
            def own(m):
                md=m.get('match_data',{})
                if md.get('match_id')!=mid or str(md.get('op_code')) not in ('10','11','14'):return False
                value=json.loads(base64.b64decode(md.get('data','')))
                return str(md['op_code']) in ('11','14') or(value.get('actionActorUserId')==p['uid'] and value.get('bossActionSequence',0)>sequence)
            try:wire=ws.await_message(own,timeout=12)
            except TimeoutError:
                code,row=self.console('GET','/match/'+quote(mid,safe='')+'/state')
                state=json.loads(row.get('state','{}')) if code==200 else {}
                actor=state.get('stats',{}).get(p['uid'],{})
                print('EVIDENCE boss timeout '+json.dumps({'match_exists':code==200,'started':state.get('started'),'ended':state.get('ended'),
                    'active_is_owner':state.get('active_user_id')==p['uid'],'paused':state.get('turn_deadline_paused'),'own_hp':actor.get('hp'),
                    'own_damage':actor.get('base_damage'),'sequence':state.get('_boss_sequence'),'global':state.get('global_boss'),
                    'pending':bool(state.get('_boss_pending'))}),flush=True)
                raise
            value=json.loads(base64.b64decode(wire['match_data']['data']))
            if str(wire['match_data']['op_code'])=='14':raise RuntimeError('Boss action rejected '+str(value.get('reason')))
            return value
        a,b,c=self.player(),self.player(),self.player()
        catalog=self.storage('duel_match3_item_defs','catalog',system)['items']
        for p in (a,b,c):
            self.rpc(p,'duel_character_get');self.seed_sheet(p,['global_boss_teleport'],[5])
            sheet=self.storage('duel_match3_character','sheet',p['uid'])
            sheet['equipment']=[]
            for slot in ('Helmet','Shoulders','Chest','Gloves','Legs','Feet','WeaponLeft','WeaponRight'):
                candidates=[(d.get('tier',1),i) for i,d in catalog.items() if d.get('kind','equipment')=='equipment' and d.get('slot')==slot and d.get('quality')=='epic']
                sheet['equipment'].append(sorted(candidates)[-1][1] if candidates else '')
            put('duel_match3_character','sheet',p['uid'],sheet)
        info=self.rpc(a,'duel_global_boss_get');event=info['event_id']
        self.check(info.get('hp')==550000 and info.get('attempts_total')==3 and info.get('token_count')==5,
            'boss defaults use calibrated550000HP and exactly three weekly attacks, accessible to levelone')
        self.check(info.get('base_damage')==3500 and info.get('base_armor')==1000 and info.get('base_crit')==.05
            and info.get('base_heal')==0 and info.get('no_healing') and info.get('max_turns')==30,
            'boss section receives frozen event damage armor crit no-healing and turn-limit stats')
        bad=self.rpc(a,'duel_global_boss_enter',{'event_id':event,'request_id':'invalid-'+self.run_id,'damage':10000000})
        self.check(bad.get('err')=='bad_payload','boss RPC rejects client supplied damage')
        base_stats=self.stats(a);ea,wa,ba=launch(a,'boss-a-'+self.run_id)
        self.check(ba.get('globalBoss') and ba.get('bHp')==info['hp'] and ba.get('bossPlayerTurnsLeft')==30,
            'boss board exposes shared HP and bounded attempt turns')
        self.check(ba.get('roundNumber')==1,'fresh global boss snapshot starts authoritative round one')
        self.check(ba.get('aBaseDamage',0)>1000 and ba.get('aHp',0)>10000,
            'isolated boss fixture equips actual eight-slot epic gear before admission')
        replay=self.rpc(a,'duel_global_boss_enter',{'event_id':event,'request_id':'boss-a-'+self.run_id})
        self.check(replay.get('match_id')==ea['match_id'] and self.rpc(a,'duel_global_boss_get').get('token_count')==4,
            'retry of current boss launch reuses same attempt without second teleport charge')
        outsider=self.connect(b);intrusion=outsider.command('match_join',{'match_id':ea['match_id']})
        self.check('error' in intrusion,'another account cannot join a private boss attempt')
        self.check(ba.get('turnPaused') and ba.get('turnEndsAtUnixMs')==0 and not ba.get('bossAdaptationTriggered')
            and not ba.get('bossScreamTriggered'),'initial boss snapshot is paused and does not replay an old passive or scream')
        wa.match_data(ea['match_id'],17);running=wa.event(ea['match_id'],10)
        self.check(not running.get('turnPaused') and 29000<=running.get('turnEndsAtUnixMs',0)-running.get('serverNowUnixMs',0)<=30000,
            'realOP17 immediately returns a visible thirty-second wall-clock deadline')
        wa.match_data(ea['match_id'],16);clock_again=wa.event(ea['match_id'],10)
        self.check(clock_again.get('turnEndsAtUnixMs')==running.get('turnEndsAtUnixMs'),
            'snapshot refresh does not move the authoritative boss turn deadline')
        after=move(a,ea,wa,clock_again)
        self.check(after.get('globalBoss') and after.get('bossMyDamage',after.get('globalBossMyDamage',0))>=0,
            'real board action is accepted by global boss engine')
        settled=self.rpc(a,'duel_match3_forfeit',{'match_id':ea['match_id']});over=wa.event(ea['match_id'],11)
        self.check(settled.get('terminal') and over.get('globalBoss') and over.get('bossAttemptFinished'),
            'HTTP surrender settles a boss attempt and sends the boss terminal packet')
        final_stats=self.stats(a)
        self.check(all(final_stats.get(k,0)==base_stats.get(k,0) for k in ('played','wins','losses','pve_wins','pve_losses')),
            'boss attempt does not award ordinary mine or PvP result counters')
        for n in (2,3):
            en,wn,_=launch(a,'boss-a-'+str(n)+'-'+self.run_id)
            if n==2:
                wn.match_data(en['match_id'],17);decision=wn.event(en['match_id'],10)
                sequence=decision.get('bossActionSequence',0)
                def expired_turn(message):
                    md=message.get('match_data',{})
                    if md.get('match_id')!=en['match_id']or str(md.get('op_code'))!='10':return False
                    value=json.loads(base64.b64decode(md.get('data','')))
                    return value.get('bossActionSequence',0)>sequence and value.get('activeUserId')==en['bot_user_id']and value.get('turnPaused')
                wire=wn.await_message(expired_turn,timeout=35)
                passed=json.loads(base64.b64decode(wire['match_data']['data']))
                self.check(passed.get('bossPlayerTurnsLeft')==29 and passed.get('bossActionSequence')==sequence+1,
                    'actual thirty-second player deadline settles exactly one authoritative boss timeout')
                wn.match_data(en['match_id'],13,{'actionType':4})
                wn.match_data(en['match_id'],16)
                refreshed=wn.event(en['match_id'],10)
                self.check(refreshed.get('bossActionSequence')==passed.get('bossActionSequence')
                    and refreshed.get('aMana')==passed.get('aMana')and refreshed.get('activeUserId')==en['bot_user_id'],
                    'late boss ability after real expiry cannot spend mana or recover the expired player turn')
            self.check(self.rpc(a,'duel_match3_forfeit',{'match_id':en['match_id']}).get('terminal'),
                'boss weekly attempt'+str(n)+' can finish without stale admission')
            wn.event(en['match_id'],11)
        exhausted=self.rpc(a,'duel_global_boss_enter',{'event_id':event,'request_id':'boss-a-four-'+self.run_id})
        self.check(exhausted.get('err')=='weekly_attempts_exhausted' and self.rpc(a,'duel_global_boss_get').get('token_count')==2,
            'fourth weekly boss attack is rejected without charging a fourth teleport')
        # Only this disposable event is reduced to a deterministic finishing
        # fixture. Calibrated production defaults were checked above.
        shared=self.storage('duel_global_boss',event,system);shared['hp']=1
        shared['config']['base_damage']=500;shared['config']['base_armor']=0
        put('duel_global_boss',event,system,shared)
        eb,wb,bb=launch(b,'boss-b-'+self.run_id)
        self.check(bb.get('globalBoss'),'second account launches its own arena against same global boss')
        ec,wc,bc=launch(c,'boss-c-'+self.run_id)
        self.check(bc.get('globalBoss'),'third account reserves a late attempt before global death')
        # Force only the disposable global HP to1; damage still comes from the
        # real player's validated ordinary board move, never a damage RPC.
        for turn in range(12):
            out=move(b,eb,wb,bb)
            if out.get('bossAttemptFinished') or self.rpc(b,'duel_global_boss_get').get('status')=='dead':break
            bb=out
            while bb.get('activeUserId')!=b['uid'] and bb.get('aHp',0)>0 and bb.get('bHp',0)>0:
                sequence=bb.get('bossActionSequence',0);wb.match_data(eb['match_id'],17)
                def next_boss_action(message):
                    md=message.get('match_data',{})
                    if md.get('match_id')!=eb['match_id']or str(md.get('op_code'))!='10':return False
                    value=json.loads(base64.b64decode(md.get('data','')))
                    return value.get('actionActorUserId')==eb['bot_user_id']and value.get('bossActionSequence',0)>sequence
                wire=wb.await_message(next_boss_action,timeout=12)
                bb=json.loads(base64.b64decode(wire['match_data']['data']))
            if bb.get('aHp',0)<=0 or bb.get('bHp',0)<=0:break
        dead=self.rpc(b,'duel_global_boss_get')
        self.check(dead.get('status')=='dead' and dead.get('hp')==0 and dead.get('qualified'),
            'validated player damage kills the one shared boss and marks contributor eligible')
        late=wc.event(ec['match_id'],11,timeout=8)
        self.check(late.get('bossAttemptRefunded') and self.rpc(c,'duel_global_boss_get').get('token_count')==5
            and self.rpc(c,'duel_global_boss_get').get('attempts_used')==0,
            'global death before first action refunds both weekly attack and teleport')
        ranking=self.rpc(b,'duel_global_boss_leaderboard_get',{'event_id':event,'limit':1})
        entries=ranking.get('entries')or[];own=ranking.get('self_entry')or{}
        self.check(ranking.get('ok') and len(entries)<=1 and own.get('user_id')==b['uid'] and own.get('damage')==dead.get('my_damage')
            and own.get('attempts_used')==1 and own.get('rank',0)>0,
            'actual native boss ranking contains one weekly total per owner and own position outside a bounded page')
        allowed={'rank','user_id','display_name','avatar_id','damage','attempts_used','is_self'}
        self.check(all(set(row)<=allowed for row in entries+[own]) and own.get('display_name')==b['username'],
            'ranking exposes current display names and avatars without private receipt or reward IDs')
        self.check(self.rpc(b,'duel_global_boss_leaderboard_get',{'event_id':event,'limit':5000}).get('err')=='bad_payload'
            and self.rpc(b,'duel_global_boss_leaderboard_get',{'event_id':'week-old','limit':1}).get('err')=='event_changed',
            'ranking rejects oversized pages and a different weekly event')
        self.boss_ui_snapshots=[{'name':'boss-state','response':info},{'name':'boss-running-clock','response':running},{'name':'boss-ranking','response':ranking}]
        wb.close() # Offline reward delivery is independent of a live socket.
        deadline=time.monotonic()+12;mail={}
        while time.monotonic()<deadline:
            mail=raw(b,'duel_mail_get')
            if mail.get('unread_count')==1:break
            time.sleep(.4)
        self.check(mail.get('ok') and mail.get('unread_count')==1,'defeated global boss delivers one durable chest to offline contributor mailbox')
        if mail.get('unread_count')!=1:
            stored=self.storage('duel_global_boss',event,system);proof=self.storage('duel_global_boss_player',event,b['uid'])
            print('EVIDENCE mail '+json.dumps({'rpc':mail,'mail_done':stored.get('mail_done'),'cursor':bool(stored.get('mail_cursor')),
                'worker':bool(stored.get('worker_match_id')),'worker_lease':stored.get('worker_until'),'proof_qualified':proof.get('qualified'),
                'proof_created':proof.get('reward_mail_created')}),flush=True)
        if mail.get('messages'):
            message=mail['messages'][0];attachments=message.get('attachments',[])
            self.check(len(attachments)==7 and sum(i.get('def_id','').startswith('recipe_drop_t1_gold_') for i in attachments)==1
                and any(i.get('def_id')=='tesseract' for i in attachments),'boss chest contains one random legendary recipe, tesseract and supplies')
            before=self.rpc(b,'duel_player_resources_get')
            with ThreadPoolExecutor(max_workers=3)as pool:claims=list(pool.map(lambda _:self.rpc(b,'duel_mail_claim',{'message_id':message['id']}),range(3)))
            after=self.rpc(b,'duel_player_resources_get')
            self.check(all(r.get('ok') and r.get('claimed') for r in claims) and after.get('gold')==before.get('gold')+1500,
                'three concurrent mailbox claims grant one exact resource and inventory reward')
            self.check(not self.rpc(c,'duel_mail_claim',{'message_id':message['id']}).get('ok'),
                'non-contributor cannot claim another account boss chest')
        self.check(not self.rpc(a,'duel_global_boss_enter',{'event_id':event,'request_id':'dead-'+self.run_id}).get('ok'),
            'globally defeated event rejects new attempts')

    def storage_snapshot(self, uid):
        """Read all rows including versions, so a same-value rewrite is still detected."""
        rows, cursor = {}, ""
        for _ in range(20):
            query = {"user_id": uid, "limit": 100}
            if cursor:
                query["cursor"] = cursor
            code, page = self.console("GET", "/storage?" + urlencode(query))
            if code != 200:
                raise RuntimeError("Read-only test Storage snapshot failed: HTTP " + str(code))
            for row in page.get("objects", []):
                owner = row.get("user_id", row.get("userId"))
                if owner != uid:
                    raise RuntimeError("Console Storage owner filter was not respected")
                key = (row["collection"], row["key"])
                rows[key] = {name: row.get(name) for name in ("version", "value", "permission_read", "permission_write")}
            cursor = page.get("next_cursor", page.get("cursor", ""))
            if not cursor:
                return rows
        raise RuntimeError("Test Storage snapshot exceeded its bounded pagination")

    def practice_smoke(self):
        """Production-safe: temporary account, Console GET only, no paid boss admission."""
        p = self.player(); self.rpc(p, "duel_character_get")
        original = self.console
        def get_only(method, path, payload=None):
            if method != "GET":
                raise RuntimeError("Production practice smoke refused a Console write")
            return original(method, path, payload)
        self.console = get_only
        try:
            self._practice_trace(p, self.connect(p), rounds=2, require_positive_damage=False)
        finally:
            self.console = original # Existing finally cleanup removes this temporary account only.

    def quote_preflight(self):
        """Harmless runtime-first proof: invalid quotes on one empty temporary account."""
        p = self.player(); self.rpc(p, "duel_character_get")
        original = self.console
        def get_only(method, path, payload=None):
            if method != "GET": raise RuntimeError("Quote preflight refused a Console write")
            return original(method, path, payload)
        self.console = get_only
        watched = {("duel_match3_character", "sheet"), ("duel_match3_progress", "profile")}
        try:
            baseline = self.storage_snapshot(p["uid"])
            before = {key: baseline.get(key) for key in watched}
            if before[("duel_match3_character", "sheet")] is None:
                raise RuntimeError("Quote preflight requires an initialized empty-account character sheet")
            for value in (-1, "0", .5, False):
                reply = self.rpc(p, "duel_workshop_craft_start", {"slot_index": 4, "output_def_id": "eq_t1_epic_legs", "expected_ingot_n": value})
                self.check(reply.get("ok") is False and reply.get("err") == "invalid_craft_quote",
                    "active craft quote guard rejects invalid " + type(value).__name__ + " before recipe/access/stock checks")
            current = self.storage_snapshot(p["uid"])
            after = {key: current.get(key) for key in watched}
            self.check(after == before, "runtime-first quote preflight changes no sheet/wallet version, inventory, resources or workshop job")
        finally:
            self.console = original

    def _practice_trace(self, p, ws, rounds, require_positive_damage):
        system = "00000000-0000-0000-0000-000000000000"
        day, week = 86400, 604800
        event_id = "week-" + str(math.floor((time.time() + 10800 + 3 * day) / week) * week - 3 * day - 10800)
        event = self.storage("duel_global_boss", event_id, system)
        config = event.get("config") or self.storage("duel_global_boss", "config", system)
        full_hp = math.ceil(max(1, min(1_000_000_000, int(config.get("max_hp", 550000)))) / 50000) * 50000
        baseline = self.storage_snapshot(p["uid"])
        # The system maintenance lease and shared boss state must also be untouched.
        watched_system = {("duel_storage_maintenance", "v1"), ("duel_global_boss", event_id), ("duel_global_boss", "index"), ("duel_global_boss", "config")}
        system_before = {key: value for key, value in self.storage_snapshot(system).items() if key in watched_system}
        forged = self.rpc(p, "duel_global_boss_practice", {"damage": 999999})
        stale = self.rpc(p, "duel_global_boss_practice", {"session_epoch": p["epoch"] + 1})
        self.check(forged.get("err") == "bad_payload" and not stale.get("ok"), "practice rejects forged damage and stale session without durable admission")
        for index in range(rounds):
            entry = self.rpc(p, "duel_global_boss_practice")
            self.check(entry.get("ok") and entry.get("practice") and not entry.get("spent_token"), "practice launches without a paid admission or token, run" + str(index + 1))
            if not entry.get("match_id"):
                raise RuntimeError("Practice did not return a private match")
            mid = entry["match_id"]
            joined = ws.command("match_join", {"match_id": mid})
            if "match" not in joined:
                raise RuntimeError("Practice match join rejected")
            board = ws.event(mid, 10)
            self.check(board.get("globalBossPractice") and board.get("bHp") == board.get("bMaxHp") == full_hp
                and board.get("aHp") == board.get("aMaxHp") and board.get("globalBossMyDamage", 0) == 0,
                "fresh practice starts both sides at full HP and zero personal damage, run" + str(index + 1))
            self.check(board.get("roundNumber") == 1 and board.get("bossActionSequence", 0) == 0
                and board.get("bossOwnTurn", 0) == 0 and not board.get("bossAdaptationTriggered") and not board.get("bossScreamTriggered")
                and board.get("aPetardCd", 0) == 0 and board.get("aMana", 0) == 0,
                "fresh practice resets round, adaptation, scream, mana and ability cooldown, run" + str(index + 1))
            accepted = 0
            for _ in range(12 if require_positive_damage else 1):
                # Let a response turn resolve only after the preceding animation ACK.
                while board.get("activeUserId") != p["uid"]:
                    previous = board.get("bossActionSequence", 0); ws.match_data(mid, 17)
                    def next_action(message):
                        data = message.get("match_data", {})
                        if data.get("match_id") != mid or str(data.get("op_code")) not in ("10", "11"):
                            return False
                        value = json.loads(base64.b64decode(data.get("data", "")))
                        return str(data["op_code"]) == "11" or value.get("bossActionSequence", 0) > previous
                    wire = ws.await_message(next_action, timeout=15)
                    board = json.loads(base64.b64decode(wire["match_data"]["data"]))
                    if str(wire["match_data"]["op_code"]) == "11":
                        raise RuntimeError("Practice ended before its bounded own action")
                sequence = board.get("bossActionSequence", 0)
                ws.match_data(mid, 17)
                if board.get("turnPaused"):
                    def running(message):
                        data = message.get("match_data", {})
                        if data.get("match_id") != mid or str(data.get("op_code")) != "10":
                            return False
                        value = json.loads(base64.b64decode(data.get("data", "")))
                        return value.get("bossActionSequence", 0) == sequence and not value.get("turnPaused") and not value.get("actionActorUserId")
                    ws.await_message(running, timeout=8)
                ws.match_data(mid, 13, valid_swap(board["board"]))
                def own_action(message):
                    data = message.get("match_data", {})
                    if data.get("match_id") != mid or str(data.get("op_code")) not in ("10", "11", "14"):
                        return False
                    value = json.loads(base64.b64decode(data.get("data", "")))
                    return str(data["op_code"]) in ("11", "14") or (value.get("actionActorUserId") == p["uid"] and value.get("bossActionSequence", 0) > sequence)
                wire = ws.await_message(own_action, timeout=15)
                board = json.loads(base64.b64decode(wire["match_data"]["data"]))
                if str(wire["match_data"]["op_code"]) != "10":
                    raise RuntimeError("Practice did not acknowledge an ordinary nonlethal board action")
                accepted += 1
                if board.get("globalBossMyDamage", 0) > 0 or not require_positive_damage:
                    break
            damage = board.get("globalBossMyDamage", 0)
            self.check(accepted > 0 and (damage > 0 or not require_positive_damage)
                and damage == full_hp - board.get("bHp", full_hp), "ordinary practice action resolves only against private full-HP boss state")
            # OP12 is the real socket surrender. An ordinary HTTP forfeit RPC would
            # legitimately update its own generic durable RPC budget, obscuring this check.
            ws.match_data(mid, 12); terminal = ws.event(mid, 11)
            self.check(terminal.get("globalBossPractice") and terminal.get("bossAttemptFinished")
                and not terminal.get("bossQualified") and not terminal.get("bossAttemptRefunded")
                and terminal.get("bossGlobalMaxHp") == full_hp and terminal.get("bossMyDamage") == damage
                and terminal.get("bossGlobalHp") == full_hp - damage,
                "practice terminal keeps actual personal damage/full initial HP denominator and no eligibility/refund")
            percent = 100 * terminal.get("bossMyDamage", 0) / full_hp
            self.check(0 <= percent <= 100, "practice damage percentage has the full initial HP denominator")
            after = self.storage_snapshot(p["uid"])
            system_after = {key: value for key, value in self.storage_snapshot(system).items() if key in watched_system}
            changed = sorted(key[0] + "/" + key[1] for key in set(after) | set(baseline) if after.get(key) != baseline.get(key))
            self.check(not changed and system_after == system_before,
                "practice admission/action/terminal/reset perform zero durable writes, including rate budget and maintenance lease")
            if changed:
                print("EVIDENCE practice changed collections/keys=" + json.dumps(changed), flush=True)

    def global_boss_practice(self):
        self.isolated_only("Unpaid global boss practice regression")
        system = "00000000-0000-0000-0000-000000000000"
        p = self.player(); self.rpc(p, "duel_character_get")
        catalog = self.storage("duel_match3_item_defs", "catalog", system)["items"]
        info = self.rpc(p, "duel_global_boss_get"); event_id = info["event_id"]
        def put(collection, key, uid, value):
            path = "/storage/" + quote(collection, safe="") + "/" + quote(key, safe="") + "/" + uid
            code, row = self.console("GET", path)
            code, _ = self.console("PUT", path, {"value": json.dumps(value), "version": row.get("version", "*") if code == 200 else "*", "permission_read": 0, "permission_write": 0})
            if code != 200:
                raise RuntimeError("Isolated practice fixture CAS failed")
        self.seed_money(p, energy=0)
        sheet = self.storage("duel_match3_character", "sheet", p["uid"])
        sheet.update(equipment=["eq_t1_legendary_" + slot for slot in ("helmet", "shoulders", "chest", "gloves", "legs", "feet", "weapon_l", "weapon_r")],
            inventory=["eq_t1_normal_helmet"] * 25, inventory_counts=[1] * 25,
            inventory_pending_loot=[{"def_id": "ingot_green", "amount": 3}],
            consumables={"schema": 1, "armed": {"item_id": "elixir_assault", "activation_id": "practice-fixture", "armed_at": int(time.time())},
                         "bounty": {"started_at": int(time.time()), "ends_at": int(time.time()) + 3600}})
        put("duel_match3_character", "sheet", p["uid"], sheet)
        shared = self.storage("duel_global_boss", event_id, system); shared["hp"] = 1; shared["status"] = "active"
        put("duel_global_boss", event_id, system, shared)
        put("duel_global_boss_player", event_id, p["uid"], {"schema": 1, "event_id": event_id, "attempts_used": 3, "total_damage": 123, "qualified": False, "requests": {}})
        self._practice_trace(p, self.connect(p), rounds=2, require_positive_damage=True)
        # The ordinary paid path is validated on a different disposable account.
        paid = self.player(); self.rpc(paid, "duel_character_get"); self.seed_sheet(paid, ["global_boss_teleport"], [2])
        paid_entry = self.rpc(paid, "duel_global_boss_enter", {"event_id": event_id, "request_id": "paid-after-practice-" + self.run_id})
        self.check(paid_entry.get("ok") and paid_entry.get("spent_token") and not paid_entry.get("practice"), "normal paid entry still requires and spends a teleport after practice")
        paid_ws = self.connect(paid); paid_ws.command("match_join", {"match_id": paid_entry["match_id"]}); board = paid_ws.event(paid_entry["match_id"], 10)
        self.check(not board.get("globalBossPractice") and board.get("bHp") == 1, "normal paid battle retains actual depleted shared HP instead of practice full HP")
        paid_ws.match_data(paid_entry["match_id"], 12); over = paid_ws.event(paid_entry["match_id"], 11)
        saved = self.storage("duel_global_boss_player", event_id, paid["uid"])
        token_sheet = self.storage("duel_match3_character", "sheet", paid["uid"])
        tokens = sum(n for item, n in zip(token_sheet.get("inventory", []), token_sheet.get("inventory_counts", [])) if item == "global_boss_teleport")
        self.check(not over.get("globalBossPractice") and tokens == 1 and saved.get("attempts_used") == 1,
            "normal paid terminal preserves one real weekly attempt and exactly one token debit")

    def equipment_sets(self):
        self.isolated_only("Actual equipment set and32 recipe costs")
        system = "00000000-0000-0000-0000-000000000000"
        items = self.storage("duel_match3_item_defs", "catalog", system)["items"]
        p = self.player(); self.rpc(p, "duel_character_get")
        ws = self.connect(p)
        slots = ("helmet", "shoulders", "chest", "gloves", "legs", "feet", "weapon_l", "weapon_r")
        slot_names = ("Helmet", "Shoulders", "Chest", "Gloves", "Legs", "Feet", "WeaponLeft", "WeaponRight")
        def sheet_put(**updates):
            path = "/storage/duel_match3_character/sheet/" + p["uid"]
            code, row = self.console("GET", path)
            value = json.loads(row["value"]); value.update(updates)
            code, _ = self.console("PUT", path, {"value": json.dumps(value), "version": row["version"], "permission_read": 1, "permission_write": 0})
            if code != 200: raise RuntimeError("Equipment fixture CAS failed")
        sheet_put(equipment=[""] * 8)
        base = self.rpc(p, "duel_character_get")["stats"]
        def expected(ids, multiplier):
            result = {}
            for stat in ("hp", "damage", "armor", "healing", "crit_chance"):
                extra = sum(items.get(item, {}).get(stat, 0) for item in ids if item)
                result[stat] = base[stat] + extra * (1 if stat == "crit_chance" else multiplier)
                if stat != "crit_chance": result[stat] = math.floor(result[stat] + .5)
            return result
        def equal_stats(actual, wanted):
            return all(abs(actual.get(stat, -1) - value) < 1e-6 for stat, value in wanted.items())
        for quality in ("normal", "rare", "epic", "legendary"):
            full = ["eq_t1_" + quality + "_" + slot for slot in slots]; seven = full[:-1] + [""]
            sheet_put(equipment=seven, inventory=[full[7]] + [""] * 24, inventory_counts=[1] + [0] * 24)
            before = self.rpc(p, "duel_character_get")
            self.check(equal_stats(before["stats"], expected(seven, 1)), quality + ": actual seven-piece profile has no set multiplier")
            complete = self.rpc(p, "duel_character_item_move", {"op": "inv_to_equip", "inv_index": 0, "slot_index": 7})
            self.check(complete.get("ok") and complete.get("equipment_def_ids") == full and equal_stats(complete.get("stats", {}), expected(full, 1.25)),
                quality + ": eighth item RPC immediately returns correct full-set stats, critical chance unchanged")
            entry = self.rpc(p, "duel_global_boss_practice")
            joined = ws.command("match_join", {"match_id": entry["match_id"]})
            if "match" not in joined: raise RuntimeError("Set actor validation could not join its practice fixture")
            combat = ws.event(entry["match_id"], 10); stats = complete["stats"]
            self.check(combat.get("aMaxHp") == stats["hp"] and combat.get("aBaseDamage") == stats["damage"]
                and combat.get("aBaseArmor") == stats["armor"] and combat.get("aBaseHeal") == stats["healing"]
                and abs(combat.get("aBaseCrit", -1) - stats["crit_chance"]) < 1e-6,
                quality + ": actual battle actor and immediate character response use identical complete-set stats")
            ws.match_data(entry["match_id"], 12); ws.event(entry["match_id"], 11)
            again = self.rpc(p, "duel_character_get")
            self.check(again.get("stats") == complete.get("stats"), quality + ": repeated profile read does not compound the set multiplier")
            removed = self.rpc(p, "duel_character_item_move", {"op": "equip_to_inv", "inv_index": 0, "slot_index": 7})
            self.check(removed.get("ok") and equal_stats(removed.get("stats", {}), expected(seven, 1)), quality + ": unequip RPC immediately removes the set multiplier")
        mixed = ["eq_t1_epic_" + slot for slot in slots[:-1]] + ["eq_t1_rare_weapon_r"]
        sheet_put(equipment=mixed, inventory=[""] * 25, inventory_counts=[0] * 25)
        self.check(equal_stats(self.rpc(p, "duel_character_get")["stats"], expected(mixed, 1)), "eight occupied slots with mixed quality do not activate a set")
        recipes = sorted((key, item) for key, item in items.items() if key.startswith("eq_t1_") and item.get("kind") == "equipment")
        self.check(len(recipes) == 32, "canonical backend exposes all32 equipment recipe costs")
        for item_id, item in recipes:
            inventory, counts = [], []
            def add(item, amount):
                while amount > 0:
                    count = min(amount, 200 if item.startswith("ingot_") else 1)
                    inventory.append(item); counts.append(count); amount -= count
            ingot_n = item.get("craft_ingot_n", 0); tess = item.get("craft_tesseract_n", 0)
            if ingot_n: add(item["craft_ingot_def"], ingot_n + 3)
            if tess: add("tesseract", tess + 1)
            if item.get("craft_item_id"): add(item["craft_item_id"], 1)
            inventory += [""] * (25 - len(inventory)); counts += [0] * (25 - len(counts))
            def prepare():
                sheet_put(equipment=[""] * 8, inventory=inventory, inventory_counts=counts, learned_recipes=[item["craft_recipe_id"]],
                    first_recipe_received=True, inventory_pending_loot=[], workshop_slots=[{"output_def_id": "", "ends_at": 0} for _ in range(8)])
                self.seed_money(p, ore=100000, gold=100000)
            prepare()
            index = slot_names.index(item["slot"])
            old_epic = {"helmet": 8, "chest": 25, "gloves": 50, "weapon_l": 125, "weapon_r": 150, "legs": 180, "shoulders": 220, "feet": 270}
            if item_id.startswith("eq_t1_epic_"):
                before_sheet = self.storage("duel_match3_character", "sheet", p["uid"])
                before_wallet = self.storage("duel_match3_progress", "profile", p["uid"])
                legacy = self.rpc(p, "duel_workshop_craft_start", {"slot_index": index, "output_def_id": item_id})
                self.check(not legacy.get("ok") and legacy.get("err") == "Рецепт обновлён. Для изготовления этой вещи обновите клиент игры."
                    and self.storage("duel_match3_character", "sheet", p["uid"]) == before_sheet
                    and self.storage("duel_match3_progress", "profile", p["uid"]) == before_wallet,
                    item_id + ": published unquoted client receives readable update notice with zero resource/fodder/job mutation")
                mismatch = self.rpc(p, "duel_workshop_craft_start", {"slot_index": index, "output_def_id": item_id,
                    "expected_ingot_n": old_epic[item_id.removeprefix("eq_t1_epic_")]})
                self.check(mismatch.get("err") == "craft_quote_changed" and mismatch.get("craft_quote") == {"schema": 1, "output_def_id": item_id, "ingot_n": ingot_n}
                    and self.storage("duel_match3_character", "sheet", p["uid"]) == before_sheet
                    and self.storage("duel_match3_progress", "profile", p["uid"]) == before_wallet,
                    item_id + ": stale displayed quote returns the actual ingot price without charging or creating a job")
            elif item_id == "eq_t1_normal_helmet":
                legacy = self.rpc(p, "duel_workshop_craft_start", {"slot_index": index, "output_def_id": item_id})
                self.check(legacy.get("ok") and legacy.get("workshop_output_def_ids", [""] * 8)[index] == item_id,
                    "published unquoted client can still craft unchanged common recipes")
                prepare()
            response = self.rpc(p, "duel_workshop_craft_start", {"slot_index": index, "output_def_id": item_id, "expected_ingot_n": ingot_n})
            saved = self.storage("duel_match3_character", "sheet", p["uid"])
            wallet = self.storage("duel_match3_progress", "profile", p["uid"])
            remaining = lambda item_id: sum(n for item, n in zip(saved.get("inventory", []), saved.get("inventory_counts", [])) if item == item_id)
            self.check(response.get("ok") and wallet.get("ore") == 100000 - item["craft_ore"] and wallet.get("gold") == 100000 - item["craft_gold"]
                and (not ingot_n or remaining(item["craft_ingot_def"]) == 3) and (not tess or remaining("tesseract") == 1)
                and (not item.get("craft_item_id") or remaining(item["craft_item_id"]) == 0)
                and saved["workshop_slots"][index].get("output_def_id") == item_id,
                item_id + ": actual craft debits exact canonical ore/gold/materials and previous gear once, starts correct slot")

    def seed_money(self, p, **money):
        self.isolated_only("Test-only profile seeding")
        path = "/storage/duel_match3_progress/profile/" + p["uid"]
        status, row = self.console("GET", path)
        value = json.loads(row["value"]) if status == 200 else {}
        value.update(money)
        payload = {"value": json.dumps(value), "version": row.get("version", "*") if status == 200 else "*",
            "permission_read": 1, "permission_write": 0}
        status, _ = self.console("PUT", path, payload)
        if status != 200:
            raise RuntimeError("Test-only resource seed failed: HTTP " + str(status))

    def seed_sheet(self, p, inventory, counts, learned=None):
        self.isolated_only("Inventory fixture")
        path = "/storage/duel_match3_character/sheet/" + p["uid"]
        status, row = self.console("GET", path)
        value = json.loads(row["value"]) if status == 200 else {}
        value.update(inventory=inventory, inventory_counts=counts, inventory_extra_slots=0,
                     inventory_pending_loot=[], learned_recipes=learned or [])
        status, _ = self.console("PUT", path, {"value": json.dumps(value),
            "version": row.get("version", "*") if status == 200 else "*", "permission_read": 1, "permission_write": 0})
        if status != 200:
            raise RuntimeError("Isolated smith sheet fixture failed")

    def seed_tournament_access(self, p, **money):
        self.isolated_only("Server-owned ten-win tournament fixture")
        self.seed_money(p, **money, mine={"unlocked": {"easy": 1, "medium": 0, "hard": 0},
            "floor_states": {"easy:1": {"wins": 10}}})

    def guidance(self):
        self.isolated_only("Guidance auth and paid-gate fixtures")
        def raw(p, name, payload=None):
            status, response = http(self.args.url, "POST", "/v2/rpc/" + name,
                json.dumps(payload or {}), "Bearer " + p["token"])
            if status != 200:
                raise RuntimeError(name + " HTTP " + str(status))
            return json.loads(response.get("payload", "{}"))
        def read(p):
            return raw(p, "duel_player_guidance_get")
        def update_record(collection, key, p, mutate):
            path = "/storage/" + collection + "/" + key + "/" + p["uid"]
            status, row = self.console("GET", path)
            value = json.loads(row["value"]) if status == 200 else {}
            mutate(value)
            status, _ = self.console("PUT", path, {"value": json.dumps(value),
                "version": row.get("version", "*") if status == 200 else "*", "permission_read": 1, "permission_write": 0})
            if status != 200:
                raise RuntimeError("Isolated guidance source fixture failed")
        p, other = self.player(), self.player()
        first = read(p)
        self.check(first.get("ok") and first.get("tutorial_status") == "pending" and first.get("mine_wins") == 0
            and first.get("workshop_unlocked") is False and first.get("tournaments_unlocked") is False,
            "real newly created device atomically receives pending tutorial with both progressive gates locked")
        for forged in ({"user_id": other["uid"]}, {"mine_wins": 10}, {"workshop_unlocked": True}):
            self.check(raw(p, "duel_player_guidance_get", forged).get("err") == "bad_payload",
                "guidance rejects forged target, progress and first-recipe fields")
        self.check(self.rpc(p, "duel_player_guidance_set", {"result": "completed", "session_epoch": p["epoch"] - 1}).get("err") == "session_stale",
            "tutorial mutation rejects an actual stale session epoch")
        completed = self.rpc(p, "duel_player_guidance_set", {"result": "completed"})
        self.check(completed.get("tutorial_status") == "completed" and not completed.get("workshop_unlocked") and not completed.get("tournaments_unlocked"),
            "tutorial completion cannot manufacture recipe ownership or ten monster wins")
        self.seed_money(p, gold=5000, ore=5000)
        self.seed_sheet(p, ["ingot_green"], [100])
        for wins in (0, 9):
            self.seed_money(p, mine={"floor_states": {"easy:1": {"wins": wins}}}, defeated={"mine_1": wins})
            update_record("duel_match3_stats", "summary", p, lambda value: value.setdefault("modes", {}).setdefault("mine", {}).update(wins=wins))
            status = read(p)
            self.check(status.get("mine_wins") == wins and status.get("tournaments_unlocked") is False,
                "three trusted legacy counter views use max at " + str(wins) + " wins rather than adding to bypass ten")
            for kind, tier in (("gold", "600"), ("smith", "green")):
                denied = self.rpc(p, "duel_arena_queue_join", {"arena_kind": kind, "bet_tier": tier,
                    "mine_wins": 999, "tournaments_unlocked": True})
                money = self.rpc(p, "duel_player_resources_get")
                sheet = self.storage("duel_match3_character", "sheet", p["uid"])
                self.check(denied.get("err") == "tournaments_locked" and money.get("gold") == 5000
                    and sheet.get("inventory_counts", [0])[0] == 100,
                    kind + " gate rejects forged eligibility before charging currencies or ingots at " + str(wins) + " wins")
        self.seed_tournament_access(p)
        ten = read(p)
        self.check(ten.get("mine_wins") == 10 and ten.get("tournaments_unlocked") is True,
            "exactly ten durable wins unlock real tournament RPC")
        entered = self.rpc(p, "duel_arena_queue_join", {"arena_kind": "gold", "bet_tier": "600"})
        self.check(entered.get("ok") and self.rpc(p, "duel_player_resources_get").get("gold") == 4400,
            "newly unlocked account pays exactly one authoritative tournament fee")
        self.check(self.rpc(p, "duel_arena_queue_leave", {"arena_kind": "gold"}).get("ok")
            and self.rpc(p, "duel_player_resources_get").get("gold") == 5000,
            "newly unlocked queue cancellation returns the exact paid fee")
        catalog = self.storage("duel_match3_item_defs", "catalog", "00000000-0000-0000-0000-000000000000")["items"]
        output = next(key for key, value in catalog.items() if value.get("kind") == "equipment"
            and value.get("quality") == "normal" and value.get("slot") == "Helmet")
        recipe = catalog[output]["craft_recipe_id"]
        craft = self.rpc(p, "duel_workshop_craft_start", {"slot_index": 0, "output_def_id": output, "first_recipe_received": True})
        self.check(craft.get("err") == "workshop_locked" and self.rpc(p, "duel_player_resources_get").get("gold") == 5000,
            "real craft start ignores forged first-recipe marker and preserves wallet")
        self.seed_sheet(p, [recipe], [1])
        recipe_state = read(p)
        self.check(recipe_state.get("workshop_unlocked") is True
            and self.rpc(p, "duel_workshop_craft_start", {"slot_index": 0, "output_def_id": output}).get("err") == "recipe_not_learned",
            "first recipe unlocks workshop presentation but still requires learning the exact craft recipe")
        sold = self.rpc(p, "duel_character_item_sell", {"source": "inventory", "inv_index": 0,
            "expected_def_id": recipe, "request_id": uuid.uuid4().hex})
        saved = self.storage("duel_match3_character", "sheet", p["uid"])
        self.check(sold.get("ok") and saved.get("first_recipe_received") is True,
            "actual sale observes and persists first-recipe history before consuming the scroll")
        update_record("duel_match3_character", "sheet", p, lambda value: value.update(inventory=[], inventory_counts=[],
            inventory_pending_loot=[], inventory_sale_receipts=[], learned_recipes=[]))
        self.check(read(p).get("workshop_unlocked") is True,
            "durable first-recipe flag survives sold item, receipt rollover and empty inventory")
        filler = next(key for key, value in catalog.items() if value.get("kind") == "equipment")
        self.seed_sheet(other, [filler] * 25, [1] * 25)
        update_record("duel_match3_character", "sheet", other,
            lambda value: value.update(first_recipe_received=False, inventory_pending_loot=[{"def_id": recipe, "amount": 1}]))
        pending = read(other)
        self.check(pending.get("workshop_unlocked") is True and pending.get("mine_wins") == 0 and not pending.get("tournaments_unlocked"),
            "full-bag pending first recipe opens only its owner's workshop without granting tournament wins")
        with ThreadPoolExecutor(max_workers=6) as pool:
            replies = list(pool.map(lambda result: self.rpc(other, "duel_player_guidance_set", {"result": result}),
                ("completed", "skipped", "completed", "skipped", "completed", "skipped")))
        self.check(all(value.get("ok") for value in replies) and read(other).get("tutorial_status") == "completed",
            "six concurrent complete/skip requests converge monotonically to completed")
        path = "/storage/duel_player_guidance/v1/" + other["uid"]
        _, before = self.console("GET", path)
        replay = self.rpc(other, "duel_player_guidance_set", {"result": "skipped"})
        _, after = self.console("GET", path)
        self.check(replay.get("tutorial_status") == "completed" and before.get("version") == after.get("version"),
            "completed tutorial replay neither rewrites nor resets its server receipt")
        status, _ = http(self.args.url, "PUT", "/v2/storage", {"objects": [{"collection": "duel_player_guidance",
            "key": "v1", "value": json.dumps({"schema": 1, "tutorial": "pending"}), "permission_write": 1}]}, "Bearer " + p["token"])
        self.check(status != 200 and read(p).get("tutorial_status") == "completed",
            "real storage request guard rejects tutorial replacement and cannot reset another account")
        status, _ = self.console("DELETE", path)
        if status not in (200, 404):
            raise RuntimeError("Isolated legacy tutorial fixture failed")
        status, session = http(self.args.url, "POST", "/v2/account/authenticate/device?create=true", {"id": other["device_id"]}, self.basic)
        legacy = read(other)
        self.check(status == 200 and session.get("created") is not True and legacy.get("tutorial_status") == "legacy",
            "existing device reauthentication never opts a markerless legacy account into automatic tutorial")
        self.guidance_ui_snapshots = [{"name": "new-locked", "response": first}, {"name": "ten-wins", "response": ten},
            {"name": "recipe-received", "response": recipe_state}, {"name": "full-bag-pending", "response": pending}, {"name": "legacy", "response": legacy}]

    def smith(self):
        self.isolated_only("Smith full-inventory cancellation")
        catalog = self.storage("duel_match3_item_defs", "catalog", "00000000-0000-0000-0000-000000000000")["items"]
        filler = next(key for key, value in catalog.items()
            if value.get("kind") not in ("recipe", "material", "tesseract") and value.get("max_stack", 1) == 1)
        p = self.player()
        self.seed_tournament_access(p)
        self.seed_sheet(p, ["ingot_green"], [100])
        tier = {"arena_kind": "smith", "bet_tier": "green", "entry_amount": 1, "rules_version": 1}
        joined = self.rpc(p, "duel_arena_queue_join", tier)
        sheet = self.storage("duel_match3_character", "sheet", p["uid"])
        self.check(joined.get("ok") and sheet.get("inventory_counts", [0])[0] == 90
            and joined.get("entry_cost", {}).get("amount") == 10,
            "smith server ignores forged client cost/version and charges authoritative ten-ingot price")
        self.seed_sheet(p, [filler] * 25, [1] * 25)
        with ThreadPoolExecutor(max_workers=2) as pool:
            cancelled = list(pool.map(lambda _: self.rpc(p, "duel_arena_queue_leave", tier), range(2)))
        sheet = self.storage("duel_match3_character", "sheet", p["uid"])
        pending = sheet.get("inventory_pending_loot", [])
        self.check(sum(r.get("ok") is True for r in cancelled) == 1 and pending == [{"def_id": "ingot_green", "amount": 10}],
            "concurrent full-inventory smith cancellations persist exactly one complete ten-ingot refund")
        replay = self.rpc(p, "duel_arena_queue_leave", tier)
        self.check(replay.get("ok") is False and self.storage("duel_match3_character", "sheet", p["uid"]).get("inventory_pending_loot") == pending,
            "replayed cancellation cannot grant participation loot or duplicate pending refund")
        self.arena(smith=True)
        return self.arena(smith=True, smith_recipe_owned=True)

    def arena(self, smith=False, smith_recipe_owned=False):
        self.isolated_only("Arena regression")
        participants = [self.player() for _ in range(8)]
        tier = {"arena_kind": "smith", "bet_tier": "green"} if smith else {"arena_kind": "gold", "bet_tier": "2400"}
        initial = self.rpc(participants[0], "duel_arena_queue_poll", tier)
        if initial.get("queue_count", 0) > 0:
            raise RuntimeError("Selected public arena queue is occupied; refusing to mix test and real players")
        for p in participants:
            self.seed_tournament_access(p, gold=5000)
            if smith:
                self.seed_sheet(p, ["ingot_green"], [100])
        with ThreadPoolExecutor(max_workers=8) as pool:
            joined_results = list(pool.map(lambda p: self.rpc(p, "duel_arena_queue_join", tier), participants))
        for joined in joined_results:
            self.check(joined.get("ok") is True, "one temporary participant enters " + ("smith-green-v2" if smith else "gold-2400") + " arena")
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
        self.capture_arena_ui("qf-countdown", participants[-1], polls[-1])
        rows = tournament.get("qf", [])
        self.check(len(rows) == 4 and {r[side] for r in rows for side in ("uid_a", "uid_b")} == roster,
            "the tournament bracket contains exactly the eight temporary accounts")
        self.check(all(isinstance(r.get("avatar_a"), str) and isinstance(r.get("avatar_b"), str) for r in rows),
            "authoritative tournament DTO includes paired avatar IDs for every formed participant")
        if not all(r[side] in roster for r in rows for side in ("uid_a", "uid_b")):
            raise RuntimeError("Unexpected participant found; stopping without modifying that match")
        for row in rows:
            for side in ("a", "b"):
                known = row.get("hp_known_" + side)
                self.check(known in (True, False) and (known is True
                    or row.get("hp_" + side) == row.get("hp_max_" + side) == 0),
                    "uninitialized arena HP is explicitly unknown with zero current/maximum")
        by_uid = {p["uid"]: p for p in participants}
        sockets = {uid: self.connect(p) for uid, p in by_uid.items()}
        after_entry = [self.rpc(p, "duel_player_resources_get") for p in participants]
        self.check(all(value.get("ingots") == 90 if smith else value.get("gold") == 2600 for value in after_entry),
            "eight simultaneous arena joins charge every participant exactly one entry fee")
        if smith:
            status, catalog = self.console("GET", "/storage/duel_match3_item_defs/catalog/00000000-0000-0000-0000-000000000000")
            if status != 200:
                raise RuntimeError("Smith regression requires canonical isolated item catalog")
            definitions = json.loads(catalog["value"])["items"]
            filler = next(key for key, value in definitions.items()
                if value.get("kind") not in ("recipe", "material", "tesseract") and value.get("max_stack", 1) == 1)
            for p in participants:
                self.seed_sheet(p, [filler] * 25, [1] * 25, ["recipe_drop_t1_green_Helmet"] if smith_recipe_owned else [])
            self.check(all(v.get("entry_cost", {}).get("amount") == 10 and v.get("rules_version") == 2
                and v.get("prize_ingots") == 20 and v.get("consolation_ingots") == 2
                for v in (poll["tournament"] for poll in polls)),
                "smith production DTO quotes versioned ten-ingot stake, twenty champion and two consolation ingots")
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
        completed_rounds = {}
        completed_boards = {}
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
            fight_boards = {}
            for row in pairs:
                uid_a, uid_b, mid = row["uid_a"], row["uid_b"], row["match_id"]
                if uid_a not in roster or uid_b not in roster:
                    raise RuntimeError("Refusing to operate on a non-test arena participant")
                for uid in (uid_a, uid_b):
                    result = sockets[uid].command("match_join", {"match_id": mid})
                    if "match" not in result:
                        raise RuntimeError("Arena match join failed")
                board_a = sockets[uid_a].event(mid, 10)
                board_b = sockets[uid_b].event(mid, 10)
                self.check(all(board_a.get(key) == board_b.get(key)
                    for key in ("aHp", "bHp", "aMaxHp", "bMaxHp")),
                    round_name + ": both arena clients receive identical authoritative health")
                fight_boards[mid] = board_a
                fights.append((mid, sockets[uid_a], sockets[uid_b], by_uid[uid_a], by_uid[uid_b]))
            live = self.rpc(survivor, "duel_arena_queue_poll", tier).get("tournament", {})
            self.capture_arena_ui(round_name + "-live", survivor, {"ok": True, "tournament": live, "in_queue": False})
            live_rows = {row.get("match_id"): row for row in live.get(field, [])}
            for row in pairs:
                mirrored = live_rows.get(row["match_id"], {})
                self.check_arena_hp(mirrored, fight_boards[row["match_id"]],
                    round_name + ": live bracket")
            # All different pairs finish together, reaching separate authoritative callbacks/VMs.
            with ThreadPoolExecutor(max_workers=len(fights)) as pool:
                results = list(pool.map(lambda fight: self.finish(*fight), fights))
            if smith:
                for fight, result in zip(fights, results):
                    own = next((row for row in result.get("arenaCraftRewards", []) if row.get("userId") == fight[3]["uid"]), {})
                    loser_result = fight[1].event(fight[0], 11)
                    loser_own = next((row for row in loser_result.get("arenaCraftRewards", []) if row.get("userId") == fight[3]["uid"]), {})
                    self.check(own.get("outcome") == "participation" and own.get("ingots") == 2
                        and own.get("pendingCount") == 2 and loser_own == own,
                        "smith defeated client receives its exact two-ingot pending compensation in actual game-over broadcast")
                    if round_name == "final":
                        champion = next((row for row in result.get("arenaCraftRewards", []) if row.get("userId") == fight[4]["uid"]), {})
                        self.check(champion.get("outcome") == "champion"
                            and champion.get("ingots") == (30 if smith_recipe_owned else 20)
                            and champion.get("recipeItemId", "") == ("" if smith_recipe_owned else "recipe_drop_t1_green_Helmet")
                            and champion.get("pendingCount") == (30 if smith_recipe_owned else 21),
                            "smith champion receives exact defeated-floor Helmet recipe or thirty-ingot owned-pool fallback, including full-inventory pending")
            winners = [fight[4] for fight in fights]
            status, stored = self.console("GET", "/storage/arena_tournament/" + tid
                + "/00000000-0000-0000-0000-000000000000")
            if status != 200:
                raise RuntimeError("Completed test tournament state unavailable: HTTP " + str(status))
            saved = json.loads(stored["value"])
            completed_rounds[field] = pairs
            if round_name == "final":
                persisted = (saved.get("phase") == "done" and saved.get("reward_receipt", {}).get("user_id") == winners[0]["uid"]
                    and len(saved.get("final", [])) == 1 and saved["final"][0].get("status") == "done")
                if saved.get("final"):
                    self.check_arena_hp(saved["final"][0], fight_boards[fights[0][0]],
                        "completed final forfeit preserves actual survivor and loser HP")
            else:
                next_round = "sf" if round_name == "qf" else "final"
                next_pairs = saved.get(next_round, [])
                next_roster = {row[side] for row in next_pairs for side in ("uid_a", "uid_b")}
                persisted = (len(next_pairs) * 2 == len(winners)
                    and next_roster == {p["uid"] for p in winners}
                    and (saved.get("phase") == next_round
                        or (saved.get("phase") == "countdown" and saved.get("next_round") == next_round)))
            self.check(persisted, round_name + ": simultaneous fight results preserve every winner in the committed next stage")
            survivor = winners[0]
            if round_name != "final":
                checkpoint = self.rpc(survivor, "duel_arena_queue_poll", tier)
                history = checkpoint.get("tournament", {})
                self.capture_arena_ui("after-" + round_name, survivor, checkpoint)
                for past_field, prior_rows in completed_rounds.items():
                    retained = {row.get("slot"): row for row in history.get(past_field, [])}
                    self.check(len(retained) == len(prior_rows),
                        round_name + ": production poll retains every completed " + past_field + " row")
                    for prior in prior_rows:
                        row = retained.get(prior.get("slot"), {})
                        self.check(all(row.get(key) == prior.get(key) and bool(row.get(key))
                            for key in ("uid_a", "uid_b", "display_a", "display_b"))
                            and row.get("status") == "done" and row.get("winner_uid") == prior["uid_b"],
                            round_name + ": historical names, participants and actual winner survive in the production DTO")
                        self.check_arena_hp(row, fight_boards.get(prior.get("match_id"), {})
                            if past_field == field else completed_boards[past_field][prior["match_id"]],
                            round_name + ": historical " + past_field + " HP")
                completed_boards[field] = fight_boards
        final_resources = self.rpc(survivor, "duel_player_resources_get")
        # No bets: gold 5000 - entry 2400 + final 12000 = 14600.
        self.check(final_resources.get("gold") == (5300 if smith else 14600), "tournament grants its final currency prize exactly once")
        for _ in range(3):
            self.rpc(survivor, "duel_arena_queue_poll", tier)
        repeated_resources = self.rpc(survivor, "duel_player_resources_get")
        self.check(repeated_resources.get("gold") == (5300 if smith else 14600),
            "repeated completed-tournament polls cannot repeat its final prize")
        if smith:
            saved = self.storage("arena_tournament", tid, "00000000-0000-0000-0000-000000000000")
            receipts = saved.get("craft_receipts", {})
            self.check(len(receipts) == 8 and sum(r.get("outcome") == "champion" for r in receipts.values()) == 1
                and all(part.get("entry_cost", {}).get("amount") == 10 for part in saved.get("parts", {}).values()),
                "smith durable terminal state records exact paid costs and one reward receipt per human")
            for p in participants:
                sheet = self.storage("duel_match3_character", "sheet", p["uid"])
                pending = sum(row["amount"] for row in sheet.get("inventory_pending_loot", []))
                self.check(pending == ((30 if smith_recipe_owned else 21) if p["uid"] == survivor["uid"] else 2),
                    "completed smith polls and repeated match result cannot duplicate held craft rewards")
            path = "/storage/duel_match3_character/sheet/" + survivor["uid"]
            status, row = self.console("GET", path)
            sheet = json.loads(row["value"])
            sheet["inventory"][0:2], sheet["inventory_counts"][0:2] = ["", ""], [0, 0]
            status, _ = self.console("PUT", path, {"value": json.dumps(sheet), "version": row["version"], "permission_read": 1, "permission_write": 0})
            if status != 200:
                raise RuntimeError("Isolated smith free-slot fixture failed")
            delivered = self.rpc(survivor, "duel_character_loot_deliver")
            sheet = self.storage("duel_match3_character", "sheet", survivor["uid"])
            self.check(delivered.get("ok") is True and not sheet.get("inventory_pending_loot")
                and "ingot_green" in sheet.get("inventory", [])
                and (smith_recipe_owned or "recipe_drop_t1_green_Helmet" in sheet.get("inventory", [])),
                "real inventory delivery drains guaranteed champion items after space is freed without loss or duplication")

    def check_arena_hp(self, row, board, label):
        # Human matches publish a/b in sorted user-id order; bracket slots are
        # independently shuffled. Compare with the real board, never a fixed 150.
        ids = sorted((row.get("uid_a", ""), row.get("uid_b", "")))
        if not all(ids):
            self.check(False, label + ": pair identifiers are present")
            return
        for side in ("a", "b"):
            prefix = "a" if row["uid_" + side] == ids[0] else "b"
            current, maximum = board.get(prefix + "Hp"), board.get(prefix + "MaxHp")
            self.check(isinstance(current, (int, float)) and isinstance(maximum, (int, float)) and maximum > 0
                and row.get("hp_known_" + side) is True
                and row.get("hp_" + side) == current and row.get("hp_max_" + side) == maximum,
                label + ": side " + side + " current/max/known match authoritative BOARD_SYNC")

    def capture_arena_ui(self, name, viewer, poll):
        """Only a genuine disposable-player poll DTO; no tokens, credentials or invented fixtures."""
        self.arena_ui_snapshots.append({"name": name, "viewer_uid": viewer["uid"],
            "poll_json": json.dumps(poll, ensure_ascii=False, separators=(",", ":"))})

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


def resource_totals(lines):
    totals = {}
    for line in lines or []:
        resource, amount = line["resource"], line["amount"]
        if not isinstance(resource, str) or not isinstance(amount, (int, float)) or isinstance(amount, bool):
            raise ValueError("Invalid server resource line")
        totals[resource] = totals.get(resource, 0) + amount
    return totals


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
    parser.add_argument("--scenario", choices=("probe", "friends", "lifecycle", "pvp", "double-disconnect", "overlap", "race", "arena", "smith", "daily", "consumables", "natural", "race-unpaid", "race-receipts", "race-legacy-refund", "race-policy", "cosmetics", "guidance", "global-boss", "global-boss-practice", "equipment-sets", "practice-smoke", "quote-preflight", "shop", "all"), default="pvp")
    parser.add_argument("--item-catalog", type=Path)
    parser.add_argument("--catalog-migration-helper", type=Path)
    parser.add_argument("--consumable-manifest", type=Path)
    parser.add_argument("--fail-fast", action="store_true")
    parser.add_argument("--report", type=Path)
    args = parser.parse_args()
    target = urlsplit(args.url)
    console_target = urlsplit(args.console_url)
    if args.scenario in ("friends", "lifecycle", "race", "arena", "smith", "daily", "consumables", "natural", "race-unpaid", "race-receipts", "race-legacy-refund", "race-policy", "cosmetics", "guidance", "global-boss", "global-boss-practice", "equipment-sets", "shop", "all") and (
            target.hostname not in ("127.0.0.1", "localhost") or target.port != 28350
            or console_target.hostname not in ("127.0.0.1", "localhost") or console_target.port != 28351):
        parser.error("This scenario requires the isolated localhost test backend on ports 28350 and 28351")
    suite = Suite(args)
    try:
        if args.scenario in ("all", "lifecycle", "smith", "daily", "consumables", "cosmetics", "guidance", "global-boss", "global-boss-practice", "equipment-sets", "shop"):
            suite.isolated_only("Canonical smith catalog fixture")
            if not args.item_catalog:
                raise RuntimeError("This scenario requires --item-catalog from the disposable runner")
            catalog = json.loads(args.item_catalog.read_text(encoding="utf-8-sig"))
            if not isinstance(catalog.get("items"), dict) or not catalog["items"]:
                raise RuntimeError("Invalid smith canonical catalog fixture")
            path = "/storage/duel_match3_item_defs/catalog/00000000-0000-0000-0000-000000000000"
            status, _ = suite.console("GET", path)
            if status != 404:
                raise RuntimeError("Refusing to replace an existing isolated item catalog")
            seed = json.loads(json.dumps(catalog))
            if args.catalog_migration_helper:
                if not args.consumable_manifest:
                    raise RuntimeError("Catalog migration fixture requires its fixed manifest")
                manifest = json.loads(args.consumable_manifest.read_text())
                if len(manifest.get("items", {})) != 4:
                    raise RuntimeError("Expected exactly four fixed consumable definitions")
                for item_id in manifest["items"]:
                    del seed["items"][item_id]
                seed["version"] = 3
            status, _ = suite.console("PUT", path, {"value": json.dumps(seed), "version": "*", "permission_read": 1, "permission_write": 0})
            if status != 200:
                raise RuntimeError("Canonical smith catalog creation failed")
            if args.catalog_migration_helper:
                base = [sys.executable, str(args.catalog_migration_helper), "--url", args.console_url,
                    "--config", str(args.config), "--manifest", str(args.consumable_manifest)]
                def migrate(extra):
                    command = subprocess.run(base + extra, capture_output=True, text=True, timeout=30)
                    if command.returncode:
                        raise RuntimeError("Isolated consumable catalog migration helper failed")
                    return json.loads(command.stdout)
                planned = migrate([])
                backup = args.config.parent.parent / "catalog-before-consumables.json"
                applied = migrate(["--apply", "--backup", str(backup)])
                replay = migrate([])
                actual = suite.storage("duel_match3_item_defs", "catalog", "00000000-0000-0000-0000-000000000000")
                suite.check(planned.get("mode") == "planned" and len(planned.get("added_ids", [])) == 4
                    and planned.get("existing_definitions_preserved") == len(catalog['items'])-4,
                    "actual isolated migration dry-run plans exactly four additions while preserving every existing definition including boss teleport")
                suite.check(applied.get("mode") == "applied" and actual.get('items') == catalog.get('items')
                    and actual.get('version') == 4 and backup.is_file(),
                    "actual consumable migration creates a backup and preserves all canonical item definitions at its versionfour boundary")
                suite.check(replay.get("mode") == "unchanged" and not replay.get("added_ids"),
                    "repeating the catalog migration after success is an idempotent no-op")
        if args.scenario == "probe":
            status, tournaments = suite.console("GET", "/storage?collection=arena_tournament")
            suite.check(status == 200, "arena storage can be inspected without reading player identities")
            print("Arena persisted object count=" + str(tournaments.get("total_count", 0)), flush=True)
        else:
            scenarios = {"friends": suite.friends, "lifecycle": suite.lifecycle, "guidance": suite.guidance, "cosmetics": suite.cosmetics, "daily": suite.daily, "consumables": suite.consumables, "smith": suite.smith,
                "pvp": suite.pvp, "double-disconnect": suite.double_disconnect,
                "overlap": suite.overlap_reconnect, "race": suite.race, "arena": suite.arena, "natural": suite.natural,
                "race-unpaid": suite.race_unpaid, "race-receipts": suite.race_receipts,
                "race-legacy-refund": suite.race_legacy_refund,"global-boss":suite.global_boss,"shop":suite.shop,
                "global-boss-practice":suite.global_boss_practice,"equipment-sets":suite.equipment_sets}
            if args.scenario == "practice-smoke":
                scenarios = {"practice-smoke": suite.practice_smoke}
            if args.scenario == "quote-preflight":
                scenarios = {"quote-preflight": suite.quote_preflight}
            if args.scenario == "race-policy":
                scenarios = {"race-policy": suite.race_policy}
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
                    if args.report:
                        trace = args.report.with_name(args.report.stem + "-traceback.log")
                        with trace.open("a", encoding="utf-8") as output:
                            output.write("SCENARIO " + name + "\n" + traceback.format_exc() + "\n")
                if args.fail_fast and (suite.failures or suite.errors):
                    break
    finally:
        try:
            suite.cleanup()
        except Exception as error:
            suite.errors.append("cleanup: " + type(error).__name__ + ": " + str(error))
            print("ERROR cleanup: " + type(error).__name__, flush=True)
        print("RESULT passed=" + str(sum(c["passed"] for c in suite.checks)) + " failed=" + str(len(suite.failures))
            + " errors=" + str(len(suite.errors)), flush=True)
        if args.report:
            args.report.write_text(json.dumps({"checks": suite.checks, "errors": suite.errors,
                "arena_ui_snapshots": suite.arena_ui_snapshots, "cosmetic_ui_snapshots": getattr(suite, "cosmetic_ui_snapshots", []),
                "daily_ui_snapshots": getattr(suite, "daily_ui_snapshots", []),
                "guidance_ui_snapshots": getattr(suite, "guidance_ui_snapshots", []),
                "consumable_ui_snapshots": getattr(suite, "consumable_ui_snapshots", []),"boss_ui_snapshots":getattr(suite,"boss_ui_snapshots",[])}, indent=2), encoding="utf-8")
    return 1 if suite.failures or suite.errors else 0


if __name__ == "__main__":
    sys.exit(main())
