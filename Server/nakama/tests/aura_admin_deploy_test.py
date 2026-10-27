"""Offline real-helper tests. Docker/HTTPS are mocked; no production actions."""
from pathlib import Path
import contextlib,hashlib,importlib.util,io,json,os,sys,tempfile,unittest
from unittest.mock import patch
REPO=Path(__file__).resolve().parents[3]
spec=importlib.util.spec_from_file_location('aura_install',REPO/'Server/nakama/deploy/install_aura_admin.py')
install=importlib.util.module_from_spec(spec);spec.loader.exec_module(install)
class Tests(unittest.TestCase):
 def setUp(self):
  scratch=REPO/'.cache/qa/aura-admin';scratch.mkdir(parents=True,exist_ok=True)
  self.temp=tempfile.TemporaryDirectory(dir=scratch);self.addCleanup(self.temp.cleanup)
  self.root=Path(self.temp.name).resolve();self.bundle=self.root/'aura-admin0314-aaaaaaaaaaaa';self.bundle.mkdir()
  # Exercise the real installer with repository public assets and generated
  # config bytes; no private historical deployment directory is required.
  for name in install.ASSETS:
   (self.bundle/name).write_bytes((REPO/'Server/nakama/admin/aura'/name).read_bytes())
  (self.bundle/'install_aura_admin.py').write_bytes((REPO/'Server/nakama/deploy/install_aura_admin.py').read_bytes())
  for name in install.CONFIGS:
   baseline=(REPO/'Server/nakama/deploy'/name).read_bytes()
   (self.bundle/('baseline-'+name)).write_bytes(baseline)
   (self.bundle/('updated-'+name)).write_bytes(baseline+b'\n# reviewed offline fixture update\n')
  self.files={p.name:install.digest(p.read_bytes())for p in self.bundle.iterdir()}
  (self.bundle/'manifest.json').write_text(json.dumps({'schema':1,'bundleId':self.bundle.name,'files':self.files}))
  for name in install.CONFIGS:(self.root/name).write_bytes((self.bundle/('baseline-'+name)).read_bytes())
  self.before={n:(self.root/n).read_bytes()for n in install.CONFIGS};self.calls=[];self.fail_test=False
  def compose(*args):
   self.calls.append(args)
   if args==('config','--format','json'):return json.dumps({'name':'escape','services':{'api-gateway':{'image':'nginx:1.30.6-alpine'}}})
   if self.fail_test and args[:1]==('run',):self.fail_test=False;raise install.InstallError('test candidate rejected')
   return ''
  self.stack=contextlib.ExitStack();self.addCleanup(self.stack.close)
  self.stack.enter_context(patch.object(install,'ROOT',self.root))
  self.stack.enter_context(patch.object(install,'__file__',str(self.bundle/'install_aura_admin.py')))
  self.stack.enter_context(patch.object(install,'compose',side_effect=compose))
  self.stack.enter_context(patch.object(install.stat,'S_IMODE',return_value=0o700))
  self.public=self.stack.enter_context(patch.object(install,'verify_public',return_value={'assets':4,'auraStorageWrites':0}))
  previous=Path.cwd();os.chdir(self.bundle);self.addCleanup(os.chdir,previous)
 def main(self,apply=False):
  with patch.object(sys,'argv',['installer']+(['--apply']if apply else[])),contextlib.redirect_stdout(io.StringIO()):install.main()
 def test_default_preflight_does_not_write_or_restart(self):
  self.main();self.assertFalse((self.root/'backups').exists());self.assertFalse((self.root/'admin').exists());self.assertEqual(self.calls,[('config','--format','json')])
 def test_baseline_mismatch_refuses_before_backup_or_mutation(self):
  (self.root/'compose.yml').write_text('operator edit')
  with self.assertRaises(install.InstallError):self.main(True)
  self.assertFalse((self.root/'backups').exists());self.assertFalse(self.calls)
 def test_payload_hash_mismatch_refuses(self):
  (self.bundle/'aura-model.js').write_text('tampered')
  with self.assertRaises(install.InstallError):self.main(True)
  self.assertFalse(self.calls)
 def test_extra_bundle_file_refused(self):
  (self.bundle/'unexpected.txt').write_text('extra')
  with self.assertRaises(install.InstallError):self.main(True)
 def test_success_exact_backups_candidate_check_and_gateway_only(self):
  self.main(True)
  for name in install.CONFIGS:self.assertEqual((self.root/name).read_bytes(),(self.bundle/('updated-'+name)).read_bytes())
  for name in install.ASSETS:self.assertEqual((self.root/'admin/aura'/name).read_bytes(),(self.bundle/name).read_bytes())
  backup=next((self.root/'backups').iterdir());saved=json.loads((backup/'before.json').read_text())
  for entry in saved['files']:
   if entry['existed']:self.assertEqual(install.digest((backup/entry['snapshot']).read_bytes()),entry['sha256'])
  self.assertTrue((backup/'result.json').is_file());self.assertIn(('run','--rm','--no-deps','-T','api-gateway','nginx','-t'),self.calls)
  self.assertEqual(sum(args[:1]==('up',)for args in self.calls),1);self.public.assert_called_once()
  self.assertFalse(any('nakama'in args or 'postgres'in args for args in self.calls))
 def test_candidate_nginx_failure_rolls_back_exact_files(self):
  self.fail_test=True
  with self.assertRaises(install.InstallError):self.main(True)
  for name,data in self.before.items():self.assertEqual((self.root/name).read_bytes(),data)
  for name in install.ASSETS:self.assertFalse((self.root/'admin/aura'/name).exists())
  backup=next((self.root/'backups').iterdir());self.assertTrue(json.loads((backup/'failure.json').read_text())['rolledBack']);self.public.assert_not_called()
 def test_public_failure_restores_existing_assets_and_configs(self):
  target=self.root/'admin/aura';target.mkdir(parents=True)
  for name in install.ASSETS:(target/name).write_text('old '+name)
  self.public.side_effect=install.InstallError('public check failed')
  with self.assertRaises(install.InstallError):self.main(True)
  for name in install.ASSETS:self.assertEqual((target/name).read_text(),'old '+name)
  for name,data in self.before.items():self.assertEqual((self.root/name).read_bytes(),data)
  self.assertEqual(sum(args[:1]==('up',)for args in self.calls),2)
 def test_paths_outside_escape_refused(self):
  for path in (self.root.parent/'outside',self.root/'../outside'):
   with self.assertRaises(install.InstallError):install.safe(path,False)
 @unittest.skipUnless(os.name=='posix','Real POSIX mode semantics are tested in server-local preflight, not Windows ACLs')
 def test_atomic_requested_modes_survive_private_umask(self):
  previous=os.umask(0o077)
  try:
   target=self.root/'admin/aura/index.html';install.atomic(target,b'public',0o644)
   self.assertEqual(target.parent.stat().st_mode&0o777,0o755)
   self.assertEqual(target.stat().st_mode&0o777,0o644)
   config=self.root/'test-config';install.atomic(config,b'config',0o640)
   self.assertEqual(config.stat().st_mode&0o777,0o640)
  finally:os.umask(previous)
if __name__=='__main__':unittest.main()
