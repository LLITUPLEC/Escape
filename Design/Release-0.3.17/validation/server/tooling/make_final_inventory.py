"""Emit a read-only final inventory job; this producer never contacts a server."""
from pathlib import Path
import argparse,base64,json,sys
root=Path.cwd().resolve();parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--require-clean-stages',action='store_true');args=parser.parse_args()
def read(name):return json.loads((root/name).read_text(encoding='utf-8-sig'))
payload=dict(BASELINE=read('Design/Release-0.3.17/validation/server/publication-baseline.json'),
 DEPLOY_PROOF=read('Design/Release-0.3.17/validation/server/backend-apply-proof.json'),
 PLAN=read('.cache/release-work/publication/escape0317-c2177bdcec304a9d95bbc2ebd04a3a58-plan.json'),REQUIRE_CLEAN_STAGES=args.require_clean_stages)
assert payload['DEPLOY_PROOF']['passed']is True and payload['BASELINE']['runtime']['sha256']==payload['DEPLOY_PROOF']['runtimeSha256']
assert read('Design/Release-0.3.17/validation/server/retention.json')['retentionApplied']is True
encoded=base64.b64encode(json.dumps(payload).encode()).decode()
sys.stdout.write("import base64,json\nglobals().update(json.loads(base64.b64decode('"+encoded+"')))\n")
sys.stdout.write((Path(__file__).parent/'final_inventory_remote.py').read_text(encoding='utf-8-sig'))
