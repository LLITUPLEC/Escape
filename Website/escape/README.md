# Страница Escape

Опубликована **0.3.16 / Android versionCode 18**: https://prokrust-play.ru/escape/.
APK `Escape-0.3.16-3f40a293713f.apk`, **124355967 байт**, SHA-256 `3f40a293713fa7689ca54929f597201cd5f0c9ca7a6a304be46bd95b7810e847`.
Публикация выполнена 28 октября 2026 года в 04:44:11 UTC. Все 32 публичных файла, включая полный APK и 19 выбранных WAV, прочитаны по HTTPS; размеры и SHA совпали с локальным пакетом. Отчёт: `Design/Release-0.3.16/validation/public-file-integrity.json`.

Сборка и подпись проверены, присутствуют ARMv7/ARM64, development/debuggable отключены. Финальный client snapshot совпадает со всеми 2059 файлами. Физический Android отдельно не проверялся. Свидетельства сборки: `Design/Release-0.3.16/validation/`.

Предыдущий выпуск **0.3.15 / Android versionCode 17** сохранён: `Escape-0.3.15-e7ac556941de.apk`, **123571699 байт**, SHA-256 `e7ac556941debbe4295b14cfd298f7a278d4da8ffb97ee8e89d59c954f6b83c8`. Его исторические свидетельства: `Design/Release-0.3.15/validation/release-completion.json`.

## Исходники и локальные результаты

- `index.template.html` — шаблон страницы с историей изменений.
- `render-release.ps1` — проверка APK и подготовка `dist/`.
- `Design/Release-<version>/screens/` — три исходных PNG: `hub.png` (убежище), `character.png` (мастерская), `mine.png` (шахта).
- `Design/Audio/` — утверждённые 19 WAV, плеер и история выбора звуков.
- `Builds/<version>/` — APK, symbols и `build-status.json`.
- `dist/`, staging и bundles — генерируемые файлы, исключённые из Git.

Изображения публикуются без изменения пикселей. Кадры 0.3.16 получены настоящим Unity-рендерером из production-виджетов с автономными данными: выбранное убежище C, требования эпического рецепта в мастерской и заполненное досье монстра. Имена `character.png` и `mine.png` сохраняют контракт renderer; подпись `character.png` — «Мастерская». Источники и SHA фиксируются в `Design/Release-0.3.16/screens/provenance.json`. Физический Android отдельно не проверялся.

Выпуск включает загрузку профиля без фиктивных значений, комплекты 8/8, проверку цены крафта перед расходом ресурсов, пробный бой с мировым боссом, крупные боевые показатели и сокращённый турнирный блок.

## Новый выпуск

Полное описание устойчивых инструментов: [Tools/Release/README.md](../../Tools/Release/README.md).

После окончательных исходников, импорта, QA и выбора кадров создайте новый снимок; исторический снимок прошедшего выпуска не заменяйте:

```powershell
python Tools/Release/source_snapshot.py --version <version> --create
# Unity: Project.Editor.ExpeditionAndroidBuild.BuildReleaseBatch
./Tools/Release/validate-apk.ps1 -ExpectedVersion <version> -MinimumVersionCode <code>
python Tools/Release/source_snapshot.py --version <version>
./Website/escape/render-release.ps1 -ApkPath '<проверенный APK>' -ScreensRoot 'Design/Release-<version>/screens' -FinalValidationJson 'Design/Release-<version>/validation/apk-validation.json'
```

Версия и code в Unity, renderer и publisher должны соответствовать новому выпуску. Сборщик использует проверенный SDK `.cache/android-sdk`, временно выбирает API 36 и восстанавливает прежние настройки. Подпись остаётся прежней.

`Tools/Release/prepare_publication.py` требует явные версию/code, SHA текущего серверного издателя и идентичность предыдущего APK. Он проверяет пакет и создаёт уникальные archive/plan в `.cache/release-work/`, не выполняя публикацию. После разрешения на публикацию загрузите файлы по точным путям плана и используйте `Tools/Release/publish_page_remote.py`; он проверяет архив, сохраняет предыдущий APK и выполняет атомарное переключение страницы.

После установки:

```powershell
./Tools/Release/verify-public-release.ps1 -ExpectedVersion <version> -PreviousApkUrl '<предыдущий APK HTTPS URL>' -PreviousApkBytes <bytes>
python Tools/Release/verify_public_integrity.py --version <version> --version-code <code> --report 'Design/Release-<version>/validation/public-file-integrity.json'
```

Публикация страницы не перезапускает API/БД. Серверный runtime развёртывается отдельным проверенным процессом с manifest, проверкой простоя, backup и откатом.

## Хранение

Пара для хранения локально и на VPS — текущий выпуск 0.3.16 и предыдущий 0.3.15. Более старый выпуск 0.3.14 удалён после успешной публикации и проверок. Диагностические логи и immutable JSON отражают состояние на момент соответствующего выпуска; их старые пути не являются текущими зависимостями. Инструменты и нужные fixtures перенесены в `Tools/`, `Server/nakama/data/migrations/` и `Assets/_Project/Editor/Fixtures/`. Отчёты текущей очистки: `Design/Release-0.3.16/validation/local-retention-result.json` и `Design/Release-0.3.16/validation/server/retention-result.json`.
