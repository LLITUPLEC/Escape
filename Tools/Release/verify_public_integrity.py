"""Stream every local public-bundle file over HTTPS; retain hashes, never bodies."""
import argparse
from concurrent.futures import ThreadPoolExecutor, as_completed
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re
import urllib.error
import urllib.parse
import urllib.request

root = Path(__file__).resolve().parents[2]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--version', default='0.3.18')
parser.add_argument('--version-code', type=int, default=20)
parser.add_argument('--dist', type=Path, default=root / 'Website/escape/dist')
parser.add_argument('--validation-dir', type=Path)
parser.add_argument('--report', type=Path)
parser.add_argument('--base-url', default='https://prokrust-play.ru/escape/')
args = parser.parse_args()
assert re.fullmatch(r'[0-9]+\.[0-9]+\.[0-9]+', args.version) and args.version_code > 0
dist = args.dist.resolve(strict=True)
validation_dir = args.validation_dir or root / ('Design/Release-' + args.version) / 'validation'
report_path = args.report or root / '.cache/release-work' / ('public-file-integrity-' + args.version + '.json')
base = args.base_url
origin = urllib.parse.urlsplit(base)
assert origin.scheme == 'https' and origin.netloc and origin.path.endswith('/') and not origin.username and not origin.password and not origin.query and not origin.fragment
validation = json.loads((validation_dir / 'apk-validation.json').read_text(encoding='utf-8-sig'))
assert validation['version'] == args.version and validation['version_code'] == args.version_code
assert validation['build_result'] == 'Succeeded' and validation['signature_verified'] is True
expected_apk_sha = validation['sha256']
release = json.loads((dist / 'release.json').read_text(encoding='utf-8-sig'))
assert release['version'] == args.version and release['sha256'] == expected_apk_sha and release['bytes'] == validation['bytes']

def trusted(url):
    parsed = urllib.parse.urlsplit(url)
    return parsed.scheme == origin.scheme and parsed.netloc == origin.netloc and parsed.path.startswith(origin.path) and not parsed.username and not parsed.password

class SameOriginRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, message, headers, newurl):
        if not trusted(newurl):
            raise urllib.error.HTTPError(request.full_url, code, 'Redirect left the fixed release origin', headers, fp)
        return super().redirect_request(request, fp, code, message, headers, newurl)

def file_hash(path):
    digest = hashlib.sha256()
    with path.open('rb') as source:
        for chunk in iter(lambda: source.read(1024 * 1024), b''): digest.update(chunk)
    return digest.hexdigest()

files = sorted(path for path in dist.rglob('*') if path.is_file())
assert files, 'Expected a nonempty local public bundle'
assert all(not path.is_symlink() and path.resolve().is_relative_to(dist.resolve()) for path in files)
entries = [{'path': path.relative_to(dist).as_posix(), 'expectedBytes': path.stat().st_size, 'expectedSha256': file_hash(path)} for path in files]

def verify(entry):
    row = dict(entry)
    url = base + urllib.parse.quote(entry['path'], safe='/')
    assert trusted(url)
    row.update(url=url, passed=False, actualBytes=0, actualSha256='', status=None, error='')
    digest = hashlib.sha256()
    try:
        request = urllib.request.Request(url, headers={'Accept-Encoding': 'identity', 'Cache-Control': 'no-cache', 'User-Agent': 'Escape-ReleaseIntegrity/' + args.version})
        opener = urllib.request.build_opener(SameOriginRedirect())
        with opener.open(request, timeout=45) as response:
            row['status'] = response.status
            if not trusted(response.url): raise ValueError('Unexpected final response origin')
            if response.status != 200: raise ValueError('Expected fullHTTP200 response')
            if response.headers.get('Content-Encoding', '').strip().lower() not in ('', 'identity'):
                raise ValueError('Server ignored identity content encoding')
            content_length = response.headers.get('Content-Length')
            row['contentLength'] = int(content_length) if content_length else None
            for chunk in iter(lambda: response.read(1024 * 1024), b''):
                row['actualBytes'] += len(chunk)
                digest.update(chunk)
            row['actualSha256'] = digest.hexdigest()
            row['passed'] = row['actualBytes'] == row['expectedBytes'] and row['actualSha256'] == row['expectedSha256'] and (row['contentLength'] is None or row['contentLength'] == row['expectedBytes'])
            if not row['passed']: row['error'] = 'Full response bytes or SHA256 differ from the validated local bundle'
    except Exception as error:
        row['error'] = type(error).__name__ + ': ' + str(error)
        row['actualSha256'] = digest.hexdigest()
    return row

results = []
with ThreadPoolExecutor(max_workers=4) as workers:
    futures = [workers.submit(verify, entry) for entry in entries]
    for future in as_completed(futures): results.append(future.result())
results.sort(key=lambda row: row['path'])
apk = next(row for row in results if row['path'] == 'downloads/' + release['filename'])
report = {'passed': all(row['passed'] for row in results), 'version': args.version, 'checkedUtc': datetime.now(timezone.utc).isoformat(), 'baseUrl': base, 'method': 'Full HTTP200 response streams with Accept-Encoding identity; SHA256 and byte count compared to every local public bundle file; no response bodies retained.', 'filesChecked': len(results), 'passedFiles': sum(row['passed'] for row in results), 'totalResponseBytes': sum(row['actualBytes'] for row in results), 'fullApkVerified': apk['passed'], 'apkBytes': apk['actualBytes'], 'apkSha256': apk['actualSha256'], 'responsesSaved': False, 'files': results}
report_path.parent.mkdir(parents=True, exist_ok=True)
report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps({key: value for key, value in report.items() if key != 'files'}, ensure_ascii=False), flush=True)
if not report['passed']:
    print(json.dumps({'failedFiles': [{'path': row['path'], 'error': row['error']} for row in results if not row['passed']]}, ensure_ascii=False), flush=True)
raise SystemExit(0 if report['passed'] else 1)
