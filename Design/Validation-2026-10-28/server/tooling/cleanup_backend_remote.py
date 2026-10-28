"""Remove exact owned validation paths and replaced snapshot after archived success proof."""
from pathlib import Path
from datetime import datetime,timezone
import hashlib,json,re,shutil,subprocess,sys
root=Path('/opt/escape');stage=root/sys.argv[1]
assert root.resolve()==root and stage.parent==root and stage.name=='release0315-validation-ea932cbaaca2'
assert sys.argv[2]=='a4f4504d55c07cabac3749aa58f3e33d7d9d4cb159665b85bbe634453fc86ce1'
def sha(p):
 h=hashlib.sha256()
 with p.open('rb')as stream:
  for data in iter(lambda:stream.read(1048576),b''):h.update(data)
 return h.hexdigest()
def tree(p):
 assert p.is_dir()and not p.is_symlink()and p.resolve()==p
 out={}
 for f in sorted(p.rglob('*')):
  assert not f.is_symlink()
  if f.is_file():out[f.relative_to(p).as_posix()]=sha(f)
 return out
def digest(files):return hashlib.sha256(json.dumps(files,sort_keys=True).encode()).hexdigest()
assert sha(stage/'sanitized-backend-proof.tar.gz')==sys.argv[2]
post=json.loads((stage/'post-deployment-verification.json').read_text());assert post['passed']and post['guardActiveBeforePriceCas']
expected=json.loads((stage/'expected-tested-runtime.json').read_text());assert tree(root/'runtime')==expected['files']
ctx=json.loads((stage/'context.json').read_text())
initial=json.loads((stage/'initial-preflight.json').read_text(encoding='utf-8-sig'))
fresh=Path(post['freshBackup']);rollback=root/'backups/runtime-release-20261027T162323Z';obsolete=root/'backups/snapshot-0.3.15-20261027T193047Z'
assert fresh.parent==root/'backups'and fresh.name=='runtime-release-20261027T223730Z'
backup_manifest=json.loads((fresh/'manifest.json').read_text());fresh_files=tree(fresh);fresh_files.pop('manifest.json')
assert fresh_files==backup_manifest['files']and backup_manifest['pgRestoreListVerified']
assert digest(tree(rollback/'runtime'))=='dfd08b4dd99dfeef305c3a31ae3347dca5c18e9a444a96f44669901db49c1c3f'
assert sha(rollback/'escape.dump')=='88f3546cdfbd363d606f880b37ad1e29ffddbb8e6d40762bb0e0df683bd012ad'
assert digest(tree(obsolete/'runtime'))=='c3a81563b3bf78e9eab55f5d7eab54d11d68dee0675599e71f3eff7dbe2ee445'
assert sha(obsolete/'escape.dump')=='68ded8d4b7e104a9df88a6bc8fd36b8090296bd17f5e7b0564f88de4ca82dc37'
assert {p.name for p in(root/'backups').iterdir()}=={fresh.name,rollback.name,obsolete.name}
def disposable_docker():
 result={}
 for kind,args in(('containers',['docker','ps','-a','--format','{{json .}}']),('volumes',['docker','volume','ls','--format','{{json .}}']),('networks',['docker','network','ls','--format','{{json .}}'])):
  rows=[json.loads(s)for s in subprocess.run(args,capture_output=True,text=True,check=True).stdout.splitlines()if s.strip()]
  result[kind]=[r for r in rows if any('escape-multiclient-'in str(v)or'escape-review-'in str(v)for v in r.values())]
 return result
assert not any(disposable_docker().values()),'Disposable Docker resources remain; do not remove arbitrary projects'
tests=root/'tests';assert not list(tests.glob('escape-multiclient-*'))and not list(root.glob('.multiclient-*'))
overlay=root/'multiclient-runtime-overlay';migration=root/'multiclient-catalog-migration'
assert tree(overlay)=={n.removeprefix('runtime/'):h for n,h in ctx['manifest'].items()if n.startswith('runtime/')}
actual_migration=tree(migration)
assert {n:h for n,h in actual_migration.items()if '__pycache__'not in Path(n).parts}=={n.removeprefix('migration/'):h for n,h in ctx['manifest'].items()if n.startswith('migration/')}
stages=[root/n for n in('release0315-validation-9f20fca7e4de','release0315-validation-c135615aa25c','release0315-validation-bb506a1cbca8',stage.name)]
dirs=[obsolete,overlay,migration,*stages]
removed=[]
for p in dirs:
 assert p.resolve()==p and not p.is_symlink()and(p.parent==root or p.parent==root/'backups')
 files=tree(p)
 removed.append(dict(path=str(p),files=len(files),bytes=sum(f.stat().st_size for f in p.rglob('*')if f.is_file()),treeSha256=digest(files)))
allow_files=['multiclient-isolated-all-container-state.json','multiclient-isolated-all-runtime-errors.log','multiclient-isolated-all-traceback.log','multiclient-isolated-all.json',
 'multiclient-isolated-equipment-sets.json','multiclient-isolated-global-boss-practice.json','multiclient-isolated-lifecycle.json',
 'multiclient-isolated-quote-preflight-container-state.json','multiclient-isolated-quote-preflight-runtime-errors.log','multiclient-isolated-quote-preflight-traceback.log','multiclient-isolated-quote-preflight.json',
 'multiclient-isolated-recovery-container-state.json','multiclient-isolated-recovery-runtime-errors.log','multiclient-isolated-recovery.json']
files=[]
for name in allow_files:
 p=root/name
 if p.exists():
  assert p.is_file()and not p.is_symlink()and p.resolve()==p
  files.append(p);removed.append(dict(path=str(p),files=1,bytes=p.stat().st_size,sha256=sha(p)))
# Preserve the success report before removing its staging directory.
postcopy=root/'backend-validation-20261028.json';assert not postcopy.exists()
postcopy.write_text(json.dumps(post,indent=2))
for p in dirs:shutil.rmtree(p)
for p in files:p.unlink()
assert {p.name for p in(root/'backups').iterdir()}=={fresh.name,rollback.name}
assert not any(p.exists()for p in dirs+files)
assert tree(root/'runtime')==expected['files']
assert not any(disposable_docker().values())
for name,row in initial['protected'].items():
 if name=='admin':assert tree(root/'admin')=={n:v['sha256']for n,v in row.items()}
 else:assert sha(root/name)==row['sha256']
web=Path('/var/www/escape');current=(web/'current').resolve(strict=True)
assert str(current)==initial['website']['current_target']
for name in('index.html','release.json'):assert sha(current/name)==initial['website'][name]['sha256']
assert {p.name:dict(bytes=p.stat().st_size,sha256=sha(p))for p in sorted((web/'downloads').glob('*.apk'))}==initial['website']['apks']
health=subprocess.run(['docker','inspect','--format','{{.State.Health.Status}}','escape-nakama-1'],capture_output=True,text=True,check=True).stdout.strip();assert health=='healthy'
report=dict(passed=True,cleanedUtc=datetime.now(timezone.utc).isoformat(),runtimeSha256=expected['sha256'],runtimeFiles=len(expected['files']),
 proofArchiveSha256=sys.argv[2],proofArchivedAndLocallyHashVerified=True,removed=removed,
 retainedBackups=[str(rollback),str(fresh)],backupCount=2,rollback014Preserved=True,newPrechangeBackupVerified=True,
 ownCandidateStagesRemaining=[],disposableDocker=disposable_docker(),temporaryAccountsRemoved=True,nakamaHealth=health,
 reusableServerHelpersPreserved=True,protectedConfigAndAdminUnchanged=True,websiteAndPublishedApksUntouched=True)
(root/'backend-cleanup-20261028.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report,indent=2))
