"""Server-local installer; upload the plan, archive and publisher, then invoke explicitly."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path, PurePosixPath
import re
import subprocess
import tarfile

def sha(path):
    digest = hashlib.sha256()
    with path.open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''): digest.update(chunk)
    return digest.hexdigest()

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--plan', type=Path, required=True)
    parser.add_argument('--expected-previous-publisher-sha256', required=True)
    args = parser.parse_args()
    parent = Path('/opt/escape/page-staging')
    assert parent.is_dir() and not parent.is_symlink()
    assert args.plan.parent == parent and not args.plan.is_symlink()
    plan = json.loads(args.plan.read_text(encoding='utf-8'))
    identifier = plan['identifier']
    version = plan['version']
    assert re.fullmatch(r'[0-9]+\.[0-9]+\.[0-9]+', version) and plan['published'] is False
    assert re.fullmatch(r'escape' + version.replace('.', '') + r'-[a-f0-9]{32}', identifier)
    stage = parent / identifier
    archive = parent / (identifier + '.tar.gz')
    uploaded = parent / (identifier + '-publisher.py')
    assert str(stage) == plan['remoteStage'] and str(archive) == plan['remoteArchive'] and str(uploaded) == plan['remotePublisher']
    assert args.plan == parent / (identifier + '-plan.json') and not stage.exists()
    assert not archive.is_symlink() and not uploaded.is_symlink()
    assert archive.stat().st_size == plan['archiveBytes'] and sha(archive) == plan['archiveSha256']
    assert sha(uploaded) == plan['publisherSha256']
    live_helper = Path('/opt/escape/deploy_download_page.py')
    expected_old = args.expected_previous_publisher_sha256
    assert re.fullmatch(r'[a-f0-9]{64}', expected_old) and plan['expectedPreviousPublisherSha256'] == expected_old
    assert not live_helper.is_symlink() and sha(live_helper) == expected_old
    previous_name = plan['previousApkFilename']
    assert re.fullmatch(r'Escape-[0-9]+\.[0-9]+\.[0-9]+-[a-f0-9]{12}\.apk', previous_name)
    assert re.fullmatch(r'[a-f0-9]{64}', plan['previousApkSha256']) and previous_name.endswith('-' + plan['previousApkSha256'][:12] + '.apk')
    previous_apk = Path('/var/www/escape/downloads') / previous_name
    assert not previous_apk.is_symlink() and previous_apk.stat().st_size == plan['previousApkBytes'] and sha(previous_apk) == plan['previousApkSha256']
    with tarfile.open(archive, 'r:gz') as bundle:
        members = bundle.getmembers()
        assert len(members) == plan['memberCount']
        names = set()
        for member in members:
            name = PurePosixPath(member.name)
            assert member.isfile() and not member.issym() and not member.islnk()
            assert member.name and not name.is_absolute() and '..' not in name.parts and chr(92) not in member.name
            assert member.name not in names
            names.add(member.name)
        stage.mkdir(mode=0o755)
        for member in members:
            destination = stage / member.name
            destination.parent.mkdir(parents=True, exist_ok=True, mode=0o755)
            with destination.open('xb') as output, bundle.extractfile(member) as source:
                for chunk in iter(lambda: source.read(1024 * 1024), b''): output.write(chunk)
            destination.chmod(0o644)
    spec = importlib.util.spec_from_file_location('escape_page_uploaded', uploaded)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    release, apk_name, apk, audio = module.validate_bundle(stage)
    assert release['version'] == version and release['sha256'] == plan['apkSha256'] and release['bytes'] == plan['apkBytes']
    assert apk_name == plan['apkFilename'] and len(audio) == plan['selectedAudioFiles']
    module.publish(stage, validate_only=True)
    assert sha(live_helper) == expected_old
    backup = parent / (identifier + '-previous-publisher.py')
    with backup.open('xb') as stream: stream.write(live_helper.read_bytes())
    assert sha(backup) == expected_old
    temporary = live_helper.with_name('.deploy_download_page-' + identifier + '.py')
    with temporary.open('xb') as stream: stream.write(uploaded.read_bytes())
    temporary.chmod(0o755)
    assert sha(temporary) == plan['publisherSha256']
    os.replace(temporary, live_helper)
    try:
        subprocess.run(['python3', str(live_helper), '--staging', str(stage)], check=True)
    except BaseException:
        rollback = live_helper.with_name('.deploy_download_page-rollback-' + identifier + '.py')
        with rollback.open('xb') as stream: stream.write(backup.read_bytes())
        rollback.chmod(0o755)
        os.replace(rollback, live_helper)
        raise
    assert previous_apk.stat().st_size == plan['previousApkBytes'] and sha(previous_apk) == plan['previousApkSha256']
    print(json.dumps(dict(state='page_installer_complete', version=version, publisherSha256=sha(live_helper),
        previousPublisherBackup=str(backup), previousApkPreserved=True, apiOrDatabaseRestarted=False)))

if __name__ == '__main__': main()
