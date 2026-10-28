"""Offline real publisher/renderer contracts; all fake media stays in an isolated fixture."""
import hashlib
import importlib.util
import json
from pathlib import Path
import shutil
import struct
import subprocess
import tempfile
import unittest
import wave
import zipfile
import zlib

ROOT = Path(__file__).resolve().parents[3]
SPEC = importlib.util.spec_from_file_location('escape_page_publisher', ROOT / 'Server/nakama/deploy/deploy_download_page.py')
PAGE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PAGE)


def fixture(staging, version='0.3.18'):
    staging.mkdir(parents=True, exist_ok=True)
    downloads = staging / 'downloads'; downloads.mkdir(exist_ok=True)
    apk = downloads / 'fixture.apk'
    with zipfile.ZipFile(apk, 'w') as bundle: bundle.writestr('AndroidManifest.xml', 'offline fixture, never install or publish')
    checksum = PAGE.sha256(apk)
    name = 'Escape-' + version + '-' + checksum[:12] + '.apk'
    apk.rename(downloads / name)
    release = dict(version=version, date='2026-10-29', filename=name, bytes=(downloads / name).stat().st_size,
                   sha256=checksum, url='./downloads/' + name, signature_verified=True, architecture=['arm64-v8a','armeabi-v7a'])
    (staging / 'release.json').write_text(json.dumps(release), encoding='utf-8')
    (staging / 'index.html').write_text('<a href="./downloads/' + name + '">offline fixture</a>', encoding='utf-8')
    audio = staging / 'audio-035'; (audio / 'selected').mkdir(parents=True, exist_ok=True)
    choices = audio / 'Escape-audio-choices-0.3.5.txt'; choices.write_text('offline choices fixture', encoding='utf-8')
    selected, files = [], []
    for i in range(19):
        identifier = 'effect_' + str(i); rel = 'selected/' + identifier + '.wav'; path = audio / rel
        with wave.open(str(path), 'wb') as stream:
            stream.setparams((1, 2, 48000, 0, 'NONE', 'not compressed')); stream.writeframes(b'\0\0' * 4)
        selected.append(dict(id=identifier, file=rel, choice='A', sha256=PAGE.sha256(path)))
        files.append(dict(file=rel, bytes=path.stat().st_size, sha256=PAGE.sha256(path)))
    (audio / 'files.json').write_text(json.dumps(files), encoding='utf-8')
    (audio / 'selected-metadata.json').write_text(json.dumps(dict(audition_version='0.3.5', selected_count=19,
       selection_sha256=PAGE.sha256(choices), selected=selected)), encoding='utf-8')
    (audio / 'index.html').write_text('<audio></audio>' + ''.join('<button data-audio="' + x['file'] + '"></button>' for x in selected), encoding='utf-8')
    return release


class PublicationContract(unittest.TestCase):
    def setUp(self):
        self.fixture_root = ROOT / '.cache/qa/publication-contract'
        self.fixture_root.mkdir(parents=True, exist_ok=True)
        self.tmp = tempfile.TemporaryDirectory(prefix='escape-page-contract-', dir=self.fixture_root)
        self.root = Path(self.tmp.name)
        self.bundle = self.root / 'bundle'; self.release = fixture(self.bundle)

    def tearDown(self):
        assert self.root.resolve().parent == self.fixture_root.resolve()
        self.tmp.cleanup()

    def manifest(self, **patch):
        value = dict(self.release, **patch)
        (self.bundle / 'release.json').write_text(json.dumps(value), encoding='utf-8')

    def test_new_semantic_release_validates_with_exact_expected_version(self):
        release, name, apk, audio = PAGE.validate_bundle(self.bundle, expected_version='0.3.18')
        self.assertEqual(release['version'], '0.3.18'); self.assertEqual(name, self.release['filename'])
        self.assertEqual(PAGE.sha256(apk), self.release['sha256']); self.assertEqual(len(audio), 19)

    def test_exact_pin_rejects_other_valid_release(self):
        with self.assertRaisesRegex(SystemExit, 'explicitly pinned'): PAGE.validate_bundle(self.bundle, expected_version='0.3.17')

    def test_previous_release_remains_valid_when_explicitly_pinned(self):
        previous = self.root / 'previous'; fixture(previous, version='0.3.17')
        self.assertEqual(PAGE.validate_bundle(previous, expected_version='0.3.17')[0]['version'], '0.3.17')

    def test_invalid_versions_and_path_injection_are_rejected(self):
        for version in [None, 18, '../0.3.18', '0.3.18/next', '0.03.18', '0.3.18-beta', '0.3.18\n']:
            with self.subTest(version=version):
                self.manifest(version=version)
                with self.assertRaisesRegex(SystemExit, 'Invalid release identity'): PAGE.validate_bundle(self.bundle)

    def test_hash_url_and_public_field_guards_remain_strict(self):
        for patch in [dict(sha256='0' * 64), dict(url='../fixture.apk'), dict(bytes=True), dict(secret='must never publish')]:
            with self.subTest(patch=list(patch)):
                self.manifest(**patch)
                with self.assertRaises(SystemExit): PAGE.validate_bundle(self.bundle, expected_version='0.3.18')

    def test_renderer_uses_new_version_and_preserves_historical_changelog(self):
        shell = shutil.which('pwsh') or shutil.which('powershell')
        if not shell: self.skipTest('PowerShell is required for the actual renderer contract')
        site = self.root / 'Website/escape'; site.mkdir(parents=True)
        for name in ['render-release.ps1','index.template.html']: shutil.copy2(ROOT / 'Website/escape' / name, site / name)
        font = self.root / 'Assets/_Project/Fonts/PTSerif'; font.mkdir(parents=True)
        for name in ['PT_Serif-Web-Regular.ttf','OFL.txt']: shutil.copy2(ROOT / 'Assets/_Project/Fonts/PTSerif' / name, font / name)
        shutil.copytree(self.bundle / 'audio-035', self.root / 'Design/Audio')
        screens = self.root / 'screens'; screens.mkdir()
        def chunk(kind, body): return struct.pack('>I',len(body)) + kind + body + struct.pack('>I',zlib.crc32(kind+body))
        png = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR',struct.pack('>IIBBBBB',240,400,8,2,0,0,0))
        png += chunk(b'IDAT',zlib.compress((b'\0'+b'\0'*720)*400)) + chunk(b'IEND',b'')
        for name in ['hub','character','mine']: (screens / (name+'.png')).write_bytes(png)
        validation = dict(self.release, version_code=20, development_build=False, debuggable=False, build_result='Succeeded',
                          native_abis=['arm64-v8a','armeabi-v7a'])
        validation_file = self.root / 'offline-apk-validation.json'; validation_file.write_text(json.dumps(validation))
        argv = [shell,'-NoProfile','-File',str(site/'render-release.ps1'),'-Version','0.3.18','-VersionCode','20',
                '-ApkPath',str(self.bundle/'downloads'/self.release['filename']),'-ScreensRoot',str(screens),'-FinalValidationJson',str(validation_file)]
        result = subprocess.run(argv, capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        rendered = (site/'dist/index.html').read_text(encoding='utf-8-sig')
        self.assertNotIn('{{',rendered); self.assertIn('Версия 0.3.18',rendered)
        self.assertIn('ПРЕДЫДУЩАЯ ВЕРСИЯ · 0.3.17',rendered)
        self.assertIn('Возвращение в бой после потери сети',rendered)
        self.assertIn('ПРЕДЫДУЩАЯ ВЕРСИЯ · 0.3.16',rendered)
        self.assertEqual(PAGE.validate_bundle(site/'dist',expected_version='0.3.18')[0]['sha256'],self.release['sha256'])


if __name__ == '__main__': unittest.main()
