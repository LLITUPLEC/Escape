"""Build a standalone, network-free SFX selection page from the audition manifest."""
from pathlib import Path
from html import escape
import json

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Design/Audio"


def button(file: str, label: str, caption: str, cls: str = "") -> str:
    return f'<button type="button" class="listen {cls}" data-audio="{escape(file, quote=True)}" data-caption="{escape(caption, quote=True)}">{escape(label)}</button>'


def card(effect: dict, index: int) -> str:
    key, title = effect["id"], effect["title"]
    a, b = effect["description"].split(" / ", 1)
    play = button(effect["variants"]["A"]["file"], "Слушать A", title + " · A")
    play += button(effect["variants"]["B"]["file"], "Слушать B", title + " · B")
    play += button(effect["pair_preview"]["file"], "A → B", title + " · сравнение A → пауза → B", "compare")
    choices = "".join(f'<label class="choice"><input type="radio" name="choice-{key}" value="{value}"><span>{caption}</span></label>'
                      for value, caption in [("A", "Выбрать A"), ("B", "Выбрать B"), ("", "Позже")])
    duration = str(effect["variants"]["A"]["duration_seconds"]).replace(".", ",")
    return f'''<li class="effect" data-id="{key}" data-title="{escape(title, quote=True)}">
      <article aria-labelledby="title-{key}">
        <div class="card-heading"><span class="number">{index:02}</span><h3 id="title-{key}">{escape(title)}</h3><span class="duration">{duration} с</span></div>
        <p class="sound-description"><b>A</b> {escape(a)}<br><b>B</b> {escape(b)}</p>
        <div class="play-row" role="group" aria-label="Прослушать: {escape(title, quote=True)}">{play}</div>
        <fieldset><legend class="sr-only">Выбор для эффекта «{escape(title)}»</legend><div class="choices">{choices}</div></fieldset>
      </article>
    </li>'''


def main():
    if not (OUT / "metadata.json").exists() and (OUT / "selected-metadata.json").exists():
        from build_selected_archive import main as build_selected
        build_selected()
        return
    metadata = json.loads((OUT / "metadata.json").read_text(encoding="utf-8"))
    battle = "\n".join(card(e, i) for i, e in enumerate(metadata["effects"], 1) if e["category"] == "battle")
    world = "\n".join(card(e, i) for i, e in enumerate(metadata["effects"], 1) if e["category"] != "battle")
    showcases = "".join(button(metadata["showcases"][s]["file"], f"10 секунд · {s}", f"Короткий пример · {s}") for s in ("A", "B"))
    montages = "".join(button(metadata["montages"][s]["file"], f"Все эффекты · {s}", f"Полный монтаж · {s}", "quiet") for s in ("A", "B"))
    template = Path(__file__).with_name("audition_page.template.html").read_text(encoding="utf-8")
    for token, value in {"{{BATTLE_CARDS}}": battle, "{{WORLD_CARDS}}": world, "{{SHOWCASES}}": showcases, "{{MONTAGES}}": montages}.items():
        assert template.count(token) == 1
        template = template.replace(token, value)
    assert "{{" not in template
    (OUT / "index.html").write_text(template, encoding="utf-8")
    print(json.dumps({"page": "Design/Audio/index.html", "effect_pairs": len(metadata["effects"]), "network_dependencies": 0}))


if __name__ == "__main__":
    main()
