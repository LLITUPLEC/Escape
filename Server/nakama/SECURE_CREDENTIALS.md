# Device credentials and session storage

JWT/refresh tokens and the Nakama device ID are authentication credentials. They are now kept in an OS vault. Email passwords are never saved. Existing UI hints (email mode, linked email and expected user ID) remain in PlayerPrefs; these hints alone cannot authenticate a player. Authentication, refresh persistence and session ownership use the selected secure record rather than shared Editor/clone hints.

## Platforms

| Platform | Protection | Status |
| --- | --- | --- |
| Windows Editor / Windows player | DPAPI `CurrentUser`, UI forbidden, scope-bound additional entropy; only DPAPI ciphertext in `persistentDataPath/secure-credentials-v1` | Actual DPAPI round-trip, replacement, scope isolation and corruption tests passed on Windows |
| Android API 23+ (project minimum: 25) | Android Keystore non-exportable AES-256 key; AES-GCM with a fresh random IV and scope as authenticated data; ciphertext in `Context.getNoBackupFilesDir()` | Java plugin compiles against the installed Android SDK; device/build test still required |
| iOS | Security.framework Keychain generic-password items, `WhenUnlockedThisDeviceOnly`, no iCloud synchronization; build hook links Security.framework | C# source checked; signed device build/runtime validation still required |
| macOS Editor / player | Security.framework Keychain, same item namespace and scope model | C# source checked; signed macOS runtime validation still required |
| Linux Editor / player, WebGL, other targets | No provider | Authentication stops with a storage error; no plaintext fallback |

Windows and Android player + Windows Editor C# assemblies were compiled with Unity 6000.3.9f1's Roslyn and existing build response files. This is a source compilation check, not a Unity player build. No active Unity instance was stopped or relaunched.

## Scopes and migration

Every record has a scope containing normalized scheme, host and port, plus the existing Editor/ParrelSync profile hash (`Application.dataPath`). Player builds have a separate empty profile. Switching a server saves/selects a separate record instead of deleting the other server's account association. An old credential without endpoint metadata is archived under `legacy://unscoped` and never sent to the current backend.

On first use, the old PlayerPrefs tokens and device ID are copied into the scope's protected record. A write and exact readback must succeed before deleting plaintext keys. Existing plaintext `nakama_device_id*.txt` backups are separately protected before deletion, including standalone and identifiable clone profiles. Existing secure records take precedence over stale plaintext after an interrupted migration. Other clones with only a PlayerPrefs device key and no discoverable file migrate that key when their own profile next starts.

Writes publish a complete protected record. Windows flushes the encrypted temporary file before atomic replacement. Android flushes before same-filesystem atomic rename and verifies decryption/readback. Keychain uses update/add of one item. A missing or corrupt key/record, locked Keychain, failed save or failed readback stops authentication instead of generating a replacement guest. A newly generated device ID is returned to authentication only after a verified secure save. Explicit logout clears session tokens/email mode while retaining the device association.

The main-thread-only Unity adapter handles legacy PlayerPrefs and native bridge calls. `GetLocalSessionEpoch()` now reads an atomic cached integer, so Nakama background callbacks no longer call `PlayerPrefs.GetInt` for RPC epoch fields.

## Tests

Run from the project root:

```powershell
./Server/nakama/tests/client_security/run.ps1
```

The 13 checks exercise real Windows DPAPI ciphertext, endpoint/profile binding and tamper rejection; write/readback failure handling; full legacy migration with alternate backups and clone/standalone identities; server switching; unavailable/corrupt vaults; interrupted migration; unscoped legacy tokens; logout; and refusing to return an unsaved new device identity. The migration tests use a fake Unity API and injected vault, with production credential-store source. Fixtures contain randomly generated test credentials and are written only to the ignored test output folder. No real account credentials are read or printed.

## Remaining limits

- Protecting an existing device ID does not revoke copies previously exposed in PlayerPrefs, files or external backups. Account-safe server credential rotation/revocation remains separate work; this migration deliberately preserves the working device linkage.
- OS vaults protect stored data against offline/plain-file access. They do not protect a running process from an attacker controlling that OS account, a rooted/jailbroken device or a compromised game process. Android hardware backing depends on the device.
- Removing PlayerPrefs keys/plain files does not erase historical OS/cloud backups or guarantee physical secure erasure on flash storage.
- Losing the OS account/profile or Android app data/Keystore can make stored credentials unrecoverable. Linked email login is the recovery method; do not delete the encrypted record as an automatic error handler.
- Apple distribution identifiers/signing should be finalized before release. Keychain accessibility and app identity must be validated on the signed target build.
