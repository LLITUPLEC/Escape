# Static publication and VPS retention

Published 0.3.16/code18 on 2026-10-28 at 04:44:11 UTC. Current website: `https://prokrust-play.ru/escape/`. APK: `Escape-0.3.16-3f40a293713f.apk`, 124355967 bytes, SHA256 `3f40a293713fa7689ca54929f597201cd5f0c9ca7a6a304be46bd95b7810e847`.

The approved 32-file archive and publisher were hash checked before publication. A fresh private DB/runtime/source/config backup was created first, its complete file manifest was verified and `pg_restore --list` passed. The static publisher then atomically switched `current`, preserving the previous release. Root independently verified all 32 full HTTPS responses against local bytes/SHA256, including the entire APK; the additional public-release checker passed all eight checks.

After those proofs passed, exact retention removed only the verified 0.3.14 website directory, APK and old rollback backup, plus this publication's eleven owned staging paths. Both 0.3.16 and 0.3.15 website directories and APKs remain. The previous APK is unchanged: `Escape-0.3.15-e7ac556941de.apk`, 123571699 bytes, SHA256 `e7ac556941debbe4295b14cfd298f7a278d4da8ffb97ee8e89d59c954f6b83c8`.

Exactly two verified backups remain:

- `/opt/escape/backups/pre-publication-0.3.16-20261028T044329Z`: current AAD runtime, DB and deployment source/config context; 94 manifested files; dump SHA256 `0b77d53c93ded9bd3cf0195cc6a1800aebcf9d456d2e6cc87e845f24b8e22d2a`.
- `/opt/escape/backups/runtime-release-20261027T223730Z`: preserved previous C3 pre-change snapshot; 82 manifested files; dump SHA256 `94a20e997ea1d8ff63184a0be1ce939038b14bbbbc12d5325d0277a9cb273a91`.

The game backend was preserved byte for byte: runtime SHA256 `aad128d4e8b67dc8b423e6725620c89fee22519e43f727fa1fcfa2b59ed36653`, 66 files, generated seed unchanged. Item/boss/shop/aura configuration versions and hashes, private project configuration and administrator files also remain unchanged. Nakama and Postgres container IDs, images, start times and restart counts match the preflight; both are healthy. No API/database restart, runtime deployment or catalog mutation occurred. No other project or Docker volume was touched. `/opt/escape/page-staging` is empty.

Selected proof:

- `final-inventory.json`: final release/APK identities, both backups, protected hashes, catalog versions and container identity/health.
- `retention-result.json`: exact removed paths and retained pair after full verification.
- `fresh-backup.json` and `fresh-backup-manifest.json`: hash-only backup verification; private DB/config bytes were not downloaded.
- `publication-plan.json`, `publication.log` and `publication-exit.json`: approved pins and successful atomic publication.
- `../public-file-integrity.json`: root's full HTTPS 32/32 verification.
- `../public-release.json`: eight additional public release/API checks.
- `tooling/`: selected one-off orchestration sources, exact publisher/installer sources and syntax/hash evidence.

Preflight and historical reports for 0.3.15/0.3.14 were preserved. No Git operations were performed by the VPS publication agent.
