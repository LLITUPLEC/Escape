"""Emit exact parent-approved backend staging cleanup after validating the durable local archive."""
from pathlib import Path
from datetime import datetime,timezone
import argparse,base64,hashlib,json,re,sys
root=Path.cwd().resolve();work=Path(__file__).resolve().parent;out=root/'Design/Release-0.3.17/validation/server'
parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--approved-cleanup',action='store_true');args=parser.parse_args()
assert args.approved_cleanup,'Parent-approved cleanup signal is required'
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
ack_path=out/'backend-archive-acknowledgment.json';ack=read(ack_path);assert ack['passed']is True and ack['proofArchivedLocallyAndShaVerified']is True
inventory_path=(root/ack['inventory_source']).resolve(strict=True);assert inventory_path.is_relative_to(root)and sha(inventory_path)==ack['inventory_sha256']
inventory=read(inventory_path);assert inventory['readOnly']is True and not any(inventory['disposableDocker'].values())and inventory['tests']==[]and inventory['rootTemporary']==[]
manifest_path=(root/ack['archive_manifest']['path']).resolve(strict=True);assert manifest_path.is_relative_to(root)and sha(manifest_path)==ack['archive_manifest']['sha256']
manifest=read(manifest_path);assert len(manifest['files'])==manifest['file_count']==21
for name,wanted in manifest['files'].items():
 p=(manifest_path.parent/name).resolve(strict=True);assert p.is_relative_to(manifest_path.parent)and not p.is_symlink()and sha(p)==wanted
assert ack['all_8_remote_stages_no_longer_required']is True and ack['temporary_overlays_and_duplicate_reportfiles_no_longer_required']is True
assert ack['exact_target_final_suite_sources_durable']is True and ack['original_failed_summaries_preserved']is True
assert ack['production_smoke_started']is False and ack['own_accounts_created']==0
assert len(inventory['stages'])==len(ack['stages'])==8
indexed={r['path']:r for r in inventory['stages']};assert set(indexed)=={r['path']for r in ack['stages']}
for row in ack['stages']:
 assert row['path']=='/opt/escape/'+row['stage']and re.fullmatch(r'release0317-validation-[a-f0-9]{12}',row['stage'])
 expected=indexed[row['path']];assert row['context_sha256']==expected['contextSha256']and row['remote_tree_sha256']==expected['treeSha256']
 for name,wanted in((row['context_durable_path'],row['context_sha256']),(row['candidate_archive_local_path'],row['candidate_archive_sha256'])):
  p=(root/name).resolve(strict=True);assert p.is_relative_to(root)and sha(p)==wanted
 if'exact_suite_durable_path'in row:assert sha(root/row['exact_suite_durable_path'])==row['exact_suite_sha256']
for row in ack['mandatory_raw_reports']+ack['durable_final_and_failure_notes']:
 p=(root/row['path']).resolve(strict=True);assert p.is_relative_to(root)and sha(p)==row['sha256']
retention=read(out/'retention.json');assert retention['passed']is True and retention['retentionApplied']is True and retention['backupCount']==2
plan=dict(proofArchivedLocallyAndShaVerified=True,runtimeSha256=retention['runtimeSha256'],archiveAcknowledgmentSha256=sha(ack_path),
 proofArchiveManifestSha256=sha(manifest_path),stages=[{k:r[k]for k in('path','contextSha256','treeSha256')}for r in inventory['stages']],
 temporaryDirectories=[{k:r[k]for k in('path','treeSha256')}for r in inventory['temporaryDirectories']],temporaryReportFiles=inventory['temporaryReportFiles'])
assert len(plan['temporaryDirectories'])==2 and len(plan['temporaryReportFiles'])==8
(out/'backend-cleanup-plan.json').write_text(json.dumps(dict(plan,checkedUtc=datetime.now(timezone.utc).isoformat()),indent=2),encoding='utf-8')
encoded=base64.b64encode(json.dumps(dict(CLEANUP_APPROVED=True,CLEANUP_PLAN=plan)).encode()).decode()
sys.stdout.write("import base64,json\nglobals().update(json.loads(base64.b64decode('"+encoded+"')))\n")
sys.stdout.write((work/'cleanup_backend_stages_remote.py').read_text(encoding='utf-8-sig'))
