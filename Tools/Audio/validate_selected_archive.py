"""Validate the selected-only archive without accessing production Assets or the network."""
from array import array
import hashlib
from html.parser import HTMLParser
import json
from pathlib import Path
import re
import subprocess
from urllib.parse import urlsplit
import wave

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Design/Audio"


class Page(HTMLParser):
    def __init__(self):
        super().__init__()
        self.tags = []; self.script = []; self.in_script = False
    def handle_starttag(self, tag, attrs):
        self.tags.append((tag, dict(attrs)))
        if tag == "script": self.in_script = True
    def handle_endtag(self, tag):
        if tag == "script": self.in_script = False
    def handle_data(self, data):
        if self.in_script: self.script.append(data)


def main():
    metadata = json.loads((OUT / "selected-metadata.json").read_text(encoding="utf-8"))
    assert len(metadata["selected"]) == metadata["selected_count"] == 19
    assert hashlib.sha256((OUT / "Escape-audio-choices-0.3.5.txt").read_bytes()).hexdigest() == metadata["selection_sha256"]
    page = Page(); page.feed((OUT / "index.html").read_text(encoding="utf-8"))
    ids = [a["id"] for _, a in page.tags if "id" in a]
    assert len(ids) == len(set(ids))
    players = [a for tag, a in page.tags if tag == "audio"]
    assert len(players) == 1 and players[0].get("preload") == "none" and "autoplay" not in players[0] and "src" not in players[0]
    assert not [a for tag, a in page.tags if tag in ("input", "fieldset", "iframe", "video", "object", "embed")]
    controls = [a for tag, a in page.tags if tag == "button" and "data-audio" in a]
    assert len(controls) == 19
    expected = {e["file"] for e in metadata["selected"]}
    assert {c["data-audio"] for c in controls} == expected
    assert len(list((OUT / "selected").glob("*.wav"))) == 19
    for effect in metadata["selected"]:
        path = OUT / effect["file"]
        assert path.resolve().is_relative_to((OUT / "selected").resolve())
        assert hashlib.sha256(path.read_bytes()).hexdigest() == effect["sha256"]
        with wave.open(str(path), "rb") as clip:
            assert clip.getparams()[:3] == (1, 2, 48000)
            pcm = array("h", clip.readframes(clip.getnframes()))
        assert max(abs(v) for v in pcm) < 32767 and pcm[0] == pcm[-1] == 0
    for _, attrs in page.tags:
        for key in ("href", "src", "data-audio"):
            if key not in attrs: continue
            value = attrs[key]
            if value.startswith("#"): assert value[1:] in ids; continue
            url = urlsplit(value)
            assert not url.scheme and not url.netloc and not value.startswith("/")
            path = (OUT / value).resolve()
            assert path.is_relative_to(OUT.resolve()) and path.is_file(), value
    script = "".join(page.script)
    assert "fetch(" not in script and "new Audio" not in script and "XMLHttpRequest" not in script
    # node --check accepts JavaScript from stdin, without creating temporary files.
    subprocess.run(["node", "--check"], input=script, text=True, capture_output=True, check=True)
    assert not re.search(r"[A-Za-z]:[\\/]|file://|https?://", json.dumps(metadata, ensure_ascii=False))
    report = {"passed": True, "selected_wav_hashes": 19, "relative_links_valid": True, "audio_players": 1,
              "radio_choices": 0, "autoplay": False, "network_dependencies": 0,
              "protected_history_metadata_has_no_absolute_private_paths": True, "javascript_syntax": "passed",
              "browser_visual_qa_performed": False}
    (OUT / "archive-validation.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report))


if __name__ == "__main__":
    main()
