using System;
using System.Security.Cryptography;
using System.Text;

namespace Project.Nakama
{
    /// <summary>Account data caches belong to a server and its authenticated user, never an email/device alone.</summary>
    public static class LocalAccountCacheScope
    {
        private static string _lastScheme, _lastHost, _lastUserId, _lastScope;
        private static int _lastPort;
        /// <summary>Call on the Unity main thread; a missing authenticated session has no persistent cache.</summary>
        public static string Current
        {
            get
            {
                var bootstrap = NakamaBootstrap.Instance;
                var config = bootstrap?.Config;
                var userId = bootstrap?.Session?.UserId;
                if (config == null || string.IsNullOrEmpty(userId))
                    return null;
                var scheme = config.GetScheme();
                if (_lastScope == null || _lastScheme != scheme || _lastHost != config.host || _lastPort != config.port || _lastUserId != userId)
                {
                    _lastScheme = scheme;
                    _lastHost = config.host;
                    _lastPort = config.port;
                    _lastUserId = userId;
                    _lastScope = Create(scheme, config.host, config.port, userId);
                }
                return _lastScope;
            }
        }

        public static string Create(string scheme, string host, int port, string userId)
        {
            if (string.IsNullOrEmpty(userId)) return null;
            var identity = (scheme ?? "").Trim().ToLowerInvariant() + "://"
                + (host ?? "").Trim().TrimEnd('/').ToLowerInvariant() + ":" + port + "/" + userId;
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(identity))).Replace("-", "").ToLowerInvariant();
        }

        public static bool IsCurrent(string scope) =>
            !string.IsNullOrEmpty(scope) && string.Equals(scope, Current, StringComparison.Ordinal);
    }
}
