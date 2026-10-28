"""Create a pinned SSH-stdin script locally; never contacts or modifies a server."""
from pathlib import Path
import argparse,ast,base64,hashlib,json,re,sys,tarfile
root=Path.cwd().resolve();p=argparse.ArgumentParser()
p.add_argument('--stage',required=True);p.add_argument('--candidate-sha256',required=True);p.add_argument('--mode',choices=('dryrun','apply'),default='dryrun');p.add_argument('--approved-apply',action='store_true')
p.add_argument('--readiness',type=Path,default=root/'Design/Validation-2026-10-28-Reconnect/server/candidate-ready-network-final.json')
args=p.parse_args();assert re.fullmatch(r'release0317-validation-[a-f0-9]{12}',args.stage)and re.fullmatch(r'[a-f0-9]{64}',args.candidate_sha256)
assert args.mode!='apply'or args.approved_apply,'Separate apply approval flag is required'
baseline=json.loads((root/'Design/Release-0.3.17/validation/server/preflight.json').read_text(encoding='utf-8-sig'))
readiness_path=args.readiness.resolve(strict=True);assert readiness_path.is_relative_to(root)
descriptor=json.loads(readiness_path.read_text(encoding='utf-8-sig'))
assert descriptor['stage']==args.stage and descriptor['runtime_sha256']==args.candidate_sha256,'Readiness describes another candidate'
assert descriptor['local_lua']['passed']and not descriptor['local_lua']['sources_changed_during_run']
context_path=root/'.cache/release-work/backend'/args.stage/'context.json';context=json.loads(context_path.read_text())
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def digest(rows):return hashlib.sha256(json.dumps(rows,sort_keys=True).encode()).hexdigest()
modules={p.name:sha(p)for p in sorted((root/'Server/nakama/modules').glob('*.lua'))}
assert modules=={n.removeprefix('runtime/'):h for n,h in context['manifest'].items()if n.startswith('runtime/')},'Stage is obsolete or source changed after its pin'
expected=dict(baseline['runtime']['files'],**modules);assert digest(expected)==args.candidate_sha256
assert len(expected)==descriptor['runtime_files']and expected['zz_escape_seed.lua']==baseline['runtime']['files']['zz_escape_seed.lua']
local_suite_path=(root/descriptor['local_suite_source']).resolve(strict=True);assert local_suite_path.is_relative_to(root)
local_suite=json.loads(local_suite_path.read_text(encoding='utf-8-sig'))
assert local_suite['passed']and not local_suite['sources_changed_during_run']and local_suite['named_cases']==descriptor['local_lua']['named_cases']
assert local_suite['source_sha256']==modules,'Local Lua proof does not cover current module bytes'
inputs={
 'multiclient_review.py':'Server/nakama/tests/multiclient/run.py','isolated_runner.py':'Server/nakama/tests/multiclient/isolated_runner.py',
 'recovery_probe.py':'Server/nakama/tests/multiclient/recovery_probe.py','recovery-item-catalog.json':'Server/nakama/data/duel_match3_item_defs.json',
 'migration/merge_consumable_catalog.py':'Server/nakama/tools/merge_consumable_catalog.py','migration/storage_admin.py':'Server/nakama/tools/storage_admin.py',
 'migration/consumable-item-defs.json':'Server/nakama/data/migrations/consumables-0.3.9.json','migration/merge_equipment_ingots.py':'Server/nakama/tools/merge_equipment_ingots.py',
 'security_smoke.py':'Server/nakama/deploy/security_smoke.py','deploy_runtime.py':'Server/nakama/deploy/deploy_runtime.py'}
assert set(context['manifest'])=={'runtime/'+name for name in modules}|set(inputs)
for name,path in inputs.items():assert sha(root/path)==context['manifest'][name],'Reusable validation source drift: '+name
assert context['manifest']['multiclient_review.py']==descriptor['suite_sha256']
assert context['manifest']['security_smoke.py']==descriptor['security_smoke_sha256']
assert context['manifest']['deploy_runtime.py']==descriptor['deployer_sha256']
archive=context_path.parent/'candidate.tar.gz'
with tarfile.open(archive,'r:gz')as bundle:
 members=bundle.getmembers();assert len(members)==len(context['manifest'])and {m.name for m in members}==set(context['manifest'])
 for member in members:
  assert member.isfile()and not member.issym()and not member.islnk()
  assert hashlib.sha256(bundle.extractfile(member).read()).hexdigest()==context['manifest'][member.name]
assert sha(root/'Server/nakama/deploy/deploy_runtime.py')==baseline['canonicalHelpers']['deploy_runtime.py']
composition_path=(root/descriptor['network_composition_source']).resolve(strict=True);assert composition_path.is_relative_to(root)
composition=json.loads(composition_path.read_text(encoding='utf-8-sig'))
assert composition['passed']is True and composition['final_stage']==args.stage and composition['runtime_sha256']==args.candidate_sha256
assert composition['runtime_files_equal']is True and composition['target_checks_passed']==32
assert composition['final_suite_sha256']==descriptor['suite_sha256']and composition['changed_suite_methods']==['consumables']
assert composition['target_function_and_all_helpers_byte_identical']is True and composition['all_other_module_ast_equal']is True
target_stage=composition['completed_target_stage'];assert re.fullmatch(r'release0317-validation-[a-f0-9]{12}',target_stage)and target_stage!=args.stage
target_context_path=root/'.cache/release-work/backend'/target_stage/'context.json';target_context=json.loads(target_context_path.read_text(encoding='utf-8-sig'))
assert target_context['stage']==target_stage and set(target_context['manifest'])==set(context['manifest'])
assert target_context['manifest']['multiclient_review.py']==composition['completed_target_suite_sha256']
assert {n:h for n,h in target_context['manifest'].items()if n!='multiclient_review.py'}=={n:h for n,h in context['manifest'].items()if n!='multiclient_review.py'},'Reused target has different runtime/catalog/validation inputs'
def archived_suite(path,manifest):
 with tarfile.open(path,'r:gz')as bundle:
  members=bundle.getmembers();assert len(members)==len(manifest)and {m.name for m in members}==set(manifest)
  result=None
  for member in members:
   assert member.isfile()and not member.issym()and not member.islnk()
   data=bundle.extractfile(member).read();assert hashlib.sha256(data).hexdigest()==manifest[member.name]
   if member.name=='multiclient_review.py':result=data.decode('utf-8')
  assert result is not None;return result
old_suite=archived_suite(target_context_path.parent/'candidate.tar.gz',target_context['manifest'])
new_suite=archived_suite(archive,context['manifest'])
old_ast=ast.parse(old_suite);new_ast=ast.parse(new_suite)
def suite_methods(module,source):
 suite=[n for n in module.body if isinstance(n,ast.ClassDef)and n.name=='Suite'];assert len(suite)==1
 return {n.name:ast.get_source_segment(source,n)for n in suite[0].body if isinstance(n,(ast.FunctionDef,ast.AsyncFunctionDef))}
old_methods=suite_methods(old_ast,old_suite);new_methods=suite_methods(new_ast,new_suite)
assert set(old_methods)==set(new_methods)
assert sorted(n for n in old_methods if old_methods[n]!=new_methods[n])==['consumables']
method_hashes={n:hashlib.sha256(s.encode()).hexdigest()for n,s in old_methods.items()if n!='consumables'}
assert method_hashes==composition['checked_suite_method_sha256'],'Equivalence method proof does not match archived source bytes'
for module in(old_ast,new_ast):
 suite=next(n for n in module.body if isinstance(n,ast.ClassDef)and n.name=='Suite')
 suite.body=[n for n in suite.body if not(isinstance(n,(ast.FunctionDef,ast.AsyncFunctionDef))and n.name=='consumables')]
assert ast.dump(old_ast,include_attributes=False)==ast.dump(new_ast,include_attributes=False),'Target helpers or module globals changed'
ready=dict(descriptor,passed=True,no_runtime_changes_after_local_suite=True,catalog_migration_required=False,
 runtime_modules=len(modules),module_tree_sha256_without_production_seed=digest(modules),
 reusable_validation_inputs_sha256={path:context['manifest'][name]for name,path in inputs.items()},
 readiness_source=str(readiness_path.relative_to(root)).replace('\\','/'),readiness_source_sha256=sha(readiness_path),local_suite_sha256=sha(local_suite_path),
 candidate_archive_sha256=sha(archive),context_sha256=sha(context_path),
 network_evidence_kind='composed_exact_runtime_with_target_source_equivalence',
 network_equivalence_sha256=sha(composition_path),completed_target_stage=target_stage,
 completed_target_suite_sha256=composition['completed_target_suite_sha256'],completed_target_report_sha256=composition['target_report_sha256'],
 completed_target_checks=composition['target_checks_passed'],target_context_sha256=sha(target_context_path),
 target_archive_sha256=sha(target_context_path.parent/'candidate.tar.gz'),target_equivalence_independently_verified=True)
payload=dict(BASELINE=baseline,READY=ready,STAGE_NAME=args.stage,CONTEXT_SHA256=sha(context_path),CANDIDATE_SHA256=args.candidate_sha256,MODE=args.mode,APPLY_APPROVED=args.approved_apply)
encoded=base64.b64encode(json.dumps(payload).encode()).decode()
script="import base64,json\nglobals().update(json.loads(base64.b64decode('"+encoded+"')))\n"+(Path(__file__).parent/'backend_orchestrator.py').read_text()
if args.mode=='dryrun':sys.stdout.write(script)
else:
 data=base64.b64encode(script.encode()).decode()
 sys.stdout.write("from pathlib import Path\nimport base64,json,os,subprocess\nroot=Path('/opt/escape');stage=root/"+repr(args.stage)+"\n")
 sys.stdout.write("assert stage.resolve()==stage and stage.parent==root and stage.is_dir()and not stage.is_symlink()\nos.umask(0o077)\n")
 sys.stdout.write("reviewed=stage/'run-backend-reviewed.py';wrapper=stage/'run-backend-job.py'\nassert not reviewed.exists()and not wrapper.exists()and not(stage/'backend-orchestration-exit.json').exists()\n")
 sys.stdout.write("reviewed.write_bytes(base64.b64decode('"+data+"'))\n")
 wrapper="from pathlib import Path\nfrom datetime import datetime,timezone\nimport subprocess,json\ns=Path(__file__).parent\nr=subprocess.run(['python3','-B',str(s/'run-backend-reviewed.py')],cwd='/opt/escape')\n(s/'backend-orchestration-exit.json').write_text(json.dumps(dict(exit_code=r.returncode,finishedUtc=datetime.now(timezone.utc).isoformat())))\n"
 sys.stdout.write('wrapper.write_text('+repr(wrapper)+")\nwith(stage/'backend-orchestration.log').open('xb')as log:\n p=subprocess.Popen(['python3','-B',str(wrapper)],cwd=root,stdout=log,stderr=subprocess.STDOUT,start_new_session=True)\nprint(json.dumps(dict(started=True,pid=p.pid,stage=str(stage),scope='approved pinned backend; no catalog migration or forced active games')))\n")
