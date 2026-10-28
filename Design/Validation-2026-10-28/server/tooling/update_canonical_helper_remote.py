from pathlib import Path
from datetime import datetime,timezone
import hashlib,json,os,subprocess
root=Path('/opt/escape');target=root/'deploy_runtime.py';incoming=root/'.backend-deploy-helper-20261028.py.incoming'
backup=root/'backups/runtime-release-20261027T223730Z'
old_sha='a428f360cf9893c0f75ee1b2d6cd54c75cb54eeeb8d73bc355411ca412913c31'
new_sha='ff499e1a887c724b1bcc072ed41f1d55735b210bfdd2f2a624ae6858530f74d2'
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def tree(p):
 assert p.resolve()==p and p.is_dir()and not p.is_symlink()
 out={}
 for f in sorted(p.rglob('*')):
  assert not f.is_symlink()
  if f.is_file():out[f.relative_to(p).as_posix()]=sha(f)
 return out
assert target.parent==incoming.parent==root and all(p.resolve()==p and p.is_file()and not p.is_symlink()for p in(target,incoming))
assert sha(target)==old_sha and sha(incoming)==new_sha
old=target.read_bytes();new=incoming.read_bytes();compile(new,str(target),'exec')
manifest_path=backup/'manifest.json';old_manifest_bytes=manifest_path.read_bytes();manifest=json.loads(old_manifest_bytes)
actual=tree(backup);actual.pop('manifest.json');assert actual==manifest['files']
dump_hash=sha(backup/'escape.dump')
tooling=backup/'tooling';tooling.mkdir(mode=0o700)
(tooling/'deploy_runtime.py.before-guard').write_bytes(old)
(tooling/'pre-helper-update-manifest.json').write_bytes(old_manifest_bytes)
manifest['files']=tree(backup);manifest['files'].pop('manifest.json')
manifest['toolingSupplement']=dict(oldCanonicalHelperSha256=old_sha,newCanonicalHelperSha256=new_sha,
 updatedUtc=datetime.now(timezone.utc).isoformat(),originalPrechangeManifestPreserved='tooling/pre-helper-update-manifest.json')
tmp=root/'.backend-backup-manifest-20261028.tmp';assert not tmp.exists()
tmp.write_text(json.dumps(manifest,indent=2));os.chmod(tmp,0o600);os.replace(tmp,manifest_path)
os.chmod(incoming,target.stat().st_mode&0o777);os.replace(incoming,target)
try:
 compile(target.read_bytes(),str(target),'exec')
 help_result=subprocess.run(['python3',str(target),'--help'],cwd=root,capture_output=True,text=True,check=True)
 for arg in('--expected-runtime-sha256','--expected-tested-sha256','--catalog-probe-suite','--catalog-probe-suite-sha256'):
  assert arg in help_result.stdout
 assert sha(target)==new_sha
except BaseException:
 incoming.write_bytes(old);os.chmod(incoming,target.stat().st_mode&0o777);os.replace(incoming,target);raise
assert sha(backup/'escape.dump')==dump_hash
actual=tree(backup);actual.pop('manifest.json');assert actual==manifest['files']
runtime=tree(root/'runtime');runtime_digest=hashlib.sha256(json.dumps(runtime,sort_keys=True).encode()).hexdigest()
assert runtime_digest=='aad128d4e8b67dc8b423e6725620c89fee22519e43f727fa1fcfa2b59ed36653'
report=dict(passed=True,checkedUtc=datetime.now(timezone.utc).isoformat(),canonicalHelper=str(target),
 oldSha256=old_sha,newSha256=new_sha,sourceExact=True,pythonSyntaxPassed=True,helpPassed=True,requiredPinsPresent=True,atomicReplacement=True,
 oldHelperPreserved=str(tooling/'deploy_runtime.py.before-guard'),prechangeBackup=str(backup),backupManifestVerified=True,
 backupFiles=len(actual),backupManifestSha256=sha(manifest_path),dumpSha256Unchanged=dump_hash,
 runtimeSha256=runtime_digest,runtimeFiles=len(runtime),backupCount=len(list((root/'backups').iterdir())),siteOrApkChanged=False)
(root/'backend-deploy-tooling-20261028.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report,indent=2))
