#!/usr/bin/env python3
"""Install only the reviewed Console aura editor and Escape gateway change.

Default: local preflight only. --apply backs up exact current files, checks the
candidate with a disposable gateway container, recreates only api-gateway, and
rolls back files/gateway on failure. No Console login or aura Storage writes.
Run from its private prepared /opt/escape/aura-admin0314-<12hex> bundle.
"""
import argparse
from datetime import datetime,timezone
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys
from urllib.request import Request,build_opener,HTTPRedirectHandler
from urllib.error import HTTPError
import uuid

ROOT=Path('/opt/escape')
ASSETS=('index.html','aura.css','aura-model.js','aura-page.js')
CONFIGS=('nginx.conf.template','compose.yml')
BASE='https://console.prokrust-play.ru'

class InstallError(Exception):pass
def digest(data):return hashlib.sha256(data).hexdigest()
def normalized(data):return data.replace(b'\r\n',b'\n')
def safe(path,required=True):
 path=Path(path)
 if not path.is_absolute() or ROOT not in path.parents or path.is_symlink() or path.resolve()!=path:
  raise InstallError('Path is outside the exact Escape deployment scope')
 if required and not path.is_file():raise InstallError('Required deployment file is absent')
 return path
def atomic(path,data,mode=0o644):
 path=safe(path,False);path.parent.mkdir(mode=0o755,parents=True,exist_ok=True)
 safe(path.parent/'guard',False)
 if path.parent==ROOT/'admin/aura':path.parent.chmod(0o755)
 temp=path.with_name('.'+path.name+'.'+uuid.uuid4().hex)
 fd=os.open(temp,os.O_WRONLY|os.O_CREAT|os.O_EXCL,mode)
 try:
  # umask077 protects bundle/backups, but must not turn public assets into0600
  # or prevent exact config/rollback mode preservation.
  if hasattr(os,'fchmod'):os.fchmod(fd,mode)
  else:os.chmod(temp,mode)
  with os.fdopen(fd,'wb')as out:out.write(data);out.flush();os.fsync(out.fileno())
  os.replace(temp,path)
 finally:
  if temp.exists():temp.unlink()
def write_new(path,value):
 fd=os.open(path,os.O_WRONLY|os.O_CREAT|os.O_EXCL,0o600)
 with os.fdopen(fd,'w',encoding='utf-8')as out:json.dump(value,out,ensure_ascii=False,indent=2)
def command(*args):
 result=subprocess.run(args,cwd=ROOT,capture_output=True,text=True,timeout=180)
 if result.returncode:raise InstallError('Scoped gateway command failed; output was not printed')
 return result.stdout
def compose(*args):return command('docker','compose','--project-directory',str(ROOT),'-f',str(ROOT/'compose.yml'),'-p','escape',*args)
class NoRedirect(HTTPRedirectHandler):
 def redirect_request(self,*_):raise InstallError('Unexpected public redirect')
def verify_public(bundle):
 opener=build_opener(NoRedirect())
 for name in ASSETS:
  url=BASE+'/escape-admin/aura/'+(''if name=='index.html'else name)
  with opener.open(Request(url,headers={'Accept-Encoding':'identity','Cache-Control':'no-cache'}),timeout=20)as response:
   data=response.read(1024*1024)
   if response.status!=200 or digest(data)!=digest((bundle/name).read_bytes()):raise InstallError('Public aura asset differs from the reviewed bundle')
   cache=response.headers.get('Cache-Control','').lower()
   csp=response.headers.get('Content-Security-Policy','')
   if 'no-store'not in cache or 'private'not in cache or "frame-ancestors 'none'"not in csp or "script-src 'self'"not in csp:raise InstallError('Public private-cache/CSP headers differ')
 try:
  with opener.open(BASE+'/v2/console/status',timeout=20):raise InstallError('Console unexpectedly permits anonymous status access')
 except HTTPError as error:
  if error.code not in(401,403):raise InstallError('Console authentication guard could not be verified')
 return {'assets':4,'exactSha256':True,'privateNoStore':True,'csp':True,'anonymousConsoleStatusDenied':True,'auraStorageWrites':0}
def preflight(bundle):
 if ROOT.resolve()!=ROOT or Path.cwd()!=bundle or bundle.parent!=ROOT or not re.fullmatch(r'aura-admin0314-[0-9a-f]{12}',bundle.name)or bundle.resolve()!=bundle or stat.S_IMODE(bundle.stat().st_mode)&0o077:raise InstallError('Use the exact private reviewed Escape bundle')
 manifest=json.loads(safe(bundle/'manifest.json').read_text())
 expected=set(ASSETS)|{'install_aura_admin.py','baseline-nginx.conf.template','updated-nginx.conf.template','baseline-compose.yml','updated-compose.yml'}
 if manifest.get('schema')!=1 or manifest.get('bundleId')!=bundle.name or set(manifest.get('files',{}))!=expected or {p.name for p in bundle.iterdir()}!=expected|{'manifest.json'}:raise InstallError('Unexpected bundle manifest/files')
 for name,sha in manifest['files'].items():
  if digest(safe(bundle/name).read_bytes())!=sha:raise InstallError('Reviewed bundle hash mismatch')
 for name in CONFIGS:
  current=safe(ROOT/name).read_bytes();before=(bundle/('baseline-'+name)).read_bytes()
  if normalized(current)!=normalized(before):raise InstallError('Live gateway/compose differs from the reviewed baseline; rebase before apply')
 target=ROOT/'admin/aura'
 if target.exists()and(not target.is_dir()or target.is_symlink()or target.resolve()!=target or any(p.name not in ASSETS or not p.is_file()or p.is_symlink()for p in target.iterdir())):raise InstallError('Unexpected existing aura editor files')
 # A private deployment root and exact Compose project are required; never dump
 # resolved Compose/env output because it contains server-local credentials.
 config=json.loads(compose('config','--format','json'))
 gateway=config.get('services',{}).get('api-gateway',{})
 if config.get('name')!='escape' or gateway.get('image')!='nginx:1.30.6-alpine':raise InstallError('Unexpected Escape gateway project/image')
 return manifest
def main():
 parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--apply',action='store_true');args=parser.parse_args()
 os.umask(0o077);bundle=Path(__file__).absolute().parent
 manifest=preflight(bundle)
 plan={'mode':'preflight','bundleId':bundle.name,'root':str(ROOT),'targetAssets':str(ROOT/'admin/aura'),'gatewayService':'escape/api-gateway','publicUrl':BASE+'/escape-admin/aura/','auraStorageWrites':0,'configBaselinesMatch':True}
 if not args.apply:print(json.dumps(plan));return
 backup=ROOT/'backups'/('aura-admin0314-'+datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%S-')+uuid.uuid4().hex[:12]);safe(backup/'guard',False);backup.mkdir(parents=True,mode=0o700,exist_ok=False)
 original={};public_dir=ROOT/'admin/aura';directory_before={'existed':public_dir.exists(),'mode':stat.S_IMODE(public_dir.stat().st_mode)if public_dir.exists()else None}
 for name in CONFIGS:
  path=safe(ROOT/name);original[str(path)]={'bytes':path.read_bytes(),'mode':stat.S_IMODE(path.stat().st_mode)}
 for name in ASSETS:
  path=safe(ROOT/'admin/aura'/name,False);original[str(path)]={'bytes':path.read_bytes(),'mode':stat.S_IMODE(path.stat().st_mode)}if path.exists()else None
 metadata=[]
 for index,(name,value)in enumerate(original.items()):
  snapshot='file-'+str(index)+'.backup'
  if value is not None:
   fd=os.open(backup/snapshot,os.O_WRONLY|os.O_CREAT|os.O_EXCL,0o600)
   with os.fdopen(fd,'wb')as out:out.write(value['bytes'])
  metadata.append({'target':name,'existed':value is not None,'snapshot':snapshot if value else None,'sha256':digest(value['bytes'])if value else None,'mode':value['mode']if value else None})
 write_new(backup/'before.json',{'plan':plan,'files':metadata,'publicDirectory':directory_before})
 preflight(bundle) # Last baseline check before writing any deployment file.
 changed=False
 try:
  changed=True
  for name in ASSETS:atomic(ROOT/'admin/aura'/name,(bundle/name).read_bytes())
  for name in CONFIGS:atomic(ROOT/name,(bundle/('updated-'+name)).read_bytes(),original[str(ROOT/name)]['mode'])
  compose('config','--quiet')
  compose('run','--rm','--no-deps','-T','api-gateway','nginx','-t')
  compose('up','-d','--no-deps','--force-recreate','api-gateway')
  compose('exec','-T','api-gateway','nginx','-t')
  checks=verify_public(bundle)
  result={**plan,'mode':'applied','backup':str(backup),'checks':checks}
  write_new(backup/'result.json',result);print(json.dumps(result))
 except Exception:
  rollback=False
  if changed:
   try:
    for name,value in original.items():
     path=safe(Path(name),False)
     if value is None:
      if path.exists():path.unlink()
     else:atomic(path,value['bytes'],value['mode'])
    if directory_before['existed']:public_dir.chmod(directory_before['mode'])
    elif public_dir.exists():public_dir.rmdir() # only the now-empty owned directory
    compose('config','--quiet');compose('run','--rm','--no-deps','-T','api-gateway','nginx','-t');compose('up','-d','--no-deps','--force-recreate','api-gateway');compose('exec','-T','api-gateway','nginx','-t');rollback=True
   except Exception:pass
  write_new(backup/'failure.json',{'rolledBack':rollback,'backup':str(backup),'auraStorageWrites':0})
  raise InstallError('Aura editor install failed; rollback='+str(rollback)+'; exact backups: '+str(backup))from None
if __name__=='__main__':
 try:main()
 except Exception as error:
  print('ERROR: '+(str(error)if isinstance(error,InstallError)else'Preflight stopped; no sensitive command output printed'),file=sys.stderr);sys.exit(1)
