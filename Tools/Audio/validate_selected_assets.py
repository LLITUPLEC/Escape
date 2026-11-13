"""Verify runtime SFX and the historical selections. Writes only reports/manifest."""
import argparse
import json
import re

from pathlib import Path
from runtime_sfx import (AUDITIONS, CURRENT_OVERRIDES, ROOT, RUNTIME_FILES, SFX, digest,
                         load_overrides, pcm_channels, registration, verify_protected, verify_wav)


def check_slot(scene, slot, guid):
    pattern = r"\b" + slot + r": \{fileID: 8300000, guid: " + guid + r", type: 3\}"
    assert re.search(pattern, scene), slot


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--register-prepared", type=Path, metavar="DIR",
                        help="Register 20 prepared WAVs after checking the installed bytes and Unity bindings")
    args = parser.parse_args()
    current = registration(args.register_prepared) if args.register_prepared else None
    overrides = load_overrides(current)
    metadata = json.loads((AUDITIONS / "selected-metadata.json").read_text(encoding="utf-8"))
    assert metadata["selected_count"] == len(metadata["selected"]) == 19
    assert digest(AUDITIONS / "Escape-audio-choices-0.3.5.txt") == metadata["selection_sha256"]
    scene = (ROOT / "Assets/_Project/Scenes/DuelMatch3.unity").read_text(encoding="utf-8-sig")
    bank = (ROOT / metadata["resource_bank"]).read_text(encoding="utf-8")
    guids = []
    bank_references = 0
    scene_references = 0
    effects = {effect["id"]: effect for effect in metadata["selected"]}
    for key in sorted(effects.keys() | overrides.keys()):
        effect = effects.get(key)
        override = overrides.get(key)
        if effect:
            assert digest(AUDITIONS / effect["file"]) == effect["sha256"], key
        if override:
            production = verify_wav(override)
        else:
            production = ROOT / effect["production"]
            assert digest(production) == effect["sha256"] and pcm_channels(production) == 1, key
        binding = override or effect
        importer = Path(str(production) + ".meta").read_text(encoding="utf-8")
        guid = re.search(r"^guid: (\w+)$", importer, re.M)[1]
        guids.append(guid)
        if binding["cue_index"] is not None:
            assert f'  - cue: {binding["cue_index"]}\n    clip: {{fileID: 8300000, guid: {guid}, type: 3}}' in bank, key
            assert "normalize: 0" in importer and "3D: 0" in importer, key
            bank_references += 1
        slot = RUNTIME_FILES.get(key, (None, None, None))[2]
        if slot:
            check_slot(scene, slot, guid)
            scene_references += 1
    assert len(guids) == len(set(guids)) == len(effects.keys() | overrides.keys())
    assert len(re.findall(r"^  - cue:", bank, re.M)) == bank_references
    protected_count = verify_protected(metadata, overrides)
    for slot, filename in (("sfxAbilityFury", "Bloodlust_cast.ogg"), ("sfxAbilityShield", "Heroism_Cast.ogg")):
        importer = (SFX / (filename + ".meta")).read_text(encoding="utf-8")
        check_slot(scene, slot, re.search(r"^guid: (\w+)$", importer, re.M)[1])
    service = (ROOT / "Assets/_Project/Scripts/Audio/ExpeditionAudio.cs").read_text(encoding="utf-8")
    assert "private void Update(" not in service
    assert "LastCueFrames[index] == Time.frameCount" in service
    assert "_source.PlayOneShot(clip)" in service
    if current is not None:
        manifest = json.dumps(current, indent=2) + "\n"
        if not CURRENT_OVERRIDES.exists() or CURRENT_OVERRIDES.read_text(encoding="utf-8") != manifest:
            CURRENT_OVERRIDES.write_text(manifest, encoding="utf-8")
    report = {"passed": True, "historical_pcm_matches": len(effects),
              "runtime_override_pcm_matches": len(overrides) - 1, "protected_countdown_binding": True,
              "bank_references": bank_references, "existing_battle_scene_references": scene_references + 2,
              "unique_clip_guids": len(set(guids)), "protected_hashes_match": protected_count,
              "elevator_unchanged": True, "existing_battle_importers_preserved_at_install": metadata["existing_battle_importers_unchanged"],
              "service_has_no_update_polling": True, "same_cue_same_frame_coalescing": True,
              "runtime_playback_or_device_listening_performed": False}
    destination = ROOT / ".cache/qa/selected-audio/asset-report.json"
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report))


if __name__ == "__main__":
    main()
