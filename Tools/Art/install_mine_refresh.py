"""Install requested generated assets without modifying source PNG pixels."""
from pathlib import Path
import hashlib
import json
import re
import shutil
import uuid

ROOT = Path(__file__).resolve().parents[2]
MEDIA = ROOT / 'Docs/Art/MineRefresh'
ASSETS = ROOT / 'Assets/_Project'
NAMES = ['Острый скол', 'Ржавый гвоздь', 'Гнилой свищ', 'Крушило',
         'Сухой грохот', 'Мёртвый плывун', 'Чёрная копоть', 'Забой',
         'Слепой бурав', 'Липкий ломун', 'Тихий шорох', 'Жила']


def write(path, value):
    path.write_text(value, encoding='utf-8', newline='\n')


def meta_for(path, template):
    meta = Path(str(path) + '.meta')
    if not meta.exists():
        text = template.read_text(encoding='utf-8-sig')
        text = re.sub(r'(?m)^guid: [0-9a-f]+', 'guid: ' + uuid.uuid4().hex, text)
        text = re.sub(r'(?m)^(\s+spriteID:) [0-9a-f]+', r'\1 ' + uuid.uuid4().hex, text)
        write(meta, text)
    return re.search(r'(?m)^guid: ([0-9a-f]+)', meta.read_text(encoding='utf-8-sig'))[1]


def main():
    entries = []
    for manifest in ['art-manifest.json', 'generated-utility-art.json']:
        entries.extend(json.loads((MEDIA / manifest).read_text(encoding='utf-8'))['assets'])
    assert len(entries) == 15 and len({x['destination'] for x in entries}) == 15
    for entry in entries:
        target = (ROOT / entry['destination']).resolve()
        assert target.is_relative_to(ASSETS.resolve()), target
        source = Path(entry['source'])
        if source.exists():
            shutil.copyfile(source, target)
        assert target.is_file(), target
        entry['sha256'] = hashlib.sha256(target.read_bytes()).hexdigest()
        entry['bytes'] = target.stat().st_size
        assert target.read_bytes()[0:8] == b'\x89PNG\r\n\x1a\n'

    template = ASSETS / 'Resources/Expedition/Monsters/mine-5.png.meta'
    for floor, name in enumerate(NAMES, 1):
        guid = meta_for(ASSETS / f'Resources/Expedition/Monsters/mine-{floor}.png', template)
        definition = ASSETS / f'Data/Match3/Monsters/mine_{floor}.asset'
        text = definition.read_text(encoding='utf-8-sig')
        # Unity may wrap an escaped YAML string across continuation lines.
        text = re.sub(r'(?m)^  displayName:[^\n]*(?:\n {4}[^\n]*)*',
                      '  displayName: ' + json.dumps(name, ensure_ascii=False), text)
        text = re.sub(r'(?m)^  icon:.*$', '  icon: {fileID: 21300000, guid: ' + guid + ', type: 3}', text)
        write(definition, text)

    for key in ['health-red', 'healing-cross']:
        meta_for(ASSETS / f'Resources/Expedition/Icons/{key}.png', ASSETS / 'Resources/Expedition/Icons/friends.png.meta')

    new_tesseract = ASSETS / 'Resources/Expedition/Items/tesseract.png'
    old_item = ASSETS / 'img/items/tesser.png'
    old_hud = ASSETS / 'img/resources_hud/tesseract.png'
    new_meta = Path(str(new_tesseract) + '.meta')
    old_meta = Path(str(old_item) + '.meta')
    # Retain the inventory GUID, so existing scene/catalog references survive replacement.
    if not new_meta.exists():
        shutil.copyfile(old_meta, new_meta)
    assert 'guid: 5bf4d0b3d4221c3448e19cb7dcb46915' in new_meta.read_text(encoding='utf-8-sig')
    for path in ASSETS.rglob('*'):
        if path.suffix not in {'.cs', '.asset', '.unity', '.prefab'}:
            continue
        if path.suffix == '.cs' and path.name not in {'MineSceneController.cs', 'ArenaMenuModePanelBinder.cs', 'DuelMatch3Manager.cs'}:
            continue
        text = path.read_text(encoding='utf-8-sig')
        changed = text.replace('c47a791f5c471a7418387e625e481d31', '5bf4d0b3d4221c3448e19cb7dcb46915')
        changed = changed.replace('Assets/_Project/img/resources_hud/tesseract.png', 'Assets/_Project/Resources/Expedition/Items/tesseract.png')
        changed = changed.replace('Assets/_Project/img/items/tesser.png', 'Assets/_Project/Resources/Expedition/Items/tesseract.png')
        if changed != text:
            write(path, changed)
    for path in [old_item, old_hud, old_meta, Path(str(old_hud) + '.meta')]:
        assert path.resolve().is_relative_to(ASSETS.resolve())
        if path.exists():
            path.unlink()
    write(MEDIA / 'installed-art.json', json.dumps(dict(mode='built-in image_gen', pngPixelsModified=False, assets=entries), ensure_ascii=False, indent=2) + '\n')
    print(f'Installed {len(entries)} original PNGs; bound 12 named mine monsters; replaced both legacy tesseract sprites with one resource.')


if __name__ == '__main__':
    main()
