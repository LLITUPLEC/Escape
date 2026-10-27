using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    public sealed class ExpeditionTournamentEntryAction : MonoBehaviour
    {
        [NonSerialized] public Button.ButtonClickedEvent Submit;
    }

    /// <summary>Local stake preview; only the explicit confirmation invokes the existing paid-entry callback.</summary>
    public sealed class ExpeditionTournamentStakeChooser : MonoBehaviour
    {
        public sealed class Choice
        {
            public string Tier, Currency;
            public int Stake, Gold, Ore, IngotPrize, Consolation, RecipeFallbackIngots;
            public string StakeLabel => Number(Stake) + " " + Currency;
            public string PrizeLabel => IngotPrize > 0 ? Number(Gold) + " золота · " + Number(Ore) + " руды\n" + Number(IngotPrize) + " слитков + рецепт*"
                : Gold > 0 && Ore > 0 ? Number(Gold) + " золота\n" + Number(Ore) + " руды"
                : Gold > 0 ? Number(Gold) + " золота" : Number(Ore) + " руды";
            public string DifferenceLabel => Currency == "золота" ? "Базовая разница: +" + Number(Gold - Stake) + " золота"
                : Currency == "руды" ? "Базовая разница: +" + Number(Ore - Stake) + " руды"
                : Consolation > 0 ? "За завершённый проигрыш: " + Consolation + " слитка того же цвета"
                : "Взнос в слитках, приз — в золоте и руде";
        }

        private sealed class Row
        {
            public Choice Choice;
            public Button Button;
            public Button.ButtonClickedEvent Submit;
            public GameObject Mark;
        }

        private RectTransform _panel;
        private Button _confirm;
        private TMP_Text _confirmLabel;
        private readonly List<Row> _rows = new List<Row>();
        private Row _selected;
        private bool _submitted;
        public string Kind { get; private set; }
        public int RulesVersion { get; private set; } = 2;
        public string SelectedTier => _selected?.Choice.Tier;
        public Button ConfirmButton => _confirm;

        private static string Number(int amount) => amount.ToString("N0", CultureInfo.GetCultureInfo("ru-RU"));

        /// <summary>Mirrors the seven existing server entry tiers; predictions adjust the champion's base prize separately.</summary>
        public static Choice Describe(string kind, string tier, int rulesVersion = 2)
        {
            if (kind == "ore")
            {
                if (tier != "500" && tier != "2000") return null;
                var stake = tier == "500" ? 500 : 2000;
                return new Choice { Tier = tier, Currency = "руды", Stake = stake, Ore = stake * 5 };
            }
            if (kind == "gold")
            {
                if (tier != "600" && tier != "2400") return null;
                var stake = tier == "600" ? 600 : 2400;
                return new Choice { Tier = tier, Currency = "золота", Stake = stake, Gold = stake * 5 };
            }
            if (tier != "green" && tier != "blue" && tier != "purple") return null;
            var reward = tier == "green" ? 300 : tier == "blue" ? 600 : 1200;
            var currency = tier == "green" ? "зелёных слитков" : tier == "blue" ? "синих слитков" : "фиолетовых слитков";
            var index = tier == "green" ? 0 : tier == "blue" ? 1 : 2;
            return new Choice { Tier = tier, Currency = currency, Stake = rulesVersion <= 1 ? 50 : new[]{10,15,20}[index],
                Gold = reward, Ore = reward, IngotPrize = rulesVersion <= 1 ? 0 : new[]{20,30,40}[index],
                Consolation = rulesVersion <= 1 ? 0 : new[]{2,3,4}[index], RecipeFallbackIngots = rulesVersion <= 1 ? 0 : new[]{10,15,20}[index] };
        }

        public static void Apply(Transform root, TMP_Text title, Transform close, string kind, int rulesVersion = 2)
        {
            if (root == null) return;
            var view = ExpeditionUi.Ensure<ExpeditionTournamentStakeChooser>(root.gameObject);
            view.Configure(title, close, kind, rulesVersion);
        }

        private void Configure(TMP_Text title, Transform close, string kind, int rulesVersion)
        {
            RulesVersion = Math.Max(1, rulesVersion);
            Kind = kind == "ore" || kind == "gold" ? kind : "smith";
            if (_panel == null) _panel = transform.Find("Panel") as RectTransform;
            if (_panel == null) return;
            var safe = transform.Find("StakeSafeArea") as RectTransform;
            if (safe == null)
            {
                safe = ExpeditionUi.Rect(transform, "StakeSafeArea", 0, 0, 1, 1);
                safe.gameObject.AddComponent<ExpeditionSafeArea>();
            }
            _panel.SetParent(safe, false);
            DisableLayout(_panel);
            var crafting = Kind == "smith" && RulesVersion >= 2;
            ExpeditionUi.SetRect(_panel, .075f, crafting ? .18f : Kind == "smith" ? .20f : .25f, .925f, crafting ? .82f : Kind == "smith" ? .80f : .75f);
            var surface = ExpeditionUi.StylePanel(_panel.GetComponent<Image>(), ExpeditionTheme.Panel);
            surface.raycastTarget = true;
            // Consume blank panel clicks instead of bubbling into the dismissing dim button.
            var shield = ExpeditionUi.Ensure<Button>(_panel.gameObject);
            shield.targetGraphic = surface;
            shield.transition = Selectable.Transition.None;
            shield.navigation = new Navigation { mode = Navigation.Mode.None };
            if (title != null)
            {
                title.gameObject.SetActive(true);
                title.text = "Выберите взнос";
                ExpeditionUi.SetRect(title.rectTransform, .055f, .915f, .945f, .977f);
                TextStyle(title, 39, ExpeditionTheme.Ivory);
            }
            var name = Kind == "ore" ? "Турнир руды" : Kind == "gold" ? "Турнир золота" : "Турнир кузнеца";
            Label(_panel, "StakeTournamentName", name + " · 8 участников" + (Kind == "smith" && RulesVersion <= 1 ? " · прежние правила" : ""), 25, .06f, .865f, .94f, .915f, ExpeditionTheme.Muted);
            Label(_panel, "StakeColumnHeading", "ВЗНОС", 23, .075f, .805f, .44f, .85f, ExpeditionTheme.Muted);
            Label(_panel, "PrizeColumnHeading", "БАЗОВЫЙ ПРИЗ ЧЕМПИОНУ", 23, .465f, .805f, .95f, .85f, ExpeditionTheme.Amber);
            var oldHint = _panel.Find("ExpeditionStakeHint");
            if (oldHint != null) oldHint.gameObject.SetActive(false);

            _rows.Clear(); _selected = null; _submitted = false;
            for (var i = 0; i < _panel.childCount; i++)
            {
                var row = _panel.GetChild(i);
                if (!row.name.StartsWith("Row_", StringComparison.Ordinal)) continue;
                var button = row.GetComponent<Button>();
                if (button == null) continue;
                var tier = row.name.Substring(row.name.LastIndexOf('_') + 1);
                var choice = Describe(Kind, tier, RulesVersion);
                if (choice == null) continue;
                var action = ExpeditionUi.Ensure<ExpeditionTournamentEntryAction>(button.gameObject);
                if (action.Submit == null) action.Submit = button.onClick;
                _rows.Add(new Row { Choice = choice, Button = button, Submit = action.Submit });
            }
            var stride = (crafting ? .395f : .505f) / Mathf.Max(1, _rows.Count);
            for (var i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                var target = row.Button.transform;
                DisableLayout(target as RectTransform);
                var top = .79f - i * stride;
                ExpeditionUi.SetRect(target as RectTransform, .045f, top - stride + .012f, .955f, top);
                foreach (var legacy in new[] { "CostCol", "PrizeCol", "ExpeditionStakeCost", "ExpeditionStakePrize", "ExpeditionStakeArrow" })
                {
                    var child = target.Find(legacy);
                    if (child != null) child.gameObject.SetActive(false);
                }
                row.Button.onClick = new Button.ButtonClickedEvent();
                row.Button.onClick.AddListener(() => Select(row));
                ExpeditionUi.StyleButton(row.Button, false, 30);
                var resource = target.Find("ResIcon") as RectTransform;
                if (resource != null)
                {
                    ExpeditionUi.SetRect(resource, .025f, .34f, .15f, .83f);
                    var image = resource.GetComponent<Image>();
                    image.preserveAspect = true; image.raycastTarget = false;
                }
                Label(target, "StakeAmount", Number(row.Choice.Stake), 39, .16f, .53f, .435f, .91f, ExpeditionTheme.Ivory);
                Label(target, "StakeCurrency", row.Choice.Currency, 24, .145f, .28f, .45f, .57f, ExpeditionTheme.Muted);
                Label(target, "ChampionPrize", row.Choice.PrizeLabel, crafting ? 26 : 31, .47f, .30f, .965f, .91f, ExpeditionTheme.Amber);
                Label(target, "NetDifference", row.Choice.DifferenceLabel, crafting ? 21 : 22, .06f, .10f, .965f, .31f, ExpeditionTheme.Muted);
                var mark = target.Find("SelectedStakeMark")?.GetComponent<Image>();
                if (mark == null) mark = ExpeditionUi.Fill(target, "SelectedStakeMark", .008f, .16f, .018f, .84f, ExpeditionTheme.Amber);
                mark.raycastTarget = false; mark.gameObject.SetActive(false); row.Mark = mark.gameObject;
            }
            Label(_panel, "StakeRules", crafting
                ? "*Рецепт того же цвета, с побеждённого открытого этажа.\nНет доступного нового рецепта — ещё 10/15/20 слитков.\nПрогнозы бесплатны: ±20% только к золоту и руде.\nДо старта — полный возврат. После старта взнос не возвращается.\nЗа завершённый проигрыш награда разовая.\nПри заполненном инвентаре предметы ожидают места."
                : "До старта можно выйти из очереди с полным возвратом взноса.\nПосле старта взнос не возвращается. Приз — только чемпиону.\nПрогнозы бесплатны: верный +20%, неверный −20% к призу.",
                crafting ? 21 : 22, .055f, .175f, .945f, crafting ? .395f : .285f, ExpeditionTheme.Muted);
            if (_confirm == null)
            {
                _confirm = ExpeditionUi.Button(_panel, "ConfirmTournamentEntry", "Выберите взнос выше", .055f, .09f, .945f, .165f, Confirm, true, 31);
                _confirmLabel = _confirm.GetComponentInChildren<TMP_Text>(true);
            }
            _confirm.interactable = false;
            _confirmLabel.text = "Выберите взнос выше";
            if (close != null)
            {
                ExpeditionUi.SetRect(close as RectTransform, .22f, .023f, .78f, .078f);
                ExpeditionUi.StyleButton(close.GetComponent<Button>(), false, 27);
                ExpeditionUi.Label(close.GetComponent<Button>(), "Отмена");
            }
        }

        public bool SelectTier(string tier)
        {
            foreach (var row in _rows) if (row.Choice.Tier == tier) { Select(row); return true; }
            return false;
        }

        private void Select(Row row)
        {
            if (_submitted) return;
            _selected = row;
            foreach (var candidate in _rows) candidate.Mark.SetActive(candidate == row);
            _confirm.interactable = true;
            _confirmLabel.text = "Внести " + row.Choice.StakeLabel + " и войти";
            _confirmLabel.enableAutoSizing = true;
            _confirmLabel.fontSizeMin = 25; _confirmLabel.fontSizeMax = 31;
        }

        private void Confirm()
        {
            if (_selected == null || _submitted || !_confirm.interactable) return;
            _submitted = true;
            _confirm.interactable = false;
            _selected.Submit.Invoke();
        }

        private static TMP_Text Label(Transform parent, string name, string value, float size, float x0, float y0, float x1, float y1, Color color)
        {
            var text = parent.Find(name)?.GetComponent<TMP_Text>();
            if (text == null) text = ExpeditionUi.Text(parent, name, value, size, x0, y0, x1, y1, TextAlignmentOptions.Center);
            else { text.text = value; ExpeditionUi.SetRect(text.rectTransform, x0, y0, x1, y1); }
            TextStyle(text, size, color);
            return text;
        }

        private static void TextStyle(TMP_Text text, float size, Color color)
        {
            text.font = ExpeditionTheme.Font; text.fontSize = text.fontSizeMax = size;
            text.fontSizeMin = size * .87f; text.enableAutoSizing = true;
            text.color = color; text.raycastTarget = false; text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Ellipsis;
        }

        private static void DisableLayout(RectTransform root)
        {
            if (root == null) return;
            var group = root.GetComponent<LayoutGroup>(); if (group != null) group.enabled = false;
            var fitter = root.GetComponent<ContentSizeFitter>(); if (fitter != null) fitter.enabled = false;
        }
    }
}
