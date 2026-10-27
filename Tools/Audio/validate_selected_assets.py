"""Verify exact selected PCM/GUID/bank references; read-only with respect to Assets."""
from pathlib import Path
import hashlib
import json
import re
import wave

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Design/Audio"


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    metadata = json.loads((OUT / "selected-metadata.json").read_text(encoding="utf-8"))
    assert metadata["selected_count"] == len(metadata["selected"]) == 19
    assert digest(ROOT / "Design/Audio/Escape-audio-choices-0.3.5.txt") == metadata["selection_sha256"]
    scene = (ROOT / "Assets/_Project/Scenes/DuelMatch3.unity").read_text(encoding="utf-8-sig")
    bank = (ROOT / metadata["resource_bank"]).read_text(encoding="utf-8")
    slots = {"line_clear": "sfxLineClear", "cascade_fall": "sfxCascadeFall", "ability_square": "sfxAbilitySquare", "ability_cross": "sfxAbilityCross", "ability_petard": "sfxAbilityPetard", "damage_hit": "sfxDamageHit", "extra_turn": "sfxExtraTurn", "timer_end": "sfxTimerEnd", "victory": "sfxVictory", "defeat": "sfxDefeat"}
    guids = []
    for effect in metadata["selected"]:
        production = ROOT / effect["production"]
        selected = OUT / effect["file"]
        assert digest(production) == digest(selected) == effect["sha256"], effect["id"]
        with wave.open(str(production), "rb") as file:
            assert file.getparams()[:3] == (1, 2, 48000)
        importer = Path(str(production) + ".meta").read_text(encoding="utf-8")
        guid = re.search(r"^guid: (\w+)$", importer, re.M)[1]
        guids.append(guid)
        assert f'  - cue: {effect["cue_index"]}\n    clip: {{fileID: 8300000, guid: {guid}, type: 3}}' in bank
        assert "normalize: 0" in importer and "3D: 0" in importer
        if effect["id"] in slots:
            assert re.search(r"\b" + slots[effect["id"]] + r": \{fileID: 8300000, guid: " + guid + r", type: 3\}", scene), effect["id"]
    assert len(guids) == len(set(guids)) == 19
    assert len(re.findall(r"^  - cue:", bank, re.M)) == 19
    for path, expected in metadata["protected_sha256"].items():
        assert digest(ROOT / path) == expected, path
    service = (ROOT / "Assets/_Project/Scripts/Audio/ExpeditionAudio.cs").read_text(encoding="utf-8")
    assert "private void Update(" not in service
    assert "LastCueFrames[index] == Time.frameCount" in service
    assert "_source.PlayOneShot(clip)" in service
    report = {"passed": True, "selected_pcm_matches": 19, "bank_references": 19, "existing_battle_scene_references": 10,
              "unique_clip_guids": 19, "protected_hashes_match": len(metadata["protected_sha256"]),
              "elevator_unchanged": True, "existing_battle_importers_preserved_at_install": metadata["existing_battle_importers_unchanged"],
              "service_has_no_update_polling": True, "same_cue_same_frame_coalescing": True,
              "runtime_playback_or_device_listening_performed": False}
    destination = ROOT / ".utmp/expedition/validation-audio-selected/asset-report.json"
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report))


if __name__ == "__main__":
    main()
