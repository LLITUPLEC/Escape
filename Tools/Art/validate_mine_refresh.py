"""Validate original generated art, catalog bindings and delivery footprint."""
from pathlib import Path
import hashlib
import json
import math
import re
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
MEDIA = ROOT / 'Docs/Art/MineRefresh'
NAMES = ['Острый скол', 'Ржавый гвоздь', 'Гнилой свищ', 'Крушило',
         'Сухой грохот', 'Мёртвый плывун', 'Чёрная копоть', 'Забой',
         'Слепой бурав', 'Липкий ломун', 'Тихий шорох', 'Жила']


def main():
    manifest = json.loads((MEDIA / 'installed-art.json').read_text(encoding='utf-8'))
    assets = manifest['assets']
    assert len(assets) == 15
    hashes, rows = set(), []
    for asset in assets:
        path = ROOT / asset['destination']
        digest = hashlib.sha256(path.read_bytes()).hexdigest()
        assert digest == asset['sha256'], path
        assert digest not in hashes, path
        hashes.add(digest)
        with Image.open(path) as image:
            assert image.mode == 'RGBA', (path, image.mode)
            alpha = image.getchannel('A')
            histogram = alpha.histogram()
            assert histogram[0] > image.width * image.height * .05, (path, 'No transparent padding')
            assert sum(histogram[192:]) > image.width * image.height * .02, (path, 'No visible subject')
            bounds = alpha.point(lambda value: 255 if value >= 192 else 0).getbbox()
            rows.append(dict(id=asset['id'], dimensions=image.size, solidBounds=bounds,
                             transparentPixels=histogram[0], sourceBytes=path.stat().st_size, sha256=digest))

    guids = set()
    for floor, name in enumerate(NAMES, 1):
        expected = ROOT / f'Assets/_Project/Resources/Expedition/Monsters/mine-{floor}.png'
        guid = re.search(r'(?m)^guid: ([0-9a-f]+)', Path(str(expected) + '.meta').read_text(encoding='utf-8-sig'))[1]
        definition = (ROOT / f'Assets/_Project/Data/Match3/Monsters/mine_{floor}.asset').read_text(encoding='utf-8-sig')
        actual_name = json.loads(re.search(r'(?m)^  displayName: (.+)$', definition)[1])
        assert actual_name == name, (floor, actual_name)
        assert re.search(r'(?m)^  displayName: [^\n]*\n  floor:', definition), (floor, 'Stale YAML continuation')
        assert re.search(r'(?m)^  icon:.*guid: ' + guid, definition), floor
        assert guid not in guids
        guids.add(guid)

    for obsolete in ['img/items/tesser.png', 'img/resources_hud/tesseract.png']:
        assert not (ROOT / 'Assets/_Project' / obsolete).exists()
        assert not (ROOT / 'Assets/_Project' / (obsolete + '.meta')).exists()
    tesser_guid = '5bf4d0b3d4221c3448e19cb7dcb46915'
    matches = [p for p in (ROOT / 'Assets').rglob('*.meta') if ('guid: ' + tesser_guid) in p.read_text(encoding='utf-8-sig')]
    assert len(matches) == 1 and matches[0].name == 'tesseract.png.meta', matches
    item = (ROOT / 'Assets/_Project/Data/Character/Items/tesseract.asset').read_text(encoding='utf-8-sig')
    assert tesser_guid in item
    grounding = json.loads((ROOT / 'Assets/_Project/Resources/Expedition/Art/combatant-grounding.json').read_text(encoding='utf-8'))
    footprints = {entry['name']: entry for entry in grounding['entries']}
    for row in rows:
        if row['id'].startswith('mine-'):
            key = 'mine-' + str(int(row['id'].split('-')[1]))
            assert footprints[key]['sha256'] == row['sha256'], key
    # ASTC6x6 stores one16-byte block per6x6 texels; Unity imports monsters at1024.
    per_monster = math.ceil(1024 / 6) ** 2 * 16
    report = dict(ok=True, mode='built-in image_gen', pngPixelsModified=False,
                  checks=['15 unique transparent original PNGs', '12 floor-specific names and sprite GUIDs',
                          '12 regenerated grounding footprints', 'Both obsolete tesseract PNGs removed',
                          'Single retained tesseract GUID and item reference'],
                  assets=rows, sourcePngBytes=sum(row['sourceBytes'] for row in rows),
                  footprintEstimate=dict(format='ASTC6x6', importedMonsterSize=1024, mipmaps=False,
                                         monsters12Bytes=12 * per_monster, alternative36Bytes=36 * per_monster,
                                         savedBytesBySharedDifficulty=24 * per_monster,
                                         note='Texture payload estimate; total APK size requires a release build.'))
    output = ROOT / '.cache/qa/mine-refresh/art'
    output.mkdir(parents=True, exist_ok=True)
    (output / 'art-validation.json').write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(f'PASS15 original transparent assets,12 monster bindings/footprints, single replacement tesseract. ASTC monster payload: {12 * per_monster} bytes vs {36 * per_monster} bytes for36 variants.')


if __name__ == '__main__':
    main()
