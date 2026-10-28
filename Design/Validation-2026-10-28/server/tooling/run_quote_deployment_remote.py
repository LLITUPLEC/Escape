"""Pinned final backend apply: activate quote guard at old prices before catalog CAS."""
from pathlib import Path
import hashlib,json,os,re,subprocess,sys
root=Path('/opt/escape');stage=root/sys.argv[1];mode=sys.argv[2]
assert root.resolve()==root and stage.parent==root and re.fullmatch(r'release0315-validation-[a-f0-9]{12}',stage.name)
assert mode in('preflight','apply')
ctx=json.loads((stage/'context.json').read_text())
for name,digest in ctx['manifest'].items():
 p=stage/'candidate'/name
 assert p.is_file()and not p.is_symlink()and hashlib.sha256(p.read_bytes()).hexdigest()==digest
installer=stage/'candidate/deploy_runtime.py';migration=stage/'candidate/migration/merge_equipment_ingots.py';probe=stage/'candidate/multiclient_review.py'
command=['python3',str(installer),'--expected-runtime-sha256','c3a81563b3bf78e9eab55f5d7eab54d11d68dee0675599e71f3eff7dbe2ee445',
 '--expected-tested-sha256','aad128d4e8b67dc8b423e6725620c89fee22519e43f727fa1fcfa2b59ed36653',
 '--catalog-migration',str(migration),'--catalog-migration-sha256',ctx['manifest']['migration/merge_equipment_ingots.py'],
 '--catalog-expected-changes','8','--catalog-probe-suite',str(probe),'--catalog-probe-suite-sha256',ctx['manifest']['multiclient_review.py'],
 '--catalog-probe-scenario','quote-preflight']
os.umask(0o077)
if mode=='preflight':
 r=subprocess.run(command,cwd=root,capture_output=True,text=True)
 (stage/'deployment-preflight.log').write_text(r.stdout+r.stderr)
 print(r.stdout+r.stderr);raise SystemExit(r.returncode)
assert json.loads((stage/'validation-exit.json').read_text())['exit_code']==0
summary=json.loads((stage/'network-validation-summary.json').read_text())
assert summary['success']and summary['runtime_sha256']=='aad128d4e8b67dc8b423e6725620c89fee22519e43f727fa1fcfa2b59ed36653'
assert summary['production_runtime_before_sha256']==summary['production_runtime_after_sha256']=='c3a81563b3bf78e9eab55f5d7eab54d11d68dee0675599e71f3eff7dbe2ee445'
assert set(summary['scenarios'])=={'global-boss-practice','equipment-sets','quote-preflight','recovery'}
for result in summary['scenarios'].values():assert result['exit_code']==0 and result['failed']==0 and not result['errors']and result['tested_runtime_matches_final_candidate']
common=summary['commonHandlerProof'];assert common['checks']==788 and common['runtimeSha256']=='8cf98718b4f4b3bff2204a2a04c33f88760162ae4b66abcd27325f0895f4794e'
assert common['deltaFiles']==['duel_match3_economy_rpc.lua','duel_workshop_quote.lua']
assert not(stage/'deployment-exit.json').exists()and not(stage/'deployment.log').exists()
wrapper=stage/'run-apply.py'
wrapper.write_text('from pathlib import Path\nfrom datetime import datetime,timezone\nimport json,subprocess\ncommand='+repr(command+['--apply'])+
 "\nstage=Path(__file__).parent\nr=subprocess.run(command,cwd='/opt/escape')\n"+
 "(stage/'deployment-exit.json').write_text(json.dumps(dict(exit_code=r.returncode,finishedUtc=datetime.now(timezone.utc).isoformat())))\n")
with(stage/'deployment.log').open('xb')as log:
 p=subprocess.Popen(['python3',str(wrapper)],cwd=root,stdout=log,stderr=subprocess.STDOUT,start_new_session=True)
print(json.dumps(dict(pid=p.pid,stage=str(stage),scope='authorized guarded-runtime-first backend apply; CAS only after live guard proof',siteOrApkChanged=False)))
