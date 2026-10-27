using System;
using System.Threading;
using System.Threading.Tasks;
using Project.Nakama;
using Project.Utils;
using UnityEngine;

namespace Project.Character
{
    /// <summary>Only server replies authorize unlocks and the selected skin. Local preferences are migration hints.</summary>
    public static class CharacterAppearanceService
    {
        [Serializable] public sealed class Requirement { public string id, label; public int current, required; public bool met; }
        [Serializable] public sealed class ThemeState
        {
            public string id; public bool unlocked; public Requirement[] requirements;
            public int unlock_reward_matter; public float unlock_reward_hp_percent; public bool reward_granted;
        }
        [Serializable] public sealed class State
        {
            public bool ok; public string err, gender, selected_theme; public ThemeState[] themes;
            public int unlock_reward_schema, unlocked_prestige_count, rewarded_matter_total, unlock_reward_matter_granted;
            public float passive_hp_bonus_percent;
        }
        [Serializable] private sealed class SelectRequest { public string theme_id; }
        [Serializable] private sealed class GetRequest { public string legacy_theme_id; }
        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
        private static string _scope, _lastError;
        private static bool _loaded, _saving;
        private static State _state;
        public static event Action Changed;
        public static event Action<int> UnlockRewardsGranted;
        public static bool IsLoaded { get { ObserveAccount(); return _loaded; } }
        public static bool IsSaving { get { ObserveAccount(); return _saving; } }
        public static string LastError { get { ObserveAccount(); return _lastError; } }
        public static string SelectedTheme { get { ObserveAccount(); return _loaded ? _state.selected_theme : "default"; } }
        public static float PassiveHpBonusPercent { get { ObserveAccount(); return _loaded ? _state.passive_hp_bonus_percent : 0; } }
        public static int RewardedPrestigeCount { get { ObserveAccount(); return _loaded ? _state.unlocked_prestige_count : 0; } }
        public static int TotalRewardedMatter { get { ObserveAccount(); return _loaded ? _state.rewarded_matter_total : 0; } }

        public static ThemeState GetTheme(string id)
        {
            ObserveAccount();
            if (!_loaded || _state.themes == null) return null;
            foreach (var theme in _state.themes) if (theme != null && theme.id == id) return theme;
            return null;
        }

        private static void ObserveAccount()
        {
            var scope = LocalAccountCacheScope.Current;
            if (string.Equals(scope, _scope, StringComparison.Ordinal) && (!_loaded || _state.gender == (AccountGender.IsFemale ? "female" : "male"))) return;
            _scope = scope; _loaded = _saving = false; _state = null; _lastError = null;
            Changed?.Invoke();
        }

        public static async Task RefreshAsync(CancellationToken ct, bool force = false)
        {
            await Gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var context = await MainThreadDispatcher.RunAsync(() =>
                {
                    ObserveAccount();
                    return AccountGender.IsLoaded && !AccountGender.CanChoose && !string.IsNullOrEmpty(_scope) ? _scope : null;
                }).ConfigureAwait(false);
                if (context == null) return;
                if (!force && await MainThreadDispatcher.RunAsync(() => _loaded).ConfigureAwait(false)) return;
                var bootstrap = NakamaBootstrap.Instance;
                var session = bootstrap?.Session;
                if (bootstrap?.Client == null || session == null) return;
                var legacy = await MainThreadDispatcher.RunAsync(() => CharacterAppearance.LegacyTheme).ConfigureAwait(false);
                var payload = string.IsNullOrEmpty(legacy) ? "{}" : JsonUtility.ToJson(new GetRequest { legacy_theme_id = legacy });
                try
                {
                    var rpc = await bootstrap.Client.RpcAsync(session, "duel_player_cosmetics_get", payload, canceller: ct).ConfigureAwait(false);
                    var reply = string.IsNullOrEmpty(rpc?.Payload) ? null : JsonUtility.FromJson<State>(rpc.Payload);
                    await MainThreadDispatcher.RunAsync(() =>
                    {
                        if (!LocalAccountCacheScope.IsCurrent(context) || !ReferenceEquals(bootstrap, NakamaBootstrap.Instance) || !ReferenceEquals(session, bootstrap.Session)) return;
                        if (!Accept(reply)) { _lastError = Error(reply?.err); Changed?.Invoke(); }
                    }).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception)
                {
                    await MainThreadDispatcher.RunAsync(() =>
                    {
                        if (!LocalAccountCacheScope.IsCurrent(context)) return;
                        _lastError = "Не удалось загрузить образы. Нажмите «Обновить»."; Changed?.Invoke();
                    }).ConfigureAwait(false);
                }
            }
            finally { Gate.Release(); }
        }

        public static async Task<bool> SelectAsync(string themeId, CancellationToken ct)
        {
            var requestedScope = await MainThreadDispatcher.RunAsync(() => LocalAccountCacheScope.Current).ConfigureAwait(false);
            await Gate.WaitAsync(ct).ConfigureAwait(false);
            string context = null;
            try
            {
                context = await MainThreadDispatcher.RunAsync(() =>
                {
                    ObserveAccount();
                    if (!_loaded || !AccountGender.IsLoaded || AccountGender.CanChoose || string.IsNullOrEmpty(_scope) || _scope != requestedScope) return null;
                    if (themeId != "default" && (GetTheme(themeId)?.unlocked != true)) return null;
                    _saving = true; _lastError = null; Changed?.Invoke(); return _scope;
                }).ConfigureAwait(false);
                if (context == null) return false;
                var bootstrap = NakamaBootstrap.Instance;
                var session = bootstrap?.Session;
                if (bootstrap?.Client == null || session == null) return false;
                var rpc = await bootstrap.Client.RpcAsync(session, "duel_player_cosmetics_select", JsonUtility.ToJson(new SelectRequest { theme_id = themeId }), canceller: ct).ConfigureAwait(false);
                var reply = string.IsNullOrEmpty(rpc?.Payload) ? null : JsonUtility.FromJson<State>(rpc.Payload);
                return await MainThreadDispatcher.RunAsync(() =>
                {
                    if (!LocalAccountCacheScope.IsCurrent(context) || !ReferenceEquals(bootstrap, NakamaBootstrap.Instance) || !ReferenceEquals(session, bootstrap.Session)) return false;
                    var accepted = Accept(reply);
                    if (!accepted) { _lastError = Error(reply?.err); Changed?.Invoke(); }
                    return accepted;
                }).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception)
            {
                await MainThreadDispatcher.RunAsync(() =>
                {
                    if (context != null && LocalAccountCacheScope.IsCurrent(context)) { _lastError = "Не удалось применить образ. Повторите попытку."; Changed?.Invoke(); }
                }).ConfigureAwait(false);
                return false;
            }
            finally
            {
                if (context != null) await MainThreadDispatcher.RunAsync(() =>
                {
                    if (LocalAccountCacheScope.IsCurrent(context)) { _saving = false; Changed?.Invoke(); }
                }).ConfigureAwait(false);
                Gate.Release();
            }
        }

        private static bool Accept(State value)
        {
            if (value == null || !value.ok || !AccountGender.IsLoaded || value.gender != (AccountGender.IsFemale ? "female" : "male") || value.themes == null ||
                value.themes.Length != CharacterAppearance.Themes.Count) return false;
            var seen = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            var selectedUnlocked = value.selected_theme == "default";
            var rewardedCount = 0;
            foreach (var theme in value.themes)
            {
                if (theme == null || !CharacterAppearance.IsTheme(theme.id) || !seen.Add(theme.id)) return false;
                if (theme.requirements == null && theme.id == "dawnwarden" && theme.unlocked) theme.requirements = Array.Empty<Requirement>();
                if (theme.requirements == null || theme.requirements.Length > 3) return false;
                if (theme.id == value.selected_theme) selectedUnlocked = theme.unlocked;
                foreach (var requirement in theme.requirements)
                    if (requirement == null || requirement.current < 0 || requirement.required < 1 || string.IsNullOrEmpty(requirement.label)) return false;
                if (value.unlock_reward_schema == 1)
                {
                    var prestige = theme.id != "dawnwarden";
                    if (theme.unlock_reward_matter != (prestige ? 5 : 0) ||
                        !Mathf.Approximately(theme.unlock_reward_hp_percent, prestige ? .5f : 0f) ||
                        theme.reward_granted != (prestige && theme.unlocked)) return false;
                    if (theme.reward_granted) rewardedCount++;
                }
            }
            if (!selectedUnlocked) return false;
            if (value.unlock_reward_schema == 1 && (value.unlocked_prestige_count != rewardedCount ||
                value.rewarded_matter_total != rewardedCount * 5 || !Mathf.Approximately(value.passive_hp_bonus_percent, rewardedCount * .5f) ||
                value.unlock_reward_matter_granted < 0 || value.unlock_reward_matter_granted > value.rewarded_matter_total ||
                value.unlock_reward_matter_granted % 5 != 0)) return false;
            var previousCount = _state?.unlocked_prestige_count ?? 0;
            _state = value; _loaded = true; _lastError = null; Changed?.Invoke();
            if (value.unlock_reward_schema == 1 && rewardedCount > previousCount) Project.UI.MailService.RequestRefresh(true);
            if (value.unlock_reward_schema == 1 && value.unlock_reward_matter_granted > 0 && rewardedCount > previousCount)
                UnlockRewardsGranted?.Invoke(value.unlock_reward_matter_granted);
            return true;
        }

        private static string Error(string code) => code == "appearance_locked" ? "Этот образ ещё не открыт. Выполните условия." :
            code == "registration_incomplete" ? "Завершите создание героя." : "Образы пока недоступны. Нажмите «Обновить».";
        private static void GenderChanged()
        {
            ObserveAccount();
            if (Application.isPlaying && AccountGender.IsLoaded && !AccountGender.CanChoose) _ = RefreshAsync(CancellationToken.None);
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            AccountGender.Changed -= GenderChanged;
            _scope = _lastError = null; _loaded = _saving = false; _state = null; Changed = null; UnlockRewardsGranted = null;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register() { AccountGender.Changed -= GenderChanged; AccountGender.Changed += GenderChanged; GenderChanged(); }
    }
}
