using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Project.Nakama.Security;
using UnityEngine;

internal static class CredentialSecurityTests
{
    private const string Endpoint = "https://prokrust-play.ru:443";
    private const string Profile = "123456abcdef";
    private const string OtherProfile = "fedcba654321";
    private static string _root;
    private static int _passed;

    private sealed class MemoryVault : ISecretVault
    {
        public readonly Dictionary<string, string> Values = new Dictionary<string, string>();
        public bool FailWrite;
        public bool WrongReadback;
        public string Read(string key) => Values.TryGetValue(key, out var value) ? WrongReadback ? "mismatch" : value : null;
        public void Write(string key, string value)
        {
            if (FailWrite) throw new CredentialStorageException();
            Values[key] = value;
        }
    }

    private static string Scope(string endpoint, string profile) => "escape.credentials.v1|" + endpoint + "|" + profile;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
    private static void ExpectStorageFailure(Action action)
    {
        try { action(); } catch (CredentialStorageException) { return; }
        throw new Exception("Expected fail-closed credential storage error");
    }
    private static void Test(string name, Action action)
    {
        action();
        _passed++;
        Console.WriteLine("PASS " + name);
    }
    private static MemoryVault Reset(string name)
    {
        PlayerPrefs.Reset();
        Application.persistentDataPath = Path.Combine(_root, name);
        Directory.CreateDirectory(Application.persistentDataPath);
        var type = typeof(NakamaCredentialStore);
        type.GetField("_current", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
        var vault = new MemoryVault();
        type.GetField("_vault", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, vault);
        return vault;
    }
    private static void SeedLegacy()
    {
        PlayerPrefs.SetString(NakamaCredentialStore.EndpointPref, Endpoint);
        PlayerPrefs.SetString("nakama.session.auth_token", "fixture-auth-" + Guid.NewGuid());
        PlayerPrefs.SetString("nakama.session.refresh_token", "fixture-refresh-" + Guid.NewGuid());
        PlayerPrefs.SetString("nakama.session.email_password", "never-persist-this-fixture");
        PlayerPrefs.SetInt("nakama.use_email_session", 1);
        PlayerPrefs.SetString("nakama.session.email_user_id", "expected-user");
        PlayerPrefs.SetString("nakama.ui.known_linked_email", "fixture@example.invalid");
        PlayerPrefs.SetString("nakama.device_id.dev." + Profile, "current-device-fixture");
        File.WriteAllText(Path.Combine(Application.persistentDataPath, "nakama_device_id." + Profile + ".txt"), "older-device-backup-fixture");
    }

    public static int Main(string[] args)
    {
        _root = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(_root);
        try
        {
            Test("DPAPI encrypted round trip and atomic replacement", () =>
            {
                var directory = Path.Combine(_root, "dpapi");
                var vault = new WindowsSecretVault(directory);
                var key = Scope(Endpoint, Profile);
                var secret = "unique-fixture-" + Guid.NewGuid().ToString("N");
                Check(vault.Read(key) == null, "Missing record must be distinct from corruption");
                CredentialPersistence.SaveVerified(vault, key, secret);
                Check(vault.Read(key) == secret, "Round trip changed value");
                var file = Path.Combine(directory, CredentialPersistence.Hash(key) + ".dpapi");
                Check(!Encoding.UTF8.GetString(File.ReadAllBytes(file)).Contains(secret), "Raw secret reached disk");
                CredentialPersistence.SaveVerified(vault, key, secret + "-new");
                Check(Directory.GetFiles(directory, "*.tmp").Length == 0, "Atomic write left temporary files");
            });
            Test("DPAPI binds ciphertext to endpoint/profile and rejects tampering", () =>
            {
                var directory = Path.Combine(_root, "dpapi-scope");
                var vault = new WindowsSecretVault(directory);
                var key = Scope(Endpoint, Profile);
                var other = Scope("https://other.invalid:443", OtherProfile);
                CredentialPersistence.SaveVerified(vault, key, "fixture-value");
                var source = Path.Combine(directory, CredentialPersistence.Hash(key) + ".dpapi");
                File.Copy(source, Path.Combine(directory, CredentialPersistence.Hash(other) + ".dpapi"));
                ExpectStorageFailure(() => vault.Read(other));
                var blob = File.ReadAllBytes(source);
                blob[blob.Length - 1] ^= 0x7f;
                File.WriteAllBytes(source, blob);
                ExpectStorageFailure(() => vault.Read(key));
            });
            Test("failed save retains legacy source", () =>
            {
                var vault = new MemoryVault { FailWrite = true };
                var cleaned = false;
                ExpectStorageFailure(() => CredentialPersistence.SaveVerified(vault, "key", "fixture", () => cleaned = true));
                Check(!cleaned, "Cleanup ran after failed write");
            });
            Test("failed readback retains legacy source", () =>
            {
                var vault = new MemoryVault { WrongReadback = true };
                var cleaned = false;
                ExpectStorageFailure(() => CredentialPersistence.SaveVerified(vault, "key", "fixture", () => cleaned = true));
                Check(!cleaned, "Cleanup ran after failed verification");
            });
            Test("cleanup follows verified durable save", () =>
            {
                var vault = new MemoryVault();
                var cleaned = false;
                CredentialPersistence.SaveVerified(vault, "key", "fixture", () =>
                {
                    Check(vault.Read("key") == "fixture", "Cleanup ran before durable round trip");
                    cleaned = true;
                });
                Check(cleaned, "Successful migration did not clean up");
            });
            Test("legacy JWT refresh device backups migrate without identity loss", () =>
            {
                var vault = Reset("migration");
                SeedLegacy();
                PlayerPrefs.SetString("nakama.device_id", "standalone-device-fixture");
                File.WriteAllText(Path.Combine(Application.persistentDataPath, "nakama_device_id.txt"), "standalone-device-fixture");
                PlayerPrefs.SetString("nakama.device_id.dev." + OtherProfile, "clone-device-fixture");
                File.WriteAllText(Path.Combine(Application.persistentDataPath, "nakama_device_id." + OtherProfile + ".txt"), "clone-device-fixture");
                var expectedAuth = PlayerPrefs.GetString("nakama.session.auth_token");
                NakamaCredentialStore.Prepare(Endpoint, Profile);
                var record = NakamaCredentialStore.Snapshot();
                Check(record.authToken == expectedAuth && record.deviceId == "current-device-fixture"
                    && record.userId == "expected-user" && record.useEmail, "Active account association changed");
                PlayerPrefs.SetInt("nakama.use_email_session", 0); // Another Editor clone can change shared UI hints.
                PlayerPrefs.SetString("nakama.ui.known_linked_email", "");
                Check(NakamaCredentialStore.UsesEmail && NakamaCredentialStore.HasKnownEmail,
                    "Shared clone preferences changed the active credential mode");
                Check(!PlayerPrefs.HasKey("nakama.session.auth_token") && !PlayerPrefs.HasKey("nakama.session.refresh_token")
                    && !PlayerPrefs.HasKey("nakama.session.email_password") && !PlayerPrefs.HasKey("nakama.device_id.dev." + Profile)
                    && !PlayerPrefs.HasKey("nakama.device_id") && !PlayerPrefs.HasKey("nakama.device_id.dev." + OtherProfile), "Plaintext credentials remain in preferences");
                Check(Directory.GetFiles(Application.persistentDataPath, "nakama_device_id*.txt").Length == 0, "Plaintext device backup remains");
                NakamaCredentialStore.Prepare(Endpoint, "");
                Check(NakamaCredentialStore.GetDeviceId("") == "standalone-device-fixture", "Standalone identity was lost");
                NakamaCredentialStore.Prepare(Endpoint, OtherProfile);
                Check(NakamaCredentialStore.GetDeviceId(OtherProfile) == "clone-device-fixture", "Clone identity was lost");
            });
            Test("endpoint switching preserves accounts and never reuses another backend JWT", () =>
            {
                Reset("endpoints");
                SeedLegacy();
                NakamaCredentialStore.Prepare(Endpoint, Profile);
                var original = NakamaCredentialStore.Snapshot();
                var otherEndpoint = "https://other.invalid:443";
                Check(NakamaCredentialStore.Prepare(otherEndpoint, Profile), "Endpoint change was not reported");
                var fresh = NakamaCredentialStore.Snapshot();
                Check(string.IsNullOrEmpty(fresh.authToken) && string.IsNullOrEmpty(fresh.userId) && !fresh.useEmail, "Credentials crossed endpoints");
                var newDevice = NakamaCredentialStore.GetDeviceId(Profile);
                Check(newDevice != original.deviceId, "Device credential crossed endpoints");
                NakamaCredentialStore.Prepare(Endpoint, Profile);
                Check(NakamaCredentialStore.Snapshot().authToken == original.authToken
                    && NakamaCredentialStore.GetDeviceId(Profile) == original.deviceId, "Returning to backend lost account");
            });
            Test("unavailable vault preserves legacy credentials and blocks guest creation", () =>
            {
                var vault = Reset("unavailable");
                SeedLegacy();
                var expected = PlayerPrefs.GetString("nakama.session.auth_token");
                vault.FailWrite = true;
                ExpectStorageFailure(() => NakamaCredentialStore.Prepare(Endpoint, Profile));
                Check(PlayerPrefs.GetString("nakama.session.auth_token") == expected
                    && File.Exists(Path.Combine(Application.persistentDataPath, "nakama_device_id." + Profile + ".txt")), "Failed migration destroyed legacy identity");
                ExpectStorageFailure(() => NakamaCredentialStore.GetDeviceId(Profile));
            });
            Test("corrupt vault blocks fallback and retains legacy recovery material", () =>
            {
                var vault = Reset("corrupt");
                SeedLegacy();
                vault.Values[Scope(Endpoint, Profile)] = "{}";
                ExpectStorageFailure(() => NakamaCredentialStore.Prepare(Endpoint, Profile));
                Check(PlayerPrefs.HasKey("nakama.session.auth_token"), "Corrupt vault caused credential deletion");
                ExpectStorageFailure(() => NakamaCredentialStore.GetDeviceId(Profile));
            });
            Test("restart after committed migration keeps vault authoritative", () =>
            {
                var vault = Reset("interrupted");
                SeedLegacy();
                var record = new NakamaCredentialStore.Record
                {
                    scope = Scope(Endpoint, Profile), deviceId = "vault-device", authToken = "vault-auth",
                    refreshToken = "vault-refresh", userId = "vault-user", knownEmail = "vault@example.invalid", useEmail = true
                };
                vault.Values[record.scope] = JsonUtility.ToJson(record);
                NakamaCredentialStore.Prepare(Endpoint, Profile);
                Check(NakamaCredentialStore.Snapshot().authToken == "vault-auth"
                    && NakamaCredentialStore.GetDeviceId(Profile) == "vault-device", "Stale preferences overwrote vault");
                Check(!PlayerPrefs.HasKey("nakama.session.auth_token"), "Interrupted migration did not finish cleanup");
            });
            Test("unknown legacy endpoint is archived without sending its credentials", () =>
            {
                var vault = Reset("unscoped");
                SeedLegacy();
                PlayerPrefs.DeleteKey(NakamaCredentialStore.EndpointPref);
                NakamaCredentialStore.Prepare(Endpoint, Profile);
                Check(string.IsNullOrEmpty(NakamaCredentialStore.Snapshot().authToken), "Unscoped token sent to current backend");
                Check(vault.Values.ContainsKey(Scope("legacy://unscoped", Profile)), "Unscoped recovery identity lost");
            });
            Test("logout erases tokens while keeping device identity", () =>
            {
                Reset("logout");
                SeedLegacy();
                NakamaCredentialStore.Prepare(Endpoint, Profile);
                var device = NakamaCredentialStore.GetDeviceId(Profile);
                NakamaCredentialStore.ClearEmail();
                var record = NakamaCredentialStore.Snapshot();
                Check(string.IsNullOrEmpty(record.authToken) && string.IsNullOrEmpty(record.refreshToken)
                    && string.IsNullOrEmpty(record.userId) && !record.useEmail && record.deviceId == device, "Logout changed device or retained tokens");
            });
            Test("new device is never returned before durable save", () =>
            {
                var vault = Reset("new-device-failure");
                NakamaCredentialStore.Prepare(Endpoint, Profile);
                vault.FailWrite = true;
                ExpectStorageFailure(() => NakamaCredentialStore.GetDeviceId(Profile));
                Check(string.IsNullOrEmpty(NakamaCredentialStore.Snapshot().deviceId), "Failed save committed an in-memory identity");
                vault.FailWrite = false;
                var device = NakamaCredentialStore.GetDeviceId(Profile);
                Check(vault.Values[Scope(Endpoint, Profile)].Contains(device), "Returned device was not in durable storage");
            });
            Console.WriteLine("Credential security checks: " + _passed + " passed.");
            return 0;
        }
        catch (Exception e)
        {
            // Deliberately exclude exception data/record contents from output.
            Console.Error.WriteLine("FAIL " + e.GetType().Name + ": " + e.Message);
            return 1;
        }
    }
}
