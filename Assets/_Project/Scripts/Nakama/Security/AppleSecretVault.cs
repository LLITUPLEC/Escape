#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX || (UNITY_IOS && !UNITY_EDITOR)
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Project.Nakama.Security
{
    /// <summary>Non-synchronizing, device-bound Keychain generic-password records.</summary>
    public sealed class AppleSecretVault : ISecretVault
    {
#if UNITY_IOS && !UNITY_EDITOR
        private const string Security = "__Internal";
        private const string CoreFoundation = "__Internal";
        private const string SystemLibrary = "__Internal";
#else
        private const string Security = "/System/Library/Frameworks/Security.framework/Security";
        private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        private const string SystemLibrary = "/usr/lib/libSystem.B.dylib";
#endif
        private readonly string _service;
        private static readonly IntPtr SecurityHandle = dlopen(Security == "__Internal" ? null : Security, 2);
        private static readonly IntPtr CoreHandle = dlopen(CoreFoundation == "__Internal" ? null : CoreFoundation, 2);
        public AppleSecretVault(string service) { _service = service; }

        public string Read(string key)
        {
            try
            {
                using var query = Query(key);
                query.Set(Constant("kSecReturnData"), Constant("kCFBooleanTrue", true));
                query.Set(Constant("kSecMatchLimit"), Constant("kSecMatchLimitOne"));
                var status = SecItemCopyMatching(query.Pointer, out var result);
                if (status == -25300) return null; // errSecItemNotFound only; all other errors fail closed.
                if (status != 0 || result == IntPtr.Zero) throw new CredentialStorageException();
                try
                {
                    var length = CFDataGetLength(result).ToInt64();
                    if (length <= 0 || length > 1024 * 1024) throw new CredentialStorageException();
                    var bytes = new byte[(int)length];
                    Marshal.Copy(CFDataGetBytePtr(result), bytes, 0, bytes.Length);
                    try { return Encoding.UTF8.GetString(bytes); }
                    finally { Array.Clear(bytes, 0, bytes.Length); }
                }
                finally { CFRelease(result); }
            }
            catch { throw new CredentialStorageException(); }
        }

        public void Write(string key, string value)
        {
            try
            {
                using var query = Query(key);
                using var attributes = new Dictionary();
                attributes.Data(Constant("kSecValueData"), value);
                var status = SecItemUpdate(query.Pointer, attributes.Pointer);
                if (status == -25300)
                {
                    query.Data(Constant("kSecValueData"), value);
                    query.Set(Constant("kSecAttrAccessible"), Constant("kSecAttrAccessibleWhenUnlockedThisDeviceOnly"));
                    // Defaults to non-synchronizing. Never opt in to iCloud Keychain.
                    status = SecItemAdd(query.Pointer, IntPtr.Zero);
                }
                if (status != 0) throw new CredentialStorageException();
            }
            catch { throw new CredentialStorageException(); }
        }

        private Dictionary Query(string key)
        {
            var query = new Dictionary();
            query.Set(Constant("kSecClass"), Constant("kSecClassGenericPassword"));
            query.Text(Constant("kSecAttrService"), _service);
            query.Text(Constant("kSecAttrAccount"), CredentialPersistence.Hash(key));
            return query;
        }

        private static IntPtr Constant(string name, bool core = false)
        {
            var address = dlsym(core ? CoreHandle : SecurityHandle, name);
            if (address == IntPtr.Zero) throw new CredentialStorageException();
            return Marshal.ReadIntPtr(address);
        }

        private sealed class Dictionary : IDisposable
        {
            public IntPtr Pointer { get; } = CFDictionaryCreateMutable(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            // Zero callbacks: keep created CF objects alive explicitly until the dictionary is disposed.
            private readonly List<IntPtr> _owned = new List<IntPtr>();
            public void Set(IntPtr key, IntPtr value) => CFDictionarySetValue(Pointer, key, value);
            public void Text(IntPtr key, string text)
            {
                var value = CFStringCreateWithCString(IntPtr.Zero, text, 0x08000100);
                if (value == IntPtr.Zero) throw new CredentialStorageException();
                _owned.Add(value);
                Set(key, value);
            }
            public void Data(IntPtr key, string text)
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                var value = CFDataCreate(IntPtr.Zero, bytes, new IntPtr(bytes.Length));
                Array.Clear(bytes, 0, bytes.Length);
                if (value == IntPtr.Zero) throw new CredentialStorageException();
                _owned.Add(value);
                Set(key, value);
            }
            public void Dispose()
            {
                if (Pointer != IntPtr.Zero) CFRelease(Pointer);
                foreach (var value in _owned) CFRelease(value);
            }
        }

        [DllImport(SystemLibrary)] private static extern IntPtr dlopen(string path, int mode);
        [DllImport(SystemLibrary)] private static extern IntPtr dlsym(IntPtr handle, string name);
        [DllImport(Security)] private static extern int SecItemCopyMatching(IntPtr query, out IntPtr result);
        [DllImport(Security)] private static extern int SecItemAdd(IntPtr attributes, IntPtr result);
        [DllImport(Security)] private static extern int SecItemUpdate(IntPtr query, IntPtr attributes);
        [DllImport(CoreFoundation)] private static extern IntPtr CFDictionaryCreateMutable(IntPtr allocator, IntPtr capacity, IntPtr keys, IntPtr values);
        [DllImport(CoreFoundation)] private static extern void CFDictionarySetValue(IntPtr dictionary, IntPtr key, IntPtr value);
        [DllImport(CoreFoundation)] private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, string text, uint encoding);
        [DllImport(CoreFoundation)] private static extern IntPtr CFDataCreate(IntPtr allocator, byte[] bytes, IntPtr length);
        [DllImport(CoreFoundation)] private static extern IntPtr CFDataGetLength(IntPtr data);
        [DllImport(CoreFoundation)] private static extern IntPtr CFDataGetBytePtr(IntPtr data);
        [DllImport(CoreFoundation)] private static extern void CFRelease(IntPtr value);
    }
}
#endif
