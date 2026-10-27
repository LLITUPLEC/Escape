"""Smoke-test only the isolated Escape backend using a disposable test account."""
import argparse
import base64
import hashlib
import json
from pathlib import Path
import socket
import ssl
import struct
import time
from urllib.error import HTTPError
from urllib.parse import urlencode, urlsplit
from urllib.request import Request, urlopen
import uuid

parser = argparse.ArgumentParser()
parser.add_argument("--url", default="http://127.0.0.1:27350")
parser.add_argument("--config", type=Path, default=Path("secrets/server.yml"))
args = parser.parse_args()
config = json.loads(args.config.read_text())
basic = base64.b64encode((config["socket"]["server_key"] + ":").encode()).decode()
token = None
checks = 0

def request(method, path, value=None, authenticated=True):
    data = None if value is None else json.dumps(value).encode()
    headers = {"Content-Type": "application/json"}
    headers["Authorization"] = "Bearer " + token if authenticated else "Basic " + basic
    req = Request(args.url + path, data=data, headers=headers, method=method)
    try:
        with urlopen(req, timeout=15) as response:
            body = response.read()
            return response.status, json.loads(body) if body else {}
    except HTTPError as error:
        return error.code, {}

def check(condition, message):
    global checks
    if not condition:
        raise AssertionError(message)
    checks += 1
    print("PASS " + message, flush=True)

def rpc(name, payload):
    status, body = request("POST", "/v2/rpc/" + name, json.dumps(payload))
    if status != 200:
        return status, {}
    return status, json.loads(body.get("payload", "{}"))

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


def websocket_check(epoch):
    status, before = rpc("duel_match3_stats_get", {})
    check(status == 200 and before.get("ok") is True, "trusted match counters can be read before a real match")
    status, created = rpc("duel_match3_pve_create", {
        "bot_id": "mine_1", "floor": 1, "difficulty": "easy", "session_epoch": epoch,
    })
    check(status == 200 and created.get("ok") is True and bool(created.get("match_id")),
          "authoritative PvE match can be created")
    ws = JsonWebSocket(args.url, token)
    try:
        check(True, "authenticated WebSocket upgrade works")
        match_id = created["match_id"]
        ws.send_json({"cid": "smoke-join", "match_join": {"match_id": match_id}})
        board = ws.wait_match_data(match_id, 10)
        check(bool(board.get("board")) and bool(board.get("activeUserId")), "authoritative PvE board starts over WebSocket")
        ws.send_json({"match_data_send": {"match_id": match_id, "op_code": "12", "data": ""}})
        game_over = ws.wait_match_data(match_id, 11)
        check(game_over.get("winnerUserId") == created.get("bot_user_id") and game_over.get("isDraw") is not True,
              "server finishes a started PvE match after player forfeit")
    finally:
        ws.close()
    status, after = rpc("duel_match3_stats_get", {})
    before_modes = {entry["id"]: entry for entry in before.get("modes", [])}
    after_modes = {entry["id"]: entry for entry in after.get("modes", [])}
    check(status == 200 and after.get("ok") is True
          and after.get("played") == before.get("played", 0) + 1
          and after.get("losses") == before.get("losses", 0) + 1
          and after.get("wins") == before.get("wins", 0)
          and after_modes.get("mine", {}).get("losses") == before_modes.get("mine", {}).get("losses", 0) + 1,
          "authoritative match finish records exactly one trusted loss")

def main():
    global token
    status, _ = request("GET", "/healthcheck", authenticated=False)
    check(status == 200, "health endpoint responds")
    suffix = uuid.uuid4().hex[:12]
    status, session = request("POST", "/v2/account/authenticate/device?create=true&username=sec_" + suffix,
                              {"id": "escape-security-" + uuid.uuid4().hex}, authenticated=False)
    check(status == 200 and bool(session.get("token")), "new device can authenticate")
    token = session["token"]
    try:
        status, claim = rpc("duel_session_claim", {})
        check(status == 200 and claim.get("ok") is True, "session claim works")
        epoch = claim["session_epoch"]
        status, stats_before = rpc("duel_match3_stats_get", {})
        check(status == 200 and stats_before.get("ok") is True, "server initializes character and statistics")
        status, _ = rpc("duel_leaderboard_admin_purge", {"mode": "all_lb"})
        check(status in (404, 501), "purge RPC is unavailable to players")
        status, forged = rpc("duel_match3_stats_record", {"won": True, "mode": "duel", "session_epoch": epoch})
        check(status == 200 and forged.get("ok") is False, "forged client result is rejected")
        status, stats_after = rpc("duel_match3_stats_get", {})
        check(status == 200 and all(stats_after.get(k) == stats_before.get(k) for k in ("played", "wins", "losses")),
              "forged result leaves trusted counters unchanged")
        for collection, key in (("duel_match3_progress", "profile"), ("duel_match3_character", "sheet"),
                                ("duel_match3_stats", "summary"), ("duel_match3_cheat_whitelist", "emails"),
                                ("arena_tournament", "forged"), ("duel_global_boss", "config"),
                                ("duel_global_boss_player", "week-forged"), ("duel_global_boss_attempt", "attempt-forged"),
                                ("duel_mail", "inbox"), ("duel_shop_catalog", "v1"), ("duel_shop_receipts", "v1")):
            status, _ = request("PUT", "/v2/storage", {"objects": [{"collection": collection, "key": key,
                "value": json.dumps({"gold": 99999999, "xp": 99999999}), "permission_read": 1, "permission_write": 1}]})
            check(status == 404, "direct game storage write blocked: " + collection)
        status, _ = request("PUT", "/v2/storage/delete", {"object_ids": [{"collection": "duel_match3_progress", "key": "profile"}]})
        check(status == 404, "direct progress deletion is blocked")
        status, _ = request("PUT", "/v2/storage", {"objects": [{"collection": "client_settings", "key": "smoke",
            "value": "{}", "permission_read": 1, "permission_write": 1}]})
        check(status == 200, "unreserved player settings remain writable")
        status, _ = request("PUT", "/v2/storage/delete", {"object_ids": [{"collection": "client_settings", "key": "smoke"}]})
        check(status == 200, "unreserved player settings remain deletable")
        status, _ = request("PUT", "/v2/account", {"username": "renamed_" + suffix})
        check(status == 404, "direct username change cannot bypass game price")
        status, catalog = rpc("duel_match3_item_catalog_get", {})
        check(status == 200 and catalog.get("ok") is True and bool(catalog.get("items")), "static item catalog is available")
        status, mine = rpc("duel_match3_pve_catalog_get", {"difficulty": "easy"})
        check(status == 200 and mine.get("ok") is True, "mine catalog is available")
        websocket_check(epoch)
    finally:
        status, wiped = rpc("duel_account_wipe", {})
        check(status == 200 and wiped.get("account_deleted") is True, "disposable test account deleted")
    print(f"Security smoke: {checks} checks passed; credentials were not printed.")


if __name__ == "__main__":
    main()
