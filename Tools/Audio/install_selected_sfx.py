"""Install historical A/B choices or rebuild their bank with active SFX overrides.

Requires --write for changes. The ElevenLabs clips and original archive are
verified in place when the current runtime manifest exists.
"""
from __future__ import annotations
import argparse
import json
from pathlib import Path
import re
import shutil
import uuid
import wave

from runtime_sfx import (ARCHIVE_KEYS, AUDITIONS, CURRENT_OVERRIDES, ROOT, SFX, digest,
                         load_overrides, verify_protected, verify_wav)

CHOICES = ROOT / "Design/Audio/Escape-audio-choices-0.3.5.txt"


def guid(path: Path):
    meta = Path(str(path) + ".meta")
    return re.search(r"^guid: (\w+)$", meta.read_text(encoding="utf-8"), re.M)[1]


def new_meta(path: Path, body: str = ""):
    meta = Path(str(path) + ".meta")
    if not meta.exists():
        key = uuid.uuid5(uuid.NAMESPACE_URL, "escape/" + path.relative_to(ROOT).as_posix()).hex
        meta.write_text(f"fileFormatVersion: 2\nguid: {key}\n" + body, encoding="utf-8")


def folder(path: Path):
    path.mkdir(parents=True, exist_ok=True)
    new_meta(path, "folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--write", action="store_true")
    args = parser.parse_args()
    raw = CHOICES.read_text(encoding="utf-8-sig")
    choices = {key: value for value, key in re.findall(r": (A|B|не выбран) \[([a-z_]+)\]", raw)}
    assert len(choices) == 20 and choices.get("mine_elevator") == "не выбран", "Expected exactly 19 choices and unselected lift"
    assert set(choices) == set(ARCHIVE_KEYS) | {"mine_elevator"}
    assert all(choices[key] in ("A", "B") for key in ARCHIVE_KEYS)
    selected_metadata = json.loads((AUDITIONS / "selected-metadata.json").read_text(encoding="utf-8"))
    if (AUDITIONS / "metadata.json").exists():
        metadata = json.loads((AUDITIONS / "metadata.json").read_text(encoding="utf-8"))
        catalogue = {effect["id"]: effect for effect in metadata["effects"]}
    else:
        catalogue = {effect["id"]: {"title": effect["title"], "variants": {effect["choice"]: effect}} for effect in selected_metadata["selected"]}
    overrides = load_overrides()
    protected = selected_metadata["protected_sha256"]
    verify_protected(selected_metadata, overrides)
    for row in overrides.values():
        verify_wav(row)
    battle_importers = {p.relative_to(ROOT).as_posix(): digest(p) for p in (SFX / "Match3").glob("*.meta")}
    update_archive = args.write and not CURRENT_OVERRIDES.exists()
    entries = []
    for index, key in enumerate(ARCHIVE_KEYS):
        variant = catalogue[key]["variants"][choices[key]]
        source = AUDITIONS / variant["file"]
        archive_file = "selected/" + key + ".wav"
        assert digest(source) == variant["sha256"], source
        target = SFX / ("Match3/m3_" + key + ".wav" if index < 10 else "Expedition/" + key + ".wav")
        with wave.open(str(source), "rb") as file:
            assert file.getparams()[:3] == (1, 2, 48000)
        entries.append({"id": key, "cue_index": index, "choice": choices[key], "title": catalogue[key]["title"],
                        "audition_source": source.relative_to(ROOT).as_posix(), "production": target.relative_to(ROOT).as_posix(),
                        "file": "selected/" + key + ".wav", "sha256": variant["sha256"],
                        "duration_seconds": variant["duration_seconds"], "peak_dbfs": variant["peak_dbfs"],
                        "rms_dbfs": variant["rms_dbfs"], "source": variant["source"]})
        if update_archive:
            (AUDITIONS / "selected").mkdir(exist_ok=True)
            destination = AUDITIONS / archive_file
            if source.resolve() != destination.resolve():
                shutil.copyfile(source, destination)
        if args.write and key not in overrides:
            if index >= 10:
                folder(target.parent)
            shutil.copyfile(source, target)
            new_meta(target, "AudioImporter:\n  serializedVersion: 7\n  defaultSettings:\n    serializedVersion: 2\n    loadType: 0\n    sampleRateSetting: 0\n    sampleRateOverride: 48000\n    compressionFormat: 2\n    quality: 1\n    conversionMode: 0\n  platformSettingOverrides: {}\n  forceToMono: 1\n  normalize: 0\n  preloadAudioData: 1\n  loadInBackground: 0\n  ambisonic: 0\n  3D: 0\n  userData: Selected original Expedition audition; explicit user choices\n  assetBundleName: \n  assetBundleVariant: \n")
    if args.write:
        folder(ROOT / "Assets/_Project/Scripts/Audio")
        for name in ("ExpeditionAudio.cs", "ExpeditionAudioBank.cs"):
            new_meta(ROOT / "Assets/_Project/Scripts/Audio" / name)
        bank_folder = ROOT / "Assets/_Project/Resources/Expedition/Audio"
        folder(bank_folder)
        bank = bank_folder / "SelectedSfx.asset"
        script_guid = guid(ROOT / "Assets/_Project/Scripts/Audio/ExpeditionAudioBank.cs")
        bank_text = "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!114 &11400000\nMonoBehaviour:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n  m_GameObject: {fileID: 0}\n  m_Enabled: 1\n  m_EditorHideFlags: 0\n  m_Script: {fileID: 11500000, guid: " + script_guid + ", type: 3}\n  m_Name: SelectedSfx\n  m_EditorClassIdentifier: \n  entries:\n"
        for entry in entries:
            replacement = overrides.get(entry["id"])
            production = SFX / replacement["file"] if replacement else ROOT / entry["production"]
            bank_text += f'  - cue: {entry["cue_index"]}\n    clip: {{fileID: 8300000, guid: {guid(production)}, type: 3}}\n'
        for key, replacement in overrides.items():
            if key in ARCHIVE_KEYS or replacement["cue_index"] is None:
                continue
            production = SFX / replacement["file"]
            bank_text += f'  - cue: {replacement["cue_index"]}\n    clip: {{fileID: 8300000, guid: {guid(production)}, type: 3}}\n'
        if not bank.exists() or bank.read_text(encoding="utf-8") != bank_text:
            bank.write_text(bank_text, encoding="utf-8")
        new_meta(bank, "NativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 11400000\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n")
    verify_protected(selected_metadata, overrides)
    assert all(digest(ROOT / relative) == previous for relative, previous in battle_importers.items())
    report = {"audition_version": "0.3.5", "status": "selected_assets_installed" if args.write else "dry_run",
              "selection_sha256": digest(CHOICES), "selected_count": len(entries), "selected": entries,
              "unselected": [{"id": "mine_elevator", "action": "existing audio and behavior unchanged"}],
              "protected_sha256": protected, "protected_unchanged": True, "existing_battle_importers_unchanged": True,
              "resource_bank": "Assets/_Project/Resources/Expedition/Audio/SelectedSfx.asset"}
    if update_archive:
        (AUDITIONS / "selected-metadata.json").write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"written": args.write, "selected_count": len(entries), "protected_unchanged": True,
                      "existing_battle_importers_unchanged": True, "choices": {e["id"]: e["choice"] for e in entries}}, ensure_ascii=False))


if __name__ == "__main__":
    main()
