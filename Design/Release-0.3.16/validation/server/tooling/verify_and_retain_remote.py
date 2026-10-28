"""Read-only publication proof, or exact retention cleanup after pinned HTTPS proofs."""
from pathlib import Path
from datetime import datetime,timezone
import copy,hashlib,importlib.util,json,re,shutil,subprocess
root=Path('/opt/escape');web=Path('/var/www/escape');parent=root/'page-staging'
assert root.resolve()==root and web.resolve()==web and parent.resolve()==parent
assert PLAN['version']=='0.3.16'and PLAN['versionCode']==18 and PLAN['published']is False
identifier=PLAN['identifier'];assert re.fullmatch(r'escape0316-[a-f0-9]{32}',identifier)
assert PLAN['expectedPreviousPublisherSha256']==BASELINE['publisher']['sha256']
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
def digest(files):return hashlib.sha256(json.dumps(files,sort_keys=True).encode()).hexdigest()
assert INTEGRITY['passed']and INTEGRITY['version']=='0.3.16'and INTEGRITY['fullApkVerified']
assert INTEGRITY['apkBytes']==PLAN['apkBytes']and INTEGRITY['apkSha256']==PLAN['apkSha256']
assert INTEGRITY['filesChecked']==INTEGRITY['passedFiles']==PLAN['memberCount']
assert SMOKE['success']and SMOKE['version']=='0.3.16'and SMOKE['api_health_status']==200
assert SMOKE['previous_apk_bytes']==PLAN['previousApkBytes']and all(v=='passed'for v in SMOKE['checks'].values())
bundle_files={r['path']:dict(bytes=r['expectedBytes'],sha256=r['expectedSha256'])for r in INTEGRITY['files']}
assert len(bundle_files)==PLAN['memberCount']and all(r['passed']for r in INTEGRITY['files'])
fresh=json.loads((root/'publication-backup-0316.json').read_text());assert fresh['passed']and fresh['pgRestoreListVerified']and fresh['fullFileManifestVerified']
backup=Path(fresh['backup']);assert backup.parent==root/'backups'and re.fullmatch(r'pre-publication-0\.3\.16-[0-9]{8}T[0-9]{6}Z',backup.name)
backup_manifest=json.loads((backup/'manifest.json').read_text());actual_backup=hashes(tree(backup));actual_backup.pop('manifest.json')
assert actual_backup==backup_manifest['files']and backup_manifest['runtimeSha256']==BASELINE['runtime']['sha256']
assert sha(backup/'escape.dump')==fresh['dumpSha256']and sha(backup/'manifest.json')==fresh['manifestSha256']
assert sha(backup/'source/deploy_download_page.py')==BASELINE['publisher']['sha256']
previous_backup=root/'backups/runtime-release-20261027T223730Z';old_backup=root/'backups/runtime-release-20261027T162323Z'
previous_manifest=json.loads((previous_backup/'manifest.json').read_text());previous_files=hashes(tree(previous_backup));previous_files.pop('manifest.json')
assert previous_files==previous_manifest['files']
assert sha(previous_backup/'manifest.json')==BASELINE['backups'][str(previous_backup)]['manifestSha256']
assert sha(previous_backup/'escape.dump')==BASELINE['backups'][str(previous_backup)]['dump']['sha256']
old_release=[Path(n)for n,r in BASELINE['website']['releases'].items()if r['version']=='0.3.14'];assert len(old_release)==1
old_release=old_release[0];old_apk=web/'downloads/Escape-0.3.14-46a458ea2e41.apk'
def check_backend():
 rt=hashes(tree(root/'runtime'));assert rt==BASELINE['runtime']['files']and len(rt)==66
 for n,row in BASELINE['protected'].items():
  if n=='admin':assert tree(root/'admin')==row
  else:assert sha(root/n)==row['sha256']
 for n,h in BASELINE['canonicalHelpers'].items():assert sha(root/n)==h
 assert sha(root/'deploy_download_page.py')==PLAN['publisherSha256']
 spec=importlib.util.spec_from_file_location('release_storage_admin',root/'tools/storage_admin.py');admin=importlib.util.module_from_spec(spec);spec.loader.exec_module(admin)
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
  assert raw['State']['Status']=='running'
  if before['name']=='escape-nakama-1':assert raw['State']['Health']['Status']=='healthy'
def check_retained_web():
 current=(web/'current').resolve(strict=True);assert current.parent==web/'releases'and current!=Path(BASELINE['website']['currentTarget'])
 assert tree(current)==bundle_files
 release=json.loads((current/'release.json').read_text(encoding='utf-8-sig'))
 assert release['version']=='0.3.16'and release['sha256']==PLAN['apkSha256']
 previous=Path(BASELINE['website']['currentTarget']);assert tree(previous)==BASELINE['website']['releases'][str(previous)]['files']
 for name,n,h in((PLAN['apkFilename'],PLAN['apkBytes'],PLAN['apkSha256']),(PLAN['previousApkFilename'],PLAN['previousApkBytes'],PLAN['previousApkSha256'])):
  p=web/'downloads'/name;assert p.is_file()and not p.is_symlink()and p.stat().st_size==n and sha(p)==h
 assert(root/'Caddyfile').read_bytes()==Path('/etc/caddy/sites/escape.caddy').read_bytes()
 return current
check_backend();current=check_retained_web()
report=dict(passed=True,checkedUtc=datetime.now(timezone.utc).isoformat(),version='0.3.16',versionCode=18,
 runtimeSha256=BASELINE['runtime']['sha256'],runtimeFiles=66,seedPreserved=True,catalogsUnchanged=True,protectedConfigAndAdminUnchanged=True,
 nakamaPostgresIdentitiesAndStartTimesUnchanged=True,noApiDatabaseRestart=True,nakamaHealth='healthy',currentTarget=str(current),
 fullHttpsFiles=INTEGRITY['filesChecked'],fullHttpsApkVerified=True,previous015WebAndApkPreserved=True,
 freshAadBackup=str(backup),freshBackupManifestSha256=fresh['manifestSha256'],freshBackupPgRestoreListVerified=True,
 previousC3BackupPreserved=str(previous_backup),publisherSha256=PLAN['publisherSha256'],retentionApplied=False)
if DO_CLEANUP:
 assert {str(p)for p in(root/'backups').iterdir()}=={str(backup),str(previous_backup),str(old_backup)}
 assert {str(p)for p in(web/'releases').iterdir()}=={str(current),BASELINE['website']['currentTarget'],str(old_release)}
 assert tree(old_release)==BASELINE['website']['releases'][str(old_release)]['files']
 assert old_apk.is_file()and not old_apk.is_symlink()and old_apk.stat().st_size==BASELINE['website']['apks'][old_apk.name]['bytes']and sha(old_apk)==BASELINE['website']['apks'][old_apk.name]['sha256']
 old_files=tree(old_backup);old_runtime={n.removeprefix('runtime/'):r['sha256']for n,r in old_files.items()if n.startswith('runtime/')}
 assert digest(old_runtime)==BASELINE['backups'][str(old_backup)]['runtimeSha256']
 assert old_files['escape.dump']==BASELINE['backups'][str(old_backup)]['dump']and len(old_files)==BASELINE['backups'][str(old_backup)]['files']
 assert len(OWNED_STAGE_NAMES)==len(set(OWNED_STAGE_NAMES))and all(n==identifier or n.startswith(identifier+'-')or n==identifier+'.tar.gz'for n in OWNED_STAGE_NAMES)
 assert {p.name for p in parent.iterdir()if p.name.startswith(identifier)}==set(OWNED_STAGE_NAMES)
 paths=[parent/n for n in OWNED_STAGE_NAMES]
 assert(parent/identifier).is_dir()and tree(parent/identifier)==bundle_files
 assert sha(parent/(identifier+'.tar.gz'))==PLAN['archiveSha256']
 assert sha(parent/(identifier+'-publisher.py'))==PLAN['publisherSha256']
 assert sha(parent/(identifier+'-installer.py'))==INSTALLER_SHA256
 assert json.loads((parent/(identifier+'-plan.json')).read_text())==PLAN
 assert json.loads((parent/(identifier+'-full-integrity.json')).read_text())==INTEGRITY
 assert json.loads((parent/(identifier+'-public-smoke.json')).read_text())==SMOKE
 assert json.loads((parent/(identifier+'-publication-exit.json')).read_text())['exit_code']==0
 assert sha(parent/(identifier+'-previous-publisher.py'))==BASELINE['publisher']['sha256']
 for p in paths:
  assert p.parent==parent and p.resolve()==p and not p.is_symlink()
  if p.is_dir():tree(p)
 for p in(old_release,old_backup):assert p.resolve()==p and not p.is_symlink()
 removed=[dict(path=str(old_release),files=len(BASELINE['website']['releases'][str(old_release)]['files'])),dict(path=str(old_apk),sha256=sha(old_apk)),dict(path=str(old_backup),files=len(old_files))]
 for p in(old_release,old_backup):shutil.rmtree(p)
 old_apk.unlink()
 for p in paths:
  if p.is_dir():shutil.rmtree(p)
  else:p.unlink()
 cache=parent/'__pycache__';removed_cache=[]
 if cache.exists():
  assert cache.is_dir()and not cache.is_symlink()
  for p in cache.glob(identifier+'-publisher.cpython-*.pyc'):
   assert p.is_file()and not p.is_symlink()and p.resolve().parent==cache
   removed_cache.append(p.name);p.unlink()
  if not any(cache.iterdir()):cache.rmdir()
 check_backend();assert check_retained_web()==current
 assert {str(p)for p in(root/'backups').iterdir()}=={str(backup),str(previous_backup)}
 assert {str(p)for p in(web/'releases').iterdir()}=={str(current),BASELINE['website']['currentTarget']}
 assert {p.name for p in(web/'downloads').glob('*.apk')}=={PLAN['apkFilename'],PLAN['previousApkFilename']}
 assert not any(p.name.startswith(identifier)for p in parent.iterdir())
 report.update(retentionApplied=True,removed=removed,ownedStagePathsRemoved=[str(p)for p in paths],ownedPublisherBytecodeRemoved=removed_cache,
  retainedReleaseVersions=['0.3.16','0.3.15'],retainedBackups=sorted(str(p)for p in(root/'backups').iterdir()),backupCount=2)
out=root/('release-0.3.16-retention.json'if DO_CLEANUP else'release-0.3.16-verification.json')
out.write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
