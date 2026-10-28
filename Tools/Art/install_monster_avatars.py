"""Install authored ImageGen monster portraits without altering image pixels or body sprites."""
import argparse
import hashlib
import json
import re
import shutil
import uuid
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
DEST = ROOT / 'Assets/_Project/Resources/Expedition/Monsters/Avatars'
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--sources-json', type=Path, required=True)
args = parser.parse_args()
sources = json.loads(args.sources_json.read_text(encoding='utf-8-sig'))
assert set(sources) == {str(n) for n in range(1, 13)}, 'Twelve distinct generated portraits are required'
DEST.mkdir(parents=True, exist_ok=True)
folder_meta = DEST.with_suffix('.meta')
if not folder_meta.exists():
    folder_meta.write_text('fileFormatVersion: 2\nguid: ' + uuid.uuid5(uuid.NAMESPACE_URL, 'escape/monster-avatars/v1').hex + '\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n', encoding='utf-8')
assets = []
for number in range(1, 13):
    source = Path(sources[str(number)]).resolve(strict=True)
    destination = DEST / f'mine-{number}-avatar-v1.png'
    digest = hashlib.sha256(source.read_bytes()).hexdigest()
    with Image.open(source) as image:
        assert image.width == image.height and image.width >= 512
        assert image.mode == 'RGBA' and image.getextrema()[3][0] == 0 and image.getextrema()[3][1] == 255
        size = list(image.size)
    assert not destination.exists() or hashlib.sha256(destination.read_bytes()).hexdigest() == digest, 'Never replace an existing artwork with different pixels'
    shutil.copyfile(source, destination)
    guid = uuid.uuid5(uuid.NAMESPACE_URL, f'escape/monster-avatar/v1/mine-{number}').hex
    body = ROOT / f'Assets/_Project/Resources/Expedition/Monsters/mine-{number}.png'
    meta = body.with_suffix('.png.meta').read_text(encoding='utf-8-sig')
    meta = re.sub(r'(?m)^guid: [0-9a-f]+$', 'guid: ' + guid, meta)
    meta = re.sub(r'(?m)(\s*maxTextureSize:) \d+', r'\1 512', meta)
    meta = re.sub(r'(?m)(\s*spriteID:) [0-9a-f]+', r'\1 ' + uuid.uuid5(uuid.NAMESPACE_URL, f'escape/monster-avatar-sprite/v1/mine-{number}').hex, meta)
    destination.with_suffix('.png.meta').write_text(meta, encoding='utf-8')
    definition = ROOT / f'Assets/_Project/Data/Match3/Monsters/mine_{number}.asset'
    content = definition.read_text(encoding='utf-8-sig')
    entry = '  avatar: {fileID: 21300000, guid: ' + guid + ', type: 3}'
    if re.search(r'(?m)^  avatar:', content):
        content = re.sub(r'(?m)^  avatar:.*$', entry, content)
    else:
        content = re.sub(r'(?m)^(  icon:.*)$', r'\1\n' + entry, content)
    definition.write_text(content, encoding='utf-8')
    assets.append({'floor': number, 'source': str(source), 'reference': str(body.relative_to(ROOT)),
        'referenceSha256': hashlib.sha256(body.read_bytes()).hexdigest(),
        'destination': str(destination.relative_to(ROOT)), 'sha256': digest, 'size': size, 'guid': guid})
manifest = ROOT / 'Docs/Art/MonsterAvatars/art-manifest.json'
manifest.parent.mkdir(parents=True, exist_ok=True)
manifest.write_text(json.dumps({'mode': 'built-in image_gen', 'date': '2026-10-29',
    'purpose': 'Dedicated identity-preserving monster face/core portraits for battle headers and future medals; complete body art is unchanged.',
    'generation': 'One reference-driven close-up per original monster, transparent background, no border/text, consistent detailed industrial fantasy style.',
    'assets': assets}, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
print(json.dumps({'installed': len(assets), 'manifest': str(manifest.relative_to(ROOT))}))
