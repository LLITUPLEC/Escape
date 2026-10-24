"""Publish only the static Escape download page; the existing API/DB are not restarted."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess


def sha256(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def publish(staging, validate_only=False):
    staging = staging.resolve(strict=True)
    if not staging.is_relative_to(Path("/opt/escape/page-staging")):
        raise SystemExit("Staging must be inside /opt/escape/page-staging")
    release = json.loads((staging / "release.json").read_text(encoding="utf-8-sig"))
    allowed_keys = {"version", "date", "filename", "bytes", "sha256", "url", "signature_verified", "architecture"}
    if set(release) - allowed_keys:
        raise SystemExit("Unexpected fields in the public release manifest")
    version = release["version"]
    checksum = release["sha256"]
    if version != "0.3.5" or not isinstance(checksum, str) or not re.fullmatch(r"[0-9a-f]{64}", checksum):
        raise SystemExit("Invalid release identity")
    if type(release["bytes"]) is not int or release["bytes"] <= 0:
        raise SystemExit("APK bytes must be a positive integer")
    # Accept the generated public manifest contract, never a local validation/log bundle.
    apk_name = "Escape-" + version + "-" + checksum[:12] + ".apk"
    if release.get("filename") != apk_name or release.get("url") != "./downloads/" + apk_name:
        raise SystemExit("Release filename/URL does not match the APK identity")
    if not isinstance(release.get("date"), str) or not re.fullmatch(r"\d{4}-\d{2}-\d{2}", release["date"]):
        raise SystemExit("Invalid public release date")
    if "signature_verified" in release and release["signature_verified"] is not True:
        raise SystemExit("Unexpected signature verification value")
    if "architecture" in release and (not isinstance(release["architecture"], list) or
            len(release["architecture"]) != 2 or set(release["architecture"]) != {"arm64-v8a", "armeabi-v7a"}):
        raise SystemExit("Unexpected native architecture declaration")
    apk = staging / "downloads" / apk_name
    if sha256(apk) != checksum or apk.stat().st_size != release["bytes"]:
        raise SystemExit("APK hash or byte count does not match release.json")
    allowed_root = {"index.html", "release.json"}
    audio_root = staging / "audio-035"
    audio_manifest = json.loads((audio_root / "files.json").read_text(encoding="utf-8-sig"))
    if not isinstance(audio_manifest, list) or len(audio_manifest) != 64:
        raise SystemExit("Expected exactly 64 audition WAV files")
    allowed_audio = {"index.html", "files.json"}
    for entry in audio_manifest:
        rel = entry.get("file", "")
        if not re.fullmatch(r"(?:A|B|pairs|montages|showcases)/[A-Za-z0-9_-]+\.wav", rel) or rel in allowed_audio:
            raise SystemExit("Unexpected or duplicate public audition path")
        media = audio_root / rel
        if (type(entry.get("bytes")) is not int or not 44 < entry["bytes"] < 10_000_000 or
                media.stat().st_size != entry["bytes"] or sha256(media) != entry.get("sha256")):
            raise SystemExit("Audition WAV identity mismatch")
        with media.open("rb") as stream:
            magic = stream.read(12)
        if magic[:4] != b"RIFF" or magic[8:12] != b"WAVE":
            raise SystemExit("Audition media is not PCM WAV")
        allowed_audio.add(rel)
    for path in staging.rglob("*"):
        if path.is_symlink():
            raise SystemExit("Symlinks are not accepted in the public bundle")
        if not path.is_file():
            continue
        rel = path.relative_to(staging)
        if len(rel.parts) == 1 and rel.name in allowed_root:
            continue
        if rel.parts[0] == "downloads" and path == apk:
            continue
        if len(rel.parts) == 2 and rel.parts[0] == "assets" and rel.name in {
                "hub.png", "character.png", "mine.png", "PTSerif-Regular.ttf", "OFL.txt", "mark.svg"}:
            continue
        if rel.parts[0] == "audio-035" and rel.as_posix()[10:] in allowed_audio:
            continue
        raise SystemExit("Unexpected public bundle file: " + str(rel))
    html = (staging / "index.html").read_text(encoding="utf-8-sig")
    if "{{" in html or apk_name not in html:
        raise SystemExit("Unresolved template or APK link missing")
    if validate_only:
        print(json.dumps({"state": "validated_only", "version": version,
                          "apk_sha256": checksum, "bytes": apk.stat().st_size,
                          "audition_wav_files": len(audio_manifest)}, ensure_ascii=False))
        return
    root = Path("/var/www/escape")
    releases = root / "releases"
    releases.mkdir(parents=True, exist_ok=True, mode=0o755)
    root.chmod(0o755)
    releases.chmod(0o755)
    downloads = root / "downloads"
    downloads.mkdir(exist_ok=True, mode=0o755)
    downloads.chmod(0o755)
    public_apk = downloads / apk_name
    if public_apk.exists():
        if sha256(public_apk) != checksum or public_apk.stat().st_size != release["bytes"]:
            raise SystemExit("An existing immutable APK has different bytes")
    else:
        temporary_apk = downloads / ("." + apk_name + ".partial")
        shutil.copy2(apk, temporary_apk)
        temporary_apk.chmod(0o644)
        os.replace(temporary_apk, public_apk)
    current = root / "current"
    if current.exists() and not current.is_symlink():
        raise SystemExit("The current path is an ordinary directory; refusing to replace it")
    previous = os.readlink(current) if current.is_symlink() else None
    stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")
    target = releases / (version + "-" + checksum[:12] + "-" + stamp)
    shutil.copytree(staging, target)
    for path in [target, *target.rglob("*")]:
        path.chmod(0o755 if path.is_dir() else 0o644)
    pending = root / (".current-" + stamp)
    pending.symlink_to(target)
    os.replace(pending, current)
    try:
        desired_config = Path("/opt/escape/Caddyfile").read_bytes()
        active_config = Path("/etc/caddy/sites/escape.caddy").read_bytes()
        if desired_config != active_config:
            subprocess.run(["python3", "/opt/escape/update_proxy.py"], check=True)
    except BaseException:
        if previous is None:
            current.unlink()
        else:
            rollback = root / (".rollback-" + stamp)
            rollback.symlink_to(previous)
            os.replace(rollback, current)
        raise
    print(json.dumps({"state": "published", "url": "https://prokrust-play.ru/escape/",
                      "version": version, "apk_sha256": checksum, "bytes": apk.stat().st_size,
                      "release_directory": str(target)}, ensure_ascii=False))


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--staging", type=Path, required=True)
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args()
    publish(args.staging, args.validate_only)
