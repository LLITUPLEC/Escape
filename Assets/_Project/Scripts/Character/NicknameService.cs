using System;
using System.Threading;
using System.Threading.Tasks;
using Project.Nakama;
using Project.Utils;
using UnityEngine;

namespace Project.Character
{
    [Serializable]
    public sealed class NicknameStatusRpcResponse
    {
        public bool ok;
        public string err;
        public string username;
        public int nickname_changes;
        public long next_change_gold_cost;
        public bool free_change_available;
        public int min_len;
        public int max_len;
    }

    [Serializable]
    public sealed class NicknameChangeRpcResponse
    {
        public bool ok;
        public string err;
        public string username;
        public int nickname_changes;
        public long next_change_gold_cost;
        public long gold_spent;
        public long need;
        public long gold;
        public PlayerResourcesRpcResponse resources;
        public Progression progression;
    }

    /// <summary>Смена Nakama Username (1-я бесплатно, далее за золото).</summary>
    public static class NicknameService
    {
        public const string RpcStatusGet = "duel_nickname_status_get";
        public const string RpcChange = "duel_nickname_change";

        /// <summary>Ник успешно изменён на сервере — обновить HUD и кэш.</summary>
        public static event Action<string> UsernameChanged;

        public static void NotifyUsernameChanged(string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return;
            var trimmed = username.Trim();
            // Подписчики трогают UI / PlayerPrefs — только main thread.
            MainThreadDispatcher.Enqueue(() => UsernameChanged?.Invoke(trimmed));
        }

        public static async Task<NicknameStatusRpcResponse> GetStatusAsync(CancellationToken ct)
        {
            if (NakamaBootstrap.Instance == null)
                return new NicknameStatusRpcResponse { ok = false, err = "nakama_not_initialized" };

            await NakamaBootstrap.Instance.EnsureConnectedAsync(ct).ConfigureAwait(false);
            if (!NakamaBootstrap.Instance.IsReady || NakamaBootstrap.Instance.Client == null || NakamaBootstrap.Instance.Session == null)
                return new NicknameStatusRpcResponse { ok = false, err = "nakama_not_ready" };

            try
            {
                var rpc = await NakamaBootstrap.Instance.Client.RpcAsync(
                    NakamaBootstrap.Instance.Session, RpcStatusGet, "{}", canceller: ct).ConfigureAwait(false);
                var parsed = JsonUtility.FromJson<NicknameStatusRpcResponse>(rpc?.Payload ?? "{}");
                return parsed ?? new NicknameStatusRpcResponse { ok = false, err = "bad_payload" };
            }
            catch (Exception e)
            {
                return new NicknameStatusRpcResponse { ok = false, err = e.Message };
            }
        }

        public static async Task<NicknameChangeRpcResponse> ChangeAsync(string username, CancellationToken ct)
        {
            if (NakamaBootstrap.Instance == null)
                return new NicknameChangeRpcResponse { ok = false, err = "nakama_not_initialized" };

            await NakamaBootstrap.Instance.EnsureConnectedAsync(ct).ConfigureAwait(false);
            if (!NakamaBootstrap.Instance.IsReady || NakamaBootstrap.Instance.Client == null || NakamaBootstrap.Instance.Session == null)
                return new NicknameChangeRpcResponse { ok = false, err = "nakama_not_ready" };

            // PlayerPrefs только с main thread (иначе GetString/GetInt exception).
            var sessionEpoch = await MainThreadDispatcher
                .RunAsync(NakamaBootstrap.GetLocalSessionEpoch)
                .ConfigureAwait(false);

            var body = JsonUtility.ToJson(new NicknameChangePayload
            {
                session_epoch = sessionEpoch,
                username = username ?? "",
            });

            try
            {
                var rpc = await NakamaBootstrap.Instance.Client.RpcAsync(
                    NakamaBootstrap.Instance.Session, RpcChange, body, canceller: ct).ConfigureAwait(false);
                var parsed = JsonUtility.FromJson<NicknameChangeRpcResponse>(rpc?.Payload ?? "{}");
                if (parsed == null)
                    return new NicknameChangeRpcResponse { ok = false, err = "bad_payload" };

                if (parsed.ok && !string.IsNullOrWhiteSpace(parsed.username))
                {
                    await MainThreadDispatcher.RunAsync(() =>
                    {
                        if (parsed.progression != null)
                            PlayerResourcesService.PatchCachedFromProgression(parsed.progression);
                        else if (parsed.resources != null && parsed.resources.ok)
                        {
                            var prog = new Progression
                            {
                                gold = parsed.resources.gold,
                                ore = parsed.resources.ore,
                                ingots = parsed.resources.ingots,
                                matter = parsed.resources.matter,
                                keys = parsed.resources.keys,
                                energy = parsed.resources.energy,
                                energy_max = parsed.resources.energy_max,
                            };
                            PlayerResourcesService.PatchCachedFromProgression(prog);
                        }

                        NotifyUsernameChanged(parsed.username);
                    }).ConfigureAwait(false);
                }

                return parsed;
            }
            catch (Exception e)
            {
                return new NicknameChangeRpcResponse { ok = false, err = e.Message };
            }
        }

        public static string DescribeError(NicknameChangeRpcResponse r)
        {
            if (r == null) return "Неизвестная ошибка.";
            return (r.err ?? "") switch
            {
                "bad_length" => "Ник должен быть от 3 до 17 символов.",
                "bad_chars" => "Только латиница, цифры и _.",
                "same_username" => "Это уже ваш текущий ник.",
                "username_taken" => "Этот ник уже занят.",
                "not_enough_gold" => $"Не хватает золота (нужно {Math.Max(0, r.need)}).",
                "unauthorized" => "Нет сессии.",
                "session_epoch_mismatch" => "Сессия устарела. Перезайдите.",
                _ => string.IsNullOrWhiteSpace(r.err) ? "Не удалось сменить ник." : r.err,
            };
        }

        [Serializable]
        private sealed class NicknameChangePayload
        {
            public int session_epoch;
            public string username;
        }
    }
}
