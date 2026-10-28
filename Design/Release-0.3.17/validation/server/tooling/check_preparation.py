"""Local orchestration guard checks; never invokes SSH, Nakama, publication or Docker."""
from pathlib import Path
from datetime import datetime,timezone
import ast,hashlib,json,os,shutil,subprocess,sys,uuid
root=Path.cwd().resolve();work=Path(__file__).resolve().parent;checks=[]
scripts=sorted(work.glob('*.py'))
for path in scripts:compile(path.read_bytes(),str(path),'exec')
checks.append('All prepared Python sources compile without executing their remote side effects')
old_stage='release0317-validation-41d30b908a76';old_candidate='d8ccf6796638be97f55e89c85762ef7f85f9cf9f22011a718b3a83e28f3a21c2'
for mode,extra,label in(('apply',[],'Apply requires a separate explicit approval flag'),('dryrun',[],'Obsolete candidate is rejected against current source/ready/context before any SSH script is emitted')):
 result=subprocess.run([sys.executable,str(work/'make_backend_job.py'),'--stage',old_stage,'--candidate-sha256',old_candidate,'--mode',mode,*extra],cwd=root,capture_output=True,text=True)
 assert result.returncode!=0 and not result.stdout,label
 checks.append(label)
fixture=work/('rollback-guard-'+uuid.uuid4().hex);fixture.mkdir();assert fixture.resolve().is_relative_to(work)
old=fixture/'old.py';old.write_bytes(b'old-source\n');target=fixture/'security_smoke.py';target.write_bytes(b'new-source\n')
tree=ast.parse((work/'backend_orchestrator.py').read_text());selected=[n for n in tree.body if isinstance(n,ast.FunctionDef)and n.name in('sha','restore_smoke')]
namespace=dict(hashlib=hashlib,os=os,shutil=shutil,ROOT=fixture,BASELINE=dict(canonicalHelpers={'security_smoke.py':hashlib.sha256(old.read_bytes()).hexdigest()}))
exec(compile(ast.Module(body=selected,type_ignores=[]),'actual-orchestration-helper-functions','exec'),namespace)
new_sha=hashlib.sha256(target.read_bytes()).hexdigest();assert namespace['restore_smoke'](old,new_sha)and target.read_bytes()==old.read_bytes()
checks.append('Actual rollback helper atomically restores exactly its own pinned smoke update')
target.write_bytes(b'concurrent-operator-source\n');before=target.read_bytes();assert not namespace['restore_smoke'](old,new_sha)and target.read_bytes()==before
checks.append('Actual rollback helper preserves a concurrent operator change instead of overwriting it')
report=dict(passed=True,checkedUtc=datetime.now(timezone.utc).isoformat(),scope='Local Python syntax, obsolete-stage/approval guards and actual helper CAS rollback on owned fixture; no network or production actions',
 checks=checks,sourceSha256={p.name:hashlib.sha256(p.read_bytes()).hexdigest()for p in scripts},productionMutations=False,
 finalBackendCandidateApproved=False,staticPublicationApproved=False)
out=root/'Design/Release-0.3.17/validation/server/orchestration-preparation.json';out.write_text(json.dumps(report,indent=2));print(json.dumps(dict(passed=True,checks=len(checks),scripts=len(scripts),productionMutations=False)))
