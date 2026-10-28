from pathlib import Path
from datetime import datetime,timezone
import hashlib,json,os,re,subprocess,sys
root=Path('/opt/escape');parent=root/'page-staging';identifier=sys.argv[1];plan_sha=sys.argv[2]
assert re.fullmatch(r'escape0316-[a-f0-9]{32}',identifier)
plan_path=parent/(identifier+'-plan.json');assert hashlib.sha256(plan_path.read_bytes()).hexdigest()==plan_sha
plan=json.loads(plan_path.read_text());assert plan['identifier']==identifier and plan['version']=='0.3.16'and plan['versionCode']==18
assert plan['apkSha256']=='3f40a293713fa7689ca54929f597201cd5f0c9ca7a6a304be46bd95b7810e847'
installer=parent/(identifier+'-installer.py');assert hashlib.sha256(installer.read_bytes()).hexdigest()=='241aaaeaabaebc763cf48b309ccb7d1f177b3807fae7c309a1fe75302ec83cb3'
fresh=json.loads((root/'publication-backup-0316.json').read_text());assert fresh['passed']and fresh['pgRestoreListVerified']and fresh['fullFileManifestVerified']
assert fresh['runtimeSha256']=='aad128d4e8b67dc8b423e6725620c89fee22519e43f727fa1fcfa2b59ed36653'
assert(datetime.now(timezone.utc)-datetime.fromisoformat(fresh['createdUtc'])).total_seconds()<300
backup=Path(fresh['backup']);assert hashlib.sha256((backup/'manifest.json').read_bytes()).hexdigest()==fresh['manifestSha256']
command=['python3','-B',str(installer),'--plan',str(plan_path),'--expected-previous-publisher-sha256',plan['expectedPreviousPublisherSha256']]
wrapper=parent/(identifier+'-run-publication.py');assert not wrapper.exists()
wrapper.write_text('from pathlib import Path\nfrom datetime import datetime,timezone\nimport json,subprocess\ncommand='+repr(command)+
 "\nroot=Path('/opt/escape');parent=root/'page-staging'\nr=subprocess.run(command,cwd=root)\n"+
 '(parent/'+repr(identifier+'-publication-exit.json')+').write_text(json.dumps(dict(exit_code=r.returncode,finishedUtc=datetime.now(timezone.utc).isoformat(),noApiOrDatabaseRestart=True)))\n')
os.umask(0o077)
with(parent/(identifier+'-publication.log')).open('xb')as log:
 process=subprocess.Popen(['python3','-B',str(wrapper)],cwd=root,stdout=log,stderr=subprocess.STDOUT,start_new_session=True)
print(json.dumps(dict(started=True,pid=process.pid,identifier=identifier,onlyStaticPublication=True,oldArtifactsRetained=True)))
