"""Injected pinned context; dryrun is read-only. Apply requires a separate explicit approval flag."""
from pathlib import Path
from datetime import datetime,timezone
import hashlib,importlib.util,json,os,re,shutil,subprocess,time
ROOT=Path('/opt/escape')
assert ROOT.resolve()==ROOT and re.fullmatch(r'release0317-validation-[a-f0-9]{12}',STAGE_NAME)
STAGE=ROOT/STAGE_NAME
assert STAGE.resolve()==STAGE and STAGE.is_dir()and not STAGE.is_symlink()
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
def digest(rows):return hashlib.sha256(json.dumps(rows,sort_keys=True).encode()).hexdigest()
def check_production(smoke_sha=None,runtime_files=None):
 assert tree(ROOT/'runtime')==(runtime_files or BASELINE['runtime']['files']),'Production runtime drift'
 assert sha(ROOT/'deploy_download_page.py')==BASELINE['publisher']['sha256']
 for name,row in BASELINE['protected'].items():
  if name=='admin':assert tree(ROOT/'admin')=={n:r['sha256']for n,r in row.items()}
  else:assert sha(ROOT/name)==row['sha256']
 for name,wanted in BASELINE['canonicalHelpers'].items():
  assert sha(ROOT/name)==(smoke_sha if name=='security_smoke.py'and smoke_sha else wanted)
 current=Path('/var/www/escape/current').resolve(strict=True);assert str(current)==BASELINE['website']['currentTarget']
 for name,row in BASELINE['website']['releases'][str(current)]['files'].items():assert sha(current/name)==row['sha256']
 for name,row in BASELINE['website']['apks'].items():
  p=Path('/var/www/escape/downloads')/name;assert p.stat().st_size==row['bytes']and sha(p)==row['sha256']
 spec=importlib.util.spec_from_file_location('checked_admin',ROOT/'tools/storage_admin.py');admin=importlib.util.module_from_spec(spec);spec.loader.exec_module(admin)
 config=json.loads((ROOT/'secrets/server.yml').read_text());console=admin.Console('http://127.0.0.1:27351')
 try:
  console.login(config['console']['username'],config['console']['password'])
  for key,wanted in BASELINE['catalogs'].items():
   collection,name=key.split('/',1);row=console.read(collection,name)
   assert(None if row is None else dict(version=row['version'],valueSha256=digest(row['value'])))==wanted,'Configured catalog drift: '+key
 finally:console.close()
def candidate_inputs():
 assert READY['stage']==STAGE_NAME and READY['runtime_sha256']==CANDIDATE_SHA256
 assert sha(STAGE/'context.json')==CONTEXT_SHA256
 context=json.loads((STAGE/'context.json').read_text());assert context['stage']==STAGE_NAME and context['version']=='0.3.17'
 for name,h in context['manifest'].items():
  p=STAGE/'candidate'/name;assert p.resolve().is_relative_to(STAGE/'candidate')and p.is_file()and not p.is_symlink()and sha(p)==h
 modules={n.removeprefix('runtime/'):h for n,h in context['manifest'].items()if n.startswith('runtime/')}
 assert digest(modules)==READY['module_tree_sha256_without_production_seed']
 expected=json.loads((STAGE/'expected-tested-runtime.json').read_text())
 assert expected['sha256']==CANDIDATE_SHA256 and digest(expected['files'])==CANDIDATE_SHA256
 assert len(expected['files'])==READY['runtime_files']
 assert expected['files']==dict(BASELINE['runtime']['files'],**modules)
 assert expected['files']['zz_escape_seed.lua']==BASELINE['runtime']['files']['zz_escape_seed.lua']
 assert tree(ROOT/'multiclient-runtime-overlay')==modules
 assert json.loads((ROOT/'multiclient-runtime-manifest.json').read_text())==expected
 assert context['manifest']['deploy_runtime.py']==BASELINE['canonicalHelpers']['deploy_runtime.py']
 assert context['manifest']['security_smoke.py']==READY['reusable_validation_inputs_sha256']['Server/nakama/deploy/security_smoke.py']
 return context,expected
def checked_network():
 assert READY['network_evidence_kind']=='composed_exact_runtime_with_target_source_equivalence'
 assert READY['target_equivalence_independently_verified']is True
 composed_path=STAGE/'network-validation-composed.json';assert not composed_path.is_symlink()
 composed=json.loads(composed_path.read_text())
 assert composed['evidence_kind']==READY['network_evidence_kind']and composed['success']is True and composed['stage']==STAGE_NAME
 def common(summary,suite,complete):
  assert summary['runtime_sha256']==CANDIDATE_SHA256 and summary['runtime_files']==len(expected['files'])
  assert summary['suite_sha256']==suite and summary['catalog_sha256']==context['manifest']['recovery-item-catalog.json']
  assert summary['production_runtime_changed']is False and summary['production_runtime_before_sha256']==BASELINE['runtime']['sha256']
  if complete:assert summary['production_runtime_after_sha256']==BASELINE['runtime']['sha256']
  elif 'production_runtime_after_sha256'in summary:assert summary['production_runtime_after_sha256']==BASELINE['runtime']['sha256']
 def result_valid(result):
  assert result['exit_code']==0 and type(result['checks'])is int and result['checks']>0 and result['failed']==0
  assert result['errors']==[]and result['tested_runtime_matches_final_candidate']is True
 def ref(name,exact_path):
  row=composed['provenance'][name];assert row['path']==exact_path
  p=STAGE/exact_path;assert p.resolve().is_relative_to(STAGE)and not p.is_symlink()and sha(p)==row['sha256']
  return json.loads(p.read_text())
 def report_valid(report,result):
  assert type(report['checks'])is list and len(report['checks'])==result['checks']and report['errors']==[]
  assert all(c['passed']is True for c in report['checks'])
 common(composed,context['manifest']['multiclient_review.py'],True)
 final=ref('final_summary','network-validation-summary.json')
 common(final,context['manifest']['multiclient_review.py'],True)
 assert final['success']is True and set(final['scenarios'])=={'all','recovery'}
 final_exit=ref('final_exit','exit-status.json');assert final_exit['exit_code']==0
 for name in('all','recovery'):
  result_valid(final['scenarios'][name])
  report=ref(name+'_report','multiclient-isolated-'+name+'.json')
  report_valid(report,final['scenarios'][name])
 target=ref('target_summary','completed-target/network-validation-summary.json')
 common(target,READY['completed_target_suite_sha256'],False)
 assert target.get('success')is not True and composed['provenance']['target_summary']['source_summary_success']is False
 assert 'all'in target['scenarios']and target['scenarios']['all']['exit_code']!=0,'Preserve failed whole-run evidence'
 target_result=target['scenarios']['reconnect-0317'];result_valid(target_result)
 assert target_result['checks']==READY['completed_target_checks']==32
 target_report=ref('target_report','completed-target/multiclient-isolated-reconnect-0317.json')
 assert composed['provenance']['target_report']['sha256']==READY['completed_target_report_sha256']
 report_valid(target_report,target_result)
 target_runtime=ref('target_runtime','completed-target/expected-tested-runtime.json')
 assert target_runtime==expected and digest(target_runtime['files'])==CANDIDATE_SHA256
 equivalence=ref('equivalence','target-source-equivalence-deadline-final.json')
 assert composed['provenance']['equivalence']['sha256']==READY['network_equivalence_sha256']
 assert equivalence['passed']is True and equivalence['final_stage']==STAGE_NAME and equivalence['completed_target_stage']==READY['completed_target_stage']
 assert equivalence['runtime_sha256']==CANDIDATE_SHA256 and equivalence['runtime_files_equal']is True
 assert equivalence['completed_target_suite_sha256']==READY['completed_target_suite_sha256']and equivalence['final_suite_sha256']==context['manifest']['multiclient_review.py']
 assert equivalence['target_report_sha256']==READY['completed_target_report_sha256']and equivalence['target_checks_passed']==32
 assert equivalence['changed_suite_methods']==['consumables']and equivalence['target_function_and_all_helpers_byte_identical']is True and equivalence['all_other_module_ast_equal']is True
 assert set(composed['scenarios'])=={'all','recovery','reconnect-0317'}
 for name,result in composed['scenarios'].items():
  expected_stage=READY['completed_target_stage']if name=='reconnect-0317'else STAGE_NAME
  expected_suite=READY['completed_target_suite_sha256']if name=='reconnect-0317'else context['manifest']['multiclient_review.py']
  assert result['completed_stage']==expected_stage and result['suite_sha256']==expected_suite
  original=target_result if name=='reconnect-0317'else final['scenarios'][name]
  assert {k:v for k,v in result.items()if k not in('completed_stage','suite_sha256')}==original
  result_valid(result)
 return composed
def fresh_health():
 return subprocess.run(['docker','inspect','--format','{{.State.Health.Status}}','escape-nakama-1'],capture_output=True,text=True,check=True).stdout.strip()
def container_states(after_deploy=False):
 rows=[]
 for before in BASELINE['containers']:
  raw=json.loads(subprocess.run(['docker','inspect',before['name']],capture_output=True,text=True,check=True).stdout)[0]
  assert raw['Id']==before['id']and raw['Image']==before['image']and raw['State']['Status']=='running'
  if before['name']=='escape-postgres-1'or not after_deploy:assert raw['State']['StartedAt']==before['startedAt']
  health=raw['State'].get('Health',{}).get('Status');assert health=='healthy'
  rows.append(dict(name=before['name'],id=raw['Id'],startedAt=raw['State']['StartedAt'],restartCount=raw['RestartCount'],status=raw['State']['Status'],health=health,image=raw['Image']))
 return rows
def restore_smoke(old,new_sha):
 target=ROOT/'security_smoke.py'
 if sha(target)==new_sha:
  pending=ROOT/'.security-smoke-0317.rollback';assert not pending.exists();shutil.copyfile(old,pending);pending.chmod(0o755);os.replace(pending,target)
  return True
 return sha(target)==BASELINE['canonicalHelpers']['security_smoke.py']
context,expected=candidate_inputs();check_production();container_states()
command=['python3','-B',str(ROOT/'deploy_runtime.py'),'--expected-runtime-sha256',BASELINE['runtime']['sha256'],'--expected-tested-sha256',CANDIDATE_SHA256]
assert not any('catalog' in arg or 'allow-test-games'in arg for arg in command)
preflight=subprocess.run(command,cwd=ROOT,capture_output=True,text=True)
assert preflight.returncode==0,preflight.stdout+preflight.stderr
try:
 network=checked_network();network_ready=True
except (AssertionError,FileNotFoundError):network=None;network_ready=False
if MODE=='dryrun':
 print(json.dumps(dict(passed=True,mode='read_only_dryrun',runtimeBaseline=BASELINE['runtime']['sha256'],candidateSha256=CANDIDATE_SHA256,
  candidateFiles=len(expected['files']),networkReady=network_ready,productionChanged=False,canonicalPreflight=preflight.stdout,
  proposedCommand=command+['--apply'],sourceSecuritySmokeOldSha256=BASELINE['canonicalHelpers']['security_smoke.py'],
  sourceSecuritySmokeNewSha256=context['manifest']['security_smoke.py'],catalogMigration=False,forceActiveGames=False),indent=2))
elif MODE=='apply':
 assert APPLY_APPROVED is True and network_ready,'No separate apply approval or final full network proof'
 assert not(STAGE/'backend-apply-exit.json').exists()and not(STAGE/'backend-apply.log').exists()
 os.umask(0o077)
 tools=STAGE/'predeploy-tools';tools.mkdir(mode=0o700)
 old=tools/'security_smoke.py.before-0317';shutil.copy2(ROOT/'security_smoke.py',old)
 assert sha(old)==BASELINE['canonicalHelpers']['security_smoke.py']
 # Also prove the old helper already exists in an independent verified immutable AAD snapshot.
 prior=Path('/opt/escape/backups/pre-publication-0.3.16-20261028T044329Z')
 prior_manifest=json.loads((prior/'manifest.json').read_text());prior_files=tree(prior);prior_files.pop('manifest.json')
 assert prior_files==prior_manifest['files']and prior_manifest['pgRestoreListVerified']
 assert sha(prior/'source/security_smoke.py')==sha(old)
 source=STAGE/'candidate/security_smoke.py';new_sha=context['manifest']['security_smoke.py']
 compile(source.read_bytes(),str(source),'exec')
 pending=ROOT/'.security-smoke-0317.incoming';assert not pending.exists();shutil.copyfile(source,pending);pending.chmod(0o755)
 assert sha(pending)==new_sha;os.replace(pending,ROOT/'security_smoke.py')
 canonical_succeeded=False
 try:
  subprocess.run(['python3','-B',str(ROOT/'security_smoke.py'),'--help'],cwd=ROOT,capture_output=True,text=True,check=True)
  check_production(new_sha)
  with(STAGE/'backend-apply.log').open('x')as log:
   result=subprocess.run(command+['--apply'],cwd=ROOT,stdout=log,stderr=subprocess.STDOUT)
  assert result.returncode==0,'Canonical deploy failed; inspect private log and verified rollback state'
  canonical_succeeded=True
  log=(STAGE/'backend-apply.log').read_text()
  match=re.search(r'PASS deployed tested runtime; Escape Nakama healthy; backup=(/opt/escape/backups/runtime-release-[0-9]{8}T[0-9]{6}Z)',log);assert match
  backup=Path(match[1]);assert backup.parent==ROOT/'backups'and backup.resolve()==backup
  manifest_path=backup/'manifest.json';manifest=json.loads(manifest_path.read_text());actual=tree(backup);actual.pop('manifest.json')
  assert actual==manifest['files']and manifest['pgRestoreListVerified']and tree(backup/'runtime')==BASELINE['runtime']['files']
  supplements=backup/'source';supplements.mkdir(mode=0o700)
  shutil.copy2(old,supplements/'security_smoke.py.before-0317')
  (supplements/'security-smoke-update.json').write_text(json.dumps(dict(oldSha256=sha(old),newSha256=new_sha,atomicReplacement=True,
   sourcePinVerified=True,pythonSyntaxPassed=True,helpPassed=True,checkedUtc=datetime.now(timezone.utc).isoformat()),indent=2))
  manifest['files']=tree(backup);manifest['files'].pop('manifest.json')
  manifest['sourceSupplementUtc']=datetime.now(timezone.utc).isoformat()
  temporary=tools/'backup-manifest.new';temporary.write_text(json.dumps(manifest,indent=2));os.replace(temporary,manifest_path)
  verified=tree(backup);verified.pop('manifest.json');assert verified==manifest['files']
  assert tree(ROOT/'runtime')==expected['files']and fresh_health()=='healthy'
  check_production(new_sha,expected['files']);containers=container_states(True)
  report=dict(passed=True,finishedUtc=datetime.now(timezone.utc).isoformat(),runtimeSha256=CANDIDATE_SHA256,runtimeFiles=len(expected['files']),
   baselineRuntimeSha256=BASELINE['runtime']['sha256'],backup=str(backup),backupManifestSha256=sha(manifest_path),backupManifestVerified=True,
   pgRestoreListVerified=True,securitySmokeSha256=new_sha,oldSecuritySmokeBackedUp=True,catalogMigration=False,forceActiveGames=False,
   canonicalDeployerSha256=sha(ROOT/'deploy_runtime.py'),nakamaHealth='healthy',onlyNakamaRestarted=True,siteOrApkChanged=False,
   networkEvidenceKind=network['evidence_kind'],networkComposedSha256=sha(STAGE/'network-validation-composed.json'),
   networkProvenance=network['provenance'],networkGroupChecks={name:row['checks']for name,row in network['scenarios'].items()},
   readinessSha256=READY['readiness_source_sha256'],localSuiteSha256=READY['local_suite_sha256'],targetEquivalenceIndependentlyRecomputed=READY['target_equivalence_independently_verified'])
  publication_baseline=json.loads(json.dumps(BASELINE));publication_baseline['checkedUtc']=report['finishedUtc'];publication_baseline['phase']='verified_post_backend_before_static_publication'
  publication_baseline['runtime']=expected;publication_baseline['canonicalHelpers']['security_smoke.py']=new_sha;publication_baseline['containers']=containers
  publication_baseline['backups']={}
  for path in sorted((ROOT/'backups').iterdir()):
   rows=tree(path);metadata=json.loads((path/'manifest.json').read_text());without=dict(rows);without.pop('manifest.json')
   assert without==metadata['files']and metadata['pgRestoreListVerified']
   rt={n.removeprefix('runtime/'):h for n,h in rows.items()if n.startswith('runtime/')}
   publication_baseline['backups'][str(path)]=dict(files=len(rows),runtimeSha256=digest(rt),dump=dict(bytes=(path/'escape.dump').stat().st_size,sha256=sha(path/'escape.dump')),
    manifestSha256=sha(path/'manifest.json'))
  (STAGE/'publication-baseline.json').write_text(json.dumps(publication_baseline,indent=2))
  (STAGE/'backend-apply-proof.json').write_text(json.dumps(report,indent=2))
  (STAGE/'backend-apply-exit.json').write_text(json.dumps(dict(exit_code=0,finishedUtc=report['finishedUtc'])))
  print(json.dumps(report,indent=2))
 except BaseException:
  # A metadata-write failure after a healthy deploy must not replace its required smoke
  # helper with the legacy one. The canonical deployer itself owns runtime rollback.
  smoke_restored=restore_smoke(old,new_sha)if not canonical_succeeded else False
  diagnostics=dict(exit_code=1,finishedUtc=datetime.now(timezone.utc).isoformat(),runtimeSha256=digest(tree(ROOT/'runtime')),
   securitySmokeRestored=smoke_restored,nakamaHealth=fresh_health(),databaseRestored=False)
  (STAGE/'backend-apply-exit.json').write_text(json.dumps(diagnostics,indent=2));raise
else:raise AssertionError('Only dryrun/apply modes are supported')
