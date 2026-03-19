# Nakama: дуэльные PIN домофона

1. Скопируйте `modules/duel_keypad.lua` в каталог **modules** вашего Nakama (часто `data/modules/` в Docker-образе).
2. Перезапустите Nakama.
3. В логах при старте не должно быть ошибок загрузки скрипта.

Клиент после входа в матч вызывает RPC `duel_match_ensure_pins`, затем при каждом вводе — `duel_keypad_guess`. В теле JSON передаются **`match_id`**, **`user_a`**, **`user_b`** — два реальных UUID участников дуэли, **уже отсортированные** (`user_a` &lt; `user_b` по строковому сравнению). Запись в storage создаётся у **владельца `user_a`** (лексикографически меньший id), чтобы выполнялся FK `storage_user_id_fkey` (пользователь должен существовать в БД Nakama). RPC разрешены только если `ctx.user_id` совпадает с одним из двух UUID.

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
