# Escape: отдельный Docker-проект

Рабочий каталог на сервере: `/opt/escape`. Домен: `https://prokrust-play.ru`.
Compose project: `escape`; контейнеры `escape-nakama-1`, `escape-postgres-1` и `escape-api-gateway-1`,
отдельные сеть и volume `escape_postgres-data`. База создана с нуля по запросу владельца.

API через Nginx опубликован только на `127.0.0.1:27350`, dashboard gateway —
на `127.0.0.1:27352`. Прямой Console — `127.0.0.1:27351` для SSH-туннеля;
прямой игровой API и PostgreSQL не публикуют порты на хосте. Caddy обслуживает домены
через отдельный файл `/etc/caddy/sites/escape.caddy`; прежние маршруты сохранены.
Порты и контейнеры других проектов не изменяются.

Из рабочего каталога:

```sh
cd /opt/escape
docker compose ps
docker compose logs --tail 100 nakama
docker compose up -d
```

Секреты находятся только на сервере: `.env` и `secrets/server.yml`, права `0600`,
родительский каталог закрыт от других пользователей. Пароли БД, Console, HTTP RPC,
ключи подписи JWT/refresh/Console сгенерированы независимо. Клиентский `server_key`
из Unity предназначен для идентификации приложения, администраторских прав не даёт.

Dashboard доступен по `https://console.prokrust-play.ru`; вход администратора и
доступ к его API проверены. Логин `escape-admin`, пароль — в серверном
`secrets/server.yml`. Ограничения попыток входа включены.

Для 0.3.14 подготовлена отдельная форма ауры
`https://console.prokrust-play.ru/escape-admin/aura/`, использующая тот же Console
login/JWT. Каталог `../admin/aura` нужно установить в `/opt/escape/admin/aura`;
Compose монтирует его read-only в gateway. Nginx обслуживает этот фиксированный
префикс только на Console-порту 8081 с CSP и `private,no-store`; остальные
маршруты остаются прежними. До применения сравнить текущие gateway/compose с
baseline, сохранить backup и выполнить проверку конфигурации Nginx. Наличие
этих исходников не означает, что форма уже опубликована. Проверка формы не
должна активировать реальную ауру. Порядок работы: [STORAGE_ADMIN.md](../STORAGE_ADMIN.md#форма-ауры-рядом-с-console-0314).

Альтернативный доступ к Console через SSH-туннель:

```sh
ssh -L 27351:127.0.0.1:27351 beget_escape
```

Открыть `http://127.0.0.1:27351`. Логин `escape-admin`; пароль хранится в серверном
`secrets/server.yml`. Не публиковать этот файл и не копировать его в Unity/Git.

При обновлении модулей копировать только `.lua` в `/opt/escape/runtime`, сохраняя
сгенерированный `zz_escape_seed.lua`, затем:

```sh
docker run --rm --memory 256m --cpus 0.5 \
  -v /opt/escape/runtime:/nakama/data/modules:ro \
  registry.heroiclabs.com/heroiclabs/nakama:3.37.0 \
  check --runtime.path /nakama/data/modules
docker compose restart nakama
python3 security_smoke.py --url https://prokrust-play.ru
```

Smoke-тест создаёт отдельный временный аккаунт и удаляет его в конце.
Он проверяет авторизацию, запреты RPC/Storage, каталоги, WebSocket и реальный
PvE-матч с завершением и единственной записью поражения на сервере.

При первичной установке `init_config.py --client-key-file ... --catalog-dir ... --caddy-proxy-ip ...`
создаёт новые секреты и генерирует `zz_escape_seed.lua` для совместимости с
установщиком runtime. Повторное создание секретов запрещено. После запуска
PostgreSQL выполните `python3 prepare_gateway.py`, затем запустите Nakama.

Перед открытием доступа к игре проверьте системные каталоги через `inventory`.
На пустой БД явно создайте отсутствующие записи через Console API. Загрузка Lua
не гарантирует наполнение каталогов, поскольку игровой runtime также использует
`nk.run_once`. На восстановленной базе существующие записи сохраняются.

```sh
cd /opt/escape
python3 tools/storage_admin.py --url http://127.0.0.1:27351 --config secrets/server.yml inventory
python3 tools/storage_admin.py --url http://127.0.0.1:27351 --config secrets/server.yml \
  seed-defaults --catalog-dir data
```

Проверенный экспорт настроек босса можно передать как `--global-boss-file`.
Тестовые профили, инвентари и пример глобальной ауры не импортируются.

Ограничения и доверенный IP прокси описаны в [RATE_LIMITS.md](RATE_LIMITS.md).
Для существующего проекта `python3 prepare_gateway.py` проверяет gateway сети
`escape_default` и безопасно записывает его в `.env` с резервной копией.
После изменения `/opt/escape/Caddyfile` использовать `python3 update_proxy.py`:
он меняет только маршрут Escape, валидирует конфигурацию и делает graceful reload.

Дополнительные проверки:

```sh
python3 dashboard_smoke.py
python3 rate_limit_smoke.py --url https://prokrust-play.ru
```

Rate smoke намеренно исчерпывает бюджет входа для IP машины, запускающей тест;
после него нужно дождаться восстановления бюджета перед новыми тестовыми входами.

Перед изменениями базы создавать резервную копию:

```sh
docker compose exec -T postgres pg_dump -U escape -d escape -Fc > backups/escape.dump
```

Для ежедневного зашифрованного копирования, выгрузки за пределы VPS и
восстановления используйте [инструкцию по резервному копированию](backup/README.md).
Не запускать `docker compose down -v`: это удалит базу Escape. Из каталога этого
проекта не выполнять команды управления чужими Compose-проектами.

Обновление проверенной копии runtime выполняет `deploy_runtime.py` из `/opt/escape`.
Сначала изолированный многоклиентный прогон создаёт `multiclient-runtime-manifest.json`;
скрипт сверяет с ним весь будущий runtime, включая сохранённый bootstrap каталогов.
Без `--apply` выполняется только предварительная проверка. `--apply` дополнительно
создаёт проверенный pg_dump и копию прежних модулей, повторно проверяет отсутствие
боёв, активных турниров и оплаченной очереди, затем перезапускает только `escape/nakama`.
При первой миграции старой очереди из памяти Lua ожидается также отсутствие подключённых
клиентов. После старта выполняются healthcheck, security smoke и dashboard smoke;
при ошибке модули откатываются. База и пользовательские настройки не сбрасываются.

```sh
cd /opt/escape
python3 deploy_runtime.py
python3 deploy_runtime.py --expected-runtime-sha256 SHA256_FROM_PREFLIGHT --expected-tested-sha256 SHA256_FROM_ISOLATED_TESTS --apply
```

Для явно разрешённого владельцем прерывания **тестовых** боёв можно передать
`--allow-test-games` вместе с предварительной проверкой и с `--apply`. По умолчанию
активные бои по-прежнему запрещают обновление. Этот флаг снимает только ожидание
простоя Escape: проверка ответов Console, точного протестированного runtime,
резервная копия, healthcheck и откат остаются обязательными.

Применение требует явного `--expected-runtime-sha256`: новый runtime не заменит
изменённый после предварительной проверки серверный файл. Дополнительный
`--expected-tested-sha256` фиксирует изолированно проверенный кандидат. Свежая
закрытая резервная копия включает проверенный `pg_dump`, полный runtime,
конфигурацию, секреты, admin и tools; манифест хранится внутри неё. Предыдущие
резервные копии не удаляются самим установщиком.

Для узкой миграции восьми эпических цен дополнительно передаются
`--catalog-migration`, его `--catalog-migration-sha256`,
`--catalog-expected-changes 8` и проверенный `--catalog-probe-suite` с SHA256.
Сначала запускается новый runtime при старых ценах. После healthcheck отдельный
пробный аккаунт подтверждает активную проверку ожидаемой цены; только затем
выполняется CAS каталога и выдерживается 31 секунда для истечения кеша цен.
Проверка не крафтит предметы и не меняет системные данные. Если откат каталога
не доказан, установщик сохраняет runtime с проверкой цены: старый код не должен
работать с новыми ценами. Все исходные данные и отчёты сохраняются в закрытом backup.
