from pathlib import Path
import hashlib,json,os,re,subprocess,sys
root=Path('/opt/escape');stage=root/sys.argv[1]
assert stage.parent==root and re.fullmatch(r'release0315-validation-[a-f0-9]{12}',stage.name)
assert json.loads((stage/'deployment-exit.json').read_text())['exit_code']==0
ctx=json.loads((stage/'context.json').read_text())
suite=stage/'candidate/multiclient_review.py'
assert hashlib.sha256(suite.read_bytes()).hexdigest()==ctx['manifest']['multiclient_review.py']
assert not(stage/'production-practice-smoke-exit.json').exists()
os.umask(0o077)
wrapper=stage/'run-production-smoke.py'
wrapper.write_text('''from pathlib import Path
from datetime import datetime,timezone
import importlib.util,json,sys,traceback
root=Path('/opt/escape');stage=Path(__file__).parent
spec=importlib.util.spec_from_file_location('reviewed_practice_smoke',stage/'candidate/multiclient_review.py')
module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
tracked=[];holder={};original=module.Suite.player
def player(self):
 holder['suite']=self;p=original(self);tracked.append(p['uid'])
 (stage/'production-test-account-ids.json').write_text(json.dumps(tracked))
 return p
module.Suite.player=player
sys.argv=['multiclient_review.py','--scenario','practice-smoke','--url','https://prokrust-play.ru',
 '--console-url','http://127.0.0.1:27351','--config',str(root/'secrets/server.yml'),
 '--report',str(stage/'production-practice-smoke.json')]
code=1;cleanup=[]
try:
 code=module.main()
 suite=holder['suite']
 for uid in tracked:
  status,_=suite.console('GET','/account/'+uid)
  storage_status,rows=suite.console('GET','/storage?user_id='+uid+'&limit=1')
  assert status==404 and storage_status==200 and rows.get('objects')==[], 'Temporary test account/storage still present'
  cleanup.append(dict(accountRemoved=True,userStorageRemoved=True))
 print('PASS production smoke verifies temporary account404 and no remaining owner Storage')
except BaseException:
 code=1
 (stage/'production-smoke-traceback.log').write_text(traceback.format_exc())
finally:
 (stage/'production-practice-smoke-exit.json').write_text(json.dumps(dict(exit_code=code,checkedUtc=datetime.now(timezone.utc).isoformat(),
  ownTemporaryAccounts=len(tracked),cleanup=cleanup,realPlayerAccountUsed=False,systemFixtureWrites=False)))
''')
with(stage/'production-practice-smoke.log').open('xb')as log:
 p=subprocess.Popen(['python3',str(wrapper)],cwd=root,stdout=log,stderr=subprocess.STDOUT,start_new_session=True)
print(json.dumps(dict(pid=p.pid,stage=str(stage),scenario='production-safe practice-smoke',realPlayerAccountUsed=False)))
