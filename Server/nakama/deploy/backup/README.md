# Резервное копирование Escape

Каждый день в 04:00 по Москве служба сохраняет всю PostgreSQL-базу `escape`. В неё входят учётные записи и их привязки, прогресс, инвентарь, баланс, достижения, таблицы лидеров и серверные каталоги из Storage. Снимок включает операции, уже зафиксированные в БД на момент начала выгрузки. Состояние активного боя, которое ещё находится в памяти Nakama, в снимок не входит.

Служба запускает `pg_dump -Fc`, проверяет оглавление архива через `pg_restore --list`, шифрует его `age` и публикует вместе с размером и SHA-256. Незашифрованный дамп находится во временном файле без имени, доступном только процессу резервного копирования. После успешной публикации и проверки хешей двух последних архивов служба убирает старые копии из `/var/lib/escape-backup/snapshots`. Два архива на VPS дополняет отдельное хранилище.

## Установка на VPS

На сервере нужны Python 3, `age`, Docker Compose, systemd и OpenSSH. Существующий Compose-проект должен быть доступен как `/opt/escape` и содержать сервис `postgres`. На доверенной машине создайте отдельную пару `age` (`age-keygen -o escape-age.key`, затем `age-keygen -y escape-age.key > recipient.pub`) и отдельный SSH-ключ выгрузки (`ssh-keygen -t ed25519 -f escape-backup-reader`). Перенесите на сервер только `recipient.pub` и `escape-backup-reader.pub`. Сохраните закрытый ключ `age` и его резервную копию вне VPS и Git.

Из этого каталога под `root` на VPS установите служебные файлы и укажите пути к двум открытым ключам.

```sh
getent group escape-backup >/dev/null || groupadd --system escape-backup
id -u escape-backup >/dev/null 2>&1 || useradd --system --gid escape-backup \
  --no-create-home --home-dir /var/lib/escape-backup/reader-home --shell /bin/sh escape-backup
install -d -o root -g root -m 0755 /usr/local/lib/escape-backup
install -d -o root -g escape-backup -m 0750 /etc/escape-backup \
  /var/lib/escape-backup /var/lib/escape-backup/snapshots /var/lib/escape-backup/reader-home
install -o root -g root -m 0644 backup.py /usr/local/lib/escape-backup/backup.py
install -o root -g root -m 0644 recipient.pub /etc/escape-backup/recipient.pub
printf 'restrict,command="/usr/bin/python3 -I /usr/local/lib/escape-backup/backup.py export" %s\n' \
  "$(cat escape-backup-reader.pub)" > reader-authorized-keys
install -o root -g escape-backup -m 0640 reader-authorized-keys /etc/escape-backup/reader-authorized-keys
install -o root -g root -m 0644 escape-backup.sshd.conf /etc/ssh/sshd_config.d/90-escape-backup.conf
sshd -t
sshd -T -C user=escape-backup,host=localhost,addr=127.0.0.1
systemctl reload ssh.service
install -o root -g root -m 0644 escape-backup.service escape-backup.timer /etc/systemd/system/
systemctl daemon-reload
systemctl start escape-backup.service
systemctl enable --now escape-backup.timer
systemctl list-timers escape-backup.timer
```

Перед перезагрузкой SSH проверьте в выводе `sshd -T` настройки для `escape-backup`. Должны примениться `forcecommand`, `authorizedkeysfile`, `authenticationmethods publickey`, `disableforwarding yes` и `permittty no`. На системах, где служба называется `sshd.service`, используйте её имя. Оставьте администраторскую SSH-сессию открытой до проверки нового подключения. `/bin/sh` нужен SSH для запуска принудительной команды. Интерактивная сессия и пересылка отключены, пароль заблокирован, файлы авторизации доступны для записи только `root`. В группу `escape-backup` не добавляйте других пользователей.

## Внешняя копия

Выгрузку запускайте по расписанию на машине или у провайдера, которые сохранят доступ при потере основного VPS. Проверьте отпечаток сервера и сохраните его в `known_hosts` до первого автоматического подключения. На Windows используйте [`Pull-EscapeBackup.ps1`](Pull-EscapeBackup.ps1). Протокол SSH возвращает список через команду `list` и двоичный зашифрованный архив через `get <имя>`. Скачанный файл сверяйте по размеру и SHA-256, прежде чем добавить его к внешним копиям.

```sh
ssh -T -o StrictHostKeyChecking=yes -i escape-backup-reader escape-backup@HOST list
ssh -T -o StrictHostKeyChecking=yes -i escape-backup-reader escape-backup@HOST \
  'get escape-YYYYMMDDTHHMMSSZ.dump.age' > escape-YYYYMMDDTHHMMSSZ.dump.age.part
```

Список — JSON в UTF-8 с полем `schema` равным `1` и массивом `backups`. У записи есть `name`, `createdUtc`, `bytes` и `sha256`. Снимки упорядочены от свежих к старым. Во время ротации повторите `list`, если выбранный снимок уже исчез. Для постоянно работающего внешнего хранилища оставляйте 14 дневных и 8 недельных копий и проверяйте, что последняя успешно выгруженная копия моложе 26 часов. Если компьютер выключен, выгрузки на него возобновятся после включения и доступны будут только снимки, оставшиеся на сервере.

В текущей установке задача Windows `Escape Backup Pull` запускается при входе владельца в систему и каждые 30 минут, пока он в системе. Каталог `%LOCALAPPDATA%\EscapeBackup\snapshots` хранит 48 последних скачанных архивов и файлы `.sha256`. Результат последней попытки — в `%LOCALAPPDATA%\EscapeBackup\last-run.log`. Закрытый ключ расшифровки — `%LOCALAPPDATA%\EscapeBackup\keys\recovery.agekey`. Храните его резервную копию отдельно от компьютера и VPS.

## Восстановление

Сверьте размер и SHA-256 зашифрованного файла с манифестом. Расшифровывайте архив на доверенной машине в закрытом каталоге, вне Git. Откройте его `pg_restore --list`, затем выполните полное пробное восстановление в отдельный пустой PostgreSQL той же основной версии. Убедитесь, что количество пользовательских записей, привязок устройств, Storage-объектов и записей Wallet совпадает со значениями на момент снимка, если они зафиксированы. Архив с персональными данными не публикуйте.

```sh
umask 077
age --decrypt -i escape-age.key -o escape.dump escape-YYYYMMDDTHHMMSSZ.dump.age
pg_restore --list escape.dump
```

Для восстановления на чистом VPS запустите только PostgreSQL и дождитесь готовности пустой БД `escape`. В Linux подайте дамп в `pg_restore` через stdin. После успешного восстановления запустите Nakama с миграциями и выполните серверную проверку. Для уже работающей БД с новым прогрессом поднимите старый архив отдельно и оцените перенос по данным, прежде чем что-либо перезаписывать.

```sh
cd /opt/escape
docker compose -p escape up -d --wait postgres
docker compose -p escape exec -T postgres pg_restore --exit-on-error --single-transaction \
  --no-owner --no-privileges -U escape -d escape < /private/escape.dump
docker compose -p escape up -d --wait
```

Используйте конфигурацию и runtime совместимой версии из резервной копии конфигурации или Git. Сервис сохраняет только БД. Закрытые конфигурации и ключи подписи сессий храните отдельно в зашифрованном виде, если нужно сохранить действующие сессии при переезде. С новыми ключами игрокам потребуется заново войти. Проводите пробное восстановление внешней копии раз в месяц.
