"""Active SFX bindings shared by the Escape audio installers and validators.

Public source checkouts use the pre-ElevenLabs clips. The ignored ElevenLabs
manifest selects the licensed audio installed locally for official APK builds.
"""
from pathlib import Path
import hashlib
import json
import wave

ROOT = Path(__file__).resolve().parents[2]
SFX = ROOT / "Assets/_Project/Audio/SFX"
AUDITIONS = ROOT / "Design/Audio"
PREVIOUS_OVERRIDES = Path(__file__).with_name("battle_clip_overrides.json")
CURRENT_OVERRIDES = Path(__file__).with_name("elevenlabs_runtime_overrides.json")

# Positions are serialized values of ExpeditionCue. Critical hits are routed
# directly by DuelMatch3Manager and have a scene slot only.
RUNTIME_FILES = {
    "line_clear": (0, "Match3/m3_line_clear.wav", "sfxLineClear"),
    "cascade_fall": (1, "Match3/fall.wav", "sfxCascadeFall"),
    "ability_square": (2, "Match3/m3_ability_square.wav", "sfxAbilitySquare"),
    "ability_cross": (3, "Match3/m3_ability_cross.wav", "sfxAbilityCross"),
    "ability_petard": (4, "Match3/m3_ability_petard.wav", "sfxAbilityPetard"),
    "damage_hit": (5, "Match3/m3_damage_hit.wav", "sfxDamageHit"),
    "extra_turn": (6, "Match3/5+.wav", "sfxExtraTurn"),
    "timer_end": (7, "Match3/m3_timer_end.wav", "sfxTimerEnd"),
    "victory": (8, "Match3/m3_victory.wav", "sfxVictory"),
    "defeat": (9, "Match3/m3_defeat.wav", "sfxDefeat"),
    "ui_click": (10, "Expedition/ui_click.wav", None),
    "ui_back": (11, "Expedition/ui_back.wav", None),
    "ui_blocked": (12, "Expedition/ui_blocked.wav", None),
    "queue_enter": (13, "Expedition/queue_enter.wav", None),
    "match_ready": (14, "Match3/start_battle_2.wav", None),
    "craft_start": (15, "Expedition/craft_start.wav", None),
    "craft_ready": (16, "Expedition/craft_ready.wav", None),
    "reward_claim": (17, "Expedition/reward_claim.wav", None),
    "barrier_unlock": (18, "Expedition/barrier_unlock.wav", None),
    "damage_crit": (None, "Match3/m3_damage_crit.wav", "sfxDamageCrit"),
}
ARCHIVE_KEYS = tuple(key for key, (index, _, _) in RUNTIME_FILES.items() if index is not None)
TICK_META_SHA256 = "874a51fed85fdaf47d5d4bc7745995ddf6593ea678c60ab32bdbb47852210a2c"


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def pcm_channels(path):
    with wave.open(str(path), "rb") as clip:
        channels = clip.getnchannels()
        if (channels not in (1, 2) or clip.getsampwidth() != 2 or clip.getframerate() != 48000
                or clip.getcomptype() != "NONE" or clip.getnframes() == 0):
            raise ValueError(f"Expected a nonempty 48 kHz PCM16 WAV: {path}")
    return channels


def load_overrides(current=None):
    previous = json.loads(PREVIOUS_OVERRIDES.read_text(encoding="utf-8"))
    overrides = {key: {**row, "file": "Match3/" + row["file"], "channels": 2}
                 for key, row in previous.items()}
    if current is None and CURRENT_OVERRIDES.exists():
        current = json.loads(CURRENT_OVERRIDES.read_text(encoding="utf-8"))
    if current is None:
        return overrides
    if set(current) != set(RUNTIME_FILES):
        raise ValueError("The ElevenLabs runtime manifest must contain the 20 replaceable cues")
    for key, (_, filename, _) in RUNTIME_FILES.items():
        row = current[key]
        if set(row) != {"channels", "sha256"} or type(row["channels"]) is not int or row["channels"] not in (1, 2):
            raise ValueError(f"Invalid ElevenLabs manifest entry: {key}")
        overrides[key] = {**row, "file": filename, "cue_index": RUNTIME_FILES[key][0]}
    return overrides


def registration(prepared):
    expected = {Path(filename).name for _, filename, _ in RUNTIME_FILES.values()}
    if {path.name for path in prepared.glob("*.wav")} != expected:
        raise ValueError("The preparation folder must contain exactly the 20 replaceable WAVs")
    current = {}
    for key, (_, filename, _) in RUNTIME_FILES.items():
        source = prepared / Path(filename).name
        sha256 = digest(source)
        if sha256 != digest(SFX / filename):
            raise ValueError(f"Installed clip differs from the prepared take: {filename}")
        current[key] = {"channels": pcm_channels(source), "sha256": sha256}
    return current


def verify_wav(row):
    path = SFX / row["file"]
    if digest(path) != row["sha256"] or pcm_channels(path) != row["channels"]:
        raise ValueError(f"Runtime WAV differs from its manifest: {path}")
    return path


def verify_protected(metadata, overrides):
    critical = (SFX / RUNTIME_FILES["damage_crit"][1]).relative_to(ROOT).as_posix()
    protected = dict(metadata["protected_sha256"])
    if "damage_crit" in overrides:
        del protected[critical]
    protected["Assets/_Project/Audio/SFX/Match3/tik_timer.wav.meta"] = TICK_META_SHA256
    for path, expected in protected.items():
        if digest(ROOT / path) != expected:
            raise ValueError(f"Protected audio or importer changed: {path}")
    verify_wav(overrides["tournament_countdown"])
    return len(protected) + 1
