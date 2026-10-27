"""Validate the three original generated Shelter icons without editing pixels."""
from pathlib import Path
import hashlib
import json
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
RELEASE = ROOT / 'Design/Release-0.3.15'
data = json.loads((RELEASE / 'art-manifest.json').read_text(encoding='utf-8'))
assert data['mode'] == 'built-in image_gen' and len(data['assets']) == 3
rows = []
for asset in data['assets']:
    path = ROOT / asset['destination']
    digest = hashlib.sha256(path.read_bytes()).hexdigest()
    original = Path(asset['source'])
    assert not original.exists() or digest == hashlib.sha256(original.read_bytes()).hexdigest()
    with Image.open(path) as image:
        assert image.mode == 'RGBA'
        alpha = image.getchannel('A')
        assert alpha.getextrema() == (0, 255)
        assert alpha.histogram()[0] > image.width * image.height * .05
        rows.append(dict(id=asset['id'], path=asset['destination'], sha256=digest,
                         bytes=path.stat().st_size, dimensions=image.size))
assert len({row['sha256'] for row in rows}) == 3
report = dict(passed=True, originalPixelsUnchanged=True, generationMode=data['mode'], assets=rows)
(RELEASE / 'validation/art-validation.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
print('PASS 3 unique transparent generated Shelter icons; original PNG pixels unchanged.')
