# Каталоги монстров шахты в Nakama Storage

## Куда писать

| Поле | Значение |
|------|----------|
| **Collection** | `duel_match3_bot_defs` (константа `BOTS_COLLECTION` в `duel_match3_config.lua`) |
| **User ID** | `00000000-0000-0000-0000-000000000000` (`BOTS_STORAGE_USER_ID`) — системный владелец глобальных каталогов |
| **Keys** | См. ниже |

## Ключи (Key)

1. **`catalog`** — общий каталог.
2. **`catalog_easy`** — слой для сложности шахты **easy** (перекрывает id из `catalog`).
3. **`catalog_medium`** — для **medium**.
4. **`catalog_hard`** — для **hard**.

Сервер сначала читает `catalog`, затем применяет `catalog_{difficulty}` поверх совпадающих `id`. Пропущенные этажи получают резервные параметры.

## Формат JSON (тело `value`)

Тот же формат, что в `duel_match3_bots_catalog.example.json`:

```json
{
  "version": 2,
  "bots": {
    "mine_1": { "id": "mine_1", "name": "...", "floor": 1, ... }
  }
}
```

Готовые файлы в этой папке:

- `duel_match3_bots_catalog_easy.json` — действующий лёгкий каталог.
- `duel_match3_bots_catalog_medium.json` — усиленные статы/награды.
- `duel_match3_bots_catalog_hard.json` — сильнее medium.

Сервер при запуске обновляет действующие каталоги версии 1 до версии 2, если их старые цены совпадают с известными. Он записывает три слоя одним защищённым по версиям пакетом и сохраняет дополнительные поля. При конфликте с ручными ценами остаются исходные записи, причину можно найти в логе `mine_prices_upgrade`. Новая установка создаёт записи версии 2 сразу. Пример общего каталога оставлен для исторических импортов. Актуальные цены описаны в [балансе шахты](../../../Docs/Balance/MineEntryPrices-2026-11-13.md).

## Консоль Nakama / HTTP API

1. **Console → Storage** (или API): создать запись с указанными `collection`, `key`, `user_id`.
2. В поле **Value** вставить **целиком** JSON файла (один объект с `version` и `bots`).

Пример тела для **WriteObjects** (псевдокод; точный URL — ваш Nakama):

```json
{
  "object": {
    "collection": "duel_match3_bot_defs",
    "key": "catalog_medium",
    "user_id": "00000000-0000-0000-0000-000000000000",
    "value": { ...содержимое duel_match3_bots_catalog_medium.json... },
    "permission_read": 0,
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
