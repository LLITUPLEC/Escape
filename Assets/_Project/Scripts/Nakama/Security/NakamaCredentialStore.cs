using System;
using System.IO;
using UnityEngine;

namespace Project.Nakama.Security
{
    /// <summary>Main-thread-only adapter. Credentials never go back into PlayerPrefs.</summary>
    internal static class NakamaCredentialStore
    {
        internal const string EndpointPref = "nakama.session.endpoint.v1";
        private const string AuthPref = "nakama.session.auth_token";
        private const string RefreshPref = "nakama.session.refresh_token";
        private const string PasswordPref = "nakama.session.email_password";
        private const string UserIdPref = "nakama.session.email_user_id";
        private const string UseEmailPref = "nakama.use_email_session";
        private const string KnownEmailPref = "nakama.ui.known_linked_email";
        private static ISecretVault _vault;
        private static Record _current;
        internal static bool UsesEmail => _current != null && _current.useEmail;
        internal static bool HasKnownEmail => _current != null && !string.IsNullOrWhiteSpace(_current.knownEmail);

        [Serializable]
        internal sealed class Record
        {
            public int version = 1;
            public string scope;
            public string deviceId;
            public string authToken;
            public string refreshToken;
            public string userId;
            public string knownEmail;
            public bool useEmail;
        }

        internal static bool Prepare(string endpoint, string profile)
        {
            var scope = Scope(endpoint, profile);
            if (_current != null && _current.scope == scope) return false;
            try
            {
                _vault ??= PlatformSecretVault.Create();
                var previousEndpoint = PlayerPrefs.GetString(EndpointPref, "");
                var changed = !string.IsNullOrEmpty(previousEndpoint) && previousEndpoint != endpoint;
                // An unscoped old credential must never be sent to an arbitrary new backend.
                var legacyEndpoint = !string.IsNullOrEmpty(previousEndpoint) ? previousEndpoint
                    : HasLegacyCredentials(profile) ? "legacy://unscoped" : endpoint;
                changed |= legacyEndpoint != endpoint;
                var legacyScope = Scope(legacyEndpoint, profile);
                var legacy = Read(legacyScope);
                if (legacy == null)
                {
                    legacy = ReadLegacy(legacyScope, profile);
                    Save(legacy);
                }
                else
                {
                    // A previous crash may have happened after committing the vault and before cleanup.
                    // Verify the committed record, keeping it authoritative over stale plaintext remnants.
                    CredentialPersistence.SaveVerified(_vault, legacyScope, JsonUtility.ToJson(legacy));
                }
                RemoveLegacySecrets(profile, legacyScope);

                var next = scope == legacyScope ? legacy : Read(scope);
                if (next == null)
                {
                    next = new Record { scope = scope };
                    Save(next); // Also verifies vault availability before contacting the server.
                }
                _current = next;
                MirrorMetadata();
                PlayerPrefs.SetString(EndpointPref, endpoint);
                PlayerPrefs.Save();
                return changed;
            }
            catch (CredentialStorageException) { throw; }
            catch { throw new CredentialStorageException(); }
        }

        internal static Record Snapshot()
        {
            RequireInitialized();
            return JsonUtility.FromJson<Record>(JsonUtility.ToJson(_current));
        }

        internal static string GetDeviceId(string profile)
        {
            RequireInitialized();
            if (!string.IsNullOrWhiteSpace(_current.deviceId)) return _current.deviceId;
            var next = Snapshot();
            next.deviceId = (string.IsNullOrEmpty(profile) ? "gen-" : "dev-" + profile + "-") + Guid.NewGuid().ToString("N");
            Commit(next); // Never authenticate/create an account with a credential that wasn't durably saved.
            return next.deviceId;
        }

        internal static void PersistSession(string auth, string refresh, string userId)
        {
            var next = Snapshot();
            next.authToken = auth;
            next.refreshToken = refresh ?? "";
            next.userId = userId;
            next.useEmail = true;
            Commit(next);
        }

        internal static void SetUserId(string userId)
        {
            var next = Snapshot();
            next.userId = userId;
            Commit(next);
        }

        internal static void SetKnownEmail(string email)
        {
            var next = Snapshot();
            next.knownEmail = email ?? "";
            Commit(next);
        }

        internal static void ClearTokens()
        {
            var next = Snapshot();
            next.authToken = "";
            next.refreshToken = "";
            Commit(next);
        }

        internal static void ClearEmail()
        {
            var next = Snapshot();
            next.authToken = next.refreshToken = next.userId = next.knownEmail = "";
            next.useEmail = false;
            Commit(next); // Keep the device credential/account association on explicit logout.
        }

        private static void Commit(Record record)
        {
            try
            {
                Save(record);
                _current = record;
                MirrorMetadata();
                PlayerPrefs.Save();
            }
            catch (CredentialStorageException) { throw; }
            catch { throw new CredentialStorageException(); }
        }

        private static void MirrorMetadata()
        {
            // Existing UI reads these non-secret hints; JWTs/device credentials are absent.
            PlayerPrefs.SetInt(UseEmailPref, _current.useEmail ? 1 : 0);
            PlayerPrefs.SetString(UserIdPref, _current.userId ?? "");
            PlayerPrefs.SetString(KnownEmailPref, _current.knownEmail ?? "");
        }

        private static string Scope(string endpoint, string profile) => "escape.credentials.v1|" + endpoint + "|" + profile;
        private static string DevicePref(string profile) => string.IsNullOrEmpty(profile)
            ? "nakama.device_id" : "nakama.device_id.dev." + profile;
        private static string DeviceFile(string profile) => Path.Combine(Application.persistentDataPath,
            string.IsNullOrEmpty(profile) ? "nakama_device_id.txt" : "nakama_device_id." + profile + ".txt");

        private static bool HasLegacyCredentials(string profile) =>
            PlayerPrefs.HasKey(AuthPref) || PlayerPrefs.HasKey(RefreshPref) ||
            PlayerPrefs.HasKey(DevicePref(profile)) || File.Exists(DeviceFile(profile));

        private static Record ReadLegacy(string scope, string profile)
        {
            var device = PlayerPrefs.GetString(DevicePref(profile), "");
            var path = DeviceFile(profile);
            if (string.IsNullOrWhiteSpace(device) && File.Exists(path)) device = File.ReadAllText(path).Trim();
            return new Record
            {
                scope = scope,
                deviceId = device,
                authToken = PlayerPrefs.GetString(AuthPref, ""),
                refreshToken = PlayerPrefs.GetString(RefreshPref, ""),
                userId = PlayerPrefs.GetString(UserIdPref, ""),
                knownEmail = PlayerPrefs.GetString(KnownEmailPref, ""),
                useEmail = PlayerPrefs.GetInt(UseEmailPref, 0) != 0
            };
        }

        private static void RemoveLegacySecrets(string profile, string scope)
        {
            // Preserve a potentially different file backup as encrypted recovery material before erasing it.
            ProtectLegacyDeviceFile(DeviceFile(profile), scope + "|legacy-device-file");
            if (!string.IsNullOrEmpty(profile))
            {
                // Editor and standalone previously shared PlayerPrefs. Preserve standalone's separate device
                // in its actual scope so a subsequent standalone launch does not create a fresh guest.
                var standaloneScope = scope.Substring(0, scope.LastIndexOf('|') + 1);
                if (Read(standaloneScope) == null) Save(ReadLegacy(standaloneScope, ""));
                ProtectLegacyDeviceFile(DeviceFile(""), scope + "|legacy-standalone-device-file");
                var standalone = PlayerPrefs.GetString("nakama.device_id", "");
                if (!string.IsNullOrEmpty(standalone))
                    CredentialPersistence.SaveVerified(_vault, scope + "|legacy-standalone-device-pref", standalone);
                PlayerPrefs.DeleteKey("nakama.device_id");
            }
            PlayerPrefs.DeleteKey(DevicePref(profile));
            PlayerPrefs.DeleteKey(AuthPref);
            PlayerPrefs.DeleteKey(RefreshPref);
            PlayerPrefs.DeleteKey(PasswordPref);
            PlayerPrefs.Save();

            // ParrelSync clones share persistentDataPath, but each legacy filename carries its profile.
            // Preserve all such identities under their own scope before removing plaintext backups.
            var scopePrefix = scope.Substring(0, scope.LastIndexOf('|') + 1);
            if (Directory.Exists(Application.persistentDataPath))
            {
                foreach (var path in Directory.GetFiles(Application.persistentDataPath, "nakama_device_id.*.txt"))
                {
                    var name = Path.GetFileName(path);
                    var otherProfile = name.Substring("nakama_device_id.".Length,
                        name.Length - "nakama_device_id.".Length - ".txt".Length);
                    if (!IsDevelopmentProfile(otherProfile)) continue;
                    var otherScope = scopePrefix + otherProfile;
                    if (Read(otherScope) == null) Save(ReadLegacy(otherScope, otherProfile));
                    ProtectLegacyDeviceFile(path, otherScope + "|legacy-device-file");
                    PlayerPrefs.DeleteKey(DevicePref(otherProfile));
                }
                PlayerPrefs.Save();
            }
        }

        private static bool IsDevelopmentProfile(string value)
        {
            if (value.Length != 12) return false;
            foreach (var c in value)
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            return true;
        }

        private static void ProtectLegacyDeviceFile(string path, string key)
        {
            if (!File.Exists(path)) return;
            CredentialPersistence.SaveVerified(_vault, key, File.ReadAllText(path), () => File.Delete(path));
        }

        private static Record Read(string scope)
        {
            var raw = _vault.Read(scope);
            if (raw == null) return null;
            var record = JsonUtility.FromJson<Record>(raw);
            if (record == null || record.version != 1 || record.scope != scope) throw new CredentialStorageException();
            return record;
        }

        private static void Save(Record record) => CredentialPersistence.SaveVerified(_vault, record.scope, JsonUtility.ToJson(record));
        private static void RequireInitialized()
        {
            if (_current == null) throw new CredentialStorageException();
        }
    }
}
