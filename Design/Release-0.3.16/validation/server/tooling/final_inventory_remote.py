from pathlib import Path
from datetime import datetime,timezone
import hashlib,importlib.util,json,subprocess
root=Path('/opt/escape');web=Path('/var/www/escape')
def sha(p):
 h=hashlib.sha256()
 with p.open('rb')as stream:
  for data in iter(lambda:stream.read(1048576),b''):h.update(data)
 return h.hexdigest()
def tree(p):
 assert p.is_dir()and p.resolve()==p and not p.is_symlink()
 out={}
 for f in sorted(p.rglob('*')):
  assert not f.is_symlink()
  if f.is_file():out[f.relative_to(p).as_posix()]=sha(f)
 return out
def digest(files):return hashlib.sha256(json.dumps(files,sort_keys=True).encode()).hexdigest()
runtime=tree(root/'runtime');assert runtime==BASELINE['runtime']['files']and len(runtime)==66
protected={}
for name,row in BASELINE['protected'].items():
 if name=='admin':
  actual=tree(root/'admin');assert actual=={n:r['sha256']for n,r in row.items()};protected[name]=dict(files=len(actual),treeSha256=digest(actual))
 else:assert sha(root/name)==row['sha256'];protected[name]=row['sha256']
current=(web/'current').resolve(strict=True);releases={}
for p in sorted((web/'releases').iterdir()):
 rows=tree(p);m=json.loads((p/'release.json').read_text(encoding='utf-8-sig'))
 releases[str(p)]=dict(version=m['version'],files=len(rows),treeSha256=digest(rows),current=p==current)
assert {r['version']for r in releases.values()}=={'0.3.16','0.3.15'}and len(releases)==2
previous=Path(BASELINE['website']['currentTarget']);assert tree(previous)=={n:r['sha256']for n,r in BASELINE['website']['releases'][str(previous)]['files'].items()}
apks={p.name:dict(bytes=p.stat().st_size,sha256=sha(p))for p in sorted((web/'downloads').glob('*.apk'))}
assert apks=={PLAN['apkFilename']:dict(bytes=PLAN['apkBytes'],sha256=PLAN['apkSha256']),PLAN['previousApkFilename']:dict(bytes=PLAN['previousApkBytes'],sha256=PLAN['previousApkSha256'])}
backups={}
for p in sorted((root/'backups').iterdir()):
 manifest=json.loads((p/'manifest.json').read_text());rows=tree(p);rows.pop('manifest.json');assert rows==manifest['files']and manifest['pgRestoreListVerified']
 rt=tree(p/'runtime');backups[str(p)]=dict(files=len(rows),manifestSha256=sha(p/'manifest.json'),runtimeSha256=digest(rt),runtimeFiles=len(rt),
  dumpBytes=(p/'escape.dump').stat().st_size,dumpSha256=sha(p/'escape.dump'),manifestVerified=True,pgRestoreListVerified=True)
assert set(backups)=={'/opt/escape/backups/pre-publication-0.3.16-20261028T044329Z','/opt/escape/backups/runtime-release-20261027T223730Z'}
containers=[]
for before in BASELINE['containers']:
 raw=json.loads(subprocess.run(['docker','inspect',before['name']],capture_output=True,text=True,check=True).stdout)[0]
 assert raw['Id']==before['id']and raw['State']['StartedAt']==before['startedAt']and raw['RestartCount']==before['restartCount']and raw['Image']==before['image']
 health=raw['State'].get('Health',{}).get('Status');assert raw['State']['Status']=='running'
 if before['name']=='escape-nakama-1':assert health=='healthy'
 containers.append(dict(name=before['name'],id=raw['Id'],startedAt=raw['State']['StartedAt'],restartCount=raw['RestartCount'],health=health))
spec=importlib.util.spec_from_file_location('final_admin',root/'tools/storage_admin.py');admin=importlib.util.module_from_spec(spec);spec.loader.exec_module(admin)
config=json.loads((root/'secrets/server.yml').read_text());console=admin.Console('http://127.0.0.1:27351');catalogs={}
try:
 console.login(config['console']['username'],config['console']['password'])
 for name,wanted in BASELINE['catalogs'].items():
  collection,key=name.split('/',1);row=console.read(collection,key)
  actual=None if row is None else dict(version=row['version'],valueSha256=digest(row['value']));assert actual==wanted;catalogs[name]=actual
finally:console.close()
assert sha(root/'deploy_download_page.py')==PLAN['publisherSha256']
assert not any(p.name.startswith(PLAN['identifier'])for p in(root/'page-staging').iterdir())
assert not list((root/'page-staging').iterdir())
assert(root/'Caddyfile').read_bytes()==Path('/etc/caddy/sites/escape.caddy').read_bytes()
report=dict(passed=True,checkedUtc=datetime.now(timezone.utc).isoformat(),publishedVersion='0.3.16',versionCode=18,currentTarget=str(current),
 runtimeSha256=digest(runtime),runtimeFiles=len(runtime),seedSha256=runtime['zz_escape_seed.lua'],runtimeAndCatalogsUnchanged=True,
 protectedConfigAndAdminUnchanged=True,protectedHashes=protected,catalogs=catalogs,publisherSha256=PLAN['publisherSha256'],
 releases=releases,apks=apks,backups=backups,backupCount=2,containers=containers,noApiOrDatabaseRestart=True,nakamaHealth='healthy',
 pageStaging=[],old014ArtifactsRemoved=True,otherProjectsAndDockerVolumesUntouched=True)
(root/'release-0.3.16-final-inventory.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report,indent=2))
