# Nakama: дуэли (keypad + match3 authoritative)



1. Скопируйте файлы модулей в каталог **modules** вашего Nakama (часто `data/modules/` в Docker-образе):
   - `duel_keypad.lua` (RPC для PIN дверей),
   - `duel_online.lua` (онлайн-статус: ping/count, leave, list до 50 игроков),
   - `duel_leaderboard.lua` (таблица лидеров: RPC `duel_leaderboard_get`),
   - `duel_leaderboard_scores.lua` (запись побед в Nakama Leaderboards, подключается из `duel_match3.lua`),
   - `duel_session.lua` (single-session по e-mail: эпоха сессии + уведомление при AuthenticateEmail и RPC `duel_session_claim` для silent-restore),
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
- `duel_match3_pve_catalog_get` — возвращает список этажей шахты (1..12), барьеры и текущую прогрессию.
- `duel_match3_pve_create` с payload `{"bot_id":"mine_1","floor":1,"difficulty":"easy"}` — создаёт authoritative PVE-матч и возвращает `match_id`.
  Клиент затем делает `JoinMatch` по `match_id`.
- `duel_mine_summon` с payload `{"floor":1,"difficulty":"easy"}` — мгновенно снимает КД монстра на этаже (стоимость: 5 энергии и 50 золота).
- `duel_mine_barrier_unlock` с payload `{"floor":2,"difficulty":"easy"}` — разблокирует переход на следующий этаж (с проверкой уровня и ресурсов).

Прогрессия PVE хранится в `duel_match3_progress/profile`:
- `level` (до 12),
- `xp`,
- ресурсы (`gold`, `ore`, `matter`, `energy`, ключи/чертежи),
- `mine` (сложность, открытые этажи, кд респавна/аффикс по этажам),
- `defeated` (счётчик побед по bot_id).

В `duel_match3.lua` используется каталог этажей шахты `mine_1 ... mine_12` (обычные + боссы 4/8/12) с параметрами:
- `floor`,
- `is_boss`,
- `hp_bonus`,
- `start_mana`,
- поведенческие коэффициенты ИИ (`ai_ability_chance`, `petard_bias`, `cross_bias`, `square_bias`),
- награды (`reward_xp`, `reward_gold`, `reward_ore`, `reward_matter_*`, ключи/чертежи/слитки).

Серверная статистика Match3 (по `ctx.user_id`) также доступна через RPC:
- `duel_match3_stats_get` → `{ ok, played, wins, losses }`
- `duel_match3_stats_record` с payload `{"won":true|false}` → инкрементирует сыграно/победы/поражения.

Таблица лидеров (главное меню):
- `duel_leaderboard_get` с payload `{"period":"week","type":"tournament","view_id":"tournament_ore"}`
  - `period`: `day` | `week` | `month` | `all`
  - `type`: `tournament` | `duel` | `mine`
  - `view_id`: `tournament_ore` | `tournament_gold` | `tournament_smith` | `duel_skirmish` | `duel_arena` | `mine_floor_1` … `mine_floor_12`
  - ответ: `{ ok, entries[], self_entry, rewards[] }`
  - `score` — число побед за выбранный период (authoritative leaderboard, `operator=incr`)
  - периоды по МСК: день (`00:00–23:59:59`), неделя (пн–вс), месяц (1-е — последнее число), `all` — за всё время
  - `duel_skirmish` — PvP Pro (`match3ProButton`, `pvp_pro=true`)
  - `duel_arena` — классическая 1v1 дуэль (`match3Button`)
  - победы пишутся в `duel_match3_achievements.lua` → `duel_leaderboard_scores.lua`

Онлайн / друзья (главное меню, вкладка «Онлайн»):
- `duel_online_list` с payload `{"limit":50}` (limit 1..50)
  - ответ: `{ ok, total, shown, players[{user_id,username,level}], online_ids[] }`
  - `players` — до `limit` игроков (без текущего): ник из account, `level` 1..12 из `duel_match3_progress/profile`
  - `online_ids` — все user_id в онлайн-карте
  - список друзей на клиенте — стандартный Nakama Friends API (`ListFriends` / `AddFriends` / `DeleteFriends`)

**Очистка тестовых лидербордов** (если dashboard не удаляет ID с `%` — Bad Request):

1. **PowerShell (рекомендуется):** `Server/nakama/tools/purge_leaderboards.ps1`
   - только битые: `.\purge_leaderboards.ps1 -ConsolePassword "..."` 
   - все `lb_*`: `.\purge_leaderboards.ps1 -AllLb -BrokenOnly:$false -ConsolePassword "..."`
   - Dashboard шлёт DELETE без URL-encoding `%`; скрипт кодирует ID (`%` → `%25`).

2. **Console API вручную** (порт обычно `7351`):
   ```text
   DELETE /v2/console/leaderboard/lb_duel_skirmish_w_%25GW%25V
   Authorization: Bearer <console token>
   ```

3. **RPC (временно):** положить `duel_leaderboard_admin_purge.lua` в modules, перезапустить Nakama, вызвать RPC `duel_leaderboard_admin_purge` с payload `{"mode":"broken"}` или `{"mode":"all_lb"}`, затем файл удалить.

4. **PostgreSQL** (Docker): только если API недоступен — сначала записи, потом лидерборд:
   ```sql
   DELETE FROM leaderboard_record WHERE leaderboard_id LIKE '%\%GW\%V' ESCAPE '\';
   DELETE FROM leaderboard WHERE id LIKE '%\%GW\%V' ESCAPE '\';
   ```



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


