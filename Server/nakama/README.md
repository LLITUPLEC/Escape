# Nakama: Match3 authoritative

Текущее изолированное развёртывание Escape: `https://prokrust-play.ru`, сервер
`46.173.17.51`, каталог `/opt/escape`, Compose project `escape`.
Инструкции — [deploy/README.md](deploy/README.md), результаты проверки безопасности —
[SECURITY_REVIEW.md](SECURITY_REVIEW.md).

Администрирование глобального Storage и готовые шаблоны — [STORAGE_ADMIN.md](STORAGE_ADMIN.md).
Проверка фактических каталогов и экономики — [BALANCE_REVIEW.md](BALANCE_REVIEW.md);
результаты многоклиентных боёв и турнира — [MULTICLIENT_REVIEW.md](MULTICLIENT_REVIEW.md).

Глобальные каталоги и разрешения отладки принадлежат системному UUID
`00000000-0000-0000-0000-000000000000`. При обновлении прежней базы каталоги под
старым UUID необходимо отдельно перенести; новое развёртывание создаёт чистую базу.

1. Скопируйте файлы модулей в каталог **modules** вашего Nakama (часто `data/modules/` в Docker-образе):
   - `duel_online.lua` (онлайн-статус: ping/count, leave, list до 50 игроков),
   - `duel_leaderboard.lua` (таблица лидеров: RPC `duel_leaderboard_get`),
   - `duel_leaderboard_scores.lua` (запись побед в Nakama Leaderboards, подключается из `duel_match3.lua`),
   - `duel_session.lua` (single-session по e-mail: эпоха сессии + уведомление при AuthenticateEmail и RPC `duel_session_claim` для silent-restore),
   - `duel_match3.lua` (server-authoritative Match3; проверка `session_epoch` для мутаций встроена в этот файл, отдельный `require` не нужен),
   - `duel_matchmaker.lua` (matchmaker hook, создаёт match3-матч).

2. Перезапустите Nakama.

3. В логах при старте не должно быть ошибок загрузки скриптов.



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
- Итоги записываются только authoritative match handler после завершения матча.
  Legacy RPC `duel_match3_stats_record` всегда отклоняет запись; локальный SoloBot
  не меняет доверенную серверную статистику.

В 0.3.13 `duel_match3_stats/summary.match_result_ids` хранит последние **4 ID**; существующий список из 64 ID нормализуется при серверном чтении/записи. Итоговые счётчики сохраняются отдельно. `duel_match3_journal/result:*` сохраняет **4 последние завершённые/отменённые квитанции на игрока** по всем режимам. До завершения квитанция и её `pending` защищены; служебная запись `retention_v2` также остаётся. После выдачи новых результатов старые завершённые записи удаляются автоматически, при сбое удаления — повторным проходом очистки. Поэтому четыре — лимит недавней завершённой истории, а не всей коллекции вместе с незавершённой работой и метаданными.

Повторную выдачу после удаления старой квитанции блокирует защищённый серверный ticket с монотонным sequence/nonce, выданный при создании боя. Закрытые sequence и незавершённые пробелы учитываются в `retention_v2`; старый удалённый результат не может зарегистрироваться заново по тому же UUID. Новое незавершённое действие не принимается при лимите 128, вместо потери уже заработанной награды. Активация эликсира имеет отдельный CAS-журнал из четырёх последних операций и неизменяемый `use_seq` при сетевом повторе.

Обработчик очистки запускается через RPC не чаще одного общего прохода в **60 секунд**: страницы по **100 записей**, до **10 страниц суммарно**, бюджет **250 мс между страницами**, сохранённые курсоры и порядок обхода коллекций. Незавершённые награды, активные оплаченные входы, статистика и вещи не удаляются. Для Journal действует лимит четырёх завершений; для остальных допустимых терминальных квитанций Race/PvE сохраняется возраст 30 дней, для неактивных лимитеров — семь дней. Фактическое удаление зависит от прохождения курсора и успеха CAS. Политика, миграция и исторический read-only объём базы описаны в [DURABLE_RECOVERY.md](DURABLE_RECOVERY.md#bounded-deduplication-and-automatic-retention-0313).

Таблица лидеров (главное меню):
- `duel_leaderboard_get` с payload `{"period":"week","type":"tournament","view_id":"tournament_ore"}`
  - `period`: `day` | `week` | `month` | `all`
  - `type`: `tournament` | `duel` | `mine`
  - `view_id`: `tournament_ore` | `tournament_gold` | `tournament_smith` | `duel_skirmish` | `duel_arena` | `mine_floor_1` … `mine_floor_12`
  - ответ: `{ ok, entries[], self_entry, rewards[] }`
  - `score` — число побед за выбранный период (authoritative leaderboard, `operator=incr`)
  - периоды по МСК (UTC+3): день (`00:00–23:59:59`), неделя (**пн–вс**, bucket = дата понедельника `YYYYMMDD`), месяц (1-е — последнее число), `all` — за всё время
  - устаревшие `lb_*_d_*` / `_w_*` / `_m_*` удаляются автоматически при записи/чтении рейтинга (не чаще раза в час); `*_all` сохраняются
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

3. **Игрового RPC очистки нет:** `duel_leaderboard_admin_purge.lua` — безопасный
   пустой модуль для перезаписи старых развёртываний. Очистка доступна администратору
   через Console API и SSH-туннель; не добавляйте её в API игрока.

4. **PostgreSQL** (Docker): только если API недоступен — сначала записи, потом лидерборд:
   ```sql
   DELETE FROM leaderboard_record WHERE leaderboard_id LIKE '%\%GW\%V' ESCAPE '\';
   DELETE FROM leaderboard WHERE id LIKE '%\%GW\%V' ESCAPE '\';
   ```
