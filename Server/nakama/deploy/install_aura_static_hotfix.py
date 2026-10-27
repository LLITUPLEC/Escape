#!/usr/bin/env python3
"""Reviewed 0.3.14 aura login hotfix: only three static files, never the bind dir.

Default is preflight. --apply requires the exact four published baseline hashes,
backs up original changed assets, atomically replaces only index/model/page,
checks public HTTPS, and restores only our own known writes on failure. No
Console authentication, Storage mutation, config change or service restart.
"""
import argparse
from datetime import datetime,timezone
import hashlib,json,os,re,stat,sys,uuid
from pathlib import Path
from urllib.request import Request,build_opener,HTTPRedirectHandler

ROOT=Path('/opt/escape')
ASSETS=frozenset({'index.html','aura.css','aura-model.js','aura-page.js'})
CHANGED=frozenset({'index.html','aura-model.js','aura-page.js'})
TARGET=ROOT/'admin/aura'
BASE='https://console.prokrust-play.ru/escape-admin/aura/'
HEX=re.compile(r'[0-9a-f]{64}\Z')
class HotfixError(Exception):pass
def digest(data):return hashlib.sha256(data).hexdigest()
def permissions(path):return path.stat().st_mode&0o777
def regular(path):
 if path.is_symlink()or not path.is_file()or path.resolve()!=path or ROOT not in path.parents:raise HotfixError('Unsafe or missing scoped file')
 return path
def identity():
 if TARGET.is_symlink()or not TARGET.is_dir()or TARGET.resolve()!=TARGET or permissions(TARGET)!=0o755:raise HotfixError('Existing public bind directory changed or is not0755')
 s=TARGET.stat();return(s.st_dev,s.st_ino)
def atomic(path,data,mode):
 if path.parent!=TARGET or path.name not in CHANGED:raise HotfixError('Write outside the three reviewed static assets')
 temp=TARGET/('.aura-hotfix-'+uuid.uuid4().hex)
 fd=os.open(temp,os.O_WRONLY|os.O_CREAT|os.O_EXCL,0o600)
 try:
  if hasattr(os,'fchmod'):os.fchmod(fd,mode)
  else:os.chmod(temp,mode)
  with os.fdopen(fd,'wb')as out:out.write(data);out.flush();os.fsync(out.fileno())
  os.replace(temp,path)
 finally:
  if temp.exists():temp.unlink()
def write_new(path,value):
 fd=os.open(path,os.O_WRONLY|os.O_CREAT|os.O_EXCL,0o600)
 with os.fdopen(fd,'w',encoding='utf-8')as out:json.dump(value,out,ensure_ascii=False,indent=2)
class NoRedirect(HTTPRedirectHandler):
 def redirect_request(self,*_):raise HotfixError('Unexpected public redirect')
def verify_public(expected):
 opener=build_opener(NoRedirect())
 for name in sorted(ASSETS):
  with opener.open(Request(BASE+(''if name=='index.html'else name),headers={'Accept-Encoding':'identity','Cache-Control':'no-cache'}),timeout=20)as response:
   if response.status!=200 or digest(response.read(1024*1024))!=expected[name]:raise HotfixError('Public asset SHA differs')
   if 'no-store'not in response.headers.get('Cache-Control','')or "script-src 'self'"not in response.headers.get('Content-Security-Policy',''):raise HotfixError('Existing private cache/CSP protection differs')
 return {'assets':4,'exactSha256':True,'privateNoStore':True,'csp':True}
def load(bundle):
 if ROOT.resolve()!=ROOT or bundle.parent!=ROOT or bundle.resolve()!=bundle or bundle.is_symlink()or not re.fullmatch(r'aura-login-fix0314-[0-9a-f]{12}',bundle.name)or bundle.stat().st_mode&0o077:raise HotfixError('Use the exact private reviewed hotfix bundle')
 m=json.loads(regular(bundle/'manifest.json').read_text())
 payload=set(CHANGED)|{'install_aura_static_hotfix.py'}
 if m.get('schema')!=1 or m.get('version')!='0.3.14'or m.get('bundleId')!=bundle.name or set(m.get('files',{}))!=payload or {p.name for p in bundle.iterdir()}!=payload|{'manifest.json'}:raise HotfixError('Unexpected hotfix bundle files')
 for name,sha in m['files'].items():
  if not isinstance(sha,str)or not HEX.fullmatch(sha)or digest(regular(bundle/name).read_bytes())!=sha:raise HotfixError('Hotfix payload hash mismatch')
 before,after=m.get('baseAssets',{}),m.get('newAssets',{})
 if set(before)!=set(ASSETS)or set(after)!=set(CHANGED)or any(not isinstance(v,str)or not HEX.fullmatch(v)for v in [*before.values(),*after.values()]):raise HotfixError('Unexpected four-file baseline or three-file replacement map')
 if any(after[n]!=m['files'][n]for n in CHANGED):raise HotfixError('Replacement map differs from sealed payload')
 return m,before,{**before,**after}
def live_matches(expected,original_inode=None):
 current=identity()
 if original_inode is not None and current!=original_inode:raise HotfixError('Existing bind directory inode changed; stop')
 for name,sha in expected.items():
  p=regular(TARGET/name)
  if permissions(p)!=0o644 or digest(p.read_bytes())!=sha:raise HotfixError('Published static baseline changed; rebase instead of overwriting')
 return current
def main():
 parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--apply',action='store_true');args=parser.parse_args()
 os.umask(0o077);bundle=Path(__file__).absolute().parent
 if Path.cwd()!=bundle:raise HotfixError('Current directory must be the exact reviewed bundle')
 manifest,before,after=load(bundle)
 actual={name:digest(regular(TARGET/name).read_bytes())for name in ASSETS}
 if actual==after:
  live_matches(after);checks=verify_public(after);print(json.dumps({'mode':'already_applied','bundleId':bundle.name,'checks':checks,'auraStorageWrites':0,'serviceRestarts':0}));return
 original_inode=live_matches(before);verify_public(before)
 plan={'mode':'preflight','bundleId':bundle.name,'target':str(TARGET),'changedAssets':sorted(CHANGED),'unchangedCss':True,'bindDirectoryInodePreserved':True,'auraStorageWrites':0,'consoleLogins':0,'serviceRestarts':0,'configChanges':0}
 if not args.apply:print(json.dumps(plan));return
 backups=ROOT/'backups'
 if backups.is_symlink()or(backups.exists()and backups.resolve()!=backups):raise HotfixError('Unsafe backup parent')
 backups.mkdir(mode=0o700,exist_ok=True)
 backup=backups/('aura-login-fix0314-'+datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%S-')+uuid.uuid4().hex[:12]);backup.mkdir(mode=0o700,exist_ok=False)
 originals={name:regular(TARGET/name).read_bytes()for name in CHANGED}
 for name,data in originals.items():
  fd=os.open(backup/name,os.O_WRONLY|os.O_CREAT|os.O_EXCL,0o600)
  with os.fdopen(fd,'wb')as out:out.write(data)
 write_new(backup/'before.json',{'plan':plan,'baseAssets':before,'newAssets':after,'directoryIdentity':original_inode})
 expected=dict(before);written=[]
 try:
  for name in sorted(CHANGED):
   live_matches(expected,original_inode)
   atomic(TARGET/name,(bundle/name).read_bytes(),0o644);written.append(name);expected[name]=after[name]
  live_matches(after,original_inode);checks=verify_public(after)
  result={**plan,'mode':'applied','backup':str(backup),'checks':checks,'oldAssets':before,'newAssets':after}
  write_new(backup/'result.json',result);print(json.dumps(result))
 except Exception:
  restored=[];foreign=[]
  try:current_inode=identity()
  except Exception:current_inode=None
  if current_inode==original_inode:
   for name in reversed(written):
    try:
     current=digest(regular(TARGET/name).read_bytes())
     if current==after[name]:atomic(TARGET/name,originals[name],0o644);restored.append(name)
     elif current!=before[name]:foreign.append(name) # Never overwrite an operator's unrelated edit.
    except Exception:
     foreign.append(name) # Missing/unsafe files must not prevent other owned restores.
  else:foreign.append('directory_identity_changed')
  write_new(backup/'failure.json',{'restored':restored,'foreignChangesPreserved':foreign,'backup':str(backup),'auraStorageWrites':0,'serviceRestarts':0})
  raise HotfixError('Static hotfix stopped; inspect private backup: '+str(backup))from None
if __name__=='__main__':
 try:main()
 except Exception as error:
  print('ERROR: '+(str(error)if isinstance(error,HotfixError)else'Hotfix preflight stopped; no sensitive responses printed'),file=sys.stderr);sys.exit(1)
