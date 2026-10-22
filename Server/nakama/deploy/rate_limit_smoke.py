"""Bounded live tests against Escape only; uses and deletes one test account."""
import json
import time
from urllib.error import HTTPError
from urllib.request import Request, urlopen
import uuid
import security_smoke as smoke

suffix = uuid.uuid4().hex[:12]
status, session = smoke.request("POST", "/v2/account/authenticate/device?create=true&username=rate_" + suffix,
    {"id": "escape-rate-test-" + uuid.uuid4().hex}, authenticated=False)
smoke.check(status == 200 and bool(session.get("token")), "rate test account authenticated")
smoke.token = session["token"]
try:
    ws = smoke.JsonWebSocket(smoke.args.url, smoke.token)
    try:
        denied = False
        accepted = 0
        for index in range(100):
            cid = "limit-" + str(index)
            ws.send_json({"cid": cid, "rpc": {"id": "duel_match3_stats_get", "payload": "{}"}})
            while True:
                result = ws.read_json()
                if result.get("cid") == cid:
                    break
            if "error" in result:
                smoke.check("rate_limited" in result["error"].get("message", ""), "WebSocket RPC flood is rate limited")
                denied = True
                break
            smoke.check("rpc" in result, "RPC response is valid") if index == 0 else None
            accepted += 1
        smoke.check(denied and accepted >= 1, "one player's budget is bounded across repeated socket RPC calls")
        status, _ = smoke.rpc("duel_match3_stats_get", {})
        smoke.check(status == 429, "HTTP and WebSocket share the same player budget")
        time.sleep(0.6)
        status, result = smoke.rpc("duel_match3_stats_get", {})
        smoke.check(status == 200 and result.get("ok") is True, "player budget refills after a short wait")
    finally:
        ws.close()
finally:
    status, wiped = smoke.rpc("duel_account_wipe", {})
    smoke.check(status == 200 and wiped.get("account_deleted") is True, "rate test account deleted")

def invalid_login(extra_headers=None):
    headers = {"Content-Type": "application/json", "Authorization": "Basic " + smoke.basic}
    headers.update(extra_headers or {})
    req = Request(smoke.args.url + "/v2/account/authenticate/email?create=false",
        data=json.dumps({"email": "rate-check-" + suffix + "@example.invalid", "password": uuid.uuid4().hex}).encode(),
        headers=headers, method="POST")
    try:
        with urlopen(req, timeout=10) as response:
            response.read()
            return response.status
    except HTTPError as error:
        return error.code

limited = False
for _ in range(20):
    if invalid_login() == 429:
        limited = True
        break
smoke.check(limited, "login attempts are bounded per IP")
smoke.check(invalid_login({"X-Real-IP": "198.51.100.17", "X-Forwarded-For": "203.0.113.21"}) == 429,
            "public IP headers cannot bypass the login limit")
print("Live rate-limit checks passed; no credentials or real user accounts were printed.")
