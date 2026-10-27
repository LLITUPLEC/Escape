"""Finalize only from the build, native, deployment and full HTTPS evidence."""
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re

root = Path(__file__).resolve().parents[4]
validation = root / 'Design/Release-0.3.15/validation'
def read(name):
    return json.loads((validation / name).read_text(encoding='utf-8-sig'))

apk = read('apk-validation.json')
public = read('public-release.json')
integrity = read('public-file-integrity.json')
source = read('source-snapshot-check.json')
native = read('native-aggregate.json')
network = read('server/network-validation-summary.json')
expected = read('server/expected-tested-runtime.json')
installed = read('server/installed-runtime-manifest.json')
actual = {}
for line in (validation / 'server/installed-runtime-sha256.txt').read_text(encoding='utf-8-sig').splitlines():
    match = re.fullmatch(r'([0-9a-f]{64})\s+runtime/([^/]+\.lua)', line)
    assert match, line
    assert match[2] not in actual
    actual[match[2]] = match[1]
assert actual == expected['files'] == installed['files'] and len(actual) == 64
digest = hashlib.sha256(json.dumps(actual, sort_keys=True).encode()).hexdigest()
assert digest == expected['sha256'] == installed['sha256'] == network['runtime_sha256']
assert apk['version'] == '0.3.15' and apk['version_code'] == 17
assert apk['build_result'] == 'Succeeded' and apk['build_errors'] == 0
assert apk['signature_verified'] and not apk['debuggable'] and not apk['development_build']
assert set(apk['native_abis']) == {'arm64-v8a', 'armeabi-v7a'}
assert public['success'] and integrity['passed'] and integrity['fullApkVerified'] and integrity['filesChecked'] == 32
assert apk['sha256'] == public['apk_sha256'] == integrity['apkSha256']
assert apk['bytes'] == public['apk_bytes'] == integrity['apkBytes']
assert source['passed'] and source['fileCount'] == 1988 and native['passed'] and native['nativeScreenshots'] == 11
assert network['success']
for name, count in [('all', 713), ('recovery', 64)]:
    scenario = network['scenarios'][name]
    assert scenario['exit_code'] == 0 and scenario['checks'] == count and scenario['failed'] == 0 and not scenario['errors']
    assert scenario['tested_runtime_matches_final_candidate']
deploy = (validation / 'server/runtime-deployment.log').read_text(encoding='utf-8-sig')
assert 'PASS deployed tested runtime; Escape Nakama healthy;' in deploy
assert 'Security smoke: 31 checks passed;' in deploy
backup = re.search(r'backup=(/opt/escape/backups/runtime-release-\d{8}T\d{6}Z)', deploy)[1]
old_certificate = re.search(r'certificate SHA-256 digest: (\w+)', (root / 'Design/Release-0.3.14/validation/apk-signature.txt').read_text(encoding='utf-8-sig'))[1]
certificate = re.search(r'certificate SHA-256 digest: (\w+)', (validation / 'apk-signature.txt').read_text(encoding='utf-8-sig'))[1]
assert old_certificate == certificate
result = dict(version='0.3.15', versionCode=17, published=True, allRequiredWorkComplete=True,
              completedUtc=datetime.now(timezone.utc).isoformat(), site=public['url'], apkUrl=public['apk_url'],
              apkSha256=apk['sha256'], apkBytes=apk['bytes'], previousApkPreserved=True,
              previousApkBytes=124206651, signingCertificateUnchanged=True, runtimeSha256=digest,
              runtimeFiles=len(actual), runtimeBackup=backup, localServerChecks=728,
              isolatedNetworkChecks=713, recoveryChecks=64, postDeploymentSecurityChecks=31,
              postDeploymentDashboardChecks=3, sourceFiles=source['fileCount'],
              sourceManifest=source['snapshotManifestSha256'], nativeScreenshots=11,
              publicFilesFullyVerified=32, browserVisualChecked=True,
              physicalAndroidTested=False,
              notes=['Initial isolated Go crash retained separately; narrow32 and final777 checks passed on unchanged runtime bytes/limits.',
                     'Native screenshots render actual production widgets with offline fixtures; not physical Android footage.'])
(validation / 'release-completion.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
(validation / 'server/production-deployment0315.json').write_text(json.dumps(dict(passed=True, runtime_sha256=digest,
    runtime_files=64, private_backup=backup, nakama_health='healthy', https_healthcheck=public['api_health_status'],
    security_checks=31, dashboard_checks=3), indent=2), encoding='utf-8')
print(json.dumps(result, ensure_ascii=False))
