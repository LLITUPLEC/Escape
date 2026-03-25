# Nakama: дуэли (keypad + match3 authoritative)



1. Скопируйте файлы модулей в каталог **modules** вашего Nakama (часто `data/modules/` в Docker-образе):
   - `duel_keypad.lua` (RPC для PIN дверей),
   - `duel_online.lua` (онлайн-статус),
   - `duel_session.lua` (single-session по e-mail: эпоха сессии + уведомление при входе с другого устройства),
   - `duel_match3.lua` (server-authoritative Match3; проверка `session_epoch` для мутаций встроена в этот файл, отдельный `require` не нужен),
   - `duel_relay.lua` (authoritative relay fallback),
   - `duel_matchmaker.lua` (matchmaker hook, создаёт нужный тип матча).

2. Перезапустите Nakama.

3. В логах при старте не должно быть ошибок загрузки скриптов.



Клиент после входа в матч вызывает RPC `duel_match_ensure_pins`, затем при каждом вводе — `duel_keypad_guess`. В теле JSON передаются **`match_id`**, **`user_a`**, **`user_b`** — два реальных UUID участников дуэли, **уже отсортированные** (`user_a` &lt; `user_b` по строковому сравнению). Запись в storage создаётся у **владельца `user_a`** (лексикографически меньший id), чтобы выполнялся FK `storage_user_id_fkey` (пользователь должен существовать в БД Nakama). RPC разрешены только если `ctx.user_id` совпадает с одним из двух UUID.

Для Match3 клиент отправляет в matchmaker string-property `mode=match3`; серверный hook `duel_matchmaker.lua` создаёт authoritative матч `duel_match3`, где:
- клиент отправляет только намерение хода (`op=13`),
- сервер валидирует ход/ману/cd/очередность,
- сервер считает всё поле/каскады/урон,
- сервер рассылает итоговое состояние (`op=10`) и game over (`op=11`).

Для PVE (боты) доступны RPC:
- `duel_match3_pve_catalog_get` — возвращает список ботов и текущую прогрессию.
- `duel_match3_pve_create` с payload `{"bot_id":"slime_1"}` — создаёт authoritative PVE-матч и возвращает `match_id`.
  Клиент затем делает `JoinMatch` по `match_id`.

Прогрессия PVE хранится в `duel_match3_progress/profile`:
- `level` (до 10),
- `xp`,
- `gold`,
- `defeated` (счётчик побед по bot_id).

В `duel_match3.lua` заведена таблица из 10 боссов (`slime_1 ... emperor_10`) с параметрами:
- `difficulty`,
- `hp_bonus`,
- `start_mana`,
- поведенческие коэффициенты ИИ (`ai_ability_chance`, `petard_bias`, `cross_bias`, `square_bias`),
- награды (`reward_xp`, `reward_gold`).

Серверная статистика Match3 (по `ctx.user_id`) также доступна через RPC:
- `duel_match3_stats_get` → `{ ok, played, wins, losses }`
- `duel_match3_stats_record` с payload `{"won":true|false}` → инкрементирует сыграно/победы/поражения.



Пароли **не отправляются клиенту**: хранятся в storage с `permission_read = 0` (только сервер).



Двери **1 и 3** — один двухзначный код; **2 и 4** — один трёхзначный. Допускаются ведущие нули (`04`, `007`).



## Если в Unity: `Exceeded max retry attempts` и `no_pins`



Обычно RPC на сервере **падает с ошибкой** (тогда клиент Nakama много раз ретраит и сдаётся). Частые причины:



- **`storage_user_id_fkey`**: в storage писали под несуществующим `user_id` (например, фиктивный системный UUID). Нужна актуальная версия `duel_keypad.lua` и клиент, передающий **реальную пару** `user_a` / `user_b`.

- **Неверный формат `nk.storage_write`**: поле `value` должно быть **Lua-таблицей**, а не строкой JSON.



Проверьте также:



- Файл реально лежит в каталоге modules и **подхватился** (в логах Nakama при старте видно загрузку runtime).

- **Docker**: том с `modules` смонтирован в тот путь, который указан в конфиге Nakama (`--runtime.path` / `NAKAMA_RUNTIME_PATH`).

- В логах сервера строки `duel_match_ensure_pins:` / `duel_keypad_guess:` от `nk.logger_error` — там текст ошибки Lua.



После успешного `ensure_pins` ввод перестаёт возвращать `no_pins`.



**Nakama 3.22+:** в `storage_write` нельзя указывать `version = ""` для новой записи — поле `version` нужно **опустить** (как в текущем `duel_keypad.lua`). Иначе в логах: `expects version to be a non-empty string`, RPC `duel_match_ensure_pins` падает и в Unity остаётся `no_pins`.


