"""Check pinned local inputs and the actual composed gate. No SSH, remote execution or apply."""
from pathlib import Path
from datetime import datetime,timezone
import argparse,ast,base64,copy,hashlib,json,re,subprocess,sys,uuid
root=Path.cwd().resolve();work=Path(__file__).resolve().parent
parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--evidence-dir',type=Path);args=parser.parse_args()
stage='release0317-validation-0fa70c111d27';candidate='0c16126510fe0ca50b8f99a72f5cbc6a3a4e71b6e3a3412063e452c4f78058fe'
result=subprocess.run([sys.executable,str(work/'make_backend_job.py'),'--stage',stage,'--candidate-sha256',candidate,'--mode','dryrun'],cwd=root,capture_output=True,text=True)
assert result.returncode==0,result.stderr
assert result.stdout.strip();compile(result.stdout,'prepared-final-dryrun-job','exec')
header=re.search(r"base64\.b64decode\('([A-Za-z0-9+/=]+)'\)",result.stdout);assert header
payload=json.loads(base64.b64decode(header[1]));ready=payload['READY']
assert ready['readiness_source'].endswith('candidate-ready-network-final.json')and ready['runtime_sha256']==candidate
assert ready['target_equivalence_independently_verified']is True and payload['MODE']=='dryrun'and payload['APPLY_APPROVED']is False
prepared=work/'prepared';prepared.mkdir(exist_ok=True);job=prepared/(stage+'-dryrun.py');job.write_text(result.stdout,encoding='utf-8')
context=json.loads((root/'.cache/release-work/backend'/stage/'context.json').read_text())
expected=dict(payload['BASELINE']['runtime']['files'],**{n.removeprefix('runtime/'):h for n,h in context['manifest'].items()if n.startswith('runtime/')})
module=ast.parse((work/'backend_orchestrator.py').read_text());functions=[n for n in module.body if isinstance(n,ast.FunctionDef)and n.name in('sha','digest','checked_network')];assert len(functions)==3
fixture=work/('network-gate-fixture-'+uuid.uuid4().hex);fixture.mkdir();assert fixture.resolve().is_relative_to(work)
namespace=dict(json=json,hashlib=hashlib,STAGE_NAME=stage,STAGE=fixture,CANDIDATE_SHA256=candidate,BASELINE=payload['BASELINE'],expected=dict(sha256=candidate,files=expected),context=context,READY=copy.deepcopy(ready))
exec(compile(ast.Module(body=functions,type_ignores=[]),'actual-orchestration-network-gate','exec'),namespace);gate=namespace['checked_network'];checks=[]
def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def write(name,value):
 path=fixture/name;path.parent.mkdir(parents=True,exist_ok=True);path.write_text(json.dumps(value),encoding='utf-8');return dict(path=name,sha256=sha(path))
def verdict(should_pass,label):
 try:gate();actual=True
 except(AssertionError,FileNotFoundError,KeyError,ValueError):actual=False
 assert actual==should_pass,label;checks.append(dict(name=label,passed=True))
verdict(False,'An unfinished run without composed proof cannot authorize deployment')
row=dict(exit_code=0,checks=1,failed=0,errors=[],tested_runtime_matches_final_candidate=True)
common=dict(runtime_sha256=candidate,runtime_files=len(expected),suite_sha256=context['manifest']['multiclient_review.py'],
 catalog_sha256=context['manifest']['recovery-item-catalog.json'],production_runtime_changed=False,
 production_runtime_before_sha256=payload['BASELINE']['runtime']['sha256'],production_runtime_after_sha256=payload['BASELINE']['runtime']['sha256'])
final=dict(common,success=True,scenarios={name:copy.deepcopy(row)for name in('all','recovery')})
target=dict(common,suite_sha256=ready['completed_target_suite_sha256'],scenarios={'reconnect-0317':dict(row,checks=32),'all':dict(row,exit_code=1,checks=193,failed=1)})
target.pop('production_runtime_after_sha256')
target_report=dict(checks=[dict(passed=True,check='target fixture')for _ in range(32)],errors=[])
proof=json.loads((root/'Design/Validation-2026-10-28-Reconnect/server/target-source-equivalence-deadline-final.json').read_text(encoding='utf-8-sig'))
provenance={
 'final_summary':write('network-validation-summary.json',final),'final_exit':write('exit-status.json',dict(exit_code=0)),
 'target_summary':dict(write('completed-target/network-validation-summary.json',target),source_summary_success=False),
 'target_report':write('completed-target/multiclient-isolated-reconnect-0317.json',target_report),
 'target_runtime':write('completed-target/expected-tested-runtime.json',namespace['expected'])}
proof['target_report_sha256']=provenance['target_report']['sha256']
provenance['equivalence']=write('target-source-equivalence-deadline-final.json',proof)
namespace['READY']['completed_target_report_sha256']=provenance['target_report']['sha256'];namespace['READY']['network_equivalence_sha256']=provenance['equivalence']['sha256']
for name in('all','recovery'):provenance[name+'_report']=write('multiclient-isolated-'+name+'.json',dict(checks=[dict(passed=True,check='group fixture')],errors=[]))
composed=dict(common,evidence_kind=ready['network_evidence_kind'],success=True,stage=stage,provenance=provenance,scenarios={})
for name in('all','recovery','reconnect-0317'):
 source=target if name=='reconnect-0317'else final
 composed['scenarios'][name]=dict(source['scenarios'][name],completed_stage=ready['completed_target_stage']if name=='reconnect-0317'else stage,
  suite_sha256=ready['completed_target_suite_sha256']if name=='reconnect-0317'else ready['suite_sha256'])
write('network-validation-composed.json',composed)
verdict(True,'Honest composed fixture accepts target32 while preserving the failed whole H run and original two-group I summary')
original_files={p.relative_to(fixture).as_posix():p.read_bytes()for p in fixture.rglob('*')if p.is_file()}
def restore():
 for name,data in original_files.items():(fixture/name).write_bytes(data)
def mutate(name,value,label,update_reference=None):
 restore();write(name,value)
 if update_reference:
  changed=copy.deepcopy(composed);changed['provenance'][update_reference]['sha256']=sha(fixture/name);write('network-validation-composed.json',changed)
 verdict(False,label)
changed=copy.deepcopy(final);changed['scenarios'].pop('recovery')
mutate('network-validation-summary.json',changed,'Missing recovery cannot be hidden by a true composed success flag','final_summary')
changed=copy.deepcopy(final);changed['scenarios']['all']['failed']=1
mutate('network-validation-summary.json',changed,'Failed all checks remain blocking','final_summary')
changed=copy.deepcopy(target);changed['success']=True
mutate('completed-target/network-validation-summary.json',changed,'The failed whole target-source run cannot be relabelled successful','target_summary')
changed=copy.deepcopy(target_report);changed['checks'][0]['passed']=False
mutate('completed-target/multiclient-isolated-reconnect-0317.json',changed,'Target report byte tampering is rejected')
mutate('network-validation-composed.json',dict(composed,runtime_sha256='0'*64),'Different composed runtime pin is rejected')
mutate('network-validation-composed.json',dict(composed,suite_sha256='0'*64),'Different composed suite pin is rejected')
changed=copy.deepcopy(composed);changed['scenarios']['reconnect-0317']['completed_stage']=stage
mutate('network-validation-composed.json',changed,'Target provenance cannot pretend it completed in the later all/recovery stage')
changed=copy.deepcopy(composed);changed['scenarios']['recovery']['checks']=2
mutate('network-validation-composed.json',changed,'Composed group counts must equal the untouched original summary')
changed=copy.deepcopy(proof);changed['changed_suite_methods']=['reconnect_0317']
mutate('target-source-equivalence-deadline-final.json',changed,'Equivalence proof byte tampering is rejected')
mutate('exit-status.json',dict(exit_code=1),'A failed detached validation exit blocks apply','final_exit')
changed=copy.deepcopy(namespace['expected']);changed['files']['zz_escape_seed.lua']='0'*64
mutate('completed-target/expected-tested-runtime.json',changed,'Full target runtime including seed must equal the final tested files','target_runtime')
mutate('multiclient-isolated-all.json',dict(checks=[dict(passed=False)],errors=[]),'Actual all report checks must agree with the zero-failure summary','all_report')
changed=copy.deepcopy(composed);changed['provenance']['target_summary']['path']='../another-stage/summary.json'
mutate('network-validation-composed.json',changed,'Provenance may reference only its exact owned evidence paths')
restore();(fixture/'exit-status.json').unlink();verdict(False,'Missing detached completion proof remains blocking')
negative=subprocess.run([sys.executable,str(work/'make_backend_job.py'),'--stage',stage,'--candidate-sha256',candidate,'--mode','dryrun',
 '--readiness',str(root/'Design/Validation-2026-10-28-Reconnect/server/candidate-ready.json')],cwd=root,capture_output=True,text=True)
assert negative.returncode!=0 and not negative.stdout;checks.append(dict(name='Old candidate-ready.json cannot serve as current-source readiness',passed=True))
actual_status='Pending original all/recovery and composed proof; no PASS inferred from synthetic fixtures'
actual_proof=None
if args.evidence_dir:
 evidence=args.evidence_dir.resolve(strict=True);assert evidence.is_relative_to(root)and evidence.is_dir()and not evidence.is_symlink()
 namespace['STAGE']=evidence;namespace['READY']=ready
 actual=gate();actual_status='PASS: actual completed target32 + all/recovery composed evidence validated locally'
 actual_proof=dict(directory=evidence.relative_to(root).as_posix(),composedSha256=sha(evidence/'network-validation-composed.json'),
  provenance=actual['provenance'],groupChecks={n:r['checks']for n,r in actual['scenarios'].items()})
report=dict(passed=True,checkedUtc=datetime.now(timezone.utc).isoformat(),stage=stage,runtimeSha256=candidate,runtimeFiles=ready['runtime_files'],runtimeModules=ready['runtime_modules'],
 localLuaCases=ready['local_lua']['named_cases'],readinessSource=ready['readiness_source'],readinessSha256=ready['readiness_source_sha256'],localSuiteSha256=ready['local_suite_sha256'],
 contextSha256=ready['context_sha256'],candidateArchiveSha256=ready['candidate_archive_sha256'],suiteSha256=ready['suite_sha256'],securitySmokeSha256=ready['security_smoke_sha256'],
 deployerSha256=ready['deployer_sha256'],targetSuiteSha256=ready['completed_target_suite_sha256'],targetContextSha256=ready['target_context_sha256'],
 targetArchiveSha256=ready['target_archive_sha256'],equivalenceSha256=ready['network_equivalence_sha256'],targetSourceEquivalenceIndependentlyRecomputed=True,
 allLocalContextInputsAndArchiveMembersMatch=True,generatedJob=job.relative_to(root).as_posix(),generatedJobSha256=sha(job),
 gateChecks=checks,actualNetworkStatus=actual_status,actualNetworkProof=actual_proof,networkGateFixturesOnly=actual_proof is None,
 sshAttempted=False,remoteJobExecuted=False,productionMutation=False,applyApproved=False,publicationApproved=False)
out=root/'Design/Release-0.3.17/validation/server/orchestration-current-candidate.json';out.write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps(dict(passed=True,stage=stage,runtimeSha256=candidate,localLuaCases=ready['local_lua']['named_cases'],inputPinsPass=True,gateChecks=len(checks),actualNetworkProofChecked=actual_proof is not None,sshAttempted=False,applyApproved=False)))

