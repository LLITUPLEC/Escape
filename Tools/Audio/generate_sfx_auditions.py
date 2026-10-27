"""Render two original SFX directions for audition only, never into Unity Assets.

Requires NumPy. No recordings, network, runtime hooks, or production asset changes.
Run: python Tools/Audio/generate_sfx_auditions.py
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path
import re
import wave

import numpy as np


ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Design/Audio"
RATE = 48000
SOURCE = "Original deterministic procedural PCM synthesis; no recorded or stock samples"
STYLES = {
    "A": {"title": "Минерал и металл", "description": "Сдержанные каменные сколы, механические защёлки, сухая латунь; короткая комната."},
    "B": {"title": "Резонанс и пар", "description": "Инерционные пластины, пневматика, воздушные выбросы и минеральный резонанс; чуть более пространственный хвост."},
}
SHOWCASE_KEYS = ["ui_click", "cascade_fall", "ability_cross", "ability_petard", "craft_start", "victory"]
# Ten replaceable battle slots, followed by ten proposed functional UI cues.
# The latter have real gameplay events but currently no sound binding.
EFFECTS = [
    ("line_clear", "Ряд камней", "battle", .31, "sfxLineClear", "DuelMatch3Manager.ApplyRemoteBoardSync: animSteps.phase == 1", "Три хрупких каменных скола / воздушная кристаллическая россыпь"),
    ("cascade_fall", "Каскад: падение камней", "battle", .43, "sfxCascadeFall", "DuelMatch3Manager.ApplyRemoteBoardSync: animSteps.phase == 2", "Мелкая падающая галька / полые минеральные касания"),
    ("ability_square", "Способность: квадрат", "battle", .61, "sfxAbilitySquare", "DuelMatch3Manager.ApplyRemoteBoardSync: actionType == 3", "Четыре плотных скола / сжатие резонансной плиты"),
    ("ability_cross", "Способность: крест", "battle", .57, "sfxAbilityCross", "DuelMatch3Manager.ApplyRemoteBoardSync: actionType == 2", "Пересекающиеся металлические срезы / два паровых выброса"),
    ("ability_petard", "Способность: петарда", "battle", .88, "sfxAbilityPetard", "DuelMatch3Manager.ApplyRemoteBoardSync: actionType == 4", "Компактный пороховой удар с осколками / разряд накопленного давления"),
    ("damage_hit", "Обычное попадание", "battle", .34, "sfxDamageHit", "DuelMatch3Manager: HP decrease and !critTriggered", "Глухой удар по ткани и пластине / короткий силовой импульс"),
    ("extra_turn", "Дополнительный ход", "battle", .66, "sfxExtraTurn", "DuelMatch3Manager: msg.extraTurn", "Защёлка с минеральным отблеском / отпирающийся паровой клапан"),
    ("timer_end", "Время хода истекло", "battle", .42, "sfxTimerEnd", "DuelMatch3Manager: turn deadline expires", "Два механических стопора / остановка пневматического затвора"),
    ("victory", "Победа", "battle", 1.42, "sfxVictory", "DuelMatch3Manager: won; current draw also uses this slot", "Открытие тяжёлого замка и тёплый металл / высвобождение яркого резонанса"),
    ("defeat", "Поражение", "battle", 1.22, "sfxDefeat", "DuelMatch3Manager: !won && !isDraw", "Оседающий затвор и пыль / затухание давления и гул пластины"),
    ("ui_click", "Нажатие / выбор", "ui", .15, None, "ExpeditionUi.Button / existing Unity Button.onClick", "Маленькая латунная защёлка / керамический переключатель с воздухом"),
    ("ui_back", "Назад / отмена", "ui", .21, None, "Match3SearchingPanel.HandleCancel; modal close; navigation back", "Мягкий обратный ход механизма / короткий обратный клапан"),
    ("ui_blocked", "Действие недоступно / ошибка", "ui", .34, None, "Workshop resource shortfall or rejected response; Mine insufficient resources", "Двойной упор защёлки / глухой сброс заблокированного клапана"),
    ("queue_enter", "Встать в очередь", "functional", .63, None, "DuelMatch3Manager.FindMatchAsync: ticket accepted, not button intent", "Завод пружины и фиксация / набор давления и поворот ротора"),
    ("match_ready", "Матч подтверждён", "functional", .81, None, "DuelMatch3Manager: successful matched/join confirmation, never local progress", "Сходятся два металлических замка / две резонансные створки"),
    ("craft_start", "Крафт начат / ускорен", "functional", .78, None, "WorkshopSceneController.RunCraftRequestAsync: successful Start or Rush response", "Два удара кузнечного молотка / ковка с паровым выбросом"),
    ("craft_ready", "Предмет готов", "functional", .80, None, "WorkshopSceneController: Busy -> Ready once, not each timer refresh", "Остывающая деталь и лёгкий звон / кристаллизация и выпуск пара"),
    ("reward_claim", "Забрать предмет / награду", "functional", .68, None, "WorkshopSceneController.ClaimAtSlot: successful response; confirmed reward claim", "Фурнитура, несколько монет и ткань / резонанс артефакта и мягкий щелчок"),
    ("mine_elevator", "Поездка лифта", "functional", 1.48, None, "ExpeditionMinePresentation: accepted tap travel; one-shot, not manual scroll", "Трос, редуктор и тормоз / гидравлика, шестерни и запор"),
    ("barrier_unlock", "Открытие барьера / подтверждение призыва", "functional", 1.22, None, "MineSceneController: successful BarrierUnlock or Summon response", "Размыкание цепей и каменная осыпь / цепь, пар и угасающие искры"),
]


def times(seconds: float) -> np.ndarray:
    return np.arange(round(seconds * RATE), dtype=np.float64) / RATE


def envelope(t: np.ndarray, decay: float, attack: float = .0015) -> np.ndarray:
    return -np.expm1(-t / attack) * np.exp(-t / decay)


class Renderer:
    def __init__(self, key: str, style: str, seed: int):
        hashed = hashlib.sha256(f"{seed}:{style}:{key}".encode()).digest()
        self.rng = np.random.default_rng(int.from_bytes(hashed[:8], "little"))
        self.b = style == "B"

    def noise(self, seconds: float, low: float = 120, high: float = 6200, tilt: float = -.35) -> np.ndarray:
        n = round(seconds * RATE)
        freq = np.fft.rfftfreq(n, 1 / RATE)
        # Smooth spectral edges prevent brick-wall ringing and brittle white-noise hiss.
        shape = -np.expm1(-np.power(freq / max(low, 1), 4)) * np.exp(-np.power(freq / high, 4))
        shape *= np.power(np.maximum(freq, 60) / 600, tilt)
        signal = np.fft.irfft(np.fft.rfft(self.rng.standard_normal(n)) * shape, n=n)
        return signal / max(np.std(signal), 1e-9)

    def modes(self, seconds: float, base: float, decay: float = .13, metal: bool = False) -> np.ndarray:
        t = times(seconds)
        ratios = [1, 1.43, 1.97, 2.61, 3.83, 5.19, 6.37] if metal else [1, 1.68, 2.37, 3.54, 4.73]
        if self.b:
            ratios = [1, 1.19, 1.77, 2.41, 3.17, 4.69, 6.13]
        result = np.zeros_like(t)
        for i, ratio in enumerate(ratios):
            frequency = base * ratio * self.rng.uniform(.987, 1.013)
            phase = self.rng.uniform(-.7, .7)
            # Slight initial strain/relaxation gives an inharmonic physical object, not a beep.
            bend = .0028 * frequency * (-np.expm1(-t / .025))
            result += np.sin(2 * np.pi * (frequency * t + bend) + phase) * envelope(t, decay / (1 + .19 * i)) / (1 + i) ** 1.12
        return result

    def hit(self, seconds: float, base: float = 145, metal: bool = False, weight: float = 1) -> np.ndarray:
        t = times(seconds)
        membrane = np.sin(2 * np.pi * (base * .60 * t + .29 * (-np.expm1(-t / .018)))) * envelope(t, .037 if metal else .049)
        dust = self.noise(seconds, 180 if metal else 90, 5100 if metal else 3300) * envelope(t, .018 if metal else .028)
        grain = self.noise(seconds, 1300, 6700, -.1) * envelope(t, .0038)
        ring = self.modes(seconds, base * (2.0 if metal else 1.25), (.18 if self.b else .09) if metal else .061, metal)
        return .46 * membrane * weight + .33 * dust + .075 * grain + (.27 if metal else .11) * ring

    def air(self, seconds: float, attack: float, decay: float, low: float = 420, high: float = 6500) -> np.ndarray:
        t = times(seconds)
        roughness = .76 + .13 * np.sin(2 * np.pi * 43.7 * t) + .11 * np.sin(2 * np.pi * 71.3 * t + .7)
        return self.noise(seconds, low, high, -.1) * envelope(t, decay, attack) * roughness

    @staticmethod
    def place(output: np.ndarray, sound: np.ndarray, when: float, gain: float = 1):
        start = round(when * RATE)
        count = min(len(sound), len(output) - start)
        if count > 0:
            output[start:start + count] += sound[:count] * gain

    def room(self, dry: np.ndarray, wet: float = .07) -> np.ndarray:
        result = dry.copy()
        frequency = np.fft.rfftfreq(len(dry), 1 / RATE)
        soft = np.fft.irfft(np.fft.rfft(dry) * np.exp(-np.power(frequency / 3900, 4)), n=len(dry))
        delays = [(.013, .72), (.031, .52), (.047, -.24), (.073, .24), (.107, -.14)]
        if self.b:
            wet *= 1.65
            delays += [(.151, .10), (.193, -.07)]
        for delay, gain in delays:
            n = round(delay * RATE)
            if n < len(dry):
                result[n:] += soft[:-n] * wet * gain
        return result

    def render(self, key: str, seconds: float) -> np.ndarray:
        s = np.zeros(round(seconds * RATE))
        p = lambda x, at=0, gain=1: self.place(s, x, at, gain)
        b = self.b
        if key == "line_clear":
            for at, hz, g in [(0, 380, .8), (.031, 570, .55), (.074, 830, .32)]:
                p(self.hit(.18, hz * (1.13 if b else 1), b, .45), at, g)
            p(self.air(.23, .004, .027, 1500, 7200), .01, .11 if b else .06)
        elif key == "cascade_fall":
            for at, hz, g in [(0, 180, .78), (.057, 237, .66), (.113, 159, .54), (.181, 306, .42), (.244, 224, .28), (.291, 354, .14)]:
                p(self.hit(.13, hz * (.91 if b else 1), b, .7), at, g)
            p(self.air(.35, .01, .12, 320, 2800), .02, .07 if b else .025)
        elif key == "ability_square":
            for at, hz, g in [(0, 115, 1), (.022 if not b else .037, 166, .75), (.062 if not b else .071, 141, .60), (.10, 215, .44)]:
                p(self.hit(.31, hz, b, 1.2), at, g)
            p(self.air(.45, .005 if not b else .018, .095, 75, 2400), 0, .26 if b else .16)
            if b:
                p(self.modes(.49, 249, .15, True), .034, .18)
        elif key == "ability_cross":
            for at, hz, g in [(0, 300, .83), (.073, 463, .66)]:
                p(self.air(.31, .016, .048, 550, 6200), at, .28 if b else .20)
                p(self.hit(.25, hz, True, .7), at + .023, g)
            p(self.hit(.24, 226, True, .5), .176, .26)
        elif key == "ability_petard":
            p(self.air(.18, .008, .048, 1700, 6800), 0, .14 if not b else .28)
            p(self.hit(.55, 82 if not b else 105, False, 2.4), .067 if not b else .115, 1.1)
            p(self.air(.61, .002, .075 if not b else .11, 35, 2600), .065 if not b else .11, .65)
            for at, hz, g in [(.16, 192, .27), (.231, 274, .19), (.307, 156, .15), (.401, 343, .09)]:
                p(self.hit(.18, hz, b, .5), at, g)
        elif key == "damage_hit":
            p(self.hit(.30, 86 if not b else 112, False, 1.65), 0, .90)
            p(self.noise(.25, 120, 1600, -.65) * envelope(times(.25), .042), .007, .29)
            if b:
                p(self.modes(.29, 207, .063, True), .006, .22)
        elif key == "extra_turn":
            p(self.hit(.20, 274, True, .35), 0, .54)
            p(self.hit(.23, 402, True, .3), .078, .44)
            p(self.modes(.47, 637 if b else 508, .125, True), .135, .23)
            if b:
                p(self.air(.38, .018, .075, 850, 6000), .025, .20)
        elif key == "timer_end":
            p(self.hit(.13, 315, True, .6), 0, .65)
            p(self.hit(.23, 160 if b else 201, False, 1.1), .136, .9)
            p(self.air(.25, .002, .040, 180, 2300), .12, .19 if b else .04)
        elif key == "victory":
            p(self.air(.58, .075, .15, 250, 5300), 0, .20 if not b else .38)
            p(self.hit(.43, 142, True, .85), .12, .63)
            p(self.hit(.34, 243, True, .6), .269, .45)
            p(self.modes(.98, 410 if b else 338, .24 if b else .19, True), .31, .39)
            for at, hz, g in [(.44, 694, .12), (.497, 1037, .07), (.571, 1269, .035)]:
                p(self.modes(.50, hz, .095, True), at, g)
        elif key == "defeat":
            p(self.air(.72, .012, .16, 90, 2400), 0, .27 if b else .13)
            p(self.hit(.47, 149, True, 1.2), .023, .63)
            p(self.hit(.53, 82, False, 1.5), .177, .54)
            p(self.modes(.86, 141 if not b else 179, .20, True), .223, .25)
            p(self.air(.64, .03, .15, 120, 3700), .27, .14)
        elif key == "ui_click":
            p(self.hit(.10, 540 if not b else 685, True, .2), 0, .45)
            p(self.hit(.085, 301, False, .3), .019, .31)
            if b:
                p(self.air(.10, .001, .013, 1400, 6500), .007, .17)
        elif key == "ui_back":
            p(self.air(.12, .003, .024, 700, 3100), 0, .15 if not b else .31)
            p(self.hit(.14, 280 if not b else 343, True, .3), .024, .42)
        elif key == "ui_blocked":
            p(self.hit(.13, 171, False, .65), 0, .64)
            p(self.hit(.18, 143, True, .6), .087, .54)
            p(self.air(.22, .003, .038, 180, 1800), .072, .18 if b else .045)
        elif key == "queue_enter":
            for i, at in enumerate([.012, .051, .099, .157, .224]):
                p(self.hit(.12, 310 + i * 33, True, .3), at, .24 + .025 * i)
            p(self.air(.39, .045, .13, 270, 4300), 0, .24 if b else .10)
            p(self.hit(.23, 190, True, .6), .30, .57)
        elif key == "match_ready":
            p(self.air(.46, .04, .13, 430, 5300), 0, .24 if b else .10)
            p(self.hit(.29, 276, True, .55), .04, .68)
            p(self.hit(.35, 391, True, .5), .142, .51)
            p(self.modes(.50, 670 if b else 557, .14, True), .20, .21)
        elif key == "craft_start":
            p(self.hit(.29, 118, True, 1.3), .012, .95)
            p(self.hit(.32, 163, True, 1.1), .17, .77)
            p(self.air(.46, .003, .12, 600, 4900), .20, .28 if b else .15)
            if b:
                p(self.modes(.43, 296, .14, True), .24, .19)
        elif key == "craft_ready":
            p(self.air(.46, .008, .105, 1100, 6100), 0, .26 if b else .15)
            p(self.hit(.30, 403, True, .35), .118, .53)
            p(self.modes(.48, 821 if b else 643, .14, True), .167, .25)
            for at, hz in [(.245, 1189), (.292, 1671)]:
                p(self.hit(.18, hz, True, .1), at, .045)
        elif key == "reward_claim":
            p(self.air(.36, .004, .045, 240, 2400), 0, .19)
            for at, hz, g in [(.024, 490, .50), (.074, 779, .38), (.126, 1113, .23), (.191, 653, .12)]:
                p(self.hit(.28, hz, True, .25), at, g)
            if b:
                p(self.modes(.43, 462, .14, True), .16, .18)
        elif key == "mine_elevator":
            p(self.hit(.26, 114, True, 1.0), 0, .70)
            p(self.air(1.15, .08, .37, 110, 2100), .06, .29 if b else .17)
            for i, at in enumerate([.13, .256, .387, .523, .671, .824]):
                p(self.hit(.14, 183 + 13 * (i % 3), True, .45), at, .17 + .035 * math.sin(i))
            p(self.hit(.29, 121, True, 1.05), 1.02, .74)
            p(self.air(.24, .002, .045, 500, 4000), 1.06, .22 if b else .06)
        elif key == "barrier_unlock":
            for at, hz, g in [(0, 174, .71), (.105, 256, .64), (.168, 391, .40), (.226, 305, .31), (.281, 541, .19)]:
                p(self.hit(.24, hz, True, .6), at, g)
            p(self.air(.76, .024, .22, 55, 2300), .12, .31 if b else .23)
            p(self.hit(.45, 87, False, 1.3), .271, .43)
            if b:
                p(self.modes(.63, 442, .13, True), .36, .17)
        else:
            raise ValueError(key)
        return self.room(s, .035 if key.startswith("ui_") else .085)


def active_rms(signal: np.ndarray) -> float:
    block = max(1, RATE // 100)
    trim = signal[:len(signal) // block * block]
    if len(trim) == 0:
        return float(np.sqrt(np.mean(signal ** 2)))
    power = np.mean(trim.reshape(-1, block) ** 2, axis=1)
    active = power > max(power.max() * 10 ** (-32 / 10), 1e-12)
    return float(np.sqrt(np.mean(power[active])))


def db(value: float) -> float:
    return round(float(20 * np.log10(max(value, 1e-12))), 3)


def finish(signal: np.ndarray, category: str) -> np.ndarray:
    freq = np.fft.rfftfreq(len(signal), 1 / RATE)
    filtering = -np.expm1(-np.power(freq / 38, 4)) * np.exp(-np.power(freq / 8200, 6))
    signal = np.fft.irfft(np.fft.rfft(signal) * filtering, n=len(signal))
    # Gentle crest control retains transients without hard clipping.
    signal /= max(active_rms(signal), 1e-9)
    signal = signal / np.power(1 + np.power(np.abs(signal) / 4.0, 4), .25)
    window = np.ones(len(signal))
    attack = min(round(.001 * RATE), len(signal) // 6)
    release = min(round(.028 * RATE), len(signal) // 5)
    window[:attack] = np.sin(np.linspace(0, np.pi / 2, attack)) ** 2
    window[-release:] = np.cos(np.linspace(0, np.pi / 2, release)) ** 2
    signal *= window
    signal -= np.mean(signal) * window / np.mean(window)
    target = 10 ** ((-23.0 if category == "ui" else -19.5) / 20)
    signal *= target / max(active_rms(signal), 1e-9)
    ceiling = 10 ** (-5 / 20)
    signal *= min(1.0, ceiling / max(np.max(np.abs(signal)), 1e-9))
    signal[0] = signal[-1] = 0
    return signal


def write_wave(path: Path, signal: np.ndarray) -> dict:
    path.parent.mkdir(parents=True, exist_ok=True)
    pcm = np.rint(signal * 32767).astype("<i2")
    with wave.open(str(path), "wb") as file:
        file.setparams((1, 2, RATE, 0, "NONE", "not compressed"))
        file.writeframes(pcm.tobytes())
    decoded = pcm.astype(np.float64) / 32768
    return {"file": path.relative_to(OUT).as_posix(), "duration_seconds": round(len(decoded) / RATE, 4),
            "sample_rate": RATE, "channels": 1, "bits_per_sample": 16, "bytes": path.stat().st_size,
            "peak_dbfs": db(float(np.max(np.abs(decoded)))), "rms_dbfs": db(float(np.sqrt(np.mean(decoded ** 2)))),
            "active_rms_dbfs": db(active_rms(decoded)), "dc_offset": round(float(np.mean(decoded)), 9),
            "clipped_samples": int(np.count_nonzero(np.abs(pcm.astype(np.int32)) >= 32767)),
            "sha256": hashlib.sha256(path.read_bytes()).hexdigest(), "source": SOURCE}


def protected_hashes() -> dict[str, str]:
    # Read only; protect all production audio/importers, including the three excluded effects.
    paths = list((ROOT / "Assets/_Project/Audio").rglob("*"))
    paths.append(ROOT / "Tools/Audio/generate_expedition_sfx.py")
    return {p.relative_to(ROOT).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
            for p in sorted(paths) if p.is_file()}


def write_readme(metadata: dict):
    lines = ["# Escape 0.3.5 — варианты звуков A/B", "", "Это **варианты для выбора**, они не внедрены в игру. Все файлы созданы процедурно этим генератором: шумовые возбуждения, негармонические резонаторы, механические микроудары, огибающие и короткие отражения. Это синтетические звуки, а не записи реального металла/камня. Скачанных сэмплов, скрытого стока, речи и музыки нет.", "",
             "- **A — Минерал и металл:** сухая, сдержанная механика; различимые сколы, ткань, защёлки и латунь.",
             "- **B — Резонанс и пар:** тот же мир, с более воздушной пневматикой и объёмными негармоническими хвостами.",
             "- Можно выбрать направление целиком или сочетать отдельные A/B.",
             "- Формат: PCM WAV, 48 кГц, mono, 16 bit. У каждой пары близкий активный RMS; UI тише боя. Потолок −5 dBFS оставляет запас для наложений.", "",
             "## Прослушивание", "", "[Открыть страницу выбора](index.html): все 20 пар, единый плеер, выбор A/B по каждому событию и экспорт текстом. Страница не обращается к сети и работает напрямую из папки; выбор сохраняется в браузере, если доступно локальное хранилище.", "",
             "В `pairs/` у каждого события идёт **A → пауза 0,65 с → B**; в начале 0,15 с тишины и в конце 0,35 с. В `montages/` направления разнесены по отдельным файлам, между событиями 0,65 с. Короткие `showcases/showcase-A.wav` и `showcase-B.wav` содержат шесть эффектов за 10,53 с с паузами по секунде: нажатие, каскад, крест, петарда, крафт, победа. Порядок полного монтажа — в таблице ниже, точные временные метки обоих форматов — `metadata.json`. Голосовых объявлений нет. Не требуется воспроизведение из Unity.", "",
             "Генератор проверяет сигналы и WAV, но человеческое прослушивание и оценка на динамике телефона не выполнялись. Предложенные звуки необходимо прослушать и выбрать до будущей интеграции; измерения не доказывают художественное качество или слышимость на конкретном устройстве.", "",
             "## Варианты", "", "| № | Событие | A | B | A → B |", "|---|---|---|---|---|"]
    for i, effect in enumerate(metadata["effects"], 1):
        lines.append(f'| {i:02} | {effect["title"]} | [A]({effect["variants"]["A"]["file"]}) | [B]({effect["variants"]["B"]["file"]}) | [сравнить]({effect["pair_preview"]["file"]}) |')
    lines += ["", "[Полное направление A](montages/style-A.wav) · [Полное направление B](montages/style-B.wav)", "",
              "## Границы и реальные события", "", "13 существующих боевых слотов сверены с `DuelMatch3Manager`: подготовлены 10 заменяемых эффектов. **Fury/Bloodlust, Shield/Heroism и critical hit исключены**: их аудио, импортёры и привязки не изменялись. Ничья сейчас использует существующий слот победы; новый выдуманный слот ничьей не добавлен.", "",
              "Последние 10 пар — предложения по реальным событиям интерфейса, очереди, мастерской и шахты. У них пока нет аудиопривязок. `queue_enter` предназначен для подтверждённой постановки в очередь, `match_ready` — только для подтверждённого матча. `craft_start` можно использовать после успешного создания/ускорения, `craft_ready` — единожды при переходе готовности, `reward_claim` — после подтверждённого получения. `barrier_unlock` — вариант для успешного открытия, а повторное использование для призыва требует отдельного решения при интеграции. Это не звуки каждых тиков таймера или ручной прокрутки.", "",
              "В каждом элементе `metadata.json` есть точка предполагаемого события, существующий слот (если есть), длительность, peak/RMS, SHA-256 и происхождение. `inventory.json` содержит все 13 слотов, исключения и прочитанные точки исходников. `quality-checks.json` — автоматические проверки и хеши защищённых существующих файлов. Логика, расходы, громкости и обработчики игры не менялись.", "",
              "## Повторная генерация", "", "Из корня проекта с Python и NumPy:", "", "```text", "python Tools/Audio/generate_sfx_auditions.py", "```", "", "Не запускать старый `generate_expedition_sfx.py --write`: он пишет production Assets. Новый генератор допускает запись только в эту папку Design, не создаёт `.meta` и не запускает Unity. Детерминированные варианты воспроизводятся с seed 3502026. `--verify-only` проверяет уже созданные WAV/хеши без перезаписи.", ""]
    lines += ["Для обновления standalone HTML после изменения метаданных:", "", "```text", "python Tools/Audio/build_audition_page.py", "python Tools/Audio/validate_audition_page.py", "```", "",
              "При желании открыть через локальный сервер: `python -m http.server 8765 --bind 127.0.0.1 --directory Design/Audio`, затем `http://127.0.0.1:8765/`. Публикация и интеграция в игру не выполняются.", ""]
    (OUT / "README.md").write_text("\n".join(lines), encoding="utf-8")


def verify_existing():
    metadata = json.loads((OUT / "metadata.json").read_text(encoding="utf-8"))
    entries = [v for e in metadata["effects"] for v in e["variants"].values()]
    entries += [e["pair_preview"] for e in metadata["effects"]]
    entries += list(metadata["montages"].values())
    entries += list(metadata.get("showcases", {}).values())
    for entry in entries:
        path = OUT / entry["file"]
        assert hashlib.sha256(path.read_bytes()).hexdigest() == entry["sha256"], path
        with wave.open(str(path), "rb") as file:
            assert file.getparams()[:3] == (1, 2, RATE), path
            pcm = np.frombuffer(file.readframes(file.getnframes()), dtype="<i2")
        assert np.max(np.abs(pcm.astype(np.int32))) < 32767, path
        assert pcm[0] == pcm[-1] == 0, path
    quality = json.loads((OUT / "quality-checks.json").read_text(encoding="utf-8"))
    for relative, original in quality["protected_sha256"].items():
        assert hashlib.sha256((ROOT / relative).read_bytes()).hexdigest() == original, relative
    print(json.dumps({"verified": len(entries), "effect_pairs": len(metadata["effects"]), "production_assets_written": False}))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--verify-only", action="store_true")
    parser.add_argument("--verify-selected", action="store_true", help="Verify only the retained selected archive")
    parser.add_argument("--rebuild-demos", action="store_true", help="Explicitly rebuild full A/B demos after archival")
    parser.add_argument("--seed", type=int, default=3502026)
    args = parser.parse_args()
    assert OUT.resolve().parent == (ROOT / "Design").resolve() and "Assets" not in OUT.parts
    if args.verify_selected or (args.verify_only and not (OUT / "metadata.json").exists() and (OUT / "selected-metadata.json").exists()):
        from validate_selected_archive import main as verify_selected
        verify_selected()
        return
    if args.verify_only:
        verify_existing()
        return
    if (OUT / "selected-metadata.json").exists() and not (OUT / "metadata.json").exists() and not args.rebuild_demos:
        parser.error("Selected-only archive retained. Use --verify-selected or explicitly --rebuild-demos.")
    before = protected_hashes()
    OUT.mkdir(parents=True, exist_ok=True)
    metadata = {"version": "0.3.5", "status": "audition_only_not_integrated", "source": SOURCE,
                "styles": STYLES, "seed": args.seed, "effects": [], "montages": {}, "showcases": {},
                "excluded_slots": ["sfxAbilityFury", "sfxAbilityShield", "sfxDamageCrit"],
                "human_listening_performed": False, "device_listening_performed": False}
    rendered = {}
    for key, title, category, duration, slot, trigger, description in EFFECTS:
        entry = {"id": key, "title": title, "category": category, "existing_slot": slot,
                 "trigger": trigger, "description": description, "integrated": False, "variants": {}}
        for style in STYLES:
            signal = finish(Renderer(key, style, args.seed).render(key, duration), category)
            assert np.isfinite(signal).all() and np.max(np.abs(signal)) <= 10 ** (-5 / 20) + 1e-9
            assert abs(float(np.mean(signal))) < 2e-5
            rendered[(key, style)] = signal
            item = write_wave(OUT / style / f"{key}.wav", signal)
            item["style"] = style
            entry["variants"][style] = item
        gap = np.zeros(round(.65 * RATE))
        pair = np.concatenate([np.zeros(round(.15 * RATE)), rendered[(key, "A")], gap, rendered[(key, "B")], np.zeros(round(.35 * RATE))])
        entry["pair_preview"] = write_wave(OUT / "pairs" / f"{key}_A-gap-B.wav", pair)
        entry["pair_active_rms_difference_db"] = round(abs(entry["variants"]["A"]["active_rms_dbfs"] - entry["variants"]["B"]["active_rms_dbfs"]), 3)
        metadata["effects"].append(entry)
    for style in STYLES:
        pieces = [np.zeros(round(.25 * RATE))]
        at = .25
        markers = []
        for key, *_ in EFFECTS:
            sound = rendered[(key, style)]
            markers.append({"id": key, "start_seconds": round(at, 4), "end_seconds": round(at + len(sound) / RATE, 4)})
            pieces += [sound, np.zeros(round(.65 * RATE))]
            at += len(sound) / RATE + .65
        item = write_wave(OUT / "montages" / f"style-{style}.wav", np.concatenate(pieces))
        item["markers"] = markers
        metadata["montages"][style] = item
        pieces = [np.zeros(round(.30 * RATE))]
        at = .30
        markers = []
        for key in SHOWCASE_KEYS:
            sound = rendered[(key, style)]
            markers.append({"id": key, "start_seconds": round(at, 4), "end_seconds": round(at + len(sound) / RATE, 4)})
            pieces += [sound, np.zeros(RATE)]
            at += len(sound) / RATE + 1
        item = write_wave(OUT / "showcases" / f"showcase-{style}.wav", np.concatenate(pieces))
        assert 10 <= item["duration_seconds"] <= 15
        item["markers"] = markers
        metadata["showcases"][style] = item
    (OUT / "metadata.json").write_text(json.dumps(metadata, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    slots = [{"slot": e[4], "audition_id": e[0], "excluded": False} for e in EFFECTS if e[4]]
    slots += [{"slot": "sfxAbilityFury", "excluded": True, "existing_asset": "Assets/_Project/Audio/SFX/Bloodlust_cast.ogg"},
              {"slot": "sfxAbilityShield", "excluded": True, "existing_asset": "Assets/_Project/Audio/SFX/Heroism_Cast.ogg"},
              {"slot": "sfxDamageCrit", "excluded": True, "existing_asset": "Assets/_Project/Audio/SFX/Match3/m3_damage_crit.wav"}]
    manager_path = ROOT / "Assets/_Project/Scripts/Match3/DuelMatch3Manager.cs"
    manager_source = manager_path.read_text(encoding="utf-8-sig")
    actual_slots = re.findall(r"\[SerializeField\]\s+private AudioClip (sfx\w+);", manager_source)
    assert set(actual_slots) == {slot["slot"] for slot in slots}, "Actual battle SFX inventory drifted"
    for slot in slots:
        slot["declaration_line"] = next(i for i, line in enumerate(manager_source.splitlines(), 1)
                                         if f'AudioClip {slot["slot"]};' in line)
    inventory = {"battle_slots_count": 13, "battle_pairs_count": 10, "functional_pairs_count": 10, "slots": slots,
                 "read_source_files": ["Assets/_Project/Scripts/Match3/DuelMatch3Manager.cs", "Assets/_Project/Scripts/Match3/UI/Match3SearchingPanel.cs",
                                       "Assets/_Project/Scripts/Character/UI/WorkshopSceneController.cs", "Assets/_Project/Scripts/UI/MineSceneController.cs",
                                       "Assets/_Project/Scripts/UI/ExpeditionMinePresentation.cs", "Assets/_Project/Scripts/UI/ExpeditionTheme.cs",
                                       "Tools/Audio/generate_expedition_sfx.py"],
                 "functional_audio_hooks_exist": False, "notes": "Functional triggers are proposals, not integrated callbacks. Draw currently shares victory."}
    (OUT / "inventory.json").write_text(json.dumps(inventory, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    after = protected_hashes()
    assert before == after, "Production audio or its original generator changed during audition generation"
    all_variants = [v for e in metadata["effects"] for v in e["variants"].values()]
    quality = {"passed": True, "production_audio_unchanged": True, "protected_file_count": len(before), "protected_sha256": before,
               "finite": True, "dc_offset_max_abs": max(abs(v["dc_offset"]) for v in all_variants),
               "clipped_samples": sum(v["clipped_samples"] for v in all_variants),
               "max_pair_active_rms_difference_db": max(e["pair_active_rms_difference_db"] for e in metadata["effects"]),
               "sample_rate": RATE, "channels": 1, "bits_per_sample": 16, "individual_files": len(all_variants),
               "pair_previews": len(EFFECTS), "montages": 2, "short_showcases": 2, "human_listening_performed": False,
               "device_listening_performed": False, "existing_runtime_files_written": False}
    assert quality["max_pair_active_rms_difference_db"] <= 1.2, "Audition pairs not level balanced"
    (OUT / "quality-checks.json").write_text(json.dumps(quality, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    write_readme(metadata)
    verify_existing()


if __name__ == "__main__":
    main()
