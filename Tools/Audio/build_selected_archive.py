"""Build the selected-only offline archive, retaining exact user choice history."""
from pathlib import Path
from html import escape
import hashlib
import json
import shutil

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Design/Audio"


def main():
    metadata = json.loads((OUT / "selected-metadata.json").read_text(encoding="utf-8"))
    assert metadata["selected_count"] == len(metadata["selected"]) == 19
    choice_file = ROOT / "Design/Audio/Escape-audio-choices-0.3.5.txt"
    assert hashlib.sha256(choice_file.read_bytes()).hexdigest() == metadata["selection_sha256"]
    if choice_file.resolve() != (OUT / choice_file.name).resolve():
        shutil.copyfile(choice_file, OUT / choice_file.name)
    cards = []
    for index, effect in enumerate(metadata["selected"], 1):
        path = OUT / effect["file"]
        assert hashlib.sha256(path.read_bytes()).hexdigest() == effect["sha256"]
        title = escape(effect["title"])
        source = escape(effect["file"], quote=True)
        direction = "Минерал и металл" if effect["choice"] == "A" else "Резонанс и пар"
        duration = str(effect["duration_seconds"]).replace(".", ",")
        cards.append(f'''<li class="effect" data-id="{effect["id"]}"><article aria-labelledby="effect-{effect["id"]}"><div class="heading"><span class="number">{index:02}</span><h2 id="effect-{effect["id"]}">{title}</h2></div><p class="detail">Выбрано {effect["choice"]} · {direction}<span>{duration} с</span></p><div class="actions"><button type="button" class="listen" data-audio="{source}" data-caption="{escape(effect["title"], quote=True)} · {effect["choice"]}">Слушать</button><a href="{source}" download>Скачать WAV</a></div></article></li>''')
    page = Path(__file__).with_name("selected_archive.template.html").read_text(encoding="utf-8")
    assert page.count("{{SELECTED_CARDS}}") == 1
    (OUT / "index.html").write_text(page.replace("{{SELECTED_CARDS}}", "\n".join(cards)), encoding="utf-8")
    lines = ["# Выбранные звуки Escape — история выбора 0.3.5", "", "[Открыть страницу прослушивания](index.html)", "",
             "В архиве оставлены только **19 выбранных WAV**. Выбор пользователя подтверждён исходным [текстовым списком](Escape-audio-choices-0.3.5.txt); точные хеши, направление A/B, параметры PCM и соответствие клиентским ресурсам — в [selected-metadata.json](selected-metadata.json). Упоминание «не автоматическая интеграция» в исходном текстовом списке является частью сохранённого оригинала; после отдельного запроса пользователя эти 19 файлов установлены в клиентские ресурсы.", "",
             "Звук лифта не выбран и оставлен прежним. Fury/Bloodlust, Shield/Heroism и критический удар не менялись. Их SHA-256 и хеши импортёров сохранены в selected-metadata.json. В архиве нет записей из стока: это оригинальный детерминированный синтез шумовых возбуждений, негармонических резонаторов и коротких отражений; без речи и музыки.", "",
             "Страница работает без сети, с одним общим плеером, без автозапуска и без нового выбора вариантов. Публикация выполняется отдельно; локальный архив сам ничего не отправляет.", "",
             "| № | Эффект | Выбор | WAV |", "|---|---|---|---|"]
    for i, effect in enumerate(metadata["selected"], 1):
        lines.append(f'| {i:02} | {effect["title"]} | {effect["choice"]} | [слушать]({effect["file"]}) |')
    lines += ["", "## Проверка и воспроизводимость", "", "```text", "python Tools/Audio/validate_selected_archive.py", "python Tools/Audio/validate_selected_assets.py", "pwsh -NoProfile -File Tools/Audio/validate_selected_runtime.ps1", "```", "",
              "Первая команда проверяет только этот архив и его ссылки; вторая — установленные production WAV, банк и защищённые хеши; третья выполняет реальные тела async-методов/кнопочного компонента с управляемыми заменами сервисов, без Unity и RPC. Проверка рендеринга и прослушивание на устройстве проводятся отдельно.", "",
              "`generate_sfx_auditions.py --verify-only` и `--verify-selected` после сворачивания проверяют выбранный архив. Если когда-нибудь понадобятся исходные A/B-демо, их можно воспроизвести из Tools/Audio/generate_sfx_auditions.py с seed 3502026 и явным `--rebuild-demos`. Это заново создаст папки демо только в Design, без изменений production Assets. Старый generate_expedition_sfx.py --write применять не нужно.", "",
              "Для локального HTTP-просмотра: `python -m http.server 8765 --bind 127.0.0.1 --directory Design/Audio`, затем `http://127.0.0.1:8765/`. Можно открыть index.html непосредственно вместе с папкой selected.", ""]
    (OUT / "README.md").write_text("\n".join(lines), encoding="utf-8")
    print(json.dumps({"selected_archive_built": True, "files": 19, "network_dependencies": 0}))


if __name__ == "__main__":
    main()
