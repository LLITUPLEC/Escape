# Проверки Escape 0.3.3

Финальная UI Play QA и изолированные серверные тесты выполнены в указанной ниже области; **production runtime, итоговый APK и страница 0.3.3 опубликованы и проверены**. Исправление слоёв применено после завершения первого кандидата, проверено на настоящем интерфейсе и включено в новую сборку, начатую **20:24:31 UTC**. Эта финальная сборка успешно завершилась за **583,58 с**, ошибок 0, предупреждений 2. Результаты 0.3.2 и первого кандидата 0.3.3 не используются вместо проверки итогового APK.

## Editor и UI

[ui-qa.json](ui-qa.json) описывает Unity 6000.3.9f1 Windows Editor Play Mode, Game View 1440×3088, настоящие сцены и явно отмеченные UI-фикстуры.

- [qa-results.jsonl](qa-results.jsonl) — 166 ответов текущей итерации, исходные строки 557–722; финальный проход после patch начинается со строки 668. Bridge не сохраняет timestamps, поэтому диапазон задан номерами строк и reply IDs в UI-отчёте. Включены ранние проверки, отдельные ошибки автоматизации и финальные успешные повторы.
- [achievement-probe-results.json](achievement-probe-results.json) — точный ответ `ExpeditionAchievementsQa.Run("all")`: реальные определения каталога, delta targets, 309→310, одна доставка в настоящий toast host, dedup, последовательность claim, live badge и повторное использование карточек. Cache, UI и toast восстановлены; RPC/claim/PlayerPrefs writes отсутствовали.
- [mine-travel-play.json](mine-travel-play.json) — финальный actual-frame probe revision `mine-0.3.3`, завершён 20:22:18 UTC. Проверяет геометрию комнат/шахты/кабины, таймер и реальные промежуточные положения, прибытие, retarget и отмену при ручном вводе. Это не FPS-тест.
- [bracket-ui-results.json](bracket-ui-results.json) — финальный production-рендер сетки с UI-only данными четвертьфинала/полуфинала/финала. Семь пар; 0/8/12 известных HP и 14/6/2 неизвестных HP соответственно. Проверены скрытие меню, сохранение активности объекта ArenaMenuBinder, видимость header/nav и восстановление меню после disable/destroy. Ставки блокировались; исходные данные восстановлены. Эта фикстура не совершает вход в турнир и не подтверждает сетевой турнир. Исходные helper reports: [qf](bracket-qf-report.json), [sf](bracket-sf-report.json), [final](bracket-final-report.json), [verify](bracket-verify-report.json), [restore](bracket-restore-report.json).
- [podium-probe-results.json](podium-probe-results.json) — три тестовые фигуры совмещены с реальными нарисованными поверхностями. Исходные данные представления восстановлены, leaderboard RPC не вызывался; фигуры на кадре не являются реальной выборкой участников.
- [Runtime log](runtime-compile.log), [Editor log](editor-compile.log), [Android conditional log](player-config-compile.log), [player-config-result.json](player-config-result.json) — Roslyn-проверки из `validation-release033`. Compiler logs пусты; Android symbols exit 0 записан в исходном JSON. Это не результат APK/IL2CPP.

Итоговые кадры 1440×3088: [убежище](../screenshots/hub.png), [герой](../screenshots/character.png), [шахта](../screenshots/mine.png), [закрытый этаж](../screenshots/mine-locked.png), [мастерская](../screenshots/workshop.png), [достижения](../screenshots/achievements.png), [бой](../screenshots/battle.png). View-only proof: [полуфинал](../screenshots/bracket-sf.png), [финал](../screenshots/bracket-final.png), [подиум](../screenshots/podium.png). Battle сохранён из более раннего подтверждённого прохода 0.3.3, остальные девять кадров обновлены после финальных исправлений. Размеры, SHA-256 и source paths есть в UI-отчёте. Settings/email окон нет.

[layering-fix-validation.json](layering-fix-validation.json) сохраняет два первоначальных продуктовых дефекта и финальные подтверждения исправлений. Общий World теперь имеет sorting order **5**, legacy Mine/Workshop UI — **20**, header — **70**. Возврат в убежище проверен из мастерской и арены. Турнирная сетка скрывает меню, сохраняет активный объект контроллера и видимые HUD/nav; после закрытия меню восстановлено. Отложенный patch применён после окончания первого APK, Runtime/Editor/Player compile прошли с кодом 0; затем выполнена настоящая Play QA. Старые пустые/неверные снимки заменены финальными. Bounds/nav-тесты первого прохода сами по себе не обнаруживали эти дефекты.

В `automation_incidents` UI-отчёта отдельно отмечены запросы в момент смены сцены (`Sequence contains no elements`, затем ожидаемый hub ещё был Workshop) и опечатка `Depth4` вместо разрешённого `Depth_4`. Запросы повторены после готовности сцены/исправления target и прошли. Эти случаи не записаны как оставшиеся продуктовые дефекты; исходные ответы журнала сохранены.

## Серверные проверки

| Отчёт | Подтверждено | Область |
|---|---|---|
| [multiclient-isolated-race-policy.json](multiclient-isolated-race-policy.json) | 41/41, errors `[]` | Изолированный backend и временные аккаунты: public config 2/4, оплата/settlement, refund против бота, защита от повторной обработки |
| [multiclient-isolated-arena.json](multiclient-isolated-arena.json) | 75/75, errors `[]` | Изолированный backend, восемь независимо авторизованных тестовых клиентов, турнир и авторитетные результаты |
| [multiclient-runtime-manifest.json](multiclient-runtime-manifest.json) | Хеши проверенного runtime | Привязка изолированных тестов к конкретным server modules |
| [lua-unit-tests.log](lua-unit-tests.log) | Race 37; arena 25 | Локальные Lua-фикстуры безопасности/транзакций |
| [lua51-syntax.log](lua51-syntax.log) | Пять runtime-модулей | Синтаксис Lua 5.1 |
| [server-runner-offline-checks.json](server-runner-offline-checks.json) | Offline guards и mapping | Этот конкретный offline receipt явно не запускает сеть/backend; live проверки находятся в двух отдельных multiclient receipts выше |
| [runtime-deploy-result.json](runtime-deploy-result.json), [runtime-deploy.log](runtime-deploy.log) | Production deployment passed; security 25, dashboard 3, health passed | Выложен runtime с тем же manifest hash, idle preflight пройден, rollback не потребовался; существующая API-конфигурация не менялась |

Изолированные сетевые тесты и production deployment — разные подтверждения. Выкладка завершена отдельным receipt: runtime SHA-256 `f42dd93998e33aa259a62039eeaa1715bcd069e2a191a6f515612a9677340e65`, backup `/opt/escape/backups/runtime-release-20261023T201226Z`; security/dashboard/health прошли. Тесты использовали временные аккаунты, а не изменения данных пользовательского аккаунта из Editor UI-проверок.

## Ресурсный аудит

[resource-candidates.json](resource-candidates.json) — read-only снимок до очистки root:

| Исходный PNG | Байт | Почему кандидат |
|---|---:|---|
| `battle-stage-v3.png` | 2 248 524 | Заменён `battle-world-v4`; нет runtime key или serialized GUID consumers. Осталось лишь правило Editor importer |
| `mine-landing-v2.png` | 2 130 751 | Заменён dry/damp v3 rooms; нет runtime key или serialized GUID consumers |
| `workshop-forge-v2.png` | 2 798 759 | Был явно загружен в отключённый Image; удалять вместе с foreground loader, а не только скрывать Image |

Во время подготовки документов root удалил эти PNG/`.meta` и старый forge Load: итоговый аудит отмечает их отсутствие. Автор аудита Assets не редактировал. Совокупный размер исходных PNG — **7 178 034 байт**, это не измеренная экономия APK или GPU memory. `mine-gate-v2` и `mine-encounter-v2` остаются востребованы шахтой/MonsterModalView.

## Итоговые receipts

| Артефакт | Статус |
|---|---|
| [Исправление слоёв](layering-fix-validation.json), повторная UI QA и итоговая галерея | passed |
| [Сборка](android-build-status.json), [APK](apk-validation.json), [подпись](apk-signature.txt) | Succeeded; 0.3.3 / code 5; ARMv7 + ARM64; nondevelopment/nondebuggable; подпись v2 проверена |
| [Production runtime deployment](runtime-deploy-result.json) и security/dashboard/health | passed |
| [Публикация](deploy-result.json), [HTTP/MIME/Range/кэш и API](public-release.json), [полный серверный SHA-256](server-apk-hash.json) | published / passed; предыдущий APK 0.3.2 сохранён, API HTTP 200 |
| [Браузерная проверка](browser-qa.json), [кадр](public-release-browser.png) | passed: опубликованный блок «Что нового», версия/размер и обе ссылки; viewport 1265×712, без нового responsive/phone теста |
| [Совместимость обновления](update-compatibility.json) | passed: тот же сертификат, что у 0.3.2; versionCode 4 → 5; без установки обновления на устройство |
| [Сохранность critical sound](critical-sound-preserved.json) | passed: исходный WAV и meta не изменились по SHA-256 |

Итоговый [APK — 96,5 МиБ](https://prokrust-play.ru/escape/downloads/Escape-0.3.3-b355794e9bcc.apk): **101 203 775 байт**, SHA-256 `b355794e9bcc3af0b7f3abe2400b13a338c5b8352892da0fb1b33e757a0d8846`. Хеш совпадает в локальной APK-валидации, манифесте публикации и проверке целого серверного файла. Используется прежний сертификат Android Debug; SHA-256 сертификата `a813aa59b19a728fa65298fc9fe7c921f7eed37285edee0c912852f1b2fc26b0` совпадает с 0.3.2.

`physical_device_tested=false`, `native_android_ime_tested=false`, `fps_measured=false`, `gc_profiled=false`. Наличие плавной анимации и успешных Editor/изолированных тестов не является подтверждением производительности или нативного ввода Android на телефоне.
