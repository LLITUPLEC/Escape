"""Final exact-namespace cleanup after local sanitized-proof archive and successful publication retention."""
from pathlib import Path
from datetime import datetime,timezone
import hashlib,json,re,shutil,subprocess
root=Path('/opt/escape');assert root.resolve()==root
assert CLEANUP_APPROVED is True and CLEANUP_PLAN['proofArchivedLocallyAndShaVerified']is True
retention=json.loads((root/'release-0.3.17-retention.json').read_text());assert retention['passed']and retention['retentionApplied']and retention['backupCount']==2
def sha(p):
 h=hashlib.sha256()
 with p.open('rb')as stream:
  for b in iter(lambda:stream.read(1048576),b''):h.update(b)
 return h.hexdigest()
def tree(p):
 assert p.resolve()==p and p.is_dir()and not p.is_symlink()
 rows={}
 for f in sorted(p.rglob('*')):
  assert not f.is_symlink()
  if f.is_file():rows[f.relative_to(p).as_posix()]=sha(f)
 return rows
def digest(rows):return hashlib.sha256(json.dumps(rows,sort_keys=True).encode()).hexdigest()
runtime=tree(root/'runtime');assert digest(runtime)==retention['runtimeSha256']==CLEANUP_PLAN['runtimeSha256']
assert {str(p)for p in(root/'backups').iterdir()}==set(retention['retainedBackups'])
def disposable_resources():
 out={}
 for kind,args in(('containers',['docker','ps','-a','--format','{{json .}}']),('volumes',['docker','volume','ls','--format','{{json .}}']),('networks',['docker','network','ls','--format','{{json .}}'])):
  rows=[json.loads(s)for s in subprocess.run(args,capture_output=True,text=True,check=True).stdout.splitlines()if s.strip()]
  out[kind]=[r for r in rows if any('escape-multiclient-'in str(v)or'escape-review-'in str(v)for v in r.values())]
 return out
assert not any(disposable_resources().values()),'Disposable resources remain: inspect exact owned projects before any cleanup'
assert not list((root/'tests').glob('escape-multiclient-*'))and not list(root.glob('.multiclient-*'))
directories=[];files=[];removed=[]
assert {str(p)for p in root.glob('release0317-validation-*')}=={row['path']for row in CLEANUP_PLAN['stages']},'Candidate namespace changed after inventory'
for row in CLEANUP_PLAN['stages']:
 p=Path(row['path']);assert p.parent==root and re.fullmatch(r'release0317-validation-[a-f0-9]{12}',p.name)
 assert sha(p/'context.json')==row['contextSha256']
 actual=tree(p);assert digest(actual)==row['treeSha256']
 directories.append(p);removed.append(dict(path=str(p),files=len(actual),treeSha256=row['treeSha256']))
assert len(directories)==len(set(directories))
for row in CLEANUP_PLAN['temporaryDirectories']:
 p=Path(row['path']);assert p.parent==root and p.name in('multiclient-runtime-overlay','multiclient-catalog-migration')
 actual=tree(p);assert digest(actual)==row['treeSha256']
 directories.append(p);removed.append(dict(path=str(p),files=len(actual),treeSha256=row['treeSha256']))
for row in CLEANUP_PLAN['temporaryReportFiles']:
 p=Path(row['path']);assert p.parent==root and re.fullmatch(r'multiclient-isolated-[a-z0-9-]+\.(json|log)',p.name)
 assert p.resolve()==p and p.is_file()and not p.is_symlink()and sha(p)==row['sha256']
 files.append(p);removed.append(dict(path=str(p),sha256=row['sha256']))
assert len(files)==len(set(files))
assert len(directories)==len(set(directories))
for p in directories:shutil.rmtree(p)
for p in files:p.unlink()
assert tree(root/'runtime')==runtime and not any(disposable_resources().values())
assert not any(p.exists()for p in directories+files)
assert {str(p)for p in(root/'backups').iterdir()}==set(retention['retainedBackups'])
health=subprocess.run(['docker','inspect','--format','{{.State.Health.Status}}','escape-nakama-1'],capture_output=True,text=True,check=True).stdout.strip();assert health=='healthy'
report=dict(passed=True,checkedUtc=datetime.now(timezone.utc).isoformat(),runtimeSha256=digest(runtime),runtimeFiles=len(runtime),
 removed=removed,retainedBackups=retention['retainedBackups'],backupCount=2,disposableDocker=disposable_resources(),nakamaHealth=health,
 proofArchivedLocallyAndShaVerified=True,otherProjectsAndDockerVolumesUntouched=True)
report.update(archiveAcknowledgmentSha256=CLEANUP_PLAN['archiveAcknowledgmentSha256'],proofArchiveManifestSha256=CLEANUP_PLAN['proofArchiveManifestSha256'])
(root/'release-0.3.17-backend-cleanup.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
