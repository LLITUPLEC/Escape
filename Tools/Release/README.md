# Release tools

The reusable tools live here rather than in temporary release folders. Local
work products default to `.cache/release-work`; published release evidence under
`Design/Release-<version>/validation` is retained separately. Use `--help` (or
PowerShell `Get-Help`) before invoking an unfamiliar command.

## Android

`prepare-android-sdk.ps1` creates or verifies the owned SDK copy at
`.cache/android-sdk` from already installed Unity components. It never changes
the installed SDK, downloads packages, or accepts licenses. For a fresh machine,
pass the installed component directories explicitly if the defaults differ.

`Project.Editor.ExpeditionAndroidBuild.BuildReleaseBatch` builds to
`Builds/<bundleVersion>/`, including `build-status.json` in the same directory.
`validate-apk.ps1 -ExpectedVersion 0.3.16 -MinimumVersionCode 18` uses that status
by default. Pass `-ReportDirectory` explicitly when archiving final validation.

## Client source proof

```powershell
python Tools/Release/source_snapshot.py --version 0.3.16 --inventory
```

`--inventory` is read-only. For a new release, `--create` freezes an exclusive
snapshot after final QA, assets and screenshots exist. Running without an action
compares the live sources against that snapshot and writes its check report.
Do not recreate or update historical snapshots after cleanup. The optional
`--record-sdk-restore` action records only the precise temporary Android SDK36
to original0 whole-file restoration used by the build.

## Download page

Build the public bundle with `Website/escape/render-release.ps1` first. Then use
`prepare_publication.py`, supplying the independently verified current publisher
SHA256 and the previous published APK identity through its required arguments:

```powershell
python Tools/Release/prepare_publication.py --version 0.3.16 --version-code 18 --expected-previous-publisher-sha256 <64-hex> --previous-apk-filename <published.apk> --previous-apk-bytes <bytes> --previous-apk-sha256 <64-hex>
```

This only validates local inputs and creates a unique archive and plan. It does
not upload or publish. The plan records exact hashes, byte counts and remote
upload paths. `--dist`, `--validation-dir`, `--screens-dir` and `--output-dir`
can select a prepared release explicitly.

After separate authorization to publish, upload the plan, archive and publisher
to the exact paths in the plan. Upload `publish_page_remote.py` separately and
invoke it on the server with `--plan <remote-plan>` and the same explicit
`--expected-previous-publisher-sha256`. It validates the pins, protects the prior
APK, rejects unsafe archive paths, validates the bundle before switching the
publisher, and keeps a rollback copy. It does not restart the API or database.

`verify_public_integrity.py --version 0.3.16 --version-code 18` compares full HTTPS
response streams against every local bundle file. It writes hashes and byte
counts, never response bodies. Use `--report` to archive the final result.
`verify-public-release.ps1` performs the additional website/API release checks.

## Isolated backend validation

`prepare_backend_validation.py --version 0.3.16` creates a unique candidate
archive and context manifest from actual local modules, multiclient test tools,
canonical item data and consumable migration inputs. It does not upload or run
anything remotely. All archive members are pinned by SHA256.

For an explicitly authorized isolated server run, upload its two files under
`/opt/escape/<stage>/`, then run `validate_backend_remote.py <stage> all recovery`
from `/opt/escape`. The stage basename comes from the context manifest; no old
release stage or runtime hash is embedded in the tool. The validator uses the
existing Docker isolation harness and verifies production runtime bytes remain
unchanged. Actual deployment uses `Server/nakama/deploy/deploy_runtime.py` with
its explicit tested-manifest and production-baseline arguments.

The historical completion checker for 0.3.15 is archived under
`Design/Release-0.3.15/validation/tooling`; its fixed evidence counts describe
that completed release and are not defaults for a future release.
