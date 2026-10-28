# Страница Escape

Опубликована **0.3.18 / Android versionCode 20**: https://prokrust-play.ru/escape/.
APK `Escape-0.3.18-13e354b77d89.apk`, **128893619 байт**, SHA-256 `13e354b77d898666b030f4eaef4939eb651ca6cba3858c5d360a84f4d9ce6f89`.
Публикация выполнена 29 октября 2026 года в 01:14:17 по Москве (28 октября, 22:14:17 UTC). Все 32 публичных файла, включая полный APK и 19 выбранных WAV, прочитаны по HTTPS; размеры и SHA совпали с локальным пакетом. Отчёт: `Design/Release-0.3.18/validation/public-file-integrity.json`.

Подпись совпадает с предыдущим APK; ARMv7/ARM64 и отсутствие development/debuggable проверены. Все 2122 клиентских файла совпали со снимком после сборки. Физический Android отдельно не проверялся. Свидетельства сборки: `Design/Release-0.3.18/validation/`.

Предыдущий выпуск **0.3.17 / Android versionCode 19** сохранён: `Escape-0.3.17-a692d44fa0ad.apk`, **124415319 байт**, SHA-256 `a692d44fa0adeaf40720e308b22fe11716f3f32c630bbf011859e6ef5c9cddca`. Его исторические свидетельства: `Design/Release-0.3.17/validation/public-file-integrity.json`.

## Исходники и локальные результаты

- `index.template.html` — шаблон страницы с историей изменений.
- `render-release.ps1` — проверка APK и подготовка `dist/`.
- `Design/Release-<version>/screens/` — три исходных PNG. В 0.3.18 это `hub.png` (убежище), `character.png` (каталог образов), `mine.png` (досье монстра); имена сохраняют технический контракт renderer.
- `Design/Audio/` — утверждённые 19 WAV, плеер и история выбора звуков.
- `Builds/<version>/` — APK, symbols и `build-status.json`.
- `dist/`, staging и bundles — генерируемые файлы, исключённые из Git.

Изображения публикуются без изменения пикселей. Кадры 0.3.18 получены настоящим Unity-рендерером из production-компонентов с автономными DTO: убежище, каталог по три образа в ряд и досье с двумя колонками характеристик. Источники, размеры 1080×1920 и SHA записаны в `Design/Release-0.3.18/screens/provenance.json`. Кадры не выдаются за снимки физического устройства.

В 0.3.18 каталог образов стал основным экраном, а подробности открываются в закрываемом окне без внутреннего скролла. Добавлены 12 отдельных аватарок монстров, новые бонусы и награды коллекции с доплатой ранее открытых образов. Мировой босс выполняет адаптацию, крик и обычное действие отдельными событиями. Скрытые ряды доступны разрешённым аккаунтам и у босса; удержание показывает их, короткий тап скрывает. Исправлены запуск «Спуска», турнирные имена, образы и возврат на арену.

Проверки: 881 локальный Lua-сценарий, 10 групп Unity QA, 977 изолированных сетевых проверок и полный HTTPS-контроль 32 файлов. При первой установке временный DNS-кеш шлюза вызвал 502 и автоматический откат; после добавления ограниченного ожидания готовности выполнены все 35 security-проверок и проверки dashboard. Runtime и база не сбрасывались.

## Новый выпуск

Полное описание устойчивых инструментов: [Tools/Release/README.md](../../Tools/Release/README.md).

После окончательных исходников, импорта, QA и выбора кадров создайте новый снимок; исторический снимок прошедшего выпуска не заменяйте:

```powershell
python Tools/Release/source_snapshot.py --version <version> --create
# Unity: Project.Editor.ExpeditionAndroidBuild.BuildReleaseBatch
./Tools/Release/validate-apk.ps1 -ExpectedVersion <version> -MinimumVersionCode <code>
python Tools/Release/source_snapshot.py --version <version>
./Website/escape/render-release.ps1 -Version '<version>' -VersionCode <code> -ApkPath '<проверенный APK>' -ScreensRoot 'Design/Release-<version>/screens' -FinalValidationJson 'Design/Release-<version>/validation/apk-validation.json'
```

Версия и code в Unity и renderer должны соответствовать новому выпуску. Publisher проверяет числовую версию пакета и точное совпадение с явно переданным `--expected-version`; подготовка и установка плана передают её автоматически. Сборщик использует проверенный SDK `.cache/android-sdk`, временно выбирает API 36 и восстанавливает прежние настройки. Подпись остаётся прежней.

`Tools/Release/prepare_publication.py` требует явные версию/code, SHA текущего серверного издателя и идентичность предыдущего APK. Он проверяет пакет и создаёт уникальные archive/plan в `.cache/release-work/`, не выполняя публикацию. После разрешения на публикацию загрузите файлы по точным путям плана и используйте `Tools/Release/publish_page_remote.py`; он проверяет архив, сохраняет предыдущий APK и выполняет атомарное переключение страницы.

После установки:

```powershell
./Tools/Release/verify-public-release.ps1 -ExpectedVersion <version> -PreviousApkUrl '<предыдущий APK HTTPS URL>' -PreviousApkBytes <bytes>
python Tools/Release/verify_public_integrity.py --version <version> --version-code <code> --report 'Design/Release-<version>/validation/public-file-integrity.json'
```

Публикация страницы не перезапускает API/БД. Серверный runtime развёртывается отдельным проверенным процессом с manifest, проверкой простоя, backup и откатом.

## Хранение

Сохраняются текущий выпуск 0.3.18 и предыдущий 0.3.17, их APK/symbols и две проверенные резервные копии VPS. Артефакты 0.3.16 удаляются только после публикации, HTTPS-проверок и проверки сохраняемых файлов. Исторические JSON/текстовые отчёты всех выпусков сохраняются; их старые пути отражают состояние на момент проверки. Рабочие инструменты и fixtures находятся в `Tools/`, `Server/nakama/data/migrations/` и `Assets/_Project/Editor/Fixtures/`. Отчёты очистки находятся в `Design/Release-0.3.18/validation/`.
