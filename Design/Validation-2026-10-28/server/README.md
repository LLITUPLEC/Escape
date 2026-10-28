# Backend-only update, 2026-10-28

The authorized VPS update completed successfully. Nakama is healthy. The website, published APKs 0.3.14 and 0.3.15, app version, generated seed, protected configuration and administrator files retain their original hashes. No Git action, APK build or website publication was performed.

Deployed runtime: `aad128d4e8b67dc8b423e6725620c89fee22519e43f727fa1fcfa2b59ed36653`, 66 files including the preserved production `zz_escape_seed.lua` (`8cc9c4b7da99b8b4c1168e96a00fa68f03ecde26db876582fcfb392be4552524`). The 65 repository Lua modules and six packaging/deployment/test inputs match the pinned candidate byte for byte; see `source-deployment-coherence.json`.

The server supports repeatable, private global-boss practice with full initial HP and reset state. Practice admission, join, actions, terminal result and restart do not write player Storage or shared boss progress, rewards, inventory, energy, attempt counts, ranking, maintenance or durable rate-limit state. Owner authorization and bounded in-memory rate limits still apply. Normal paid entry remains covered separately.

The matching eight-piece equipment bonus is calculated on the server and returned consistently in profile and combat actor data. Only eight known epic `craft_ingot_n` fields were changed with one exact-version CAS; all 73 definitions, catalog version 5, administrator edits and unknown fields were preserved.

| Epic equipment | Old ingots | New ingots |
|---|---:|---:|
| Helmet | 8 | 110 |
| Chest | 25 | 270 |
| Gloves | 50 | 350 |
| Left weapon | 125 | 460 |
| Right weapon | 150 | 510 |
| Legs | 180 | 560 |
| Shoulders | 220 | 670 |
| Feet | 270 | 740 |

Crafting now checks an expected price before charging. Clients without a quote receive a readable update-required error for these changed recipes and lose no resources. Unchanged recipes retain legacy behavior. No new client binary was published.

The applied order was: fresh verified DB/runtime/config/admin/tools backup; activate the guarded runtime while prices remain old; health check and six live quote checks; CAS the eight prices; wait 31 seconds for the catalog cache; security/dashboard smoke; production practice smoke; exact hash/config/catalog/backup verification; archive sanitized evidence; remove exact owned temporary paths. `deployment-proof/final-runtime/deployment.log` records the guard passing before the catalog CAS.

| Validation | Result | Scope |
|---|---:|---|
| Local Lua | 796 named cases, all suites pass | Final source |
| Common network suite | 788 checks pass | Runtime `8cf98718…`; identical shared handlers |
| Focused lifecycle | 70 checks pass | Same shared-handler runtime |
| Practice | 20 checks pass | Final runtime `aad128d4…`; also normal paid admission |
| Equipment, actual craft debits and legacy/quoted prices | 72 checks pass | Final runtime `aad128d4…` |
| Quote preflight | 6 checks pass | Final runtime; repeated live before price CAS |
| Recovery | 64 checks pass | Final runtime |
| Production practice | 16 checks pass | Two actual games on one owned disposable account, zero durable writes |
| Production security/dashboard | 31 + 3 checks pass | After deployment |
| Python deployment/migration/harness contracts | 5 + 9 + 8 pass | `tooling/guard-unit-results.json` |

The complete 788-case suite ran on the shared-handler candidate `8cf98718b4f4b3bff2204a2a04c33f88760162ae4b66abcd27325f0895f4794e`. The final runtime differs in exactly `duel_match3_economy_rpc.lua` and the new pure `duel_workshop_quote.lua`; the final affected craft/practice/recovery scenarios above were run separately. The final harness correction affects only `Suite.quote_preflight` and preserves a missing wallet as explicit `None`; the rest of its Python AST was checked identical. These are scoped, reusable proofs, not a claim that the 788-case run used the final runtime.

Earlier failure evidence is retained: the first candidate exposed durable MatchJoin rate-budget writes and was corrected before production; a single HTTP disconnect in concurrent forfeit was followed by passing focused lifecycle and full-suite reruns, with no server crash; an initial quote-preflight fixture incorrectly required a persisted new-account wallet and was corrected without changing runtime bytes. Failed plans are marked superseded and were not applied. The final reports have no failed checks.

Exactly two server backups remain:

- `/opt/escape/backups/runtime-release-20261027T223730Z`: current pre-change snapshot, original runtime `c3a81563…`, DB dump 190055 bytes, SHA256 `94a20e997ea1d8ff63184a0be1ce939038b14bbbbc12d5325d0277a9cb273a91`, verified by `pg_restore --list` and file manifest.
- `/opt/escape/backups/runtime-release-20261027T162323Z`: preserved 0.3.14 rollback snapshot and original dump hash.

The replaced 0.3.15 snapshot was removed only after successful verification. All four owned candidate/staging directories, overlays, temporary migration inputs and server test reports were removed. No disposable test containers, networks, volumes or test directories remain. Test accounts and their owned Storage were deleted.

The permanent `/opt/escape/deploy_runtime.py` was atomically updated to the tested repository helper, SHA256 `ff499e1a887c724b1bcc072ed41f1d55735b210bfdd2f2a624ae6858530f74d2`; Python syntax, `--help` and required pin arguments pass. Its old `a428f360…` bytes and original pre-helper manifest are preserved in the same fresh snapshot, bringing its final manifest to 82 files without changing its runtime or DB dump. See `tooling/canonical-helper-proof.json` and `tooling/prechange-backup-manifest.final.json`.

The final independent read-only check is `final-verification.json`. `cleanup-result.json` records every exact removed path and pre-delete hash. `deployment-proof/evidence-manifest.json` records original and archived hashes for sanitized reports; private credentials, DB dumps and real players' data were not downloaded. Selected one-off orchestration sources and guard results are preserved under `tooling/`; the reusable deployment helper remains in `Server/nakama/deploy/deploy_runtime.py`.
