"""Read-only publication baseline; hashes only, no credentials or player records."""
from pathlib import Path
from datetime import datetime,timezone
import hashlib,importlib.util,json,subprocess
root=Path('/opt/escape');web=Path('/var/www/escape')
assert root.resolve()==root and web.resolve()==web
def sha(p):
 h=hashlib.sha256()
 with p.open('rb')as stream:
  for data in iter(lambda:stream.read(1048576),b''):h.update(data)
 return h.hexdigest()
def tree(p):
 assert p.resolve()==p and p.is_dir()and not p.is_symlink()
 files={}
 for f in sorted(p.rglob('*')):
  assert not f.is_symlink()
  if f.is_file():files[f.relative_to(p).as_posix()]=dict(bytes=f.stat().st_size,sha256=sha(f))
 return files
def digest(files):return hashlib.sha256(json.dumps(files,sort_keys=True).encode()).hexdigest()
runtime=tree(root/'runtime');runtime_files={n:r['sha256']for n,r in runtime.items()}
assert len(runtime)==66 and digest(runtime_files)=='aad128d4e8b67dc8b423e6725620c89fee22519e43f727fa1fcfa2b59ed36653'
protected={n:dict(bytes=(root/n).stat().st_size,sha256=sha(root/n))for n in('.env','compose.yml','nginx.conf.template','Caddyfile','secrets/server.yml')}
protected['admin']=tree(root/'admin')
publisher=dict(path=str(root/'deploy_download_page.py'),sha256=sha(root/'deploy_download_page.py'))
helpers={n:sha(root/n)for n in('deploy_runtime.py','update_proxy.py','dashboard_smoke.py','security_smoke.py')}
assert helpers['deploy_runtime.py']=='ff499e1a887c724b1bcc072ed41f1d55735b210bfdd2f2a624ae6858530f74d2'
current=(web/'current').resolve(strict=True);assert current.parent==web/'releases'
releases={}
for p in sorted((web/'releases').iterdir()):
 assert p.is_dir()and not p.is_symlink()
 manifest=json.loads((p/'release.json').read_text(encoding='utf-8-sig'))
 releases[str(p)]=dict(version=manifest['version'],manifest=manifest,files=tree(p),current=p==current)
apks={p.name:dict(bytes=p.stat().st_size,sha256=sha(p))for p in sorted((web/'downloads').glob('*.apk'))}
assert len(apks)==2
assert apks['Escape-0.3.15-e7ac556941de.apk']==dict(bytes=123571699,sha256='e7ac556941debbe4295b14cfd298f7a278d4da8ffb97ee8e89d59c954f6b83c8')
assert json.loads((current/'release.json').read_text(encoding='utf-8-sig'))['version']=='0.3.15'
spec=importlib.util.spec_from_file_location('admin',root/'tools/storage_admin.py');admin=importlib.util.module_from_spec(spec);spec.loader.exec_module(admin)
config=json.loads((root/'secrets/server.yml').read_text());console=admin.Console('http://127.0.0.1:27351')
try:
 console.login(config['console']['username'],config['console']['password'])
 catalogs={}
 for collection,key in(('duel_match3_item_defs','catalog'),('duel_global_boss','config'),('duel_shop_catalog','v1'),('duel_match3_server_aura','active')):
  row=console.read(collection,key)
  catalogs[collection+'/'+key]=None if row is None else dict(version=row['version'],valueSha256=digest(row['value']))
finally:console.close()
backups={}
for p in sorted((root/'backups').iterdir()):
 files=tree(p);rt={n.removeprefix('runtime/'):r['sha256']for n,r in files.items()if n.startswith('runtime/')}
 backups[str(p)]=dict(files=len(files),bytes=sum(r['bytes']for r in files.values()),runtimeSha256=digest(rt),
  dump=files.get('escape.dump'),manifestSha256=sha(p/'manifest.json')if(p/'manifest.json').exists()else None)
assert set(backups)=={'/opt/escape/backups/runtime-release-20261027T162323Z','/opt/escape/backups/runtime-release-20261027T223730Z'}
containers=[]
for name in('escape-nakama-1','escape-postgres-1'):
 raw=json.loads(subprocess.run(['docker','inspect',name],capture_output=True,text=True,check=True).stdout)[0]
 containers.append(dict(name=name,id=raw['Id'],startedAt=raw['State']['StartedAt'],restartCount=raw['RestartCount'],
  status=raw['State']['Status'],health=raw['State'].get('Health',{}).get('Status'),image=raw['Image']))
assert containers[0]['health']=='healthy'
report=dict(checkedUtc=datetime.now(timezone.utc).isoformat(),readOnly=True,publisher=publisher,canonicalHelpers=helpers,
 runtime=dict(sha256=digest(runtime_files),files=runtime_files),protected=protected,catalogs=catalogs,
 website=dict(currentTarget=str(current),releases=releases,apks=apks),backups=backups,containers=containers,
 caddyActiveMatchesProject=(root/'Caddyfile').read_bytes()==Path('/etc/caddy/sites/escape.caddy').read_bytes(),
 pageStaging=[p.name for p in sorted((root/'page-staging').iterdir())])
print(json.dumps(report,ensure_ascii=False))
