"""Emit pinned backup/launch/verify/retention scripts; no local action performs network I/O."""
from pathlib import Path
import argparse,base64,hashlib,json,sys
root=Path.cwd();work=Path(__file__).parent;out=root/'Design/Release-0.3.17/validation'
p=argparse.ArgumentParser();p.add_argument('action',choices=('backup','launch','verify','cleanup'));p.add_argument('--plan',type=Path)
p.add_argument('--expected-apk-sha256');p.add_argument('--approved-publication',action='store_true');p.add_argument('--approved-backup',action='store_true');p.add_argument('--approved-retention',action='store_true');args=p.parse_args()
def read(path):return json.loads(path.read_text(encoding='utf-8-sig'))
def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()
baseline=read(out/'server/publication-baseline.json');deploy=read(out/'server/backend-apply-proof.json');assert deploy['passed']and deploy['runtimeSha256']==baseline['runtime']['sha256']
payload=dict(BASELINE=baseline,DEPLOY_PROOF=deploy,ORIGINAL_BASELINE=read(out/'server/preflight.json'))
if args.action=='backup':
 assert args.approved_backup or args.approved_publication,'An explicit backup or publication signal is required before its fresh backup'
 source=work/'backup_before_publication_remote.py'
else:
 assert args.plan is not None and args.expected_apk_sha256
 plan=read(args.plan);assert plan['version']=='0.3.17'and plan['versionCode']==19 and plan['apkSha256']==args.expected_apk_sha256
 assert plan['expectedPreviousPublisherSha256']==baseline['publisher']['sha256']
 apk=read(out/'apk-validation.json');snapshot=read(out/'source-snapshot-check.json')
 assert apk['version']=='0.3.17'and apk['version_code']==19 and apk['signature_verified']and apk['sha256']==plan['apkSha256']and apk['bytes']==plan['apkBytes']
 assert snapshot['passed']and snapshot['snapshotManifestSha256']==plan['sourceSnapshotManifestSha256']
 payload.update(PLAN=plan,PLAN_SHA256=sha(args.plan),INSTALLER_SHA256=sha(root/'Tools/Release/publish_page_remote.py'),APPROVED_APK_SHA256=args.expected_apk_sha256)
 if args.action=='launch':
  assert args.approved_publication,'A separate static publication signal is required';payload['PUBLICATION_APPROVED']=True;source=work/'launch_publication_remote.py'
 else:
  integrity=read(out/'public-file-integrity.json');smoke=read(out/'public-release.json');assert integrity['passed']and smoke['success']
  payload.update(INTEGRITY=integrity,SMOKE=smoke,DO_CLEANUP=args.action=='cleanup')
  identifier=plan['identifier'];payload['OWNED_STAGE_NAMES']=[identifier,identifier+'.tar.gz',*[identifier+s for s in('-publisher.py','-plan.json','-installer.py','-run-publication.py','-publication.log','-publication-exit.json','-previous-publisher.py','-full-integrity.json','-public-smoke.json')]]
  if args.action=='cleanup':
   assert args.approved_retention and read(out/'server/pre-retention-verification.json')['passed']
   for name in('publication.log','publication-exit.json','fresh-backup-manifest.json'):assert(out/'server'/name).is_file()
   assert sha(out/'server/fresh-backup-manifest.json')==read(out/'server/fresh-backup.json')['manifestSha256']
  source=work/'verify_and_retain_remote.py'
encoded=base64.b64encode(json.dumps(payload).encode()).decode()
sys.stdout.write("import base64,json\nglobals().update(json.loads(base64.b64decode('"+encoded+"')))\n")
sys.stdout.write(source.read_text())
