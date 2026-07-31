using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Project.Nakama;
using UnityEngine;

namespace Project.Friends
{
    public static class FriendsService
    {
        private const string RpcOnlineList = "duel_online_list";
        private const int FriendsPageLimit = 100;

        public static async Task<FriendsListResult> ListFriendsAsync(CancellationToken ct)
        {
            if (NakamaBootstrap.Instance == null)
                return FailFriends("nakama_not_initialized");

            await NakamaBootstrap.Instance.EnsureConnectedAsync(ct).ConfigureAwait(false);
            if (!IsClientReady())
                return FailFriends("nakama_not_ready");

            try
            {
                var onlineIds = await FetchOnlineIdSetAsync(ct).ConfigureAwait(false);
                var collected = new List<FriendListEntry>(32);
                string cursor = null;

                do
                {
                    var page = await NakamaBootstrap.Instance.Client.ListFriendsAsync(
                            NakamaBootstrap.Instance.Session,
                            state: null,
                            limit: FriendsPageLimit,
                            cursor: cursor,
                            canceller: ct)
                        .ConfigureAwait(false);

                    if (page?.Friends != null)
                    {
                        foreach (var friend in page.Friends)
                        {
                            if (friend?.User == null)
                                continue;

                            var state = (FriendRelationState)friend.State;
                            if (state == FriendRelationState.Blocked)
                                continue;

                            var userId = friend.User.Id ?? string.Empty;
                            var username = string.IsNullOrWhiteSpace(friend.User.Username)
                                ? "Survivor"
                                : friend.User.Username.Trim();
                            var online = (!string.IsNullOrEmpty(userId) && onlineIds.Contains(userId))
                                         || friend.User.Online;

                            collected.Add(new FriendListEntry
                            {
                                UserId = userId,
                                Username = username,
                                Online = online,
                                State = state,
                            });
                        }
                    }

                    cursor = page?.Cursor;
                } while (!string.IsNullOrEmpty(cursor) && !ct.IsCancellationRequested);

                collected.Sort((a, b) =>
                {
                    var onlineCmp = b.Online.CompareTo(a.Online);
                    if (onlineCmp != 0) return onlineCmp;
                    var stateCmp = ((int)a.State).CompareTo((int)b.State);
                    if (stateCmp != 0) return stateCmp;
                    return string.Compare(a.Username, b.Username, StringComparison.OrdinalIgnoreCase);
                });

                return new FriendsListResult { Ok = true, Friends = collected.ToArray() };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Friends] ListFriends failed: " + e.Message);
                return FailFriends(e.Message);
            }
        }

        public static async Task<FriendsMutationResult> AddFriendByUsernameAsync(string username, CancellationToken ct)
        {
            var name = (username ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(name))
                return FailMutation("empty_username");

            if (NakamaBootstrap.Instance == null)
                return FailMutation("nakama_not_initialized");

            await NakamaBootstrap.Instance.EnsureConnectedAsync(ct).ConfigureAwait(false);
            if (!IsClientReady())
                return FailMutation("nakama_not_ready");

            var selfName = NakamaBootstrap.Instance.Session?.Username;
            if (!string.IsNullOrWhiteSpace(selfName)
                && string.Equals(selfName.Trim(), name, StringComparison.OrdinalIgnoreCase))
            {
                return FailMutation("cannot_add_self");
            }

            try
            {
                await NakamaBootstrap.Instance.Client.AddFriendsAsync(
                        NakamaBootstrap.Instance.Session,
                        ids: null,
                        usernames: new[] { name },
                        canceller: ct)
                    .ConfigureAwait(false);

                return new FriendsMutationResult { Ok = true };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Friends] AddFriend failed: " + e.Message);
                return FailMutation(e.Message);
            }
        }

        public static async Task<FriendsMutationResult> DeleteFriendAsync(string userId, string username, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(userId) && string.IsNullOrWhiteSpace(username))
                return FailMutation("empty_target");

            if (NakamaBootstrap.Instance == null)
                return FailMutation("nakama_not_initialized");

            await NakamaBootstrap.Instance.EnsureConnectedAsync(ct).ConfigureAwait(false);
            if (!IsClientReady())
                return FailMutation("nakama_not_ready");

            try
            {
                var ids = string.IsNullOrWhiteSpace(userId)
                    ? null
                    : new[] { userId.Trim() };
                var names = string.IsNullOrWhiteSpace(username)
                    ? null
                    : new[] { username.Trim() };

                await NakamaBootstrap.Instance.Client.DeleteFriendsAsync(
                        NakamaBootstrap.Instance.Session,
                        ids,
                        names,
                        canceller: ct)
                    .ConfigureAwait(false);

                return new FriendsMutationResult { Ok = true };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Friends] DeleteFriend failed: " + e.Message);
                return FailMutation(e.Message);
            }
        }

        public static async Task<OnlineListResult> ListOnlineAsync(CancellationToken ct, int limit = 50)
        {
            if (NakamaBootstrap.Instance == null)
                return FailOnline("nakama_not_initialized");

            await NakamaBootstrap.Instance.EnsureConnectedAsync(ct).ConfigureAwait(false);
            if (!IsClientReady())
                return FailOnline("nakama_not_ready");

            try
            {
                var body = "{\"limit\":" + Mathf.Clamp(limit, 1, 50) + "}";
                var rpc = await NakamaBootstrap.Instance.Client.RpcAsync(
                        NakamaBootstrap.Instance.Session, RpcOnlineList, body, canceller: ct)
                    .ConfigureAwait(false);

                var payload = rpc?.Payload;
                if (string.IsNullOrWhiteSpace(payload))
                    return FailOnline("empty_payload");

                var model = JsonUtility.FromJson<OnlineListRpcResponse>(payload);
                if (model == null)
                    return FailOnline("parse_failed");
                if (!model.ok)
                    return FailOnline(string.IsNullOrWhiteSpace(model.err) ? "rpc_failed" : model.err);

                var players = model.players ?? Array.Empty<OnlinePlayerDto>();
                var mapped = new OnlinePlayerEntry[players.Length];
                for (var i = 0; i < players.Length; i++)
                {
                    var dto = players[i];
                    var level = dto != null ? dto.level : 1;
                    if (level < 1) level = 1;
                    if (level > 12) level = 12;
                    mapped[i] = new OnlinePlayerEntry
                    {
                        UserId = dto?.user_id ?? string.Empty,
                        Username = string.IsNullOrWhiteSpace(dto?.username) ? "Survivor" : dto.username.Trim(),
                        Level = level,
                    };
                }

                return new OnlineListResult
                {
                    Ok = true,
                    Total = Mathf.Max(0, model.total),
                    Shown = mapped.Length > 0 ? mapped.Length : Mathf.Max(0, model.shown),
                    Players = mapped,
                    OnlineIds = model.online_ids ?? Array.Empty<string>(),
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Friends] ListOnline failed: " + e.Message);
                return FailOnline(e.Message);
            }
        }

        public static string DescribeError(string err)
        {
            if (string.IsNullOrWhiteSpace(err))
                return "Неизвестная ошибка";
            return err switch
            {
                "nakama_not_ready" => "Нет соединения с сервером",
                "nakama_not_initialized" => "Сервер не инициализирован",
                "unauthorized" => "Требуется авторизация",
                "empty_username" => "Введите ник игрока",
                "cannot_add_self" => "Нельзя добавить себя",
                "empty_target" => "Игрок не выбран",
                "empty_payload" => "Пустой ответ сервера",
                "parse_failed" => "Ошибка ответа сервера",
                "server_error" => "Ошибка сервера",
                _ => err.Contains("not found", StringComparison.OrdinalIgnoreCase)
                    || err.Contains("User not found", StringComparison.OrdinalIgnoreCase)
                    ? "Игрок не найден"
                    : "Ошибка: " + err,
            };
        }

        private static async Task<HashSet<string>> FetchOnlineIdSetAsync(CancellationToken ct)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                var online = await ListOnlineAsync(ct, 50).ConfigureAwait(false);
                if (!online.Ok || online.OnlineIds == null)
                    return set;
                foreach (var id in online.OnlineIds)
                {
                    if (!string.IsNullOrWhiteSpace(id))
                        set.Add(id.Trim());
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Friends] Online ids for friends status failed: " + e.Message);
            }

            return set;
        }

        private static bool IsClientReady() =>
            NakamaBootstrap.Instance != null
            && NakamaBootstrap.Instance.IsReady
            && NakamaBootstrap.Instance.Client != null
            && NakamaBootstrap.Instance.Session != null;

        private static FriendsListResult FailFriends(string err) =>
            new FriendsListResult { Ok = false, Err = err };

        private static FriendsMutationResult FailMutation(string err) =>
            new FriendsMutationResult { Ok = false, Err = err };

        private static OnlineListResult FailOnline(string err) =>
            new OnlineListResult { Ok = false, Err = err };
    }
}
