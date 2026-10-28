using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Globalization;
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
            public int unlock_reward_tesseract, unlock_reward_heal_flat;
            public float unlock_reward_armor_percent, unlock_reward_damage_percent, unlock_reward_heal_percent, unlock_reward_crit_percent;
        }
        [Serializable] public sealed class State
        {
            public bool ok; public string err, gender, selected_theme; public ThemeState[] themes;
            public int unlock_reward_schema, unlocked_prestige_count, rewarded_matter_total, unlock_reward_matter_granted;
            public float passive_hp_bonus_percent;
            public int rewarded_tesseract_total, unlock_reward_tesseract_granted, passive_heal_bonus_flat;
            public float passive_armor_bonus_percent, passive_damage_bonus_percent, passive_heal_bonus_percent, passive_crit_bonus_percent;
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
        public static int TotalRewardedTesseracts { get { ObserveAccount(); return _loaded ? _state.rewarded_tesseract_total : 0; } }
        public static float PassiveArmorBonusPercent { get { ObserveAccount(); return _loaded ? _state.passive_armor_bonus_percent : 0; } }
        public static float PassiveDamageBonusPercent { get { ObserveAccount(); return _loaded ? _state.passive_damage_bonus_percent : 0; } }
        public static float PassiveHealBonusPercent { get { ObserveAccount(); return _loaded ? _state.passive_heal_bonus_percent : 0; } }
        public static float PassiveCritBonusPercent { get { ObserveAccount(); return _loaded ? _state.passive_crit_bonus_percent : 0; } }
        public static int PassiveHealBonusFlat { get { ObserveAccount(); return _loaded ? _state.passive_heal_bonus_flat : 0; } }

        // This mirror validates the server wire contract. Gameplay always uses server-computed stats.
        public static ThemeState RewardDefinition(string id) => id switch
        {
            "dawnwarden" => new ThemeState { id = id },
            "voidranger" => new ThemeState { id = id, unlock_reward_matter = 15, unlock_reward_hp_percent = 1 },
            "stormforged" => new ThemeState { id = id, unlock_reward_matter = 30, unlock_reward_crit_percent = 1 },
            "emberoracle" => new ThemeState { id = id, unlock_reward_matter = 30, unlock_reward_damage_percent = 1 },
            "frostwraith" => new ThemeState { id = id, unlock_reward_matter = 50, unlock_reward_heal_percent = 2 },
            "phoenix" => new ThemeState { id = id, unlock_reward_matter = 100, unlock_reward_hp_percent = 2, unlock_reward_armor_percent = 2 },
            "bookmaker" => new ThemeState { id = id, unlock_reward_matter = 50, unlock_reward_hp_percent = 2 },
            "railwarden" => new ThemeState { id = id, unlock_reward_matter = 40, unlock_reward_hp_percent = 2, unlock_reward_armor_percent = 2 },
            "beastbreaker" => new ThemeState { id = id, unlock_reward_matter = 300, unlock_reward_hp_percent = 5, unlock_reward_armor_percent = 5, unlock_reward_damage_percent = 5, unlock_reward_heal_percent = 5 },
            "alchemist" => new ThemeState { id = id, unlock_reward_matter = 50, unlock_reward_tesseract = 1, unlock_reward_heal_flat = 300 },
            _ => null
        };

        private static string BonusText(float hp, float armor, float damage, float heal, float crit, int flatHeal)
        {
            var parts = new List<string>();
            string N(float value) => value.ToString("0.#", CultureInfo.GetCultureInfo("ru-RU"));
            if (hp > 0) parts.Add("+" + N(hp) + "% HP");
            if (armor > 0) parts.Add("+" + N(armor) + "% брони");
            if (damage > 0) parts.Add("+" + N(damage) + "% урона");
            if (heal > 0) parts.Add("+" + N(heal) + "% лечения");
            if (crit > 0) parts.Add("+" + N(crit) + " п.п. к шансу крита");
            if (flatHeal > 0) parts.Add("+" + flatHeal + " лечения");
            return parts.Count == 0 ? "Без постоянного бонуса" : string.Join(" · ", parts) + " навсегда";
        }
        public static string FormatThemeBonus(ThemeState theme) => theme == null ? "Бонусы загружаются…" :
            BonusText(theme.unlock_reward_hp_percent, theme.unlock_reward_armor_percent, theme.unlock_reward_damage_percent,
                theme.unlock_reward_heal_percent, theme.unlock_reward_crit_percent, theme.unlock_reward_heal_flat);
        public static string FormatThemeReward(ThemeState theme) => theme == null ? "Награды загружаются…" :
            theme.unlock_reward_matter + " материи" + (theme.unlock_reward_tesseract > 0 ? " · " + theme.unlock_reward_tesseract + " тессеракт" : "");
        public static string FormatCollectionBonus()
        {
            ObserveAccount();
            return !_loaded ? "Бонусы коллекции загружаются…" :
                "Коллекция: " + BonusText(_state.passive_hp_bonus_percent, _state.passive_armor_bonus_percent, _state.passive_damage_bonus_percent,
                    _state.passive_heal_bonus_percent, _state.passive_crit_bonus_percent, _state.passive_heal_bonus_flat) +
                "\nНаграды за открытие: " + _state.rewarded_matter_total + " материи" +
                (_state.rewarded_tesseract_total > 0 ? " · " + _state.rewarded_tesseract_total + " тессеракт" : "");
        }

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
            if (value == null || !value.ok || value.unlock_reward_schema < 0 || value.unlock_reward_schema > 2 || !AccountGender.IsLoaded || value.gender != (AccountGender.IsFemale ? "female" : "male") || value.themes == null ||
                value.themes.Length != CharacterAppearance.Themes.Count) return false;
            var seen = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            var selectedUnlocked = value.selected_theme == "default";
            var rewardedCount = 0;
            var matterTotal = 0; var tesseractTotal = 0; var hp = 0f; var armor = 0f; var damage = 0f; var heal = 0f; var crit = 0f; var flatHeal = 0;
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
                else if (value.unlock_reward_schema == 2)
                {
                    var expected = RewardDefinition(theme.id);
                    if (theme.unlock_reward_matter != expected.unlock_reward_matter || theme.unlock_reward_tesseract != expected.unlock_reward_tesseract ||
                        theme.unlock_reward_heal_flat != expected.unlock_reward_heal_flat ||
                        !Mathf.Approximately(theme.unlock_reward_hp_percent, expected.unlock_reward_hp_percent) ||
                        !Mathf.Approximately(theme.unlock_reward_armor_percent, expected.unlock_reward_armor_percent) ||
                        !Mathf.Approximately(theme.unlock_reward_damage_percent, expected.unlock_reward_damage_percent) ||
                        !Mathf.Approximately(theme.unlock_reward_heal_percent, expected.unlock_reward_heal_percent) ||
                        !Mathf.Approximately(theme.unlock_reward_crit_percent, expected.unlock_reward_crit_percent) ||
                        theme.reward_granted != (theme.id != "dawnwarden" && theme.unlocked)) return false;
                    if (theme.reward_granted)
                    {
                        rewardedCount++; matterTotal += expected.unlock_reward_matter; tesseractTotal += expected.unlock_reward_tesseract;
                        hp += expected.unlock_reward_hp_percent; armor += expected.unlock_reward_armor_percent; damage += expected.unlock_reward_damage_percent;
                        heal += expected.unlock_reward_heal_percent; crit += expected.unlock_reward_crit_percent; flatHeal += expected.unlock_reward_heal_flat;
                    }
                }
            }
            if (!selectedUnlocked) return false;
            if (value.unlock_reward_schema == 1 && (value.unlocked_prestige_count != rewardedCount ||
                value.rewarded_matter_total != rewardedCount * 5 || !Mathf.Approximately(value.passive_hp_bonus_percent, rewardedCount * .5f) ||
                value.unlock_reward_matter_granted < 0 || value.unlock_reward_matter_granted > value.rewarded_matter_total ||
                value.unlock_reward_matter_granted % 5 != 0)) return false;
            if (value.unlock_reward_schema == 2 && (value.unlocked_prestige_count != rewardedCount || value.rewarded_matter_total != matterTotal ||
                value.rewarded_tesseract_total != tesseractTotal || value.passive_heal_bonus_flat != flatHeal ||
                !Mathf.Approximately(value.passive_hp_bonus_percent, hp) || !Mathf.Approximately(value.passive_armor_bonus_percent, armor) ||
                !Mathf.Approximately(value.passive_damage_bonus_percent, damage) || !Mathf.Approximately(value.passive_heal_bonus_percent, heal) ||
                !Mathf.Approximately(value.passive_crit_bonus_percent, crit) || value.unlock_reward_matter_granted < 0 ||
                value.unlock_reward_matter_granted > matterTotal || value.unlock_reward_tesseract_granted < 0 ||
                value.unlock_reward_tesseract_granted > tesseractTotal)) return false;
            var previousCount = _state?.unlocked_prestige_count ?? 0;
            _state = value; _loaded = true; _lastError = null; Changed?.Invoke();
            var rewardsGranted = value.unlock_reward_matter_granted > 0 || value.unlock_reward_tesseract_granted > 0;
            if (value.unlock_reward_schema >= 1 && (rewardedCount > previousCount || rewardsGranted)) Project.UI.MailService.RequestRefresh(true);
            if (value.unlock_reward_schema >= 1 && value.unlock_reward_matter_granted > 0 && (rewardedCount > previousCount || value.unlock_reward_schema == 2))
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
