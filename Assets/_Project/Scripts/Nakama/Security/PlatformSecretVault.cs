using System;
using System.IO;
using UnityEngine;

namespace Project.Nakama.Security
{
    public static class PlatformSecretVault
    {
        // Create/call only on Unity's main thread, including the Android JNI bridge.
        public static ISecretVault Create()
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            return new WindowsSecretVault(Path.Combine(Application.persistentDataPath, "secure-credentials-v1"));
#elif UNITY_ANDROID && !UNITY_EDITOR
            return new AndroidSecretVault();
#elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX || (UNITY_IOS && !UNITY_EDITOR)
            return new AppleSecretVault(Application.identifier + ".escape.credentials.v1");
#else
            throw new CredentialStorageException();
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private sealed class AndroidSecretVault : ISecretVault
        {
            public string Read(string key) => Call("read", key, null);
            public void Write(string key, string value) => Call("write", key, value);
            private static string Call(string method, string key, string value)
            {
                try
                {
                    using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                    using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                    using var vault = new AndroidJavaClass("ru.prokrust.escape.security.CredentialVault");
                    return method == "read"
                        ? vault.CallStatic<string>(method, activity, key)
                        : vault.CallStatic<string>(method, activity, key, value);
                }
                catch { throw new CredentialStorageException(); }
            }
        }
#endif
    }
}
