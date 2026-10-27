"""Render original, deterministic Expedition foley. No downloaded samples or runtime synthesis.

Requires NumPy. Run from any directory with --write to replace the non-critical Match3 clips.
The existing critical-hit recording and its importer are protected by exact SHA-256 checks.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import uuid
import wave

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Assets/_Project/Audio/SFX/Match3"
REPORT = ROOT / ".cache/qa/audio"
RATE = 44100
RNG = np.random.default_rng(3012026)
CRIT_HASH = "53836329e7a752eed85a20ed25981e2e7ecf5b6fb8a155e92077bb0903a27341"
CRIT_META_HASH = "d85008ee6c323794a7936a8ed25b1c5eff2c73060afc8a51de12808a8462df0c"


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def timeline(seconds: float) -> np.ndarray:
    return np.arange(round(seconds * RATE), dtype=np.float64) / RATE


def colored_noise(seconds: float, low: float, high: float) -> np.ndarray:
    count = round(seconds * RATE)
    bins = np.fft.rfftfreq(count, 1 / RATE)
    shape = (1 - np.exp(-.5 * (bins / max(1, low)) ** 4)) * np.exp(-.5 * (bins / high) ** 4)
    signal = np.fft.irfft(np.fft.rfft(RNG.standard_normal(count)) * shape, n=count)
    return signal / max(np.std(signal), 1e-8)


def env(t: np.ndarray, decay: float, attack: float = .001) -> np.ndarray:
    return (1 - np.exp(-t / attack)) * np.exp(-t / decay)


def modes(t: np.ndarray, base: float, decay: float, metal: bool = False) -> np.ndarray:
    ratios = [1, 1.47, 2.09, 2.71, 4.13, 5.37] if metal else [1, 1.71, 2.33, 3.69]
    output = np.zeros_like(t)
    for i, ratio in enumerate(ratios):
        phase = RNG.uniform(-np.pi, np.pi)
        output += np.sin(2 * np.pi * base * ratio * t + phase) * env(t, decay / (1 + .27 * i)) / (1 + i) ** 1.2
    return output


def impact(seconds: float, weight: float = 120, metal: bool = False) -> np.ndarray:
    t = timeline(seconds)
    low = np.sin(2 * np.pi * (weight * t + weight * .014 * (1 - np.exp(-t / .018)))) * env(t, .047)
    grit = colored_noise(seconds, 180, 4200 if metal else 2700) * env(t, .022)
    click = colored_noise(seconds, 1900, 7400) * env(t, .005)
    return .62 * low + .26 * grit + .08 * click + .17 * modes(t, weight * 2.1, .15 if metal else .055, metal)


def place(target: np.ndarray, sound: np.ndarray, when: float, gain: float = 1) -> None:
    start = round(when * RATE)
    end = min(len(target), start + len(sound))
    if end > start:
        target[start:end] += sound[:end - start] * gain


def room(signal: np.ndarray, amount: float = .10) -> np.ndarray:
    output = signal.copy()
    for delay, gain in [(.031, amount), (.067, amount * .65), (.109, amount * .3)]:
        shift = round(delay * RATE)
        if shift < len(signal):
            output[shift:] += signal[:-shift] * gain
    return output


def finish(signal: np.ndarray, peak: float) -> np.ndarray:
    signal = signal - np.mean(signal)
    fade = min(round(.018 * RATE), len(signal) // 4)
    signal[:44] *= np.linspace(0, 1, min(44, len(signal)))
    signal[-fade:] *= np.linspace(1, 0, fade)
    maximum = max(np.max(np.abs(signal)), 1e-9)
    return signal * peak / maximum


def write_wave(path: Path, signal: np.ndarray) -> None:
    with wave.open(str(path), "wb") as output:
        output.setnchannels(1)
        output.setsampwidth(2)
        output.setframerate(RATE)
        output.writeframes(np.rint(signal * 32767).astype("<i2").tobytes())


def importer(path: Path) -> None:
    meta = Path(str(path) + ".meta")
    guid = None
    if meta.exists():
        guid = next((line.split(":", 1)[1].strip() for line in meta.read_text().splitlines() if line.startswith("guid:")), None)
    guid = guid or uuid.uuid4().hex
    # Original GUIDs preserve serialized scene/prefab references. ADPCM is suited to short,
    # preloaded mono effects; the asset contains its authored level, so normalization stays off.
    meta.write_text(f"""fileFormatVersion: 2
guid: {guid}
AudioImporter:
  serializedVersion: 7
  defaultSettings:
    serializedVersion: 2
    loadType: 0
    sampleRateSetting: 0
    sampleRateOverride: 44100
    compressionFormat: 2
    quality: 1
    conversionMode: 0
  platformSettingOverrides: {{}}
  forceToMono: 1
  normalize: 0
  preloadAudioData: 1
  loadInBackground: 0
  ambisonic: 0
  3D: 0
  userData: Original Expedition foley; Tools/Audio/generate_expedition_sfx.py
  assetBundleName:
  assetBundleVariant:
""", encoding="utf-8")


def render() -> dict[str, tuple[np.ndarray, str]]:
    clips = {}
    s = np.zeros(round(.34 * RATE))
    for at, base, gain in [(0, 155, .8), (.047, 212, .55), (.102, 178, .48), (.159, 260, .33), (.219, 190, .25)]:
        place(s, impact(.115, base), at, gain)
    clips["m3_cascade_fall.wav"] = (finish(room(s, .07), .44), "Dry stone landing cluster with small debris; short enough for cascades")

    s = np.zeros(round(.235 * RATE))
    for at, base in [(0, 390), (.025, 515), (.057, 650)]:
        place(s, impact(.14, base, True), at, .5)
    clips["m3_line_clear.wav"] = (finish(s, .39), "Crisp mineral breaks and restrained metal glints")

    t = timeline(.24)
    s = .85 * impact(.24, 86) + .19 * colored_noise(.24, 110, 1100) * env(t, .035)
    clips["m3_damage_hit.wav"] = (finish(room(s, .06), .60), "Low body impact with cloth/grit transient, distinct from the preserved critical hit")

    t = timeline(.49)
    sweep = colored_noise(.49, 650, 6700) * np.sin(np.pi * np.clip(t / .19, 0, 1)) ** 2 * np.exp(-t / .1)
    s = .17 * sweep
    for at, base, gain in [(0, 270, .7), (.043, 335, .56), (.083, 420, .44), (.124, 310, .35)]:
        place(s, impact(.25, base, True), at, gain)
    clips["m3_ability_cross.wav"] = (finish(room(s), .59), "Four directional cutting strikes and a short airy sweep")

    s = np.zeros(round(.49 * RATE))
    for at, base, gain in [(0, 125, 1), (.022, 180, .6), (.050, 156, .55), (.078, 225, .4)]:
        place(s, impact(.3, base), at, gain)
    t = timeline(.49)
    s += .14 * colored_noise(.49, 190, 2300) * env(t, .07)
    clips["m3_ability_square.wav"] = (finish(room(s, .12), .65), "Compact area fracture: four close stone impacts, no electronic beep")

    t = timeline(.67)
    s = .74 * colored_noise(.67, 30, 1800) * env(t, .065, .002)
    s += .55 * np.sin(2 * np.pi * (52 * t + .7 * (1 - np.exp(-t / .035)))) * env(t, .13)
    for at, base, gain in [(.062, 158, .28), (.122, 204, .20), (.201, 246, .12)]:
        place(s, impact(.16, base), at, gain)
    clips["m3_ability_petard.wav"] = (finish(room(s, .12), .70), "Contained explosive blast, bass body and falling fragments")

    t = timeline(.73)
    s = .55 * modes(t, 360, .25, True)
    s += .22 * colored_noise(.73, 500, 6200) * env(t, .024)
    s += .17 * np.sin(2 * np.pi * 144 * t) * env(t, .23, .022)
    place(s, impact(.22, 178, True), 0, .3)
    clips["m3_ability_shield.wav"] = (finish(room(s, .14), .54), "Forged shield: metal contact, ringing plate and protective low resonance")

    t = timeline(.77)
    inhale = colored_noise(.77, 130, 2450)
    s = .19 * inhale * np.sin(np.pi * np.clip(t / .27, 0, 1)) ** 2 * (t < .27)
    s += .24 * np.sin(2 * np.pi * (72 * t + 17 * t * t)) * env(t, .24, .065)
    place(s, impact(.40, 74, True), .20, .8)
    clips["Bloodlust.wav"] = (finish(room(s, .12), .59), "Fury: rising gritty breath of pressure into a heavy resonant strike")

    t = timeline(.43)
    s = .10 * colored_noise(.43, 1000, 6500) * env(t, .035)
    place(s, modes(timeline(.32), 720, .105, True), 0, .55)
    place(s, modes(timeline(.28), 960, .095, True), .085, .42)
    clips["m3_extra_turn.wav"] = (finish(room(s, .07), .44), "Two ascending mineral/steel chimes for an earned extra turn")

    s = np.zeros(round(.36 * RATE))
    place(s, impact(.14, 330, True), 0, .55)
    place(s, impact(.18, 230, True), .14, .8)
    clips["m3_timer_end.wav"] = (finish(s, .43), "Two dry mechanical stop clicks; readable without an alarm loop")

    s = np.zeros(round(1.36 * RATE))
    for at, hz, level in [(0, 261.63, .42), (.12, 329.63, .40), (.26, 392.0, .38), (.44, 523.25, .24)]:
        tt = timeline(1.36 - at)
        tone = modes(tt, hz, .33, True) + .08 * np.sin(2 * np.pi * hz * tt) * env(tt, .4, .025)
        place(s, tone, at, level)
    place(s, impact(.25, 115), 0, .12)
    clips["m3_victory.wav"] = (finish(room(s, .1), .52), "Warm short forged-bell resolution for victory")

    s = np.zeros(round(1.14 * RATE))
    for at, hz, level in [(0, 196, .48), (.18, 146.83, .43), (.37, 110, .36)]:
        tt = timeline(1.14 - at)
        place(s, modes(tt, hz, .29, True), at, level)
    clips["m3_defeat.wav"] = (finish(room(s, .08), .45), "Muted descending metal resonance for defeat")
    return clips


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--write", action="store_true", help="Write the new production non-critical clips")
    options = parser.parse_args()
    crit = OUT / "m3_damage_crit.wav"
    crit_meta = OUT / "m3_damage_crit.wav.meta"
    assert digest(crit) == CRIT_HASH and digest(crit_meta) == CRIT_META_HASH, "Critical sound/importer changed; stop"
    clips = render()
    report = {"sample_rate": RATE, "channels": 1, "origin": "Original deterministic foley/modal synthesis; no external recordings",
              "critical_audio_sha256": CRIT_HASH, "critical_meta_sha256": CRIT_META_HASH, "critical_unchanged": True, "clips": []}
    for name, (signal, description) in clips.items():
        assert np.isfinite(signal).all() and np.max(np.abs(signal)) < .9
        assert abs(signal[0]) < 1e-6 and abs(signal[-1]) < 1e-6
        path = OUT / name
        previous = digest(path) if path.exists() else None
        if options.write:
            write_wave(path, signal)
            importer(path)
        report["clips"].append({"file": name, "description": description, "seconds": round(len(signal) / RATE, 3),
            "peak_dbfs": round(float(20 * np.log10(np.max(np.abs(signal)))), 2),
            "rms_dbfs": round(float(20 * np.log10(np.sqrt(np.mean(signal ** 2)))), 2),
            "clipped_samples": int(np.count_nonzero(np.abs(signal) >= 1)), "old_sha256": previous,
            "sha256": digest(path) if options.write else None})
    if options.write:
        REPORT.mkdir(parents=True, exist_ok=True)
        order = ["m3_cascade_fall.wav", "m3_damage_hit.wav", "m3_ability_shield.wav", "m3_ability_cross.wav", "m3_ability_square.wav", "m3_ability_petard.wav", "Bloodlust.wav", "m3_extra_turn.wav", "m3_timer_end.wav", "m3_victory.wav", "m3_defeat.wav"]
        gaps = np.zeros(round(.16 * RATE))
        preview = np.concatenate([np.concatenate([clips[name][0], gaps]) for name in order])
        write_wave(REPORT / "sound-preview.wav", preview)
        assert digest(crit) == CRIT_HASH and digest(crit_meta) == CRIT_META_HASH
        (REPORT / "audio-assets.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({"written": options.write, "new_clips": len(clips), "critical_unchanged": True,
                      "total_pcm16_bytes": sum(len(s[0]) * 2 for s in clips.values())}))


if __name__ == "__main__":
    main()
