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

Альтернативный доступ к Console через SSH-туннель:

```sh
ssh -L 27351:127.0.0.1:27351 root@46.173.17.51
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

Первичная установка (выполнена): `init_config.py --client-key-file ... --catalog-dir ... --caddy-proxy-ip ...`
пишет новые секреты и create-only bootstrap статических каталогов. Повторное
создание credentials запрещено. Bootstrap импортирует полный `duel_match3_item_defs.json`,
каталоги ботов трёх сложностей, достижения и настройки Race. Тестовые профили,
инвентари и изменённый `duel_match3_item_catalog.example.json` не импортируются.
Пример глобальной ауры не включается автоматически.

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

Автоматическое расписание резервного копирования пока не настроено. Не запускать
`docker compose down -v`: это удалит новую базу Escape. Из каталога этого проекта
не выполнять команды управления чужими Compose-проектами.
