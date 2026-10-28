"""Archive only sanitized validation evidence; never credentials, dumps, users or configs."""
from pathlib import Path
from datetime import datetime,timezone
import hashlib,io,json,re,sys,tarfile
root=Path('/opt/escape');stage=root/sys.argv[1]
assert stage.parent==root and re.fullmatch(r'release0315-validation-[a-f0-9]{12}',stage.name)
post=json.loads((stage/'post-deployment-verification.json').read_text());assert post['passed']
backup=Path(post['freshBackup']);assert backup.parent==root/'backups'
sources={}
for name in('context.json','expected-tested-runtime.json','network-validation-summary.json','shared-handler-proof.json','validation-exit.json',
 'deployment-preflight.log','deployment.log','deployment-exit.json','production-practice-smoke.log','production-practice-smoke.json',
 'production-practice-smoke-exit.json','post-deployment-verification.json','isolated-quote-preflight.log','isolated-recovery.log',
 'multiclient-isolated-quote-preflight.json','multiclient-isolated-recovery.json'):
 sources['final-runtime/'+name]=stage/name
sources['backup/pre-change-manifest.json']=backup/'manifest.json'
sources['backup/guard-active-before-cas.json']=backup/'quote-guard-active.json'
common=root/'release0315-validation-c135615aa25c'
for name in('expected-tested-runtime.json','network-validation-summary.json','multiclient-isolated-all.json','rerun-all.log',
 'focused-lifecycle.log','focused-lifecycle-exit.json','multiclient-isolated-lifecycle.json'):
 sources['shared-handler-8cf/'+name]=common/name
affected=root/'release0315-validation-bb506a1cbca8'
for name in('expected-tested-runtime.json','isolated-global-boss-practice.log','isolated-equipment-sets.log',
 'multiclient-isolated-global-boss-practice.json','multiclient-isolated-equipment-sets.json'):
 sources['final-runtime-affected/'+name]=affected/name
for name in('isolated-quote-preflight.log','multiclient-isolated-quote-preflight.json'):
 sources['harness-wallet-assumption/'+name]=affected/name
def sanitize(text):
 text=re.sub(r'(?i)(Bearer|Basic)\s+[A-Za-z0-9._~+/=-]+',r'\1 [REDACTED]',text)
 text=re.sub(r'eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+','[REDACTED_JWT]',text)
 text=re.sub(r'(?i)([?&](?:token|password|key|secret)=)[^\s&"\']+',r'\1[REDACTED]',text)
 return text
out=stage/'sanitized-backend-proof.tar.gz';assert not out.exists()
manifest={}
with tarfile.open(out,'x:gz')as archive:
 for name,p in sources.items():
  assert p.is_file()and not p.is_symlink()and p.resolve().is_relative_to(root)
  original=p.read_bytes();safe=sanitize(original.decode('utf-8-sig')).encode('utf-8')
  manifest[name]=dict(originalSha256=hashlib.sha256(original).hexdigest(),archivedSha256=hashlib.sha256(safe).hexdigest(),bytes=len(safe),sanitized=safe!=original)
  info=tarfile.TarInfo(name);info.mode=0o600;info.size=len(safe);archive.addfile(info,io.BytesIO(safe))
 evidence=json.dumps(dict(createdUtc=datetime.now(timezone.utc).isoformat(),files=manifest),indent=2).encode()
 info=tarfile.TarInfo('evidence-manifest.json');info.mode=0o600;info.size=len(evidence);archive.addfile(info,io.BytesIO(evidence))
print(json.dumps(dict(archive=str(out),bytes=out.stat().st_size,sha256=hashlib.sha256(out.read_bytes()).hexdigest(),files=len(sources))))
