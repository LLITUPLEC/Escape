from pathlib import Path
from datetime import datetime,timezone
import hashlib,json,subprocess,sys,copy
root=Path('/opt/escape');backup=root/'backups/runtime-release-20261027T223730Z'
def sha(p):
 h=hashlib.sha256()
 with p.open('rb')as stream:
  for data in iter(lambda:stream.read(1048576),b''):h.update(data)
 return h.hexdigest()
def tree(p):
 assert p.resolve()==p and p.is_dir()and not p.is_symlink()
 out={}
 for f in sorted(p.rglob('*')):
  assert not f.is_symlink()
  if f.is_file():out[f.relative_to(p).as_posix()]=sha(f)
 return out
def digest(files):return hashlib.sha256(json.dumps(files,sort_keys=True).encode()).hexdigest()
runtime=tree(root/'runtime');assert len(runtime)==66 and digest(runtime)=='aad128d4e8b67dc8b423e6725620c89fee22519e43f727fa1fcfa2b59ed36653'
assert runtime['zz_escape_seed.lua']=='8cc9c4b7da99b8b4c1168e96a00fa68f03ecde26db876582fcfb392be4552524'
for name,row in BASELINE['protected'].items():
 if name=='admin':assert tree(root/'admin')=={n:r['sha256']for n,r in row.items()}
 else:assert sha(root/name)==row['sha256']
web=Path('/var/www/escape');current=(web/'current').resolve(strict=True)
assert str(current)==BASELINE['website']['current_target']
for name in('index.html','release.json'):assert sha(current/name)==BASELINE['website'][name]['sha256']
assert {p.name:dict(bytes=p.stat().st_size,sha256=sha(p))for p in sorted((web/'downloads').glob('*.apk'))}==BASELINE['website']['apks']
manifest=json.loads((backup/'manifest.json').read_text());files=tree(backup);files.pop('manifest.json');assert files==manifest['files']and len(files)==82
assert sha(backup/'tooling/deploy_runtime.py.before-guard')=='a428f360cf9893c0f75ee1b2d6cd54c75cb54eeeb8d73bc355411ca412913c31'
assert sha(root/'deploy_runtime.py')=='ff499e1a887c724b1bcc072ed41f1d55735b210bfdd2f2a624ae6858530f74d2'
rollback=root/'backups/runtime-release-20261027T162323Z'
assert {p.name for p in(root/'backups').iterdir()}=={backup.name,rollback.name}
assert digest(tree(rollback/'runtime'))=='dfd08b4dd99dfeef305c3a31ae3347dca5c18e9a444a96f44669901db49c1c3f'
assert sha(rollback/'escape.dump')=='88f3546cdfbd363d606f880b37ad1e29ffddbb8e6d40762bb0e0df683bd012ad'
assert not list(root.glob('release0315-validation-*'))and not list(root.glob('multiclient-isolated-*'))
assert not(root/'multiclient-runtime-overlay').exists()and not(root/'multiclient-catalog-migration').exists()
assert not list((root/'tests').glob('escape-multiclient-*'))and not list(root.glob('.multiclient-*'))
assert not list(root.glob('.backend-*'))
docker={}
for kind,args in(('containers',['docker','ps','-a','--format','{{json .}}']),('volumes',['docker','volume','ls','--format','{{json .}}']),('networks',['docker','network','ls','--format','{{json .}}'])):
 rows=[json.loads(s)for s in subprocess.run(args,capture_output=True,text=True,check=True).stdout.splitlines()if s.strip()]
 docker[kind]=[r for r in rows if any('escape-multiclient-'in str(v)or'escape-review-'in str(v)for v in r.values())]
assert not any(docker.values())
sys.path.insert(0,str(root/'tools'));import storage_admin as admin
config=json.loads((root/'secrets/server.yml').read_text());console=admin.Console('http://127.0.0.1:27351')
try:
 console.login(config['console']['username'],config['console']['password'])
 prior=json.loads((backup/'catalog-before-migration.json').read_text())['value'];wanted=copy.deepcopy(prior)
 prices={'helmet':110,'chest':270,'gloves':350,'weapon_l':460,'weapon_r':510,'legs':560,'shoulders':670,'feet':740}
 for suffix,n in prices.items():wanted['items']['eq_t1_epic_'+suffix]['craft_ingot_n']=n
 row=console.read('duel_match3_item_defs','catalog');assert row['value']==wanted
finally:console.close()
health=subprocess.run(['docker','inspect','--format','{{.State.Health.Status}}','escape-nakama-1'],capture_output=True,text=True,check=True).stdout.strip();assert health=='healthy'
report=dict(passed=True,checkedUtc=datetime.now(timezone.utc).isoformat(),runtimeSha256=digest(runtime),runtimeFiles=len(runtime),
 seedSha256=runtime['zz_escape_seed.lua'],seedPreserved=True,canonicalDeployHelperSha256=sha(root/'deploy_runtime.py'),
 protectedConfigAndAdminUnchanged=True,websiteAndBothPublishedApksUnchanged=True,onlyEightCatalogFieldsChanged=True,
 catalogStorageVersion=row['version'],catalogVersionPreserved=row['value'].get('version'),
 freshBackup=str(backup),freshBackupFiles=len(files),freshBackupManifestSha256=sha(backup/'manifest.json'),
 backupCount=2,retainedBackups=sorted(str(p)for p in(root/'backups').iterdir()),
 candidateStageDirectories=[],candidateOverlayAndMigrationAbsent=True,temporaryDocker=docker,nakamaHealth=health)
(root/'backend-final-verification-20261028.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report,indent=2))
