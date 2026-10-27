"""Offline actual hotfix helper; network mocked, source files stand in for public assets."""
import contextlib,hashlib,importlib.util,io,json,os,sys,tempfile,unittest
from pathlib import Path
from unittest.mock import patch
REPO=Path(__file__).resolve().parents[3]
spec=importlib.util.spec_from_file_location('hotfix',REPO/'Server/nakama/deploy/install_aura_static_hotfix.py');H=importlib.util.module_from_spec(spec);spec.loader.exec_module(H)
class Tests(unittest.TestCase):
 def setUp(self):
  scratch=REPO/'.cache/qa/aura-hotfix';scratch.mkdir(parents=True,exist_ok=True)
  self.tmp=tempfile.TemporaryDirectory(dir=scratch);self.addCleanup(self.tmp.cleanup);self.root=Path(self.tmp.name).resolve()
  self.target=self.root/'admin/aura';self.target.mkdir(parents=True);self.bundle=self.root/'aura-login-fix0314-aaaaaaaaaaaa';self.bundle.mkdir()
  self.old={n:('old '+n).encode()for n in H.ASSETS};self.new={n:('new '+n).encode()for n in H.CHANGED}
  for n,b in self.old.items():(self.target/n).write_bytes(b)
  for n,b in self.new.items():(self.bundle/n).write_bytes(b)
  (self.bundle/'install_aura_static_hotfix.py').write_bytes((REPO/'Server/nakama/deploy/install_aura_static_hotfix.py').read_bytes())
  m={'schema':1,'version':'0.3.14','bundleId':self.bundle.name,'baseAssets':{n:H.digest(b)for n,b in self.old.items()},'newAssets':{n:H.digest(b)for n,b in self.new.items()},'files':{p.name:H.digest(p.read_bytes())for p in self.bundle.iterdir()}}
  (self.bundle/'manifest.json').write_text(json.dumps(m))
  self.stack=contextlib.ExitStack();self.addCleanup(self.stack.close)
  for k,v in [('ROOT',self.root),('TARGET',self.target),('__file__',str(self.bundle/'install_aura_static_hotfix.py'))]:self.stack.enter_context(patch.object(H,k,v))
  self.stack.enter_context(patch.object(H,'permissions',side_effect=lambda p:0o755 if p.is_dir()else 0o644))
  self.stack.enter_context(patch.object(H.stat,'S_IMODE',return_value=0o700))
  # load() checks private directory mode directly; this platform-independent
  # fixture mocks only that bit check, while actual deployment checks POSIX bits.
  original_load=H.load
  def load(bundle):
   if os.name=='nt':
    # Path.stat itself is left real for stable directory inode verification.
    saved=H.load;H.load=original_load
    try:
     with patch.object(Path,'stat',autospec=True,side_effect=self.stat_private):return original_load(bundle)
    finally:H.load=saved
   return original_load(bundle)
  self.original_stat=Path.stat;self.stack.enter_context(patch.object(H,'load',side_effect=load))
  self.public=self.stack.enter_context(patch.object(H,'verify_public',return_value={'assets':4,'exactSha256':True}))
  previous=Path.cwd();os.chdir(self.bundle);self.addCleanup(os.chdir,previous)
 def stat_private(self,path,*args,**kwargs):
  st=self.original_stat(path,*args,**kwargs)
  if path==self.bundle:
   data=list(st);data[0]=(data[0]&~0o777)|0o700;return os.stat_result(data)
  return st
 def run_main(self,apply=False):
  with patch.object(sys,'argv',['hotfix']+(['--apply']if apply else[])),contextlib.redirect_stdout(io.StringIO()):H.main()
 def test_default_preflight_no_writes(self):
  self.run_main();self.assertFalse((self.root/'backups').exists());self.assertEqual({n:(self.target/n).read_bytes()for n in H.ASSETS},self.old)
 def test_changed_baseline_refuses_before_backup(self):
  (self.target/'aura-model.js').write_text('operator')
  with self.assertRaises(H.HotfixError):self.run_main(True)
  self.assertFalse((self.root/'backups').exists())
 def test_css_drift_refuses_and_is_never_written(self):
  (self.target/'aura.css').write_text('operator css')
  with self.assertRaises(H.HotfixError):self.run_main(True)
  self.assertEqual((self.target/'aura.css').read_text(),'operator css')
 def test_apply_three_files_preserves_directory_inode_and_css(self):
  inode=self.target.stat().st_ino;self.run_main(True)
  self.assertEqual(self.target.stat().st_ino,inode);self.assertEqual((self.target/'aura.css').read_bytes(),self.old['aura.css'])
  for n,b in self.new.items():self.assertEqual((self.target/n).read_bytes(),b)
  backup=next((self.root/'backups').iterdir())
  for n in H.CHANGED:self.assertEqual((backup/n).read_bytes(),self.old[n])
  self.assertTrue((backup/'result.json').is_file())
 def test_repeat_after_lost_output_is_read_only_already_applied(self):
  self.run_main(True);backups=list((self.root/'backups').iterdir());self.run_main(True);self.assertEqual(list((self.root/'backups').iterdir()),backups)
 def test_public_failure_restores_only_known_written_assets(self):
  self.public.side_effect=[{},H.HotfixError('post-check failed')]
  with self.assertRaises(H.HotfixError):self.run_main(True)
  for n,b in self.old.items():self.assertEqual((self.target/n).read_bytes(),b)
  self.assertTrue(next((self.root/'backups').glob('*/failure.json')).is_file())
 def test_foreign_edit_is_preserved_during_rollback(self):
  original_atomic=H.atomic;calls=0
  def atomic(path,data,mode):
   nonlocal calls
   original_atomic(path,data,mode);calls+=1
   if calls==1:(self.target/path.name).write_bytes(b'operator-after-write')
  with patch.object(H,'atomic',side_effect=atomic),self.assertRaises(H.HotfixError):self.run_main(True)
  first=sorted(H.CHANGED)[0];self.assertEqual((self.target/first).read_bytes(),b'operator-after-write')
  failure=json.loads(next((self.root/'backups').glob('*/failure.json')).read_text());self.assertIn(first,failure['foreignChangesPreserved'])
 def test_extra_or_tampered_payload_refused(self):
  (self.bundle/'extra').write_text('extra')
  with self.assertRaises(H.HotfixError):self.run_main(True)
  (self.bundle/'extra').unlink();(self.bundle/'index.html').write_text('tampered')
  with self.assertRaises(H.HotfixError):self.run_main(True)
 def test_missing_written_asset_does_not_interrupt_other_rollbacks(self):
  calls=0;removed=sorted(H.CHANGED)[1]
  def public(_):
   nonlocal calls
   calls+=1
   if calls==2:
    (self.target/removed).unlink();raise H.HotfixError('post-check failed after operator deletion')
   return{}
  self.public.side_effect=public
  with self.assertRaises(H.HotfixError):self.run_main(True)
  self.assertFalse((self.target/removed).exists())
  for name in H.CHANGED-{removed}:self.assertEqual((self.target/name).read_bytes(),self.old[name])
  failure=json.loads(next((self.root/'backups').glob('*/failure.json')).read_text());self.assertIn(removed,failure['foreignChangesPreserved']);self.assertEqual(len(failure['restored']),2)
 def test_atomic_refuses_css_or_outside_folder(self):
  for p in [self.target/'aura.css',self.root/'outside.js']:
   with self.assertRaises(H.HotfixError):H.atomic(p,b'bad',0o644)
if __name__=='__main__':unittest.main()
