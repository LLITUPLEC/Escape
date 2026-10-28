"""Emit only a read-only current production check, independently of any non-final candidate stage."""
from pathlib import Path
import base64,json,sys
root=Path.cwd();baseline=json.loads((root/'Design/Release-0.3.17/validation/server/preflight.json').read_text(encoding='utf-8-sig'))
encoded=base64.b64encode(json.dumps(baseline).encode()).decode()
source=(Path(__file__).parent/'backend_orchestrator.py').read_text()
helpers=source[source.index('def sha(p):'):source.index('context,expected=candidate_inputs()')]
sys.stdout.write("from pathlib import Path\nfrom datetime import datetime,timezone\nimport base64,hashlib,importlib.util,json,subprocess,io,contextlib\nROOT=Path('/opt/escape')\nBASELINE=json.loads(base64.b64decode('"+encoded+"'))\n")
sys.stdout.write(helpers)
sys.stdout.write('''
check_production();containers=container_states()
spec=importlib.util.spec_from_file_location('deployer',ROOT/'deploy_runtime.py');deployer=importlib.util.module_from_spec(spec);spec.loader.exec_module(deployer)
spec=importlib.util.spec_from_file_location('idle_admin',ROOT/'tools/storage_admin.py');admin=importlib.util.module_from_spec(spec);spec.loader.exec_module(admin)
config=json.loads((ROOT/'secrets/server.yml').read_text());console=admin.Console('http://127.0.0.1:27351');capture=io.StringIO();idle=False
try:
 console.login(config['console']['username'],config['console']['password'])
 try:
  with contextlib.redirect_stdout(capture):deployer.check_idle(console,admin.SYSTEM_USER_ID,False)
  idle=True
 except RuntimeError:pass
finally:console.close()
print(json.dumps(dict(passed=True,readOnly=True,checkedUtc=datetime.now(timezone.utc).isoformat(),runtimeSha256=BASELINE['runtime']['sha256'],runtimeFiles=len(BASELINE['runtime']['files']),
 publisherSha256=BASELINE['publisher']['sha256'],securitySmokeSha256=BASELINE['canonicalHelpers']['security_smoke.py'],canonicalDeployerSha256=BASELINE['canonicalHelpers']['deploy_runtime.py'],
 protectedConfigAdminCatalogAndWebUnchanged=True,containers=containers,idle=idle,idleSummary=capture.getvalue().strip(),productionChanged=False,candidateNotYetApproved=True),indent=2))
''')
