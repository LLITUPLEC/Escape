# Страница Escape

Опубликована **0.3.15 / Android versionCode 17**: https://prokrust-play.ru/escape/.
APK `Escape-0.3.15-e7ac556941de.apk`, **123571699 байт**, SHA-256 `e7ac556941debbe4295b14cfd298f7a278d4da8ffb97ee8e89d59c954f6b83c8`.
Подпись совпадает с предыдущим выпуском, проверены ARMv7/ARM64 и отсутствие development/debuggable. Все 32 публичных файла прочитаны полностью по HTTPS. Итоговые свидетельства: `Design/Release-0.3.15/validation/release-completion.json`.

Предыдущий выпуск **0.3.14 / code 16** сохранён: `Escape-0.3.14-46a458ea2e41.apk`, 124206651 байт, SHA-256 `46a458ea2e41140a8ab02cee1dd84ba542de455c9bf9fab550153b2d88f28525`.

## Исходники и локальные результаты

- `index.template.html` — шаблон страницы с историей изменений.
- `render-release.ps1` — проверка APK и подготовка `dist/`.
- `Design/Release-<version>/screens/` — три исходных PNG: `hub.png` (убежище), `character.png` (мастерская), `mine.png` (шахта).
- `Design/Audio/` — утверждённые 19 WAV, плеер и история выбора звуков.
- `Builds/<version>/` — APK, symbols и `build-status.json`.
- `dist/`, staging и bundles — генерируемые файлы, исключённые из Git.

Изображения публикуются без изменения пикселей. Кадры 0.3.15 получены настоящим Unity-рендерером из production-виджетов с автономными данными. Физический Android отдельно не проверялся.

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

Локально и на VPS сохранены только выпуски 0.3.15 и 0.3.14. Диагностические логи и immutable JSON отражают состояние на момент соответствующего выпуска; их старые пути не являются текущими зависимостями. Инструменты и нужные fixtures перенесены в `Tools/`, `Server/nakama/data/migrations/` и `Assets/_Project/Editor/Fixtures/`. История очистки находится в `Design/Release-0.3.15/validation/cleanup/`.
