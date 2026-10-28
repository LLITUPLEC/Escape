# Prepared0.3.17VPS orchestration

The backend was explicitly approved and applied at 2026-10-28 18:11 UTC;
`backend-apply-proof.json` and its canonical log record the outcome. Static
publication was separately approved and completed at 18:15:40 UTC. Full HTTPS
verification passed all 32 files, then exact retention and backend-stage cleanup
completed; `final-inventory.json` confirms 0.3.17/0.3.16, two verified backups,
healthy containers and no owned staging or disposable Docker resources. No new
optional production smoke was started; the canonical 35 and isolated 902 checks
are preserved. The first
candidate `release0317-validation-41d30b908a76` / `d8ccf679...` is obsolete for
deployment; its all-suite proof may remain useful, but it is not an approved
final runtime. Wait for root's new final stage, full manifest and explicit apply
signal.

The current final-input check uses `release0317-validation-0fa70c111d27` and
`0c16126510fe0ca50b8f99a72f5cbc6a3a4e71b6e3a3412063e452c4f78058fe`
(68 files including the preserved seed). It reads only
`candidate-ready-network-final.json` plus its exact 830-case Lua source-hash report;
the original `candidate-ready.json` is rejected. Local/context/archive inputs
have passed. The final remote read-only dryrun and guarded canonical apply both
passed; protected catalog/config/admin and website/APK bytes were unchanged.
The actual composed evidence passed local gate validation: target32 completed in
`release0317-validation-d4673ded3afd`, while all806/recovery64 completed in the
current stage. The old whole-run failure and the original two-group success are
preserved byte for byte. The producer independently compares both candidate
archives: their runtime/catalog/helper inputs are equal; only the unrelated
`Suite.consumables` fixture differs; every other method's source and the remaining
module AST match. The gate requires exact hashes for all raw reports, summary,
exit and equivalence files, and exact equality of the complete runtime files
including the production seed. Seventeen synthetic adversarial/completion checks
also pass. No remote execution is implied by these local checks.

`make_baseline_check.py` emits a read-only server check against the immutable
`Design/Release-0.3.17/validation/server/preflight.json`. It does not inspect an
unfinished candidate. The actual check passed with AAD66, the existing publisher,
all static config/catalog/admin/web pins and idle zero counts.

`make_backend_job.py --stage <FINAL> --candidate-sha256 <FINAL_SHA> --mode dryrun`
checks local source/ready/context coherence first, then emits the pinned remote
read-only canonical deployment preflight. An obsolete context or missing current
source proof is rejected before any remote script is printed. Apply additionally
requires `--mode apply --approved-apply`; invoke it only after root's explicit
signal. The emitted apply launcher runs detached under the exact owned stage and
records `backend-orchestration.log` / `backend-orchestration-exit.json`.

Always collect a producer's output and check its local exit code before piping it
to SSH. A direct producer-to-SSH pipeline could hide a producer failure behind a
successful empty SSH process. Use `python3 -B -` remotely to avoid bytecode cache
writes during read-only checks. Never interpolate source JSON into a shell
command; the producers encode it and send Python over stdin.

The backend guard requires the final `all`, `reconnect-0317` and `recovery`
proofs in `network-validation-composed.json` with matching complete runtime/seed pins and an unchanged production
baseline. It checks the canonical `ff499e1a...` deployer and an idle server.
There are no catalog migration or forced-active-game arguments. The old pinned
security smoke source is backed up privately and cross-checked against the
existing verified AAD snapshot before its atomic update. The canonical deployer
then takes a fresh DB/runtime/config/admin/tools backup, installs the tested
runtime, restarts Nakama only and runs its updated security/dashboard checks.
Its own error path owns runtime rollback without restoring the database. The
wrapper restores the old smoke helper if the canonical deploy failed; a later
proof-write failure preserves an already healthy new runtime and its matching
smoke helper for inspection.

Successful deployment adds the old smoke source to the new rollback snapshot's
manifest, verifies protected data again and writes `backend-apply-proof.json`
and `publication-baseline.json`. Download those to the release's server evidence
directory before the next phase. The baseline contains the new runtime and smoke
pins while preserving the current0.3.16 website/catalog/config/admin state.

After the validated APK/plan and root's separate static publication signal,
`make_publication_job.py backup --approved-publication` emits the fresh private
new-runtime DB/source/config backup. It verifies the full manifest and
`pg_restore --list`, and does not restart API or DB. `launch` requires the plan,
explicit APK SHA and `--approved-publication`; its launcher checks the fresh
snapshot is under five minutes old before invoking the pinned generic static
installer. Upload only the plan's exact owned paths. Keep0.3.16 and0.3.15 files
until root confirms all full HTTPS byte/SHA checks for0.3.17.

`make_publication_job.py verify --plan <PLAN> --expected-apk-sha256 <SHA>` uses
root's `public-file-integrity.json` and the additional `public-release.json`
checks. `cleanup` also requires `--approved-retention`, the archived publication
log/exit/fresh-backup manifest, and a passing pre-retention verification. It
preserves0.3.17/0.3.16 web+APK and two verified snapshots: the current new-runtime
pre-publication backup and the fresh AAD rollback from this backend apply. It
removes only the preflight-pinned0.3.15 web/APK, the two superseded original
backups (old AAD0.3.16 and C3), and the exact owned static staging paths. Unknown
or concurrent changes fail closed.

Backend validation stages/overlays and duplicated test reports are a separate
final exact-namespace cleanup. Only remove them after items/root archive the
necessary sanitized proof and supply their exact stage/context/file hashes.
Do not delete arbitrary `release0317-*` matches or Docker volumes. Verify no
disposable test containers, networks, volumes or directories remain; other
projects are out of scope.
