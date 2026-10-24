# Проверки Escape 0.3.2

Финальная UI-проверка в редакторе, release IL2CPP-сборка и публикация завершены. Результат UI — [ui-qa.json](ui-qa.json), APK — [apk-validation.json](apk-validation.json), доступность — [public-release.json](public-release.json). Опубликованы [страница игры](https://prokrust-play.ru/escape/) и [APK 0.3.2 — 95,6 МиБ](https://prokrust-play.ru/escape/downloads/Escape-0.3.2-4b85dd95f488.apk). Браузерная проверка страницы и обновлённой галереи прошла: [browser-qa.json](browser-qa.json). Результаты 0.3.1 не подменяют подтверждения новой сборки.

## Источники и границы

- Unity 6000.3.9f1 Editor Play Mode и настоящие Game View кадры текущих сцен.
- Проверки UI выполняются через EventSystem, реальные поля TMP и production-методы представлений. Локальные fixtures явно отделяются от действительных боевых событий и владения предметами.
- Кадровые пробы шахты проверяют промежуточные положения лифта/камеры, смену цели и отмену прокруткой. Это проверка поведения, а не FPS-тест.
- Roslyn Runtime/Editor и preview-проверки не подтверждают успешную нативную IL2CPP-сборку, подпись или запуск на Android.

Сохранены текущие ответы bridge: [qa-results.jsonl](qa-results.jsonl), исходные строки 427–552 рабочего журнала `.utmp/expedition/qa-results.jsonl`. Сам bridge не записывает timestamps, поэтому отбор по UTC ≥17:00 нельзя выполнить точно для каждого ответа: использован текущий диапазон 0.3.2 от подготовки оригинальных resource icons до запроса сборки. IDs, номера строк и оговорка записаны в UI-отчёте; даты исходных кадров и `startedUtc` отчёта шахты подтверждают текущий проход. В диапазоне сохранены и ранние отказы имени, исправленные до финальных успешных проверок.

Публичные кадры 1440×3088: [убежище](../screenshots/hub.png), [герой](../screenshots/character.png), [шахта](../screenshots/mine.png), [бой](../screenshots/battle.png). SHA-256, размеры и исходные пути записаны в UI-отчёте. Снимки настроек/почты и редактора имени не включены.

## Текущие результаты UI

| Область | Подтверждено сейчас | Что ещё требуется |
|---|---|---|
| Навигация и шапка | Порядок пяти переходов, отсутствие дублирующих вывесок, границы кнопок, четыре исходных resource sprites; финальные кадры и receipt сохранены | Проверка на устройстве отдельно |
| Настройки и имя | Настройки открываются из убежища и мастерской. Два прохода raycast → ввод `QaProbe12` → Backspace → восстановление текста, включая повторное открытие; отмена работает | Android IME отдельно; серверное переименование не отправлялось |
| Пустая экипировка и редкость | `all`, `rarities`, `restore` прошли: восемь настоящих slots/definitions, серые hints без подписей, цветные icons и green/blue/purple фон/рамка, данные восстановлены | Золотая редкость не проверялась; epic chest/feet используют реальные catalog fallback icons |
| Боевой HUD | Production UI проверен при 0/50/100%; числовые шкалы и отступы; настоящий raycast открывает/закрывает характеристики; финальный кадр сохранён | Проверка на устройстве отдельно |
| Режим «Спуск» | UI fixture: цель, MP, последний ход и бонус; исходное представление восстановлено | Не обозначать fixture как сыгранный сетевой матч |
| Арена | Три варианта окна ставки на нужном слое; после ухода со сцены его объекты отсутствуют; receipt сохранён | Без оплаты турнира |
| Шахта | Кадровая проба плавного движения, повторного выбора и отмены ручной прокруткой прошла; реальные Depth4/Depth1 taps; финальный кадр с цепями и примерно 1,4 площадки сохранён | Без открытия барьера; это не FPS-тест |
| Звуковые назначения | 13 SFX импортируются и назначены, critical reference сохранён; SHA-256 критического WAV и `.meta` совпали с 0.3.1 | Это не проверка звучания на физическом устройстве |

Первый автоматический тест имени после исправления построения окна завершился ложным отказом из-за платформенной семантики `TMP_InputField.shouldHideSoftKeyboard` в Windows Editor. После коррекции условия тест **прошёл дважды**, включая повторное открытие. [nickname-input-results.json](nickname-input-results.json) содержит точные успешные ответы. Это не Android IME-тест; исходный текст восстановлен и Save не нажимался.

Отчёты helper и компиляции: [оборудование](equipment-probe-results.json), [кадровая проба шахты](mine-travel-play.json), [C# Android Player symbols](player-config-result.json), [Runtime log](runtime-compile.log), [Editor log](editor-compile.log), [Player symbols log](player-config-compile.log). Последние три compiler log пусты; Runtime/Editor exit 0 зафиксирован выполнявшим команды root, Android conditional exit 0 — в исходном JSON. [Нативная IL2CPP-сборка](android-build-status.json) завершилась `Succeeded` в 18:09:55 UTC за 463,36 секунды: 0 ошибок и 2 предупреждения.

APK — versionName `0.3.2`, versionCode `4`, ARMv7 + ARM64, nondevelopment/nondebuggable. Фактический размер — **100 209 427 байт (95,6 МиБ)**, SHA-256 — `4b85dd95f488365fa5228b56045cae06f79fd7995360031ebc04aae369883596`. Поле `bytes` в raw `android-build-status.json` — `Unity BuildReport.summary.totalSize`; оно не является размером APK. Размер файла берётся из [apk-validation.json](apk-validation.json). [Подпись v2 проверена](apk-signature.txt), используется существующий сертификат Android Debug.

[Публикация](deploy-result.json) и [HTTP-проверки](public-release.json) прошли. Страница/manifest/APK доступны, Range — 206, MIME/размер/кэш корректны; прежний APK и API отвечают. Отдельный [полный SHA-256 серверного файла](server-apk-hash.json) совпадает с локальным APK; HTTP verifier сам целиком APK не скачивает.

[audio-preservation.json](audio-preservation.json) сравнивает текущие исходники с receipt 0.3.1: критический WAV — `53836329e7a752eed85a20ed25981e2e7ecf5b6fb8a155e92077bb0903a27341`, `.meta` — `d85008ee6c323794a7936a8ed25b1c5eff2c73060afc8a51de12808a8462df0c`; оба без изменений.

## Фикстура экипировки

Публичный Editor API для bridge:

```csharp
Project.Editor.ExpeditionCharacterEquipmentQa.Run("all");
```

`all` проверяет пустые слоты, реальные определения нескольких редкостей и восстановление. Для снимков доступны `empty`, `rarities`, затем обязательно `restore`. Snapshot содержит исходные equipment/inventory/count arrays и cached ItemCatalog; данные возвращаются через production binder. Пока тестовые предметы показаны, ввод героя блокируется. Helper также восстанавливает данные при ошибке, выходе из Play или reload. Он не вызывает RPC экипировки, не создаёт определения предметов и не меняет стоимость действий.

Проверены восемь отдельных слотов без подписей; приглушённые grayscale hints и shader import; настоящие иконки без серого материала и с белым tint; зелёные/синие/фиолетовые фон и рамка; наличие настоящей иконки шлема. Точные Item IDs и явно отмеченные catalog fallback icons сохранены в отчёте. Отсутствующие определения должны отражаться как неполное покрытие, а не подменяться выдуманными предметами.

## Итоговые артефакты

| Файл | Назначение | Статус |
|---|---|---|
| [ui-qa.json](ui-qa.json) | Версия Unity, разрешение, реальные сцены, fixtures и результаты повторных проверок | passed в указанной области |
| [apk-validation.json](apk-validation.json), [android-build-status.json](android-build-status.json), [apk-signature.txt](apk-signature.txt) | Build result, development/debug flags, versionName/code, ABI, подпись, bytes/SHA-256 | passed |
| [deploy-result.json](deploy-result.json) | Результат выкладки именно готового APK 0.3.2 | published |
| [public-release.json](public-release.json) | Страница/manifest, APK HEAD/Range/MIME/размер/кэш, прежний APK и API | passed |
| [server-apk-hash.json](server-apk-hash.json) | Полный SHA-256 опубликованного APK | passed |
| [audio-preservation.json](audio-preservation.json) | Исходный критический WAV и importer `.meta` | unchanged |
| [Четыре игровых кадра](../screenshots/) | Обновлённый интерфейс без приватных settings/email окон | сохранены |
| [Снимок страницы](../screenshots/public-release.png) и [галерея](../screenshots/public-character.png) | Публичная версия 0.3.2, свежий кадр героя | passed |

Ссылки, размер APK, хеш и результат публикации взяты из готовых receipts. Браузерные снимки сохранены; они дополняют сетевые проверки.

## Физическое устройство и производительность

`physical_device_tested=false`. Root подтвердил результат выполненного `adb devices`: только `List of devices attached`, без строк устройств; сырой ADB receipt не сохранялся. Нативная Android IME, сенсорное управление, возврат из фона и звук через динамики телефона не проверены на устройстве. FPS, GPU/CPU frame time и GC не измерены. Наличие плавных анимаций и успешные Editor fixtures не является подтверждением целевой производительности Android.
