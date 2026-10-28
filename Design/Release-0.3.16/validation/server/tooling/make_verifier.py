from pathlib import Path
import argparse,base64,hashlib,json,sys
p=argparse.ArgumentParser();p.add_argument('mode',choices=('verify','cleanup'));args=p.parse_args()
root=Path.cwd();out=root/'Design/Release-0.3.16/validation';work=root/'.cache/release-work/0316-vps'
plan_path=root/'.cache/release-work/publication/escape0316-192a13dbfff44b3882e1f924c8b7402b-plan.json'
plan=json.loads(plan_path.read_text());identifier=plan['identifier']
integrity=json.loads((out/'public-file-integrity.json').read_text());smoke=json.loads((out/'public-release.json').read_text())
assert integrity['passed']and smoke['success']
installer_sha=hashlib.sha256((root/'Tools/Release/publish_page_remote.py').read_bytes()).hexdigest()
assert installer_sha=='241aaaeaabaebc763cf48b309ccb7d1f177b3807fae7c309a1fe75302ec83cb3'
if args.mode=='cleanup':
 assert json.loads((out/'server/pre-retention-verification.json').read_text())['passed']
 for name in('publication.log','publication-exit.json','fresh-backup-manifest.json'):
  assert(out/'server'/name).is_file()
 fresh=json.loads((out/'server/fresh-backup.json').read_text())
 assert hashlib.sha256((out/'server/fresh-backup-manifest.json').read_bytes()).hexdigest()==fresh['manifestSha256']
payload=dict(BASELINE=json.loads((out/'server/preflight.json').read_text()),PLAN=plan,INTEGRITY=integrity,SMOKE=smoke,
 DO_CLEANUP=args.mode=='cleanup',INSTALLER_SHA256=installer_sha,
 OWNED_STAGE_NAMES=[identifier,identifier+'.tar.gz',*[identifier+suffix for suffix in('-publisher.py','-plan.json','-installer.py',
 '-run-publication.py','-publication.log','-publication-exit.json','-previous-publisher.py','-full-integrity.json','-public-smoke.json')]])
encoded=base64.b64encode(json.dumps(payload).encode()).decode()
sys.stdout.write("import base64,json\nglobals().update(json.loads(base64.b64decode('"+encoded+"')))\n")
sys.stdout.write((work/'verify_and_retain_remote.py').read_text())
