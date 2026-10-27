using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Project.Nakama;
using UnityEngine;

namespace Project.Character
{
    /// <summary>Skin artwork and paired portraits. Unlocks and selection belong to the server.</summary>
    public static class CharacterAppearance
    {
        public sealed class Theme
        {
            public readonly string Id, Name, Description;
            public Theme(string id, string name, string description) { Id = id; Name = name; Description = description; }
        }

        public static readonly IReadOnlyList<Theme> Themes = Array.AsReadOnly(new[]
        {
            new Theme("dawnwarden", "Капитан рассвета", "Бронзовая защита и верный свет экспедиционного фонаря."),
            new Theme("voidranger", "Разведчик глубин", "Лёгкое снаряжение для самых тёмных проходов шахты."),
            new Theme("stormforged", "Мастер грозы", "Усиленный костюм электрика и энергия подземных генераторов."),
            new Theme("emberoracle", "Хранитель горна", "Медная защита, рабочие инструменты и жар плавильных печей."),
            new Theme("frostwraith", "Полярный следопыт", "Тёплая экипировка исследователя ледяных глубин."),
            new Theme("phoenix", "Феникс", "Спасатель, который возвращается даже из самых опасных экспедиций."),
            new Theme("bookmaker", "Хозяин ставок", "Холодный расчёт и безошибочный взгляд на исход турнира."),
            new Theme("railwarden", "Страж магистрали", "Хранитель подземных путей и ветеран стремительных спусков."),
            new Theme("beastbreaker", "Укротитель глубин", "Охотник, которому покорились самые грозные обитатели шахты."),
            new Theme("alchemist", "Алхимик шахты", "Знаток редких реагентов, защитных составов и секретных рецептов.")
        });

        [Serializable]
        public sealed class Selection
        {
            public int version = 1;
            public string body = "";
            public string avatar = "";
        }

        public const string ResourceRoot = "Expedition/Appearance/";
        private const string PrefsPrefix = "escape.appearance.v1.";
        private static readonly Dictionary<string, Sprite> Art = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        private static string _scope;
        private static bool _loaded, _female, _genderLoaded;
        private static Selection _current = new Selection();
        public static event Action Changed;

        public static string BodyId
        {
            get
            {
                RefreshAccount();
                var theme = CharacterAppearanceService.SelectedTheme;
                return theme == "default" ? (_female ? Id(0, true) : "") : theme + (_female ? "-female" : "-male");
            }
        }
        public static string AvatarId => BodyId;
        internal static string LegacyTheme { get { RefreshAccount(); return ThemeFromId(_current.body); } }
        public static bool CanSave => !string.IsNullOrEmpty(LocalAccountCacheScope.Current) && AccountGender.IsLoaded && !AccountGender.CanChoose &&
            CharacterAppearanceService.IsLoaded && !CharacterAppearanceService.IsSaving;
        public static string Id(int theme) => Id(theme, AccountGender.IsFemale);
        public static string Id(int theme, bool female) => Themes[Mathf.Clamp(theme, 0, Themes.Count - 1)].Id + (female ? "-female" : "-male");
        public static bool IsTheme(string id) { foreach (var theme in Themes) if (theme.Id == id) return true; return false; }
        public static string ThemeFromId(string id)
        {
            if (!IsValidId(id)) return null;
            foreach (var theme in Themes) if (id.StartsWith(theme.Id + "-", StringComparison.Ordinal)) return theme.Id;
            return null;
        }

        /// <summary>Retains a theme while migrating an earlier local selection to the account's immutable gender.</summary>
        public static string ForGender(string id, bool female)
        {
            if (!IsValidId(id)) return "";
            foreach (var theme in Themes)
                if (id.StartsWith(theme.Id + "-", StringComparison.Ordinal)) return theme.Id + (female ? "-female" : "-male");
            return "";
        }

        public static bool IsAllowedId(string id) => IsValidId(id) && id == ForGender(id, AccountGender.IsFemale);

        public static bool IsValidId(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            foreach (var theme in Themes)
                if (id == theme.Id + "-male" || id == theme.Id + "-female") return true;
            return false;
        }

        public static Sprite Load(string id, bool avatar)
        {
            if (!IsValidId(id)) return null;
            var path = ResourceRoot + id + (avatar ? "-avatar" : "-body");
            if (!Art.TryGetValue(path, out var sprite) || sprite == null)
            {
                sprite = Resources.Load<Sprite>(path);
                if (sprite != null) Art[path] = sprite;
            }
            return sprite;
        }

        public static Sprite Body(Sprite fallback = null) => AccountGender.IsLoaded ? Load(BodyId, false) ?? fallback : null;
        public static Sprite Avatar(Sprite fallback = null) => AccountGender.IsLoaded ? Load(AvatarId, true) ?? fallback : null;

        /// <summary>Applying an outfit always selects its paired portrait.</summary>
        public static Task<bool> ApplyBodyAsync(string id, CancellationToken ct = default)
        {
            RefreshAccount();
            if (!CanSave || !IsAllowedId(id) || Load(id, false) == null || Load(id, true) == null) return Task.FromResult(false);
            return CharacterAppearanceService.SelectAsync(ThemeFromId(id), ct);
        }

        public static Task<bool> RestoreDefaultAsync(CancellationToken ct = default) => CanSave ? CharacterAppearanceService.SelectAsync("default", ct) : Task.FromResult(false);

        public static Selection Parse(string json)
        {
            Selection value = null;
            try { if (!string.IsNullOrEmpty(json)) value = JsonUtility.FromJson<Selection>(json); }
            catch (ArgumentException) { }
            if (value == null || value.version != 1) return new Selection();
            value.body = IsValidId(value.body) ? value.body : "";
            value.avatar = value.body; // Earlier independently chosen avatars never survive migration.
            return value;
        }

        // Polling by bound images also handles authentication changes without keeping another
        // account's appearance visible when an account cache or scene survives logout.
        public static void RefreshAccount()
        {
            var scope = LocalAccountCacheScope.Current;
            var female = AccountGender.IsFemale;
            var genderLoaded = AccountGender.IsLoaded;
            if (_loaded && string.Equals(scope, _scope, StringComparison.Ordinal) && _female == female && _genderLoaded == genderLoaded) return;
            _scope = scope;
            _female = female; _genderLoaded = genderLoaded;
            _loaded = true;
            _current = string.IsNullOrEmpty(scope) ? new Selection() : Parse(PlayerPrefs.GetString(PrefsPrefix + scope, ""));
            _current.body = ForGender(_current.body, female);
            _current.avatar = ForGender(_current.avatar, female);
            if (female)
            {
                if (string.IsNullOrEmpty(_current.body)) _current.body = Id(0, true);
                if (string.IsNullOrEmpty(_current.avatar)) _current.avatar = Id(0, true);
            }
            Changed?.Invoke();
        }

        private static void ServerChanged() => Changed?.Invoke();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            AccountGender.Changed -= RefreshAccount;
            CharacterAppearanceService.Changed -= ServerChanged;
            _scope = null; _loaded = _female = _genderLoaded = false; _current = new Selection(); Art.Clear(); Changed = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterGenderChanges()
        {
            AccountGender.Changed -= RefreshAccount;
            AccountGender.Changed += RefreshAccount;
            CharacterAppearanceService.Changed -= ServerChanged;
            CharacterAppearanceService.Changed += ServerChanged;
        }
    }
}
