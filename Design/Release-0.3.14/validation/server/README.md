# Серверные проверки Escape 0.3.14

Финальный runtime `dfd08b4dd99dfeef305c3a31ae3347dca5c18e9a444a96f44669901db49c1c3f`, 62 файла. Изолированный stage: `/opt/escape/release0314-validation-11a1588d5c34`. Production обновлён 27.10.2026; итоговая проверка health снята в 06:32:27 UTC. Публикация клиента и сайта здесь не подтверждается.

| Файл | Что подтверждает |
| --- | --- |
| [network-validation-summary.json](network-validation-summary.json) | Итоговые 671 all + 64 recovery, ноль провалов и ошибок, точные байты runtime, production не менялся во время изолированных прогонов. |
| [expected-tested-runtime.json](expected-tested-runtime.json) | Хеш каждого установленного Lua-файла, включая сохранённый server seed. |
| [local-source-match.json](local-source-match.json) | Совпадение 61 локального модуля с 62 проверенными файлами и подтверждение 20 критичных цепочек. |
| [multiclient-isolated-all.json](multiclient-isolated-all.json) | Финальный полный прогон: обычные бои и турниры, surrender, shop CAS, общий босс, mail и новые stats/ranking/timer. |
| [multiclient-isolated-recovery.json](multiclient-isolated-recovery.json) | Настоящие SIGKILL/restart, восстановление попытки босса, офлайн-наград и рейтинга; пагинация, одна сумма на аккаунт, запрет прямой подделки native score. |
| [production-deployment0314.json](production-deployment0314.json) | Живой runtime совпадает с финальным tested manifest; HTTPS200, Nakama healthy, приватные backup существуют. |
| [runtime-deployment.log](runtime-deployment.log) | Существующий безопасный deployment helper: backup, health, 31 проверка защиты, 3 проверки панели и удаление собственного тестового аккаунта. |
| [production0314-smoke.json](production0314-smoke.json) | 21 проверка новых живых API и цен на ещё одном новом аккаунте, удаление finally, runtime не менялся; без боёв или выдачи наград. |
| [production-shop-price-migration.json](production-shop-price-migration.json) | Применены ровно четыре фиксированных предложения, 18 других сохранены; исходная системная строка осталась в приватном backup. |
| [deployment-preparation.json](deployment-preparation.json) | Предыдущий установленный runtime, точный проверенный overlay и резервная копия прежних deployment inputs. |
| [production-rank-cache-compatibility.json](production-rank-cache-compatibility.json) | Четыре разрешённых несекретных параметра сервера: native rank cache не отключён. |

`multiclient-isolated-global-boss.json` — ранний целевой прогон 31 проверки с настоящими образцами DTO для клиентской Unity-проверки. Финальным свидетельством служат `all` и `recovery` после исправления тестового читателя сообщений таймера. Старый читатель мог принять оставшийся в очереди заголовок часов за новый ход; runtime при исправлении теста не менялся. Тестовые подготовленные строки восстановления явно помечены в отчёте и не выдаются за пользовательские бои.

Полный backup БД и исходная строка магазина **не копировались в репозиторий**. Публичные строки рейтинга проверялись по списку разрешённых полей, без сохранения чужих аккаунтов в послерелизный отчёт. Серверные проверки не подтверждают работу на физическом Android-устройстве.

Предыдущая успешная установка `b743…` и её отчёты сохранены в [initial-b743](initial-b743/). Перед финальным повтором `dfd08b4…` один изолированный прогон прервался на HTTP `RemoteDisconnected`: 13 PASS, без провалов assertions или Lua stack. Его доказательства оставлены в [transport-first-dfd](transport-first-dfd/). Финальный полный повтор использовал те же байты runtime и те же тесты, без автоматических повторов запросов и ослабления проверок.
