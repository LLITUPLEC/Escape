"""Check the dashboard using server-local credentials, never printing them."""
import json
from pathlib import Path
from urllib.request import Request, urlopen

base = "https://console.prokrust-play.ru"
config = json.loads(Path("secrets/server.yml").read_text())
with urlopen(base + "/", timeout=15) as response:
    assert response.status == 200 and b"<html" in response.read().lower(), "Dashboard UI missing"
print("PASS dashboard HTTPS and UI")
payload = json.dumps({"username": config["console"]["username"], "password": config["console"]["password"]}).encode()
req = Request(base + "/v2/console/authenticate", data=payload,
              headers={"Content-Type": "application/json"}, method="POST")
with urlopen(req, timeout=15) as response:
    result = json.loads(response.read())
    token = result.get("token")
    assert response.status == 200 and token, "Dashboard authentication failed"
print("PASS dashboard administrator authentication")
req = Request(base + "/v2/console/status", headers={"Authorization": "Bearer " + token})
with urlopen(req, timeout=15) as response:
    assert response.status == 200, "Dashboard status API failed"
    response.read()
print("PASS dashboard authenticated API; credentials were not printed")
