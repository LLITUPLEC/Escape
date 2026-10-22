using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Project.Nakama.Security
{
    // Never substitute PlayerPrefs/plain files if the platform vault fails.
    public sealed class CredentialStorageException : Exception
    {
        public CredentialStorageException() : base("Защищённое хранилище входа недоступно. Вход остановлен; существующий аккаунт сохранён. Проверьте доступ к хранилищу устройства и повторите попытку.") { }
    }

    public interface ISecretVault
    {
        string Read(string key);
        void Write(string key, string value);
    }

    public static class CredentialPersistence
    {
        // A migration may erase legacy credentials only after a successful durable round trip.
        public static void SaveVerified(ISecretVault vault, string key, string value, Action cleanup = null)
        {
            vault.Write(key, value);
            if (!string.Equals(vault.Read(key), value, StringComparison.Ordinal))
                throw new CredentialStorageException();
            cleanup?.Invoke();
        }

        public static string Hash(string value)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }
    }

    /// <summary>Windows user-bound DPAPI; only authenticated ciphertext reaches disk.</summary>
    public sealed class WindowsSecretVault : ISecretVault
    {
        private readonly string _directory;
        public WindowsSecretVault(string directory) { _directory = directory; }

        public string Read(string key)
        {
            try
            {
                var path = GetPath(key);
                if (!File.Exists(path)) return null;
                var encrypted = File.ReadAllBytes(path);
                return Encoding.UTF8.GetString(Transform(encrypted, key, false));
            }
            catch { throw new CredentialStorageException(); }
        }

        public void Write(string key, string value)
        {
            var temporary = "";
            try
            {
                Directory.CreateDirectory(_directory);
                var path = GetPath(key);
                temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                var encrypted = Transform(Encoding.UTF8.GetBytes(value), key, true);
                // Flush through the OS cache before publishing the replacement.
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(encrypted, 0, encrypted.Length);
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            catch { throw new CredentialStorageException(); }
            finally
            {
                if (!string.IsNullOrEmpty(temporary) && File.Exists(temporary))
                    try { File.Delete(temporary); } catch { /* encrypted temporary only */ }
            }
        }

        private string GetPath(string key) => Path.Combine(_directory, CredentialPersistence.Hash(key) + ".dpapi");

        [StructLayout(LayoutKind.Sequential)]
        private struct Blob { public int Length; public IntPtr Data; }
        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptProtectData(ref Blob input, string description, ref Blob entropy,
            IntPtr reserved, IntPtr prompt, int flags, out Blob output);
        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, ref Blob entropy,
            IntPtr reserved, IntPtr prompt, int flags, out Blob output);
        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr pointer);

        private static byte[] Transform(byte[] inputBytes, string key, bool protect)
        {
            var entropyBytes = Encoding.UTF8.GetBytes("Escape.credentials.v1|" + key);
            var input = new Blob { Length = inputBytes.Length, Data = Marshal.AllocHGlobal(inputBytes.Length) };
            var entropy = new Blob { Length = entropyBytes.Length, Data = Marshal.AllocHGlobal(entropyBytes.Length) };
            var output = new Blob();
            try
            {
                Marshal.Copy(inputBytes, 0, input.Data, inputBytes.Length);
                Marshal.Copy(entropyBytes, 0, entropy.Data, entropyBytes.Length);
                // CRYPTPROTECT_UI_FORBIDDEN, CurrentUser (never LocalMachine).
                var ok = protect
                    ? CryptProtectData(ref input, "Escape credentials", ref entropy, IntPtr.Zero, IntPtr.Zero, 1, out output)
                    : CryptUnprotectData(ref input, IntPtr.Zero, ref entropy, IntPtr.Zero, IntPtr.Zero, 1, out output);
                if (!ok || output.Length <= 0) throw new CredentialStorageException();
                var result = new byte[output.Length];
                Marshal.Copy(output.Data, result, 0, result.Length);
                return result;
            }
            finally
            {
                for (var i = 0; i < input.Length; i++) Marshal.WriteByte(input.Data, i, 0);
                Marshal.FreeHGlobal(input.Data);
                Marshal.FreeHGlobal(entropy.Data);
                if (output.Data != IntPtr.Zero)
                {
                    for (var i = 0; i < output.Length; i++) Marshal.WriteByte(output.Data, i, 0);
                    LocalFree(output.Data);
                }
            }
        }
    }
}
