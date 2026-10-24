"""Verify the published audition page and every WAV against the local public bundle."""
import argparse
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re
from urllib.request import Request, urlopen


def digest(data):
    return hashlib.sha256(data).hexdigest()


def get(url, limit):
    with urlopen(Request(url, headers={"User-Agent": "Escape-release-verifier/0.3.5", "Cache-Control": "no-cache"}), timeout=25) as response:
        if response.status != 200:
            raise ValueError("HTTP status " + str(response.status))
        data = response.read(limit + 1)
        if len(data) > limit:
            raise ValueError("Unexpectedly large public file")
        return data


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--bundle", type=Path, required=True)
    parser.add_argument("--report", type=Path, required=True)
    args = parser.parse_args()
    base = "https://prokrust-play.ru/escape/audio-035/"
    expected = json.loads((args.bundle / "files.json").read_text(encoding="utf-8-sig"))
    if len(expected) != 64 or len({item["file"] for item in expected}) != 64:
        raise ValueError("Expected 64 unique public media paths")
    actual = json.loads(get(base + "files.json", 100_000).decode("utf-8-sig"))
    if actual != expected:
        raise ValueError("Published audio manifest differs from this bundle")
    html = get(base, 400_000)
    if digest(html) != digest((args.bundle / "index.html").read_bytes()):
        raise ValueError("Published audition page differs from this bundle")

    def verify(item):
        rel = item["file"]
        if not re.fullmatch(r"(?:A|B|pairs|montages|showcases)/[A-Za-z0-9_-]+\.wav", rel):
            raise ValueError("Unexpected public media path")
        size = item["bytes"]
        if type(size) is not int or not 44 < size < 10_000_000:
            raise ValueError("Unexpected media size")
        data = get(base + rel, size)
        if len(data) != size or digest(data) != item["sha256"]:
            raise ValueError("Public WAV identity mismatch: " + rel)
        if data[:4] != b"RIFF" or data[8:12] != b"WAVE":
            raise ValueError("Public media is not WAV: " + rel)
        return {"file": rel, "bytes": size, "sha256": item["sha256"], "verified": True}

    with ThreadPoolExecutor(max_workers=4) as pool:
        files = list(pool.map(verify, expected))
    report = {"passed": True, "checked_at_utc": datetime.now(timezone.utc).isoformat(),
              "page_url": base, "page_sha256": digest(html), "wav_files": len(files),
              "bytes": sum(item["bytes"] for item in files), "files": files,
              "human_listening_performed": False, "physical_device_tested": False}
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({key: value for key, value in report.items() if key != "files"}))


if __name__ == "__main__":
    main()
