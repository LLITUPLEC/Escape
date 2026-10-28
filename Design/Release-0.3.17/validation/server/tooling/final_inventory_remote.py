"""Read-only final published inventory; injected pins contain hashes/metadata, never credentials."""
from pathlib import Path
from datetime import datetime,timezone
import hashlib,importlib.util,json,subprocess
root=Path('/opt/escape');web=Path('/var/www/escape');assert root.resolve()==root and web.resolve()==web
def sha(p):
 h=hashlib.sha256()
 with p.open('rb')as stream:
  for block in iter(lambda:stream.read(1048576),b''):h.update(block)
 return h.hexdigest()
def tree(p):
 assert p.resolve()==p and p.is_dir()and not p.is_symlink()
 rows={}
 for f in sorted(p.rglob('*')):
  assert not f.is_symlink()
  if f.is_file():rows[f.relative_to(p).as_posix()]=dict(bytes=f.stat().st_size,sha256=sha(f))
 return rows
def hashes(rows):return{n:r['sha256']for n,r in rows.items()}
def digest(rows):return hashlib.sha256(json.dumps(rows,sort_keys=True).encode()).hexdigest()
retention=json.loads((root/'release-0.3.17-retention.json').read_text());assert retention['passed']and retention['retentionApplied']and retention['backupCount']==2
runtime=tree(root/'runtime');assert hashes(runtime)==BASELINE['runtime']['files']and digest(hashes(runtime))==DEPLOY_PROOF['runtimeSha256']
assert hashes(runtime)['zz_escape_seed.lua']==BASELINE['runtime']['files']['zz_escape_seed.lua']
protected={}
for name,wanted in BASELINE['protected'].items():
 if name=='admin':
  actual=tree(root/'admin');assert actual==wanted;protected[name]=actual
 else:
  actual=dict(bytes=(root/name).stat().st_size,sha256=sha(root/name));assert actual==wanted;protected[name]=actual
helpers={name:sha(root/name)for name in BASELINE['canonicalHelpers']};assert helpers==BASELINE['canonicalHelpers']
publisher=sha(root/'deploy_download_page.py');assert publisher==PLAN['publisherSha256']
spec=importlib.util.spec_from_file_location('final_inventory_admin',root/'tools/storage_admin.py');admin=importlib.util.module_from_spec(spec);spec.loader.exec_module(admin)
config=json.loads((root/'secrets/server.yml').read_text());console=admin.Console('http://127.0.0.1:27351');catalogs={}
try:
 console.login(config['console']['username'],config['console']['password'])
 for name,wanted in BASELINE['catalogs'].items():
  collection,key=name.split('/',1);row=console.read(collection,key)
  actual=None if row is None else dict(version=row['version'],valueSha256=digest(row['value']))
  assert actual==wanted;catalogs[name]=actual
finally:console.close()
current=(web/'current').resolve(strict=True);assert str(current)==retention['currentTarget']
releases={}
for p in sorted((web/'releases').iterdir()):
 rows=tree(p);manifest=json.loads((p/'release.json').read_text(encoding='utf-8-sig'))
 assert manifest['version']in('0.3.17','0.3.16')
 releases[str(p)]=dict(version=manifest['version'],current=p==current,files=rows)
assert len(releases)==2 and sorted(r['version']for r in releases.values())==['0.3.16','0.3.17']
apks={p.name:dict(bytes=p.stat().st_size,sha256=sha(p))for p in(web/'downloads').glob('*.apk')}
assert apks=={PLAN['apkFilename']:dict(bytes=PLAN['apkBytes'],sha256=PLAN['apkSha256']),PLAN['previousApkFilename']:dict(bytes=PLAN['previousApkBytes'],sha256=PLAN['previousApkSha256'])}
backups={}
for p in sorted((root/'backups').iterdir()):
 assert p.resolve()==p and not p.is_symlink()
 rows=hashes(tree(p));rows.pop('manifest.json');manifest=json.loads((p/'manifest.json').read_text())
 assert rows==manifest['files']and manifest['pgRestoreListVerified']is True
 rt={n.removeprefix('runtime/'):h for n,h in rows.items()if n.startswith('runtime/')}
 backups[str(p)]=dict(manifestSha256=sha(p/'manifest.json'),files=len(rows),runtimeSha256=digest(rt),dump=dict(bytes=(p/'escape.dump').stat().st_size,sha256=sha(p/'escape.dump')),fullManifestVerified=True,pgRestoreListVerified=True)
assert set(backups)==set(retention['retainedBackups'])and len(backups)==2
containers=[]
for wanted in BASELINE['containers']:
 raw=json.loads(subprocess.run(['docker','inspect',wanted['name']],capture_output=True,text=True,check=True).stdout)[0]
 actual=dict(name=wanted['name'],id=raw['Id'],startedAt=raw['State']['StartedAt'],restartCount=raw['RestartCount'],status=raw['State']['Status'],health=raw['State']['Health']['Status'],image=raw['Image'])
 assert actual==wanted and actual['health']=='healthy';containers.append(actual)
disposable={}
for kind,args in(('containers',['docker','ps','-a','--format','{{json .}}']),('volumes',['docker','volume','ls','--format','{{json .}}']),('networks',['docker','network','ls','--format','{{json .}}'])):
 rows=[json.loads(line)for line in subprocess.run(args,capture_output=True,text=True,check=True).stdout.splitlines()if line.strip()]
 disposable[kind]=[r for r in rows if any('escape-multiclient-'in str(v)or'escape-review-'in str(v)for v in r.values())]
assert not any(disposable.values())
stages=sorted(str(p)for p in root.glob('release0317-validation-*'))
assert not REQUIRE_CLEAN_STAGES or not stages
assert not list((root/'page-staging').glob(PLAN['identifier']+'*'))
report=dict(passed=True,readOnly=True,checkedUtc=datetime.now(timezone.utc).isoformat(),version='0.3.17',versionCode=19,
 runtime=dict(sha256=digest(hashes(runtime)),files=runtime),seedPreserved=True,protected=protected,catalogs=catalogs,
 canonicalHelpers=helpers,publisherSha256=publisher,currentTarget=str(current),releases=releases,apks=apks,backups=backups,
 backupCount=2,containers=containers,noRestartAfterBackendApply=True,disposableDocker=disposable,remainingBackendStages=stages,
 ownedStaticStagingEmpty=True,otherProjectsAndDockerVolumesUntouched=True)
print(json.dumps(report,indent=2))
