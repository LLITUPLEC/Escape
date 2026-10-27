"""Verify the nineteen selected sounds at the retained audio-035 public URL."""
import argparse
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timezone
import hashlib
import io
import json
from pathlib import Path
import re
from urllib.request import Request, urlopen
import wave


def digest(data):
    return hashlib.sha256(data).hexdigest()


def has_absolute_reference(value):
    if isinstance(value, str):
        return value.startswith(("/", "\\")) or re.search(r"[A-Za-z]:[\\/]|file://|https?://", value) is not None
    if isinstance(value, dict):
        return any(has_absolute_reference(key) or has_absolute_reference(item) for key, item in value.items())
    if isinstance(value, list):
        return any(has_absolute_reference(item) for item in value)
    return False


def get(url, limit):
    with urlopen(Request(url, headers={"User-Agent": "Escape-release-verifier/0.3.6", "Cache-Control": "no-cache"}), timeout=25) as response:
        if response.status != 200:
            raise ValueError("HTTP status " + str(response.status))
        data = response.read(limit + 1)
        if len(data) > limit:
            raise ValueError("Unexpectedly large public file")
        return data


def validate_local_bundle(bundle):
    expected = json.loads((bundle / "files.json").read_text(encoding="utf-8-sig"))
    selected = json.loads((bundle / "selected-metadata.json").read_text(encoding="utf-8-sig"))
    if (not isinstance(expected, list) or len(expected) != 19 or
            not isinstance(selected, dict) or selected.get("selected_count") != 19 or
            selected.get("audition_version") != "0.3.5" or not isinstance(selected.get("selected"), list) or len(selected["selected"]) != 19):
        raise ValueError("Expected nineteen selected audio clips and their history")
    if has_absolute_reference(selected):
        raise ValueError("Audio history contains non-public absolute paths")
    choices = selected.get("selection_sha256", "")
    if not re.fullmatch(r"[a-f0-9]{64}", choices) or digest((bundle / "Escape-audio-choices-0.3.5.txt").read_bytes()) != choices:
        raise ValueError("Choice history identity mismatch")
    chosen = {}
    for entry in selected["selected"]:
        rel, identifier = entry.get("file", ""), entry.get("id", "")
        if (not re.fullmatch(r"[a-z][a-z0-9_]*", identifier) or rel != "selected/" + identifier + ".wav" or
                rel in chosen or entry.get("choice") not in ("A", "B")):
            raise ValueError("Unexpected selected audio identity")
        chosen[rel] = entry["sha256"]
    seen = set()
    for item in expected:
        rel = item.get("file", "")
        size = item.get("bytes")
        if (set(item) != {"file", "bytes", "sha256"} or not re.fullmatch(r"selected/[a-z][a-z0-9_]*\.wav", rel) or
                rel in seen or rel not in chosen or item.get("sha256") != chosen[rel] or
                not re.fullmatch(r"[a-f0-9]{64}", item["sha256"]) or type(size) is not int or not 44 < size < 10_000_000):
            raise ValueError("Unexpected or duplicate public selected media entry")
        path = bundle / rel
        if path.is_symlink() or not path.resolve().is_relative_to(bundle.resolve()):
            raise ValueError("Selected media path escapes bundle")
        data = path.read_bytes()
        if len(data) != size or digest(data) != item["sha256"]:
            raise ValueError("Local selected WAV identity mismatch: " + rel)
        with wave.open(io.BytesIO(data), "rb") as audio:
            if audio.getparams()[:3] != (1, 2, 48000) or audio.getcomptype() != "NONE":
                raise ValueError("Selected media is not mono PCM16 48kHz WAV")
        seen.add(rel)
    html = (bundle / "index.html").read_text(encoding="utf-8-sig")
    links = re.findall(r'\bdata-audio=["\x27]([^"\x27]+)', html)
    if len(links) != 19 or set(links) != seen or len(re.findall(r"<audio\b", html, re.I)) != 1:
        raise ValueError("Selected page must contain nineteen clips and one player")
    if re.search(r"<audio\b[^>]*\bautoplay(?:\s|=|>)", html, re.I):
        raise ValueError("Selected page must not autoplay")
    return expected, selected


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--bundle", type=Path, required=True)
    parser.add_argument("--report", type=Path, required=True)
    parser.add_argument("--local-only", action="store_true", help="Validate a local rendered audio-035 bundle without network requests")
    args = parser.parse_args()
    base = "https://prokrust-play.ru/escape/audio-035/"
    expected, selected = validate_local_bundle(args.bundle)
    html = (args.bundle / "index.html").read_bytes()
    if not args.local_only:
        actual = json.loads(get(base + "files.json", 100_000).decode("utf-8-sig"))
        if actual != expected:
            raise ValueError("Published audio manifest differs from this bundle")
        actual_selected = json.loads(get(base + "selected-metadata.json", 100_000).decode("utf-8-sig"))
        if actual_selected != selected:
            raise ValueError("Published selected audio history differs from this bundle")
        if digest(get(base + "Escape-audio-choices-0.3.5.txt", 100_000)) != selected["selection_sha256"]:
            raise ValueError("Published choice history identity mismatch")
        published_html = get(base, 400_000)
        if digest(published_html) != digest(html):
            raise ValueError("Published audio page differs from this bundle")

    def verify(item):
        rel = item["file"]
        if not re.fullmatch(r"selected/[a-z][a-z0-9_]*\.wav", rel):
            raise ValueError("Unexpected public media path")
        size = item["bytes"]
        if type(size) is not int or not 44 < size < 10_000_000:
            raise ValueError("Unexpected media size")
        data = (args.bundle / rel).read_bytes() if args.local_only else get(base + rel, size)
        if len(data) != size or digest(data) != item["sha256"]:
            raise ValueError("Public WAV identity mismatch: " + rel)
        if data[:4] != b"RIFF" or data[8:12] != b"WAVE":
            raise ValueError("Public media is not WAV: " + rel)
        return {"file": rel, "bytes": size, "sha256": item["sha256"], "verified": True}

    with ThreadPoolExecutor(max_workers=4) as pool:
        files = list(pool.map(verify, expected))
    report = {"passed": True, "checked_at_utc": datetime.now(timezone.utc).isoformat(),
              "page_url": base, "page_sha256": digest(html), "wav_files": len(files),
              "scope": "local_bundle" if args.local_only else "published_https",
              "public_verified": not args.local_only, "selection_sha256": selected["selection_sha256"],
              "bytes": sum(item["bytes"] for item in files), "files": files,
              "human_listening_performed": False, "physical_device_tested": False}
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({key: value for key, value in report.items() if key != "files"}))


if __name__ == "__main__":
    main()
