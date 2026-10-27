"""Validate a local public bundle and prepare an upload archive; no network actions."""
import argparse
from datetime import datetime, timezone
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import tarfile
import uuid

ROOT = Path(__file__).resolve().parents[2]

def sha(path):
    digest = hashlib.sha256()
    with path.open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''): digest.update(chunk)
    return digest.hexdigest()

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--version', default='0.3.15')
    parser.add_argument('--version-code', type=int, default=17)
    parser.add_argument('--dist', type=Path, default=ROOT / 'Website/escape/dist')
    parser.add_argument('--validation-dir', type=Path)
    parser.add_argument('--screens-dir', type=Path)
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.cache/release-work/publication')
    parser.add_argument('--expected-previous-publisher-sha256', required=True)
    parser.add_argument('--previous-apk-filename', required=True)
    parser.add_argument('--previous-apk-bytes', type=int, required=True)
    parser.add_argument('--previous-apk-sha256', required=True)
    args = parser.parse_args()
    assert re.fullmatch(r'[0-9]+\.[0-9]+\.[0-9]+', args.version)
    assert args.version_code > 0 and args.previous_apk_bytes > 0
    for digest in (args.expected_previous_publisher_sha256, args.previous_apk_sha256):
        assert re.fullmatch(r'[a-f0-9]{64}', digest), 'Expected lowercase SHA256'
    assert re.fullmatch(r'Escape-[0-9]+\.[0-9]+\.[0-9]+-[a-f0-9]{12}\.apk', args.previous_apk_filename)
    assert args.previous_apk_filename.endswith('-' + args.previous_apk_sha256[:12] + '.apk')
    dist = args.dist.resolve(strict=True)
    validation_dir = args.validation_dir or ROOT / ('Design/Release-' + args.version) / 'validation'
    screens = args.screens_dir or ROOT / ('Design/Release-' + args.version) / 'screens'
    helper = ROOT / 'Server/nakama/deploy/deploy_download_page.py'
    spec = importlib.util.spec_from_file_location('escape_page_publisher', helper)
    publisher = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(publisher)
    release, apk_name, apk, audio = publisher.validate_bundle(dist)
    validation = json.loads((validation_dir / 'apk-validation.json').read_text(encoding='utf-8-sig'))
    assert release['version'] == validation['version'] == args.version and validation['version_code'] == args.version_code
    assert validation['build_result'] == 'Succeeded' and validation['development_build'] is False
    assert validation['signature_verified'] is True and validation['debuggable'] is False
    assert set(validation['native_abis']) == {'arm64-v8a', 'armeabi-v7a'}
    assert validation['sha256'] == release['sha256'] and validation['bytes'] == release['bytes']
    source = json.loads((validation_dir / 'source-snapshot-check.json').read_text(encoding='utf-8-sig'))
    assert source['passed'] is True and source['version'] == args.version, 'Final client snapshot must match before packaging'
    for name in ('hub', 'character', 'mine'):
        assert sha(dist / f'assets/{name}.png') == sha(screens / f'{name}.png')
    identifier = 'escape' + args.version.replace('.', '') + '-' + uuid.uuid4().hex
    output = args.output_dir.resolve()
    assert output.is_relative_to(ROOT) and not output.is_symlink()
    output.mkdir(parents=True, exist_ok=True)
    archive = output / (identifier + '.tar.gz')
    files = sorted(path for path in dist.rglob('*') if path.is_file())
    with archive.open('xb') as stream, tarfile.open(fileobj=stream, mode='w:gz') as bundle:
        for path in files:
            assert not path.is_symlink() and path.resolve().is_relative_to(dist)
            info = bundle.gettarinfo(str(path), arcname=path.relative_to(dist).as_posix())
            assert info.isfile() and info.name and not info.name.startswith('/') and '..' not in Path(info.name).parts
            info.uid = info.gid = 0
            info.uname = info.gname = ''
            info.mode = 0o644
            with path.open('rb') as contents: bundle.addfile(info, contents)
    remote = '/opt/escape/page-staging/' + identifier
    plan = dict(version=args.version, versionCode=args.version_code, preparedUtc=datetime.now(timezone.utc).isoformat(), published=False,
        identifier=identifier, archive=str(archive), archiveSha256=sha(archive), archiveBytes=archive.stat().st_size,
        memberCount=len(files), publisher=str(helper), publisherSha256=sha(helper),
        expectedPreviousPublisherSha256=args.expected_previous_publisher_sha256,
        sourceSnapshotManifestSha256=source['snapshotManifestSha256'],
        previousApkFilename=args.previous_apk_filename, previousApkBytes=args.previous_apk_bytes,
        previousApkSha256=args.previous_apk_sha256, remoteStage=remote,
        remoteArchive=remote + '.tar.gz', remotePublisher=remote + '-publisher.py', remotePlan=remote + '-plan.json',
        apkFilename=apk_name, apkBytes=release['bytes'], apkSha256=release['sha256'], selectedAudioFiles=len(audio))
    plan_path = output / (identifier + '-plan.json')
    with plan_path.open('x', encoding='utf-8') as stream: json.dump(plan, stream, ensure_ascii=False, indent=2)
    print(json.dumps(dict(plan= str(plan_path), **plan), ensure_ascii=False))

if __name__ == '__main__': main()
