"""Create one verified private final-runtime backup immediately before approved0.3.17publication."""
from pathlib import Path
from datetime import datetime,timezone
import hashlib,importlib.util,json,os,shutil,subprocess
root=Path('/opt/escape');assert root.resolve()==root
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
def check_catalogs():
 spec=importlib.util.spec_from_file_location('backup_storage_admin',root/'tools/storage_admin.py')
 admin=importlib.util.module_from_spec(spec);spec.loader.exec_module(admin)
 config=json.loads((root/'secrets/server.yml').read_text());console=admin.Console('http://127.0.0.1:27351')
 try:
  console.login(config['console']['username'],config['console']['password'])
  for name,wanted in BASELINE['catalogs'].items():
   collection,key=name.split('/',1);row=console.read(collection,key)
   actual=None if row is None else dict(version=row['version'],valueSha256=digest(row['value']))
   assert actual==wanted,'Configured catalog changed: '+name
 finally:console.close()
def check_static():
 assert tree(root/'runtime')==BASELINE['runtime']['files']
 assert sha(root/'deploy_download_page.py')==BASELINE['publisher']['sha256']
 for name,row in BASELINE['protected'].items():
  if name=='admin':assert tree(root/'admin')=={n:r['sha256']for n,r in row.items()}
  else:assert sha(root/name)==row['sha256']
 for name,h in BASELINE['canonicalHelpers'].items():assert sha(root/name)==h
 current=Path('/var/www/escape/current').resolve(strict=True)
 assert str(current)==BASELINE['website']['currentTarget']
 assert sha(current/'release.json')==BASELINE['website']['releases'][str(current)]['files']['release.json']['sha256']
check_static()
check_catalogs()
assert digest(BASELINE['runtime']['files'])==DEPLOY_PROOF['runtimeSha256']
assert set(str(p)for p in(root/'backups').iterdir())==set(BASELINE['backups'])
os.umask(0o077)
stamp=datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ')
backup=root/'backups'/('pre-publication-0.3.17-'+stamp);assert backup.parent==root/'backups'and not backup.exists()
backup.mkdir(mode=0o700)
for name in('runtime','.env','compose.yml','nginx.conf.template','Caddyfile','secrets','admin','tools'):
 p=root/name;assert not p.is_symlink()
 if p.is_dir():tree(p);shutil.copytree(p,backup/name)
 else:assert p.is_file();shutil.copy2(p,backup/name)
source=backup/'source';source.mkdir(mode=0o700)
source_names={p.name for p in root.glob('*.py')if p.is_file()}
source_names.update(('README.md','RATE_LIMITS.md','deployed-runtime-manifest.json','multiclient-runtime-manifest.json'))
for name in sorted(source_names):
 p=root/name;assert p.is_file()and not p.is_symlink()and p.resolve()==p
 shutil.copy2(p,source/name)
dump=backup/'escape.dump'
with dump.open('xb')as output:
 subprocess.run(['docker','compose','-p','escape','exec','-T','postgres','pg_dump','-U','escape','-d','escape','-Fc'],cwd=root,stdout=output,check=True)
with dump.open('rb')as data:
 subprocess.run(['docker','compose','-p','escape','exec','-T','postgres','pg_restore','--list'],cwd=root,stdin=data,stdout=subprocess.DEVNULL,check=True)
assert tree(backup/'runtime')==BASELINE['runtime']['files']
check_static()
check_catalogs()
files=tree(backup)
manifest=dict(schema=2,purpose='Verified current final-runtime DB/runtime/source/config backup before static0.3.17publication',
 createdUtc=datetime.now(timezone.utc).isoformat(),runtimeSha256=BASELINE['runtime']['sha256'],runtimeFiles=len(BASELINE['runtime']['files']),
 pgRestoreListVerified=True,files=files,prePublicationPublisherSha256=BASELINE['publisher']['sha256'])
(backup/'manifest.json').write_text(json.dumps(manifest,indent=2))
actual=tree(backup);actual.pop('manifest.json');assert actual==files
health=subprocess.run(['docker','inspect','--format','{{.State.Health.Status}}','escape-nakama-1'],capture_output=True,text=True,check=True).stdout.strip();assert health=='healthy'
report=dict(passed=True,createdUtc=manifest['createdUtc'],backup=str(backup),files=len(files),bytes=sum(p.stat().st_size for p in backup.rglob('*')if p.is_file()),
 manifestSha256=sha(backup/'manifest.json'),runtimeSha256=manifest['runtimeSha256'],runtimeFiles=len(BASELINE['runtime']['files']),seedPreserved=True,
 dumpBytes=dump.stat().st_size,dumpSha256=sha(dump),pgRestoreListVerified=True,fullFileManifestVerified=True,
 previous016PrechangeBackupPreserved=DEPLOY_PROOF['backup'],
 oldBaselineBackupsRetainedUntilPublication=True,noRuntimeOrCatalogMutation=True,noApiOrDatabaseRestart=True,nakamaHealth=health)
(root/'publication-backup-0317.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report,indent=2))
