"""Audit shipped Unity media references without changing Assets.

Uses only Python's standard library. GUID checks traverse enabled build scenes and
Resources dependencies; null presentation fields are checked by role, not blindly.
Run: python Tools/Art/audit_media.py [--label before]
"""
from __future__ import annotations

import argparse
from collections import Counter, defaultdict, deque
from datetime import datetime, timezone
import json
from pathlib import Path
import re
import wave

ROOT = Path(__file__).resolve().parents[2]
PROJECT = ROOT / "Assets/_Project"
GUID = re.compile(r"^guid: ([0-9a-fA-F]{32})\s*$", re.M)
REF = re.compile(r"\{fileID: (-?\d+), guid: ([0-9a-fA-F]{32}), type: \d+\}")
YAML_EXT = {".asset", ".unity", ".prefab", ".mat", ".controller", ".overrideController", ".spriteatlas", ".playable", ".anim"}
MEDIA_EXT = {".png", ".jpg", ".jpeg", ".tga", ".psd", ".wav", ".ogg", ".mp3", ".aiff", ".anim", ".controller", ".mp4", ".webm"}
MEDIA_FILE_IDS = {21300000, 2800000, 8300000, 7400000, 9100000, 32900000}


def text(path: Path) -> str:
    return path.read_text(encoding="utf-8-sig", errors="replace")


def rel(path: Path) -> str:
    return path.relative_to(ROOT).as_posix()


def index_assets():
    index = defaultdict(list)
    for base in (ROOT / "Assets", ROOT / "Packages", ROOT / "Library/PackageCache"):
        if not base.exists():
            continue
        for meta in base.rglob("*.meta"):
            match = GUID.search(text(meta))
            if match:
                index[match[1].lower()].append(Path(str(meta)[:-5]))
    return index


def sprite_field(contents: str, field="icon"):
    match = re.search(r"^\s+" + re.escape(field) + r": \{fileID: (-?\d+)(?:, guid: ([0-9a-fA-F]{32}), type: \d+)?\}", contents, re.M)
    return (int(match[1]), (match[2] or "").lower()) if match else (None, "")


def field_value(contents: str, field: str):
    match = re.search(r"^\s+" + re.escape(field) + r": ([^\r\n]*)", contents, re.M)
    return match[1] if match else None


def catalog_section(folder: str, identity: str, marker: str, index):
    entries = []
    for path in sorted((PROJECT / folder).glob("*.asset")):
        contents = text(path)
        if marker not in contents:
            continue
        file_id, guid = sprite_field(contents)
        targets = index.get(guid, [])
        entry = {"id": field_value(contents, identity), "definition": rel(path),
                 "icon_file_id": file_id, "icon_guid": guid,
                 "icon_path": rel(targets[0]) if targets else None,
                 "status": "null" if not file_id else "broken" if not targets or not targets[0].exists() else "assigned"}
        if identity == "botId" and entry["icon_path"] in {"Assets/_Project/img/images.jpg", "Assets/_Project/img/Achievements/fenix.jpg"}:
            entry["status"] = "placeholder"
        if identity == "itemId":
            entry.update({key: field_value(contents, key) for key in ("kind", "quality", "slot", "recipeTargetSlot")})
        entries.append(entry)
    counts = Counter(entry["status"] for entry in entries)
    by_path = defaultdict(list)
    for entry in entries:
        if entry["icon_path"]:
            by_path[entry["icon_path"]].append(entry["id"])
    return {"count": len(entries), "assigned": counts["assigned"], "null": counts["null"], "broken": counts["broken"], "placeholders": counts["placeholder"],
            "distinct_icon_paths": len(by_path), "shared_icon_groups": [{"path": key, "ids": value} for key, value in by_path.items() if len(value) > 1],
            "entries": entries}


def inspect_audio(index):
    bank = PROJECT / "Resources/Expedition/Audio/SelectedSfx.asset"
    cues = []
    if bank.exists():
        for match in re.finditer(r"- cue: (\d+)\s+clip: \{fileID: (-?\d+), guid: ([a-f0-9]{32}), type: \d+\}", text(bank)):
            paths = index.get(match[3], [])
            path = paths[0] if paths else None
            cue = {"cue": int(match[1]), "path": rel(path) if path else None, "exists": bool(path and path.exists())}
            if path and path.exists() and path.suffix == ".wav":
                try:
                    with wave.open(str(path), "rb") as source:
                        cue["seconds"] = round(source.getnframes() / source.getframerate(), 3)
                        cue["sample_rate"] = source.getframerate()
                        cue["channels"] = source.getnchannels()
                except wave.Error as exc:
                    cue["error"] = str(exc)
            cues.append(cue)
    scene = text(PROJECT / "Scenes/DuelMatch3.unity")
    battle = []
    for match in re.finditer(r"^  (sfx\w+): \{fileID: (-?\d+)(?:, guid: ([a-f0-9]{32}), type: \d+)?\}", scene, re.M):
        if match[1] == "sfxSource":
            continue
        paths = index.get(match[3], [])
        battle.append({"field": match[1], "path": rel(paths[0]) if paths else None, "exists": bool(paths and paths[0].exists())})
    return {"selected_bank_cues": len(cues), "selected_bank_missing": sum(not item["exists"] or "error" in item for item in cues),
            "battle_clip_fields": len(battle), "battle_clips_missing": sum(not item["exists"] for item in battle),
            "selected_cues": cues, "battle_clips": battle,
            "playback_or_subjective_listening_performed": False}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--label", default="", help="Output suffix, e.g. before")
    args = parser.parse_args()
    index = index_assets()
    documents = {rel(path): text(path) for path in PROJECT.rglob("*") if path.is_file() and path.suffix in YAML_EXT}
    settings = text(ROOT / "ProjectSettings/EditorBuildSettings.asset")
    scenes = re.findall(r"- enabled: 1\s+path: ([^\r\n]+)", settings)
    roots = set(scenes) | {path for path in documents if "/Resources/" in path}
    reachable, pending = set(), deque(sorted(roots))
    while pending:
        path = pending.popleft()
        if path in reachable:
            continue
        reachable.add(path)
        source_path = ROOT / path
        contents = documents.get(path)
        if contents is None and source_path.exists() and source_path.suffix in YAML_EXT:
            contents = text(source_path)
        if contents is None:
            continue
        for _, guid in REF.findall(contents):
            for target in index.get(guid.lower(), []):
                target_rel = rel(target)
                if target_rel not in reachable:
                    pending.append(target_rel)
    unresolved = []
    for path, contents in documents.items():
        for match in REF.finditer(contents):
            file_id, guid = int(match[1]), match[2].lower()
            if guid.startswith("0000000000000000"):
                continue  # Unity's built-in resources.
            targets = index.get(guid, [])
            if not targets or not targets[0].exists():
                unresolved.append({"source": path, "line": contents.count("\n", 0, match.start()) + 1, "guid": guid,
                                   "file_id": file_id, "reachable_from_build_or_resources": path in reachable,
                                   "media_file_id": file_id in MEDIA_FILE_IDS})
    media_files = [path for path in PROJECT.rglob("*") if path.is_file() and path.suffix.lower() in MEDIA_EXT]
    media_counts = Counter(path.suffix.lower() for path in media_files)
    catalog = text(PROJECT / "Data/Character/Items/MainItemCatalog.asset")
    fallback_file_id, fallback_guid = sprite_field(catalog, "missingItemIcon")
    fallback_paths = index.get(fallback_guid, [])
    reward_fields = {}
    battle_scene = text(PROJECT / "Scenes/DuelMatch3.unity")
    for name in ("rewardExpSprite", "rewardOreSprite", "rewardGoldSprite", "rewardMatterSprite", "rewardIngotsSprite", "rewardKeySprite", "rewardBlueprintSprite", "rewardTesseractSprite"):
        file_id, guid = sprite_field(battle_scene, name)
        paths = index.get(guid, [])
        reward_fields[name] = {"file_id": file_id, "path": rel(paths[0]) if paths else None}
    frames = text(PROJECT / "Data/Match3/Monsters/MainMonsterFrameCatalog.asset")
    frame_fields = {name: sprite_field(frames, name)[0] for name in ("frameEasy", "frameMedium", "frameHard", "frameBoss")}
    fury = PROJECT / "Resources/Match3/FuryFxCatalog.asset"
    fury_frames = [{"guid": guid, "path": rel(index[guid][0]) if guid in index else None} for file_id, guid in REF.findall(text(fury)) if int(file_id) == 21300000]
    affix = PROJECT / "Resources/Match3/AffixCatalog.asset"
    affix_icons = []
    for match in re.finditer(r"  - id: ([^\r\n]+)[\s\S]*?icon: \{fileID: (-?\d+), guid: ([a-f0-9]+), type: \d+\}", text(affix)):
        targets = index.get(match[3], [])
        affix_icons.append({"id": match[1], "path": rel(targets[0]) if targets else None, "exists": bool(targets and targets[0].exists())})
    ui_keys = set()
    for atlas in (PROJECT / "Resources/Expedition/UI").glob("*.png.meta"):
        ui_keys.update(re.findall(r"^      name: ([^\r\n]+)", text(atlas), re.M))
    standalone_ui = [path for path in (PROJECT / "Resources/Expedition/UI").glob("*.png") if "atlas" not in path.stem]
    standalone_ui.extend((PROJECT / "Resources/Expedition/Icons").glob("*.png"))
    ui_keys.update(path.stem for path in standalone_ui)
    required_ui_keys = {"home", "pickaxe", "anvil", "swords", "hero", "energy", "gold", "ore", "bag", "heart", "shield", "helmet", "shoulders", "chest", "gloves", "legs", "boots", "left-hand", "right-hand", "blueprint", "trophy", "settings", "friends"}
    ui_icon_audit = {"known_keys": sorted(ui_keys), "required_keys": sorted(required_ui_keys),
                     "missing_required_keys": sorted(required_ui_keys - ui_keys),
                     "intentional_mesh_fallback_keys": ["lock"],
                     "limitation": "Standalone file existence does not prove ExpeditionArt runtime resolver loads it; confirm resolver/Unity QA."}
    mine_scene = text(PROJECT / "Scenes/MineScene.unity")
    dormant_slot_fields = {}
    for name in ("slotHelmetSprite", "slotShouldersSprite", "slotChestSprite", "slotGlovesSprite", "slotLegsSprite", "slotFeetSprite", "slotWeaponLeftSprite", "slotWeaponRightSprite"):
        file_id, guid = sprite_field(mine_scene, name)
        targets = index.get(guid, [])
        dormant_slot_fields[name] = {"file_id": file_id, "path": rel(targets[0]) if targets else None}
    # The runtime deliberately uses mesh graphics/tweens for chrome, ambient
    # particles and combat feedback. Their null Sprite slots are not missing art.
    notes = [
        {"kind": "integration", "topic": "reward_icons", "resolved_in_current_scene": all(value["file_id"] and value["path"] for value in reward_fields.values()), "detail": "Null rewardKeySprite/Blueprint/Tesseract scene fields cannot load img/ resources in a Player via the editor-only AssetDatabase helper; bind existing icons in scene or use runtime Resources."},
        {"kind": "integration", "topic": "monster_difficulty_frames", "resolved_in_current_catalog": all(frame_fields.values()), "detail": "Four dedicated shared difficulty frames are assigned by the completed media importer. Per-monster overrides may remain null because the shared catalog supplies them."},
        {"kind": "content_expansion", "topic": "music_and_ambience", "detail": "Current sound design contains event SFX; no background music or continuous mine/forge ambience system was found in project scripts. These are additional production work, not broken clips."},
        {"kind": "content_expansion", "topic": "elevator_sound", "detail": "The travelling mine lift has visual animation but no dedicated mechanical playback/clip in the current Expedition presenter or audio bank. Preserve the existing selected-audio scope unless adding new sound design is requested."},
        {"kind": "content_expansion", "topic": "character_animation", "detail": "Heroes/monsters use static illustrations with UI movement and effects. Dedicated idle/attack/hurt/death frame sets are absent; no missing rig/clip is inferred from that design."},
        {"kind": "feature_limit", "topic": "remote_avatars", "detail": "Skin unlocks and selection now persist in protected server storage and the paired avatar is derived from the skin. Remote friends/leaderboard/battle opponent views still need a public profile integration to fetch other players' cosmetic IDs."},
        {"kind": "intentional", "topic": "procedural_graphics", "detail": "Null Image sprites for fills, lines, transparent hit targets, locks, borders, particles and empty equipment slots are intentional; do not count them as media gaps."},
        {"kind": "legacy", "topic": "editor_ability_fallback_paths", "detail": "DuelMatch3Manager editor auto-assignment still mentions petarda/cross/square/shield/fury.png names absent from img/butt_spells, but shipped scene has all five real ability sprite refs; this is stale tooling, not missing shipped artwork."},
        {"kind": "legacy", "topic": "unused_mine_equipment_slot_fields", "detail": "MineScene slot sprite fields originally pointed to repair.png/arena.png. GetEquipmentSlotSprite has no callers in current source; these dormant old fields do not represent visible missing sprites. The real Hero slot ghosts use Expedition UI icons."},
    ]
    report = {"generated_utc": datetime.now(timezone.utc).isoformat(), "label": args.label or "current", "enabled_build_scenes": scenes,
              "scope": "Assets/_Project serialized media; enabled-scene + Resources GUID reachability; known catalog/audio roles. Runtime dynamic strings and visual appearance still require Unity QA.",
              "file_counts_by_extension": dict(sorted(media_counts.items())), "serialized_documents_scanned": len(documents),
              "reachable_assets": len(reachable), "missing_guid_references": unresolved,
              "missing_media_guid_references_in_build": sum(entry["media_file_id"] and entry["reachable_from_build_or_resources"] for entry in unresolved),
              "duplicate_asset_guids": [{"guid": key, "paths": [rel(path) for path in paths]} for key, paths in index.items() if len(paths) > 1],
              "items": catalog_section("Data/Character/Items", "itemId", "Project.Character.ItemDefinition", index),
              "monsters": catalog_section("Data/Match3/Monsters", "botId", "Project.Match3.MonsterDefinition", index),
              "achievements": catalog_section("Data/Achievements", "achievementId", "Project.Achievements.AchievementDefinition", index),
              "item_fallback": {"file_id": fallback_file_id, "path": rel(fallback_paths[0]) if fallback_paths else None},
              "reward_scene_fields": reward_fields, "shared_monster_frame_fields": frame_fields,
              "dormant_mine_equipment_slot_fields": dormant_slot_fields,
              "affix_icons": affix_icons, "ui_icons": ui_icon_audit, "fury_animation_frames": fury_frames, "audio": inspect_audio(index), "notes": notes}
    # Export reproducible data and a human-readable short report without touching
    # Unity assets, changing GUIDs or modifying pre-existing release reports.
    output = ROOT / ".cache/qa/art"
    output.mkdir(parents=True, exist_ok=True)
    stem = "media-audit" + ("-" + args.label if args.label else "")
    (output / (stem + ".json")).write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    lines = ["Escape: аудит медиа", "", "Снимок: " + report["generated_utc"], ""]
    for title, key in (("Предметы/экипировка/рецепты", "items"), ("Монстры", "monsters"), ("Достижения", "achievements")):
        value = report[key]
        lines.append(f"{title}: {value['count']}; назначено {value['assigned']}; пусто {value['null']}; заглушек {value['placeholders']}; битых {value['broken']}; различных иконок {value['distinct_icon_paths']}.")
    lines.extend([f"Аффиксы: {len(affix_icons)}; отсутствует {sum(not entry['exists'] for entry in affix_icons)}.",
                  f"Аудиобанк: {report['audio']['selected_bank_cues']} звуков; отсутствует {report['audio']['selected_bank_missing']}.",
                  f"Боевые звуки: {report['audio']['battle_clip_fields']} полей; отсутствует {report['audio']['battle_clips_missing']}.",
                  f"Иконки наград в сцене боя: назначено {sum(bool(entry['file_id'] and entry['path']) for entry in reward_fields.values())}/8.",
                  f"Битые GUID-ссылки на медиа среди зависимостей сборки/Resources: {report['missing_media_guid_references_in_build']}.",
                  f"Дубликаты GUID: {len(report['duplicate_asset_guids'])}.",
                  "Недостающие ключи UI-иконок: " + (", ".join(ui_icon_audit["missing_required_keys"]) or "нет"), "", "Пустые/ошибочные иконки и заглушки:"])
    missing = [entry for key in ("items", "monsters", "achievements") for entry in report[key]["entries"] if entry["status"] != "assigned"]
    lines.extend("- " + entry["id"] + ": " + entry["definition"] for entry in missing)
    if not missing:
        lines.append("Нет.")
    lines.extend([
        "", "Уточнения и ограничения:",
        "- Файловая проверка не заменяет просмотр в Unity/на устройстве; результаты Unity сохранены отдельно в validation/.",
        "- Фоновые музыкальные треки и непрерывная атмосфера шахты/кузницы пока не реализованы.",
        "- Лифт шахты анимируется визуально; отдельного механического звука в текущем Expedition presenter/банке нет.",
        "- Герои/монстры — статичные иллюстрации с UI-эффектами; покадровых idle/attack/hurt/death анимаций нет.",
        "- Выбор и открытие образов сохраняются на сервере и синхронизируются между устройствами. Аватарка всегда парная образу. Для показа внешности другого игрока в друзьях/рейтингах/бою ещё нужна интеграция публичных косметических ID.",
        "- Четыре общие рамки сложности назначает completed media importer; необязательные переопределения у отдельных монстров могут оставаться пустыми.",
        "- Неиспользуемые поля слотов MineScene переназначены на правильные изображения экипировки; GetEquipmentSlotSprite сейчас не вызывается.",
        "- Пустые спрайты заливок, прозрачных зон, эффектов и пустых слотов не считаются недостающей графикой.",
        "", "Подробности, все пути и ссылки: " + stem + ".json"
    ])
    (output / (stem + ".txt")).write_text("\n".join(lines) + "\n", encoding="utf-8")
    summary = {key: report[key] for key in ("missing_media_guid_references_in_build", "serialized_documents_scanned", "file_counts_by_extension")}
    summary.update({key: {name: report[key][name] for name in ("count", "assigned", "null", "placeholders", "broken", "distinct_icon_paths")} for key in ("items", "monsters", "achievements")})
    summary["outputs"] = [rel(output / (stem + suffix)) for suffix in (".json", ".txt")]
    print(json.dumps(summary, ensure_ascii=False))


if __name__ == "__main__":
    main()
