"""Package pinned local backend validation inputs; no network or server actions."""
from pathlib import Path
import argparse, hashlib, json, re, tarfile, uuid
ROOT=Path(__file__).resolve().parents[2]
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--version',default='0.3.16')
parser.add_argument('--output-dir',type=Path,default=ROOT/'.cache/release-work/backend')
args=parser.parse_args()
assert re.fullmatch(r'[0-9]+\.[0-9]+\.[0-9]+',args.version)
stage='release'+args.version.replace('.','')+'-validation-'+uuid.uuid4().hex[:12]
OUT=args.output_dir.resolve()/stage
assert OUT.is_relative_to(ROOT)
OUT.mkdir(parents=True,exist_ok=False)
inputs={('runtime/'+p.name):p for p in sorted((ROOT/'Server/nakama/modules').glob('*.lua'))}
inputs.update({
 'multiclient_review.py':ROOT/'Server/nakama/tests/multiclient/run.py',
 'isolated_runner.py':ROOT/'Server/nakama/tests/multiclient/isolated_runner.py',
 'recovery_probe.py':ROOT/'Server/nakama/tests/multiclient/recovery_probe.py',
 'recovery-item-catalog.json':ROOT/'Server/nakama/data/duel_match3_item_defs.json',
 'migration/merge_consumable_catalog.py':ROOT/'Server/nakama/tools/merge_consumable_catalog.py',
 'migration/storage_admin.py':ROOT/'Server/nakama/tools/storage_admin.py',
 'migration/consumable-item-defs.json':ROOT/'Server/nakama/data/migrations/consumables-0.3.9.json',
 'migration/merge_equipment_ingots.py':ROOT/'Server/nakama/tools/merge_equipment_ingots.py',
 'security_smoke.py':ROOT/'Server/nakama/deploy/security_smoke.py',
 'deploy_runtime.py':ROOT/'Server/nakama/deploy/deploy_runtime.py',
})
context={'stage':stage,'version':args.version,'manifest':{n:hashlib.sha256(p.read_bytes()).hexdigest() for n,p in inputs.items()}}
with tarfile.open(OUT/'candidate.tar.gz','x:gz') as tf:
 for name,path in inputs.items():
  assert path.is_file() and not path.is_symlink() and path.resolve().is_relative_to(ROOT)
  tf.add(path,arcname=name,recursive=False)
(OUT/'context.json').write_text(json.dumps(context,indent=2),encoding='utf-8')
print(json.dumps({'stage':stage,'local_directory':str(OUT),'runtime_files':sum(n.startswith('runtime/') for n in inputs),'candidate_sha256':hashlib.sha256((OUT/'candidate.tar.gz').read_bytes()).hexdigest()}))
