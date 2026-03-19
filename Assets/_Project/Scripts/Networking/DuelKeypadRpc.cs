using System;
using System.Threading.Tasks;
using Nakama;
using Project.Nakama;
using UnityEngine;

namespace Project.Networking
{
    /// <summary>
    /// RPC Nakama: генерация PIN на сервере и проверка попыток (см. Server/nakama/modules/duel_keypad.lua).
    /// </summary>
    public static class DuelKeypadRpc
    {
        public const string RpcEnsurePins = "duel_match_ensure_pins";
        public const string RpcGuess = "duel_keypad_guess";

        /// <param name="userA">Меньший по StringComparer.Ordinal user id пары.</param>
        /// <param name="userB">Больший user id (оба — реальные пользователи Nakama, иначе FK storage).</param>
        public static async Task EnsurePinsAsync(string matchId, string userA, string userB)
        {
            if (string.IsNullOrEmpty(matchId) || NakamaBootstrap.Instance == null ||
                !NakamaBootstrap.Instance.IsReady)
                return;
            if (string.IsNullOrEmpty(userA) || string.IsNullOrEmpty(userB))
                return;

            var payload = "{\"match_id\":\"" + EscapeJson(matchId) + "\",\"user_a\":\"" + EscapeJson(userA) +
                          "\",\"user_b\":\"" + EscapeJson(userB) + "\"}";
            await NakamaBootstrap.Instance.Client.RpcAsync(NakamaBootstrap.Instance.Session, RpcEnsurePins, payload);
        }

        public static async Task<DuelKeypadGuessResult> GuessAsync(string matchId, int doorId, string guess,
            string userA, string userB)
        {
            if (NakamaBootstrap.Instance == null || !NakamaBootstrap.Instance.IsReady)
                return DuelKeypadGuessResult.Fail("nakama_not_ready");
            if (string.IsNullOrEmpty(userA) || string.IsNullOrEmpty(userB))
                return DuelKeypadGuessResult.Fail("no_duel_pair");

            var g = guess ?? "";
            var payload = "{\"match_id\":\"" + EscapeJson(matchId) + "\",\"user_a\":\"" + EscapeJson(userA) +
                          "\",\"user_b\":\"" + EscapeJson(userB) + "\",\"door_id\":" + doorId +
                          ",\"guess\":\"" + EscapeJson(g) + "\"}";
            var rpc = await NakamaBootstrap.Instance.Client.RpcAsync(NakamaBootstrap.Instance.Session, RpcGuess, payload);
            var json = rpc?.Payload;
            if (string.IsNullOrEmpty(json))
                return DuelKeypadGuessResult.Fail("empty_response");

            try
            {
                return JsonUtility.FromJson<DuelKeypadGuessResult>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[DuelKeypadRpc] parse: " + e.Message + " json=" + json);
                return DuelKeypadGuessResult.Fail("parse_error");
            }
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }

    [Serializable]
    public sealed class DuelKeypadGuessResult
    {
        public bool ok;
        public bool granted;
        public int bulls;
        public int cows;
        public string err;

        public static DuelKeypadGuessResult Fail(string e) =>
            new() { ok = false, granted = false, err = e };
    }
}
