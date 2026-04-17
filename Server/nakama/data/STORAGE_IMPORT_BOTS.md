# Импорт каталогов ботов в Nakama Storage (фаза 3)

## Куда писать

| Поле | Значение |
|------|----------|
| **Collection** | `duel_match3_bot_defs` (константа `BOTS_COLLECTION` в `duel_match3_config.lua`) |
| **User ID** | `4ad57156-201b-4abf-8d5d-7b4ed6a0364c` (`BOTS_STORAGE_USER_ID`) — системный пользователь для глобальных каталогов |
| **Keys** | См. ниже |

## Ключи (Key)

1. **`catalog`** — общий каталог (как раньше); перекрывает `BOTS_FALLBACK` в коде.
2. **`catalog_easy`** — слой для сложности шахты **easy** (перекрывает id из `catalog`).
3. **`catalog_medium`** — для **medium**.
4. **`catalog_hard`** — для **hard**.

Слияние на сервере: `fallback` → `catalog` → `catalog_{difficulty}` (последний побеждает по `id`).

## Формат JSON (тело `value`)

Тот же формат, что в `duel_match3_bots_catalog.example.json`:

```json
{
  "version": 1,
  "bots": {
    "mine_1": { "id": "mine_1", "name": "...", "floor": 1, ... }
  }
}
```

Готовые файлы в этой папке:

- `duel_match3_bots_catalog_easy.json` — копия базового примера (как «лёгкий» слой).
- `duel_match3_bots_catalog_medium.json` — усиленные статы/награды.
- `duel_match3_bots_catalog_hard.json` — сильнее medium.

## Консоль Nakama / HTTP API

1. **Console → Storage** (или API): создать запись с указанными `collection`, `key`, `user_id`.
2. В поле **Value** вставить **целиком** JSON файла (один объект с `version` и `bots`).

Пример тела для **WriteObjects** (псевдокод; точный URL — ваш Nakama):

```json
{
  "object": {
    "collection": "duel_match3_bot_defs",
    "key": "catalog_medium",
    "user_id": "4ad57156-201b-4abf-8d5d-7b4ed6a0364c",
    "value": { ...содержимое duel_match3_bots_catalog_medium.json... },
    "permission_read": 1,
    "permission_write": 0
  }
}
```

## CLI (nakama)

Если используете официальный CLI с `storage write`, передайте JSON как `value` (экранируя кавычки в shell). Удобнее один раз залить через **Console** или небольшой скрипт на HTTP.

## После изменения

Перезапустите процесс Nakama (или перезагрузите Lua-модули по вашему пайплайну), чтобы подхватились `duel_match3_metrics.lua` и правки `duel_match3.lua`. Кэш каталога ботов на сервере: ~30 с (`BOTS_CACHE_TTL_SEC`).

## §4.3 рецепты

Предметы `recipe_drop_{green|blue|purple}_{Slot}` добавлены в `duel_match3_item_catalog.example.json` и в fallback в `duel_match3.lua`. Импорт **item defs** — отдельно: коллекция `duel_match3_item_defs`, ключ `catalog`, тот же `ITEM_DEFS_STORAGE_USER_ID`, см. ваш пример каталога предметов.
