using System;
using System.Collections.Generic;
using UnityEngine;

namespace Project.Friends
{
    /// <summary>
    /// Фиктивные «онлайн»-ники (как в duel_online.lua / ARENA_BOT_DISPLAY_NAMES без сказочных).
    /// Исходящие заявки к ним хранятся локально — на сервер / боту ничего не уходит.
    /// </summary>
    public static class DecorOnlineBots
    {
        public const string UserIdPrefix = "zz-decor-online-";

        /// <summary>Синхрон с Server/nakama/modules/duel_online.lua FAKE_ONLINE_DISPLAY_NAMES.</summary>
        public static readonly string[] DisplayNames =
        {
            "Player_eUIbGX83r3",
            "_eby_kak_xo4y_",
            "Player_qKwBIlSUfZ",
            "Vanya_22",
            "Player_4e66fa56-",
        };

        public static bool IsDecorUsername(string username)
        {
            var name = (username ?? string.Empty).Trim();
            if (name.Length == 0) return false;
            for (var i = 0; i < DisplayNames.Length; i++)
            {
                if (string.Equals(DisplayNames[i], name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        public static bool IsDecorUserId(string userId)
        {
            return !string.IsNullOrEmpty(userId)
                   && userId.StartsWith(UserIdPrefix, StringComparison.Ordinal);
        }

        public static string MakeUserId(string username)
        {
            var name = CanonicalUsername(username);
            return string.IsNullOrEmpty(name) ? UserIdPrefix : UserIdPrefix + name;
        }

        public static string CanonicalUsername(string username)
        {
            var name = (username ?? string.Empty).Trim();
            if (name.Length == 0) return string.Empty;
            for (var i = 0; i < DisplayNames.Length; i++)
            {
                if (string.Equals(DisplayNames[i], name, StringComparison.OrdinalIgnoreCase))
                    return DisplayNames[i];
            }

            return name;
        }

        public static void AddOutgoingInvite(string ownerUserId, string username)
        {
            var name = CanonicalUsername(username);
            if (string.IsNullOrEmpty(name) || !IsDecorUsername(name))
                return;

            var list = LoadUsernames(ownerUserId);
            for (var i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], name, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            list.Add(name);
            SaveUsernames(ownerUserId, list);
        }

        public static void RemoveOutgoingInvite(string ownerUserId, string userIdOrUsername)
        {
            var key = (userIdOrUsername ?? string.Empty).Trim();
            if (key.Length == 0) return;

            var asName = key;
            if (IsDecorUserId(key))
                asName = key.Substring(UserIdPrefix.Length);

            var list = LoadUsernames(ownerUserId);
            var changed = false;
            for (var i = list.Count - 1; i >= 0; i--)
            {
                if (string.Equals(list[i], asName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(MakeUserId(list[i]), key, StringComparison.Ordinal))
                {
                    list.RemoveAt(i);
                    changed = true;
                }
            }

            if (changed)
                SaveUsernames(ownerUserId, list);
        }

        public static List<FriendListEntry> ListOutgoingAsFriends(string ownerUserId)
        {
            var list = LoadUsernames(ownerUserId);
            var result = new List<FriendListEntry>(list.Count);
            for (var i = 0; i < list.Count; i++)
            {
                var name = list[i];
                if (string.IsNullOrWhiteSpace(name)) continue;
                result.Add(new FriendListEntry
                {
                    UserId = MakeUserId(name),
                    Username = name,
                    Online = true,
                    State = FriendRelationState.InviteSent,
                });
            }

            return result;
        }

        private static string PrefsKey(string ownerUserId)
        {
            var id = string.IsNullOrWhiteSpace(ownerUserId) ? "_" : ownerUserId.Trim();
            return "friends.decor_outgoing.v1." + id;
        }

        private static List<string> LoadUsernames(string ownerUserId)
        {
            var raw = PlayerPrefs.GetString(PrefsKey(ownerUserId), string.Empty);
            var result = new List<string>(4);
            if (string.IsNullOrWhiteSpace(raw))
                return result;

            try
            {
                var wrapped = JsonUtility.FromJson<UsernameListDto>(raw);
                if (wrapped?.items == null)
                    return result;
                for (var i = 0; i < wrapped.items.Length; i++)
                {
                    var n = CanonicalUsername(wrapped.items[i]);
                    if (string.IsNullOrEmpty(n) || !IsDecorUsername(n))
                        continue;
                    var dup = false;
                    for (var j = 0; j < result.Count; j++)
                    {
                        if (string.Equals(result[j], n, StringComparison.OrdinalIgnoreCase))
                        {
                            dup = true;
                            break;
                        }
                    }

                    if (!dup)
                        result.Add(n);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Friends] Decor outgoing load failed: " + e.Message);
            }

            return result;
        }

        private static void SaveUsernames(string ownerUserId, List<string> usernames)
        {
            var dto = new UsernameListDto
            {
                items = usernames != null ? usernames.ToArray() : Array.Empty<string>(),
            };
            PlayerPrefs.SetString(PrefsKey(ownerUserId), JsonUtility.ToJson(dto));
            PlayerPrefs.Save();
        }

        [Serializable]
        private sealed class UsernameListDto
        {
            public string[] items;
        }
    }
}
