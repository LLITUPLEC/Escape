"""Check offline HTML structure, audio links, and WAV manifest without opening a browser."""
from collections import Counter
from html.parser import HTMLParser
import json
from pathlib import Path
import subprocess
from urllib.parse import unquote, urlsplit
import wave

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Design/Audio"


class Page(HTMLParser):
    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.tags = []
        self.scripts = []
        self.current_script = None
    def handle_starttag(self, tag, attrs):
        data = dict(attrs)
        self.tags.append((tag, data))
        if tag == "script": self.current_script = []
    def handle_endtag(self, tag):
        if tag == "script" and self.current_script is not None:
            self.scripts.append("".join(self.current_script))
            self.current_script = None
    def handle_data(self, data):
        if self.current_script is not None: self.current_script.append(data)


def main():
    if not (OUT / "metadata.json").exists() and (OUT / "selected-metadata.json").exists():
        from validate_selected_archive import main as verify_selected
        verify_selected()
        return
    source = (OUT / "index.html").read_text(encoding="utf-8")
    page = Page()
    page.feed(source)
    ids = [a["id"] for _, a in page.tags if "id" in a]
    assert len(ids) == len(set(ids)), "Duplicate DOM ids"
    audio = [a for tag, a in page.tags if tag == "audio"]
    assert len(audio) == 1 and audio[0].get("preload") == "none"
    assert "autoplay" not in audio[0] and "src" not in audio[0], "Initial playback/load forbidden"
    assert not [1 for tag, _ in page.tags if tag in ("iframe", "object", "embed", "video")]
    assert not [a for tag, a in page.tags if tag == "script" and "src" in a]
    assert not [a for tag, a in page.tags if tag == "link" and a.get("rel") in ("stylesheet", "preload", "prefetch")]
    controls = [a for tag, a in page.tags if tag == "button" and "data-audio" in a]
    assert len(controls) == 64, "20 pairs × 3 controls + 2 showcases + 2 montages"
    for _, attrs in page.tags:
        for attribute in ("src", "href", "data-audio"):
            if attribute not in attrs: continue
            value = attrs[attribute]
            if value.startswith("#"):
                assert value[1:] in ids, value
                continue
            split = urlsplit(value)
            assert not split.scheme and not split.netloc and not value.startswith("/"), value
            file = (OUT / unquote(split.path)).resolve()
            assert file.is_relative_to(OUT.resolve()) and file.is_file(), value
    cards = [a for _, a in page.tags if "effect" in a.get("class", "").split()]
    assert len(cards) == 20 and len({c["data-id"] for c in cards}) == 20
    radios = [a for tag, a in page.tags if tag == "input" and a.get("type") == "radio"]
    assert len(radios) == 60
    groups = Counter(a["name"] for a in radios)
    assert len(groups) == 20 and set(groups.values()) == {3}
    for name in groups:
        assert {a["value"] for a in radios if a["name"] == name} == {"A", "B", ""}
    assert len([1 for tag, _ in page.tags if tag == "fieldset"]) == 20
    assert len([1 for tag, _ in page.tags if tag == "legend"]) == 20
    assert len(page.scripts) == 1
    assert "fetch(" not in page.scripts[0] and "XMLHttpRequest" not in page.scripts[0]
    assert "new Audio" not in page.scripts[0], "Use one existing audio element"
    metadata = json.loads((OUT / "metadata.json").read_text(encoding="utf-8"))
    for showcase in metadata["showcases"].values():
        assert 10 <= showcase["duration_seconds"] <= 15
        assert len(showcase["markers"]) == 6
        with wave.open(str(OUT / showcase["file"]), "rb") as file:
            assert file.getparams()[:3] == (1, 2, 48000)
    fixture = {"cards": [{"id": c["data-id"], "title": c["data-title"]} for c in cards],
               "buttons": [{"audio": c["data-audio"], "caption": c["data-caption"]} for c in controls]}
    (OUT / "page-dom-fixture.json").write_text(json.dumps(fixture, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    # Parse the exact inline program, then execute real event handlers using a small
    # DOM/media fixture in Node. No browser/file URL, sound playback, or networking.
    inline = OUT / "validation-inline.cjs"
    inline.write_text(page.scripts[0], encoding="utf-8")
    subprocess.run(["node", "--check", str(inline)], check=True, capture_output=True, text=True)
    result = subprocess.run(["node", str(Path(__file__).with_name("validate_audition_interactions.cjs")), str(inline), str(OUT / "page-dom-fixture.json")],
                            check=True, capture_output=True, text=True)
    interaction = json.loads(result.stdout)
    report = {"passed": True, "effect_cards": 20, "radio_groups": 20, "audio_players": 1,
              "play_buttons": len(controls), "all_relative_audio_links_exist": True, "autoplay": False,
              "external_dependencies": 0, "inline_js_syntax": "passed", "interaction_fixture": interaction,
              "browser_visual_qa_performed": False, "device_listening_performed": False}
    (OUT / "page-validation.json").write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report))


if __name__ == "__main__":
    main()
