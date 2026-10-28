"""Read-only deployed-runtime/catalog/site/backup verification; no cleanup here."""
from pathlib import Path
from datetime import datetime,timezone
import hashlib,importlib.util,json,re,subprocess,sys
root=Path('/opt/escape');stage=root/sys.argv[1]
assert stage.parent==root and re.fullmatch(r'release0315-validation-[a-f0-9]{12}',stage.name)
initial_path=stage/'initial-preflight.json'
assert hashlib.sha256(initial_path.read_bytes()).hexdigest()==sys.argv[2]
before=json.loads(initial_path.read_text(encoding='utf-8-sig'))
assert json.loads((stage/'deployment-exit.json').read_text())['exit_code']==0
smoke_exit=json.loads((stage/'production-practice-smoke-exit.json').read_text())
assert smoke_exit['exit_code']==0 and smoke_exit['ownTemporaryAccounts']==1 and len(smoke_exit['cleanup'])==1
assert all(r['accountRemoved']and r['userStorageRemoved']for r in smoke_exit['cleanup'])
smoke=json.loads((stage/'production-practice-smoke.json').read_text())
assert smoke['checks']and all(c['passed']for c in smoke['checks'])and not smoke['errors']
def sha(p):
 h=hashlib.sha256()
 with p.open('rb')as stream:
  for chunk in iter(lambda:stream.read(1048576),b''):h.update(chunk)
 return h.hexdigest()
def tree(path):
 out={}
 for p in sorted(path.rglob('*')):
  assert not p.is_symlink()
  if p.is_file():out[p.relative_to(path).as_posix()]=sha(p)
 return out
expected=json.loads((stage/'expected-tested-runtime.json').read_text())
runtime=tree(root/'runtime');assert runtime==expected['files']
assert hashlib.sha256(json.dumps(runtime,sort_keys=True).encode()).hexdigest()==expected['sha256']
assert runtime['zz_escape_seed.lua']==before['runtime']['files']['zz_escape_seed.lua']
for name,row in before['protected'].items():
 if name=='admin':assert tree(root/'admin')=={k:v['sha256']for k,v in row.items()}
 else:assert sha(root/name)==row['sha256']
web=Path('/var/www/escape');current=(web/'current').resolve(strict=True)
assert str(current)==before['website']['current_target']
for name in('index.html','release.json'):assert sha(current/name)==before['website'][name]['sha256']
apks={p.name:dict(bytes=p.stat().st_size,sha256=sha(p))for p in sorted((web/'downloads').glob('*.apk'))}
assert apks==before['website']['apks'] and len(apks)==2
log=(stage/'deployment.log').read_text()
guard_marker='PASS active quote guard verified at old catalog prices'
catalog_marker='PASS narrow catalog CAS after guarded runtime startup'
assert log.index(guard_marker)<log.index(catalog_marker)<log.index('PASS deployed tested runtime;')
backup_match=re.search(r'PASS deployed tested runtime; Escape Nakama healthy; backup=(/opt/escape/backups/runtime-release-[0-9]{8}T[0-9]{6}Z)',log)
assert backup_match
backup=Path(backup_match[1]);assert backup.parent==root/'backups'and backup.resolve()==backup
manifest=json.loads((backup/'manifest.json').read_text());actual_backup=tree(backup);actual_backup.pop('manifest.json')
assert actual_backup==manifest['files'] and manifest['pgRestoreListVerified']
guard_report=json.loads((backup/'quote-guard-active.json').read_text())
assert len(guard_report['checks'])==6 and all(c['passed']for c in guard_report['checks'])and not guard_report['errors']
assert tree(backup/'runtime')==before['runtime']['files']
with(backup/'escape.dump').open('rb')as source:
 subprocess.run(['docker','compose','-p','escape','exec','-T','postgres','pg_restore','--list'],cwd=root,stdin=source,stdout=subprocess.DEVNULL,check=True)
sys.path.insert(0,str(stage/'candidate/migration'))
import merge_equipment_ingots as migration
import storage_admin as admin
config=json.loads((root/'secrets/server.yml').read_text());console=admin.Console('http://127.0.0.1:27351')
try:
 console.login(config['console']['username'],config['console']['password'])
 previous=json.loads((backup/'catalog-before-migration.json').read_text())
 wanted,changed=migration.merge_catalog(previous['value'])
 catalog=console.read(migration.COLLECTION,migration.KEY)
 migration.verify_row(catalog);assert catalog['value']==wanted and len(changed)==8
 def differences(old,new,path=''):
  if type(old) is not type(new):return[path]
  if isinstance(old,dict):
   result=[]
   for key in set(old)|set(new):
    child=path+'.'+key if path else key
    result.extend(differences(old[key],new[key],child)if key in old and key in new else[child])
   return result
  if isinstance(old,list):return[]if old==new else[path]
  return[]if old==new else[path]
 changed_paths=sorted(differences(previous['value'],catalog['value']))
 assert changed_paths==sorted('items.'+name+'.craft_ingot_n'for name in changed)
 for alias,collection,key in(('global-boss','duel_global_boss','config'),('shop','duel_shop_catalog','v1'),('aura','duel_match3_server_aura','active')):
  actual=console.read(collection,key);prior=before['catalogs'][alias]
  assert (actual and actual['value'])==(prior and prior['value'])
finally:console.close()
rollback=root/'backups/runtime-release-20261027T162323Z'
rollback_runtime=tree(rollback/'runtime')
assert hashlib.sha256(json.dumps(rollback_runtime,sort_keys=True).encode()).hexdigest()=='dfd08b4dd99dfeef305c3a31ae3347dca5c18e9a444a96f44669901db49c1c3f'
assert sha(rollback/'escape.dump')=='88f3546cdfbd363d606f880b37ad1e29ffddbb8e6d40762bb0e0df683bd012ad'
health=subprocess.run(['docker','inspect','--format','{{.State.Health.Status}}','escape-nakama-1'],capture_output=True,text=True,check=True).stdout.strip()
assert health=='healthy'
report=dict(passed=True,verifiedUtc=datetime.now(timezone.utc).isoformat(),runtimeSha256=expected['sha256'],runtimeFiles=len(runtime),seedPreserved=True,
 protectedConfigAndAdminUnchanged=True,websiteAndBothPublishedApksUnchanged=True,apkNames=sorted(apks),versionChanged=False,
 catalogOnlyEightKnownFields=True,catalogChangedPaths=changed_paths,catalogVersionPreserved=catalog['value'].get('version'),
 guardActiveBeforePriceCas=True,guardProbeChecks=6,guardProbeTemporaryAccountRemoved=True,
 catalogOld={n:previous['value']['items'][n]['craft_ingot_n']for n in changed},catalogNew={n:wanted['items'][n]['craft_ingot_n']for n in changed},
 freshBackup=str(backup),freshBackupRuntimeSha256=manifest['runtimeSha256'],freshBackupFiles=len(actual_backup),dumpBytes=(backup/'escape.dump').stat().st_size,
 dumpSha256=sha(backup/'escape.dump'),pgRestoreListVerified=True,rollback014Preserved=str(rollback),
 productionPracticeChecks=len(smoke['checks']),practiceZeroDurableWrites=True,temporaryAccountAndStorageRemoved=True,nakamaHealth=health,
 securitySmokePassed='Security smoke: 'in log and 'checks passed; credentials were not printed.'in log,dashboardChecks=log.count('PASS dashboard '))
assert report['securitySmokePassed']and report['dashboardChecks']==3
(stage/'post-deployment-verification.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report,indent=2))
