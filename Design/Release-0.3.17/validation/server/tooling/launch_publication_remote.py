from pathlib import Path
from datetime import datetime,timezone
import hashlib,json,os,re,subprocess
root=Path('/opt/escape');parent=root/'page-staging'
assert PUBLICATION_APPROVED is True and PLAN['version']=='0.3.17'and PLAN['versionCode']==19
identifier=PLAN['identifier'];assert re.fullmatch(r'escape0317-[a-f0-9]{32}',identifier)
plan_path=parent/(identifier+'-plan.json');assert plan_path.resolve()==plan_path and not plan_path.is_symlink()
assert hashlib.sha256(plan_path.read_bytes()).hexdigest()==PLAN_SHA256 and json.loads(plan_path.read_text())==PLAN
installer=parent/(identifier+'-installer.py');assert not installer.is_symlink()and hashlib.sha256(installer.read_bytes()).hexdigest()==INSTALLER_SHA256
assert PLAN['expectedPreviousPublisherSha256']==BASELINE['publisher']['sha256']
assert hashlib.sha256((root/'deploy_download_page.py').read_bytes()).hexdigest()==BASELINE['publisher']['sha256']
assert PLAN['apkSha256']==APPROVED_APK_SHA256
fresh=json.loads((root/'publication-backup-0317.json').read_text());assert fresh['passed']and fresh['pgRestoreListVerified']and fresh['fullFileManifestVerified']
assert fresh['runtimeSha256']==DEPLOY_PROOF['runtimeSha256']==BASELINE['runtime']['sha256']
assert(datetime.now(timezone.utc)-datetime.fromisoformat(fresh['createdUtc'])).total_seconds()<300
backup=Path(fresh['backup']);assert hashlib.sha256((backup/'manifest.json').read_bytes()).hexdigest()==fresh['manifestSha256']
command=['python3','-B',str(installer),'--plan',str(plan_path),'--expected-previous-publisher-sha256',PLAN['expectedPreviousPublisherSha256']]
wrapper=parent/(identifier+'-run-publication.py');assert not wrapper.exists()
os.umask(0o077)
wrapper.write_text('from pathlib import Path\nfrom datetime import datetime,timezone\nimport json,subprocess\ncommand='+repr(command)+
 "\nroot=Path('/opt/escape');parent=root/'page-staging'\nr=subprocess.run(command,cwd=root)\n"+
 '(parent/'+repr(identifier+'-publication-exit.json')+').write_text(json.dumps(dict(exit_code=r.returncode,finishedUtc=datetime.now(timezone.utc).isoformat(),noApiOrDatabaseRestart=True)))\n')
with(parent/(identifier+'-publication.log')).open('xb')as log:
 process=subprocess.Popen(['python3','-B',str(wrapper)],cwd=root,stdout=log,stderr=subprocess.STDOUT,start_new_session=True)
print(json.dumps(dict(started=True,pid=process.pid,identifier=identifier,onlyStaticPublication=True,oldArtifactsRetained=True)))
