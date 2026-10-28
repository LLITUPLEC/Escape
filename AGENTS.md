# Project working rules

- Keep source code, catalogs, migrations, reusable tests and tools. Put reproducible build/test output in ignored `.cache/` or `Builds/<version>/`, not at the project root.
- Keep only the latest and the immediately previous successfully published releases, their APKs/symbols, website release directories and two verified VPS backups. Before removing an older artifact, verify the retained files and preserve any still-required source inputs. Git commit history is retained.
- Local edits and previews do not constitute a published release. Do not bump the version or remove the previous published release until the user requests the next publication. The current retained pair is 0.3.16 and 0.3.15; update that fact when the next release is actually published.
- Preserve historical release reports. New development QA output belongs in `.cache/qa/`; durable design/balance decisions belong in `Docs/` and selected evidence can live in `Design/Validation-<date>/`.
- Respect the user's publication scope: backend deployment, website publication and APK builds are separate operations. Never infer a new APK/version from a server-only update.
- Keep credentials, connection configuration, keystores and deployment secrets outside Git. VPS cleanup is restricted to Escape paths and must not touch other projects or Docker volumes.
