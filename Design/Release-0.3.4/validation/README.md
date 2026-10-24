# Проверки Escape 0.3.4

**Editor UI QA, управляемая компиляция, production runtime, финальный APK и публичная доставка 0.3.4 подтверждены.** Это отдельные области подтверждения. Физическое Android-устройство, FPS и GC не проверены.

## Точная область UI-журнала

[qa-results.jsonl](qa-results.jsonl) — **120 ответов** из исходных строк **723–842** `.utmp/expedition/qa-results.jsonl`. Первый ID `aefbf6daf3284f05a55af3d053f3d9b1`, последний `ac8d7fe69c1841258a0670eff40c800a`. Bridge replies не содержат timestamps: граница закреплена номерами строк и IDs в [ui-source-scope.json](ui-source-scope.json). [ui-qa.json](ui-qa.json) связывает все 11 пунктов с точными IDs/receipts.

Окружение: Unity **6000.3.9f1**, Windows Editor Play Mode, Game View **1440×3088**. Исходный журнал сохранён вместе с неуспешной ранней пробой очереди и поздним запросом тапа до готовности UI; успешные повторы не стирают эту историю.

| Отчёт | Что подтверждено | Область |
|---|---|---|
| [sound-bindings.json](sound-bindings.json) | Настоящие manager serialized references на Bloodlust/Heroism OGG; исходный critical WAV и meta совпадают по SHA-256 | Проверка привязки и сохранности, не заявление о прослушивании каста или звука телефона |
| [achievement-ui-results.json](achievement-ui-results.json) | Два прохода: total310/две предыдущие claims, третья250/250, зелёные checks/«Получено», явное eligible действие, порядок claim, original art mesh aspect и all-claimed состояние | Настоящий каталог/production UI с временным in-memory cache; восстановлен, helper без claim/RPC/PlayerPrefs writes |
| [leaderboard-ui-results.json](leaderboard-ui-results.json) | Два show/verify/restore, второй после удаления podium loader; четыре actual prefab, значения/рамки/выравнивание, без фигур | Подписанные UI-строки, не настоящие участники или запись scores |
| [battle-polish-results.json](battle-polish-results.json) | normal и серверный показатель снятия маны0/24/50, timer60/turn/board, shield-popover separation, cooldown2, OGG references; восстановлено | UI-only fixture на настоящем локальном поле; число на петарде — `−N` маны, а не запас петард и не максимум mana bar |
| [match-states-results.json](match-states-results.json) | Семь финальных сценариев, все passed: search, search-error, victory, defeat, draw, refund, many-rewards; safe area, callbacks, overflow12/последняя строка | Локальные production-renderer fixtures, без matchmaking/RPC/account actions; cleanup подтверждён |
| [result-row-integration.json](result-row-integration.json) | Actual manager `RefreshGameOverRewardRows` → production viewport; original sprites и временные +120/+240/+85, refund+2; восстановлено | Интеграция существующего writer с UI, не реальные начисленные награды и не gameplay ShowGameOver |
| [queue-lifecycle-results.json](queue-lifecycle-results.json) | Создание → render → verify: три точки нижележащего HUD/nav заблокированы, доступен только выход, bridge active; восстановлено | Exit callback временно заменён; leave/refund RPC не выполнялся |
| [arena-dto-ui-results.json](arena-dto-ui-results.json) | Production DTO из isolated backend: after-QF(index2)6rows/8knownHP, after-SF(index4)7/12, QF-countdown(index0)4/0; маршруты/история/lock/restore | Реальные ответы протокола, локально отрисованные в Unity; этот Unity-клиент не разыгрывал оплаченный турнир |
| [startup-qa.json](startup-qa.json) | Три настоящих первых render-frame MainMenu защищены: сначала opaque startup cover, затем готовые World/header при скрытом legacy | Проверка каждого render callback и кадры; не FPS/запуск на телефоне |
| [compiler-results.json](compiler-results.json) | Runtime / Editor / Android Player conditional exit0/0/0 | Roslyn, не release IL2CPP APK |

Исходные финальные match-state receipts: [search](match-states/search.json), [search-error](match-states/search-error.json), [victory](match-states/victory.json), [defeat](match-states/defeat.json), [draw](match-states/draw.json), [refund](match-states/refund.json), [many-rewards](match-states/many-rewards.json). Они проверяют параметры настоящих компонентов и повторное применение темы/единственность callback, но не реальную выплату за игровой матч.

Compiler evidence: [Runtime log](runtime-compile.log), [Editor log](editor-compile.log), [Player conditional log](player-config-compile.log), [player-config-result.json](player-config-result.json). Runtime/Editor exit0 подтверждены root для финальных команд, соответствующие логи пусты; Player exit0 есть в исходном JSON. Сборка APK отслеживается отдельно.

## Финальные кадры и их подписи

[gallery-manifest.json](gallery-manifest.json) содержит **17 кадров** с source paths, размерами, bytes и SHA-256. Root просмотрел итоговые кадры; они не являются снимками физического устройства.

Настоящие Editor-страницы после восстановления временных состояний: [убежище](../screenshots/hub.png), [герой](../screenshots/character.png), [шахта](../screenshots/mine.png), [достижения](../screenshots/achievements.png). Первые три предназначены для веб-галереи; персональных email/settings окон нет.

Явные UI-фикстуры: [рейтинг](../screenshots/ranking.png), [щит/таймер](../screenshots/battle-shield.png), [снятие маны50](../screenshots/race-bank50.png), [очередь](../screenshots/tournament-queue.png), [QF](../screenshots/bracket-qf.png), [SF](../screenshots/bracket-sf.png), [финал](../screenshots/bracket-final.png), [manager victory rows](../screenshots/victory-real-rows.png), [manager refund rows](../screenshots/refund-real-rows.png), [поиск](../screenshots/match-state-search.png), [поражение](../screenshots/match-state-defeat.png), [ничья](../screenshots/match-state-draw.png), [12 наград/scroll](../screenshots/match-state-many-rewards.png). Временные суммы не выдавались аккаунту.

Дополнительные первые render-frame: [1](startup/startup-first-01.png), [2](startup/startup-first-02.png), [3](startup/startup-first-03.png). В первом ещё виден только защитный cover; готовая оболочка и скрытый legacy подтверждены во втором/третьем. Это не замер времени запуска или производительности.

## Неуспешные первые попытки и окончательная проверка

- Reply `e4ebe66cde92447eaf61d118deb2beaa`, строка740: первоначальная проверка queue raycast в кадре создания до первого mesh depth не прошла. Проба разделена на создание/отрисовку/verify, queue blocker усилен; финальный reply `7b58f187f9524396b7d855ef49a00571` подтвердил все три нижележащие точки и только кнопку выхода. Состояние восстановлено. История не скрыта и не выдаётся за прошедший первый запуск.
- Reply `418785ea4c08493490ba24fcad1fd1b8`, строка831: tap до готовности активной кнопки/EventSystem. После scene-ready ответ87d905d3cabd4c79b98d7f37d7be188b и фактический tap4dc5e1e24ced49cb9bf66eb52ef3aa4d прошли; рейтинг повторно показан, проверен и восстановлен. Это timing запроса, не оставшийся продуктовый дефект.

Нет общего утверждения «вся QA без RPC»: обычные lobby/profile-чтения Unity продолжались. Ограничения no-RPC/no-spend/no-write относятся только к конкретным перечисленным локальным helper-сценариям. Backend-тесты ниже выполняли настоящие транзакции временных аккаунтов.

## Backend и production

[multiclient-isolated-arena.json](multiclient-isolated-arena.json): **108 passed**, errors `[]`, настоящие независимые клиенты изолированного backend и шесть production DTO snapshots. [Runtime manifest](multiclient-runtime-manifest.json) содержит 36 файлов, SHA-256 `4df827d71553c806353dd2a0a49ea58e9bbaf6599938b5fa34818daeff482f7e`. [Локальный Lua log](arena-lua-and-race-regression.log): 27 arena-инвариантов и 37 Race-регрессий. Это регрессионные сценарии, не нагрузка и не несколько Android-приложений.

[Production deployment](runtime-deploy-result.json), [log](runtime-deploy.log): тот же проверенный runtime выложен после idle preflight, backup `/opt/escape/backups/runtime-release-20261024T090402Z`; 25 security, 3 dashboard и health прошли, rollback не потребовался, существующая API-конфигурация не менялась. [arena-overlay-preparation.json](arena-overlay-preparation.json) и [arena-implementation-checks.json](arena-implementation-checks.json) — более ранние этапные снимки подготовки агента: они не заменяют окончательные root UI/deployment receipts.

[resource-cleanup.json](resource-cleanup.json): старый podium PNG убран из Resources и сохранён в Design/SourceArt, loader удалён, serialized GUID consumers0. **2 762 012 байт исходного PNG не являются измеренной экономией APK или GPU memory.** [development-source-audit.json](development-source-audit.json) отдельно фиксирует четыре подтверждённых source blockers на24Oct, не исправленных в этом UI-релизе; локальное example140/190 не выдаётся за сегодняшнюю live Storage-проверку.

## Сборка и публичная доставка

| Подтверждение | Результат |
|---|---|
| [Сборка](android-build-status.json), [APK validation](apk-validation.json) | Succeeded за **496,11 с**, ошибок 0, предупреждений 2; версия 0.3.4 / code 6; ARMv7 + ARM64; nondevelopment/nondebuggable |
| [Подпись](apk-signature.txt), [совместимость обновления](update-compatibility.json) | v2 проверена; тот же сертификат Android Debug, что у 0.3.3, versionCode5 →6; без физической установки обновления |
| [Публикация](deploy-result.json), [публичные проверки](public-release.json) | published/passed: page/manifest/APK 200, Range 206, MIME/кэш корректны; прежний APK 0.3.3 доступен, API 200 |
| [Полный серверный SHA-256](server-apk-hash.json) | Совпадает с локально проверенным APK и публичным манифестом |
| [Browser QA](browser-qa.json), [снимок](public-release-browser.png) | Root просмотрел опубликованный блок «Что нового»,0.3.4/97,1MiB и обе ссылки. Viewport 1265×712; без нового responsive/physical-device теста |

[Итоговый APK — 97,1 МиБ](https://prokrust-play.ru/escape/downloads/Escape-0.3.4-45ee39a11284.apk): **101 805 879 байт**, SHA-256 `45ee39a11284774b74e76a29873be0136f3c66f0cd0570cabe58e0df580b4a0a`. В отличие от HEAD/Range, отдельный server receipt проверяет хеш целого выложенного файла. Сертификат SHA-256 `a813aa59b19a728fa65298fc9fe7c921f7eed37285edee0c912852f1b2fc26b0` сохранён; совместимость подтверждена статически, не обновлением телефона.

Проверка OGG ограничена SerializedObject-привязками и неизменностью critical WAV/meta: реальный PlayOneShot и прослушивание не выполнялись, включая Editor. Это не изменяет успешность проверки привязки и не выдаётся за аудио-тест устройства.

`physical_device_tested=false`, `native_android_ime_tested=false`, `fps_measured=false`, `gc_profiled=false`. Наличие анимаций и успешных Editor/backend сценариев не подтверждает мобильную производительность, весь Android lifecycle или восстановление после катастрофы.
