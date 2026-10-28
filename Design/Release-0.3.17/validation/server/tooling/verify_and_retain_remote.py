"""Pinned static publication proof and exact two-release/two-backup retention."""
from pathlib import Path
from datetime import datetime,timezone
import hashlib,importlib.util,json,re,shutil,subprocess
root=Path('/opt/escape');web=Path('/var/www/escape');parent=root/'page-staging'
assert root.resolve()==root and web.resolve()==web and parent.resolve()==parent
assert PLAN['version']=='0.3.17'and PLAN['versionCode']==19 and PLAN['published']is False
identifier=PLAN['identifier'];assert re.fullmatch(r'escape0317-[a-f0-9]{32}',identifier)
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
  if f.is_file():out[f.relative_to(p).as_posix()]=dict(bytes=f.stat().st_size,sha256=sha(f))
 return out
def hashes(rows):return{n:r['sha256']for n,r in rows.items()}
def digest(rows):return hashlib.sha256(json.dumps(rows,sort_keys=True).encode()).hexdigest()
def backup_verified(p):
 assert p.parent==root/'backups'and p.resolve()==p and not p.is_symlink()
 m=json.loads((p/'manifest.json').read_text());rows=hashes(tree(p));rows.pop('manifest.json')
 assert rows==m['files']and m['pgRestoreListVerified']
 return m,rows
assert INTEGRITY['passed']and INTEGRITY['version']=='0.3.17'and INTEGRITY['fullApkVerified']
assert INTEGRITY['apkBytes']==PLAN['apkBytes']and INTEGRITY['apkSha256']==PLAN['apkSha256']
assert INTEGRITY['filesChecked']==INTEGRITY['passedFiles']==PLAN['memberCount']
assert SMOKE['success']and SMOKE['version']=='0.3.17'and SMOKE['api_health_status']==200
assert SMOKE['previous_apk_bytes']==PLAN['previousApkBytes']and all(v=='passed'for v in SMOKE['checks'].values())
bundle={r['path']:dict(bytes=r['expectedBytes'],sha256=r['expectedSha256'])for r in INTEGRITY['files']}
assert len(bundle)==PLAN['memberCount']and all(r['passed']for r in INTEGRITY['files'])
fresh=json.loads((root/'publication-backup-0317.json').read_text());assert fresh['passed']and fresh['pgRestoreListVerified']and fresh['fullFileManifestVerified']
current_backup=Path(fresh['backup']);assert re.fullmatch(r'pre-publication-0\.3\.17-[0-9]{8}T[0-9]{6}Z',current_backup.name)
current_manifest,_=backup_verified(current_backup)
assert current_manifest['runtimeSha256']==BASELINE['runtime']['sha256']==DEPLOY_PROOF['runtimeSha256']
assert sha(current_backup/'escape.dump')==fresh['dumpSha256']and sha(current_backup/'manifest.json')==fresh['manifestSha256']
assert sha(current_backup/'source/deploy_download_page.py')==BASELINE['publisher']['sha256']
rollback=Path(DEPLOY_PROOF['backup']);rollback_manifest,_=backup_verified(rollback)
assert rollback_manifest['runtimeSha256']==ORIGINAL_BASELINE['runtime']['sha256']
assert hashes(tree(rollback/'runtime'))==ORIGINAL_BASELINE['runtime']['files']
def check_backend():
 assert hashes(tree(root/'runtime'))==BASELINE['runtime']['files']
 for n,row in BASELINE['protected'].items():
  if n=='admin':assert tree(root/'admin')==row
  else:assert sha(root/n)==row['sha256']
 for n,h in BASELINE['canonicalHelpers'].items():assert sha(root/n)==h
 assert sha(root/'deploy_download_page.py')==PLAN['publisherSha256']
 spec=importlib.util.spec_from_file_location('pub_admin',root/'tools/storage_admin.py');admin=importlib.util.module_from_spec(spec);spec.loader.exec_module(admin)
 config=json.loads((root/'secrets/server.yml').read_text());console=admin.Console('http://127.0.0.1:27351')
 try:
  console.login(config['console']['username'],config['console']['password'])
  for name,wanted in BASELINE['catalogs'].items():
   collection,key=name.split('/',1);row=console.read(collection,key)
   assert(None if row is None else dict(version=row['version'],valueSha256=digest(row['value'])))==wanted
 finally:console.close()
 for before in BASELINE['containers']:
  raw=json.loads(subprocess.run(['docker','inspect',before['name']],capture_output=True,text=True,check=True).stdout)[0]
  assert raw['Id']==before['id']and raw['State']['StartedAt']==before['startedAt']and raw['RestartCount']==before['restartCount']and raw['Image']==before['image']
  assert raw['State']['Status']=='running'and raw['State']['Health']['Status']=='healthy'
def check_web():
 current=(web/'current').resolve(strict=True);assert current.parent==web/'releases'and current!=Path(BASELINE['website']['currentTarget'])
 assert tree(current)==bundle
 release=json.loads((current/'release.json').read_text(encoding='utf-8-sig'));assert release['version']=='0.3.17'and release['sha256']==PLAN['apkSha256']
 previous=Path(BASELINE['website']['currentTarget']);assert tree(previous)==BASELINE['website']['releases'][str(previous)]['files']
 for name,n,h in((PLAN['apkFilename'],PLAN['apkBytes'],PLAN['apkSha256']),(PLAN['previousApkFilename'],PLAN['previousApkBytes'],PLAN['previousApkSha256'])):
  p=web/'downloads'/name;assert p.is_file()and not p.is_symlink()and p.stat().st_size==n and sha(p)==h
 assert(root/'Caddyfile').read_bytes()==Path('/etc/caddy/sites/escape.caddy').read_bytes()
 return current
check_backend();current=check_web()
old_web=[Path(n)for n,r in BASELINE['website']['releases'].items()if r['version']=='0.3.15'];assert len(old_web)==1;old_web=old_web[0]
old_apk=web/'downloads/Escape-0.3.15-e7ac556941de.apk'
obsolete_backups=[Path(n)for n in ORIGINAL_BASELINE['backups']]
assert set(str(p)for p in obsolete_backups)=={'/opt/escape/backups/pre-publication-0.3.16-20261028T044329Z','/opt/escape/backups/runtime-release-20261027T223730Z'}
report=dict(passed=True,checkedUtc=datetime.now(timezone.utc).isoformat(),version='0.3.17',versionCode=19,
 runtimeSha256=BASELINE['runtime']['sha256'],runtimeFiles=len(BASELINE['runtime']['files']),seedPreserved=True,catalogsUnchanged=True,protectedConfigAndAdminUnchanged=True,
 noApiDatabaseRestartDuringStaticPublication=True,nakamaHealth='healthy',currentTarget=str(current),fullHttpsFiles=INTEGRITY['filesChecked'],fullHttpsApkVerified=True,
 previous016WebAndApkPreserved=True,currentRuntimeBackup=str(current_backup),rollback016Backup=str(rollback),bothBackupsVerified=True,
 publisherSha256=PLAN['publisherSha256'],retentionApplied=False)
if DO_CLEANUP:
 assert {str(p)for p in(root/'backups').iterdir()}==set(BASELINE['backups'])|{str(current_backup)}
 assert {str(p)for p in(web/'releases').iterdir()}=={str(current),BASELINE['website']['currentTarget'],str(old_web)}
 assert tree(old_web)==BASELINE['website']['releases'][str(old_web)]['files']
 assert old_apk.is_file()and not old_apk.is_symlink()and old_apk.stat().st_size==BASELINE['website']['apks'][old_apk.name]['bytes']and sha(old_apk)==BASELINE['website']['apks'][old_apk.name]['sha256']
 removed=[]
 for p in obsolete_backups:
  _,files=backup_verified(p);before=ORIGINAL_BASELINE['backups'][str(p)]
  assert sha(p/'manifest.json')==before['manifestSha256']and sha(p/'escape.dump')==before['dump']['sha256']
  removed.append(dict(path=str(p),files=len(files),manifestSha256=sha(p/'manifest.json')))
 assert len(OWNED_STAGE_NAMES)==len(set(OWNED_STAGE_NAMES))and all(n==identifier or n.startswith(identifier+'-')or n==identifier+'.tar.gz'for n in OWNED_STAGE_NAMES)
 assert {p.name for p in parent.iterdir()if p.name.startswith(identifier)}==set(OWNED_STAGE_NAMES)
 paths=[parent/n for n in OWNED_STAGE_NAMES]
 assert tree(parent/identifier)==bundle and sha(parent/(identifier+'.tar.gz'))==PLAN['archiveSha256']
 assert sha(parent/(identifier+'-publisher.py'))==PLAN['publisherSha256']and sha(parent/(identifier+'-installer.py'))==INSTALLER_SHA256
 assert sha(parent/(identifier+'-previous-publisher.py'))==BASELINE['publisher']['sha256']
 for suffix,wanted in(('-plan.json',PLAN),('-full-integrity.json',INTEGRITY),('-public-smoke.json',SMOKE)):
  assert json.loads((parent/(identifier+suffix)).read_text())==wanted
 assert json.loads((parent/(identifier+'-publication-exit.json')).read_text())['exit_code']==0
 for p in paths:
  assert p.parent==parent and p.resolve()==p and not p.is_symlink()
  if p.is_dir():tree(p)
 assert old_web.resolve()==old_web and old_web.parent==web/'releases'and not old_web.is_symlink()
 removed.extend([dict(path=str(old_web),files=len(tree(old_web))),dict(path=str(old_apk),sha256=sha(old_apk))])
 for p in obsolete_backups:shutil.rmtree(p)
 shutil.rmtree(old_web);old_apk.unlink()
 for p in paths:
  if p.is_dir():shutil.rmtree(p)
  else:p.unlink()
 cache=parent/'__pycache__'
 if cache.exists():
  assert cache.is_dir()and not cache.is_symlink()
  for p in cache.glob(identifier+'-publisher.cpython-*.pyc'):
   assert p.is_file()and not p.is_symlink()and p.resolve().parent==cache;p.unlink()
  if not any(cache.iterdir()):cache.rmdir()
 check_backend();assert check_web()==current
 assert {str(p)for p in(root/'backups').iterdir()}=={str(current_backup),str(rollback)}
 assert {str(p)for p in(web/'releases').iterdir()}=={str(current),BASELINE['website']['currentTarget']}
 assert {p.name for p in(web/'downloads').glob('*.apk')}=={PLAN['apkFilename'],PLAN['previousApkFilename']}
 assert not any(p.name.startswith(identifier)for p in parent.iterdir())
 report.update(retentionApplied=True,removed=removed,ownedStagePathsRemoved=[str(p)for p in paths],retainedVersions=['0.3.17','0.3.16'],
  retainedBackups=sorted(str(p)for p in(root/'backups').iterdir()),backupCount=2)
(root/('release-0.3.17-retention.json'if DO_CLEANUP else'release-0.3.17-verification.json')).write_text(json.dumps(report,indent=2))
print(json.dumps(report,indent=2))
