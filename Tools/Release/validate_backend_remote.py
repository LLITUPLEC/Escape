"""Stage exact inputs and run isolated suites; production runtime is read-only."""
from pathlib import Path
from datetime import datetime,timezone
import argparse,hashlib,json,shutil,tarfile,subprocess,time,sys,re,os
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('stage',help='Exact uploaded releaseNNN-validation-<12 hex> directory basename')
parser.add_argument('scenarios',nargs='*',choices=('all','recovery','friends','cosmetics','global-boss','global-boss-practice','equipment-sets','quote-preflight'))
args=parser.parse_args()
ROOT=Path('/opt/escape').resolve()
assert Path.cwd().resolve()==ROOT and re.fullmatch(r'release[0-9]{3,12}-validation-[a-f0-9]{12}',args.stage)
stage=(ROOT/args.stage).resolve();assert stage.parent==ROOT and stage.is_dir() and not stage.is_symlink() and not(stage/'candidate').exists()
os.umask(0o077)
ctx=json.loads((stage/'context.json').read_text());assert ctx['stage']==args.stage
dest=stage/'candidate';dest.mkdir()
with tarfile.open(stage/'candidate.tar.gz','r:gz') as tf:
 assert {m.name for m in tf.getmembers()}==set(ctx['manifest']) and len(tf.getmembers())==len(ctx['manifest'])
 for member in tf.getmembers():
  assert member.isfile() and not member.issym() and not member.islnk() and not member.name.startswith('/') and '..' not in Path(member.name).parts and chr(92) not in member.name
  target=(dest/member.name).resolve();assert dest in target.parents
  target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes(tf.extractfile(member).read())
for name,digest in ctx['manifest'].items():assert hashlib.sha256((dest/name).read_bytes()).hexdigest()==digest
def tree(path):
 files={}
 for p in sorted(path.rglob('*')):
  assert not p.is_symlink()
  if p.is_file():files[p.relative_to(path).as_posix()]=hashlib.sha256(p.read_bytes()).hexdigest()
 return files
def digest(files):return hashlib.sha256(json.dumps(files,sort_keys=True).encode()).hexdigest()
production_before=tree(ROOT/'runtime');modules=tree(dest/'runtime')
assert set(production_before)-set(modules)=={'zz_escape_seed.lua'}
assert len(modules)==len([n for n in ctx['manifest'] if n.startswith('runtime/')])
expected_files=dict(production_before,**modules);expected={'sha256':digest(expected_files),'files':expected_files}
(stage/'expected-tested-runtime.json').write_text(json.dumps(expected,indent=2))
previous=stage/'previous-test-inputs';previous.mkdir()
for name in ('multiclient-runtime-overlay','multiclient-catalog-migration'):
 p=ROOT/name
 if p.exists():
  assert p.is_dir() and not p.is_symlink();p.rename(previous/name)
shutil.copytree(dest/'runtime',ROOT/'multiclient-runtime-overlay')
shutil.copytree(dest/'migration',ROOT/'multiclient-catalog-migration')
for name in ('multiclient_review.py','isolated_runner.py','recovery_probe.py','recovery-item-catalog.json'):
 if(ROOT/name).exists():shutil.copy2(ROOT/name,previous/name)
 shutil.copy2(dest/name,ROOT/name)
summary={'utc':datetime.now(timezone.utc).isoformat(),'runtime_sha256':expected['sha256'],'runtime_files':len(expected_files),
 'suite_sha256':ctx['manifest']['multiclient_review.py'],'catalog_sha256':ctx['manifest']['recovery-item-catalog.json'],
 'production_runtime_changed':False,'production_runtime_before_sha256':digest(production_before),'scenarios':{}}
print('TEST runtime SHA256='+expected['sha256']+'; files='+str(len(expected_files)),flush=True)
scenarios=args.scenarios or ['all','recovery'];assert len(scenarios)==len(set(scenarios))
for scenario in scenarios:
 started=time.monotonic();print('START isolated '+scenario,flush=True)
 report=ROOT/('multiclient-isolated-'+scenario+'.json')
 if report.exists():shutil.copy2(report,previous/('previous-'+scenario+'-report.json'));report.unlink()
 with(stage/('isolated-'+scenario+'.log')).open('w') as log:
  result=subprocess.run(['python3',str(ROOT/'isolated_runner.py'),'--scenario',scenario],cwd=ROOT,stdout=log,stderr=subprocess.STDOUT)
 tested=json.loads((ROOT/'multiclient-runtime-manifest.json').read_text());assert tested==expected
 assert tree(ROOT/'runtime')==production_before,'Production runtime changed externally during validation'
 data=json.loads(report.read_text()) if report.exists() else {}
 if report.exists():shutil.copy2(report,stage/report.name)
 logs=ROOT/('multiclient-isolated-'+scenario+'-runtime-errors.log')
 if result.returncode and logs.exists():shutil.copy2(logs,stage/logs.name)
 for suffix in ('-container-state.json','-traceback.log'):
  diagnostic=ROOT/('multiclient-isolated-'+scenario+suffix)
  if result.returncode and diagnostic.exists():shutil.copy2(diagnostic,stage/diagnostic.name)
 checks=data.get('checks',[])
 summary['scenarios'][scenario]={'exit_code':result.returncode,'seconds':round(time.monotonic()-started,1),'checks':len(checks),
  'failed':sum(not c.get('passed') for c in checks),'errors':data.get('errors',[]),'tested_runtime_matches_final_candidate':True}
 (stage/'network-validation-summary.json').write_text(json.dumps(summary,indent=2))
 print(json.dumps({scenario:summary['scenarios'][scenario]}),flush=True)
 if result.returncode:break
summary['success']=len(summary['scenarios'])==len(scenarios) and all(v['exit_code']==0 and not v['failed'] and not v['errors'] for v in summary['scenarios'].values())
summary['production_runtime_after_sha256']=digest(tree(ROOT/'runtime'))
(stage/'network-validation-summary.json').write_text(json.dumps(summary,indent=2))
print('FINAL '+json.dumps(summary),flush=True)
raise SystemExit(0 if summary['success'] else 1)
