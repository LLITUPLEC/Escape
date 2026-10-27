"""Read source alpha only; bake runtime geometry without editing/importing any PNG."""
from pathlib import Path
import hashlib
import json
import re
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
ASSETS = ROOT / 'Assets/_Project'
OUTPUT = ASSETS / 'Resources/Expedition/Art/combatant-grounding.json'


def sources():
    paths = {ASSETS / 'Resources/Expedition/Art/hero.png'}
    paths.update((ASSETS / 'Resources/Expedition/Appearance').glob('*-body.png'))
    paths.update((ASSETS / 'Resources/Expedition/Boss').rglob('*-body.png'))
    guids = {}
    for meta in ASSETS.rglob('*.png.meta'):
        match = re.search(r'^guid: ([0-9a-f]+)', meta.read_text(encoding='utf-8-sig'), re.M)
        if match:
            guids[match.group(1)] = Path(str(meta)[:-5])
    for definition in (ASSETS / 'Data/Match3/Monsters').glob('mine_*.asset'):
        match = re.search(r'icon:.*guid: ([0-9a-f]+)', definition.read_text(encoding='utf-8-sig'))
        assert match and match.group(1) in guids, definition
        paths.add(guids[match.group(1)])
    return sorted(paths)


def footprint(path):
    # Alpha192 excludes soft cast shadows. A contact row needs several solid
    # pixels, so an isolated anti-aliased speck does not move the entire body.
    with Image.open(path) as image:
        image = image.convert('RGBA')
        width, height = image.size
        alpha = image.getchannel('A')
        solid = alpha.point(lambda value: 255 if value >= 192 else 0)
        bounds = solid.getbbox()
        assert bounds, path
        left, top, right, bottom = bounds
        pixels = solid.load()
        minimum = max(3, round((right - left) * .004))
        row = bottom - 1
        while row >= top:
            xs = [x for x in range(left, right) if pixels[x, row]]
            if len(xs) >= minimum:
                break
            row -= 1
        assert row >= top, path
        # Center the contact over the lowest3% of solid body; this handles
        # separated feet, crouches and non-humanoid bodies without rect padding.
        contact_xs = []
        for y in range(max(top, row - max(2, round((bottom - top) * .03))), row + 1):
            contact_xs.extend(x for x in range(left, right) if pixels[x, y])
        contact_x = (min(contact_xs) + max(contact_xs) + 1) * .5
        return dict(name=path.stem, source=path.relative_to(ROOT).as_posix(),
                    sha256=hashlib.sha256(path.read_bytes()).hexdigest(),
                    width=width, height=height,
                    x=left/width, y=(height-bottom)/height,
                    w=(right-left)/width, h=(bottom-top)/height,
                    footX=contact_x/width, footY=(height-row-1)/height)


if __name__ == '__main__':
    entries = [footprint(path) for path in sources()]
    assert len(entries) == 34 and len({entry['name'] for entry in entries}) == 34
    data = dict(schema=1, alphaThreshold=192, sourcePngModified=False,
                groundUvY=.75, entries=entries)
    OUTPUT.write_text(json.dumps(data, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    print(f'Baked{len(entries)} original-alpha footprints; no source PNG modified: {OUTPUT.relative_to(ROOT)}')
