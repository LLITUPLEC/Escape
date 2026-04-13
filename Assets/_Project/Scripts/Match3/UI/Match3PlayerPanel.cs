using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Match3
{
    /// <summary>
    /// Represents one player's sidebar: avatar placeholder, name, HP and Mana bars.
    /// Assign all references in the Inspector (or let Match3PrefabCreator do it).
    /// </summary>
    public sealed class Match3PlayerPanel : MonoBehaviour
    {
        private const float NameFontSize = 22f;
        private const float CombatStatsFontSize = 22f;
        private const float BarValueAutoSizeMin = 22f;
        private const float BarValueAutoSizeMax = 48f;
        private const float BarCornerCutRatio = 0.10f;
        private const float CombatStatsPadding = 28f;
        private const float CombatStatsMinHeight = 110f;

        [Header("Avatar")]
        [SerializeField] public Image avatarImage;
        [SerializeField] public TMP_Text  avatarPlaceholderText;  // shows "?" until real sprite assigned

        [Header("Name")]
        [SerializeField] public TMP_Text nameText;

        [Header("HP")]
        [SerializeField] public Image hpFill;   // Image.Type = Filled, Horizontal
        [SerializeField] public TMP_Text  hpText;

        [Header("Mana")]
        [SerializeField] public Image manaFill;
        [SerializeField] public TMP_Text  manaText;

        [Header("Combat Stats")]
        [SerializeField] public TMP_Text combatStatsText;
        [SerializeField] public TMP_Text buffStateText;

        [Header("Damage Popup")]
        [SerializeField] public RectTransform damagePopupAnchor;
        [SerializeField] public DamagePopupView damagePopup;

        private bool _visualStyleApplied;

        private void Start()
        {
            ResolveReferences();
            ApplyVisualStyle();
            _visualStyleApplied = true;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            ResolveReferences();
        }
#endif

        private void ResolveReferences()
        {
            if (avatarImage == null)
                avatarImage = transform.Find("Avatar")?.GetComponent<Image>();

            avatarPlaceholderText ??= FindTmpText("AvatarTxt") ?? FindTmpText("T");
            nameText ??= FindTmpText("NameText");
            hpText ??= FindTmpText("HpValue") ?? FindTmpText("HpVal");
            manaText ??= FindTmpText("MpValue") ?? FindTmpText("MpVal");

            // Optional widgets (may be created procedurally by DuelMatch3Manager)
            combatStatsText ??= FindTmpText("CombatStatsText");
            buffStateText ??= FindTmpText("BuffStateText");
        }

        private TMP_Text FindTmpText(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            foreach (var t in GetComponentsInChildren<TMP_Text>(true))
                if (t != null && t.gameObject.name == name) return t;
            return null;
        }

        // ─── API ──────────────────────────────────────────────────────────────────

        public void SetPlayerName(string playerName)
        {
            if (nameText != null)
            {
                nameText.fontSize = NameFontSize;
                nameText.enableAutoSizing = false;
                nameText.text = playerName;
            }
        }

        public void UpdateStats(int hp, int maxHp, int mana, int maxMana)
        {
            if (!_visualStyleApplied)
            {
                ResolveReferences();
                ApplyVisualStyle();
                _visualStyleApplied = true;
            }
            ApplyBarFill(hpFill, maxHp > 0 ? Mathf.Clamp01((float)hp / maxHp) : 0f);
            ApplyBarFill(manaFill, maxMana > 0 ? Mathf.Clamp01((float)mana / maxMana) : 0f);

            if (hpText   != null) hpText.text   = $"{hp}/{maxHp}";
            if (manaText != null) manaText.text  = $"{mana}/{maxMana}";
        }

        public void UpdateCombatStats(int damageBonus, int armor, int healBonus, int critChancePercent)
        {
            if (combatStatsText == null) return;
            combatStatsText.fontSize = CombatStatsFontSize;
            combatStatsText.text =
                $"Урон:   {Mathf.Max(0, damageBonus)}\n" +
                $"Броня:  {Mathf.Max(0, armor)}\n" +
                $"Лечение: {Mathf.Max(0, healBonus)}\n" +
                $"Крит:   {Mathf.Max(0, critChancePercent)}%";
            EnsureCombatStatsFrameSize();
        }

        public void UpdateBuffState(int shieldStacks, int shieldTurnsRemaining)
        {
            if (buffStateText == null) return;
            buffStateText.text = shieldStacks > 0 ? $"Щит x{shieldStacks} ({Mathf.Max(0, shieldTurnsRemaining)})" : string.Empty;
        }

        public void ShowDamagePopup(int damageAmount, bool isCrit)
        {
            if (damagePopup == null) return;
            damagePopup.Play(damageAmount, isCrit);
        }

        private static void ApplyBarFill(Image fillImage, float ratio)
        {
            if (fillImage == null) return;
            ratio = Mathf.Clamp01(ratio);

            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillOrigin = 0;
            fillImage.fillClockwise = true;
            fillImage.fillAmount = ratio;

            // Fallback for cases where Filled type behaves inconsistently with no source sprite.
            var rt = fillImage.rectTransform;
            if (rt != null)
            {
                rt.anchorMin = new Vector2(0f, 0f);
                rt.anchorMax = new Vector2(ratio, 1f);
                rt.offsetMin = new Vector2(1f, 1f);
                rt.offsetMax = new Vector2(-1f, -1f);
            }

            var frameRt = fillImage.transform.parent as RectTransform;
            if (frameRt != null)
            {
                var outline = frameRt.GetComponent<Outline>();
                if (outline == null) outline = frameRt.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(0.85f, 0.85f, 0.9f, 0.45f);
                outline.effectDistance = new Vector2(1f, -1f);
            }
        }

        private void ApplyVisualStyle()
        {
            // Старые версии по ошибке вешали срез на корень панели (фон).
            foreach (var wrong in GetComponents<RightCornerCutEffect>())
                Destroy(wrong);

            if (nameText != null)
            {
                nameText.fontSize = NameFontSize;
                nameText.enableAutoSizing = false;
            }

            SetupBarValueText(hpFill, hpText);
            SetupBarValueText(manaFill, manaText);
            ApplyBarCornerCuts(hpFill, RightCornerCutEffect.CutCorner.TopRight);
            ApplyBarCornerCuts(manaFill, RightCornerCutEffect.CutCorner.BottomRight);

            if (combatStatsText != null)
            {
                combatStatsText.fontSize = CombatStatsFontSize;
                combatStatsText.enableAutoSizing = false;
            }

            EnsureCombatStatsFrameSize();
        }

        private void SetupBarValueText(Image fillImage, TMP_Text valueText)
        {
            if (fillImage == null || valueText == null) return;

            // Prefab: Panel → *BarTrack → *BarFill. Procedural UI: Panel → *BarFrame → *BarTrack → *BarFill.
            // Текст должен лежать внутри трека (или совпадать с родителем заливки), а не в корне панели.
            var trackRt = fillImage.transform.parent as RectTransform;
            var panelRt = transform as RectTransform;
            if (trackRt == null || panelRt == null || trackRt == panelRt) return;

            var valueRt = valueText.rectTransform;
            if (valueRt == null) return;

            if (valueRt.parent != trackRt)
                valueRt.SetParent(trackRt, false);

            valueRt.anchorMin = Vector2.zero;
            valueRt.anchorMax = Vector2.one;
            valueRt.offsetMin = new Vector2(4f, 2f);
            valueRt.offsetMax = new Vector2(-4f, -2f);
            valueRt.SetAsLastSibling();

            valueText.alignment = TextAlignmentOptions.Center;
            // В префабе часто стоит Overflow=Truncate: при шрифте выше высоты бара TMP «съедает» строку целиком.
            valueText.overflowMode = TextOverflowModes.Overflow;
            valueText.textWrappingMode = TextWrappingModes.NoWrap;
            valueText.enableAutoSizing = true;
            valueText.fontSizeMin = BarValueAutoSizeMin;
            valueText.fontSizeMax = BarValueAutoSizeMax;
            // Базовый размер для авто-подбора (иначе иногда остаётся слишком мелким из префаба).
            valueText.fontSize = BarValueAutoSizeMax;
            valueText.raycastTarget = false;
        }

        private void ApplyBarCornerCuts(Image fillImage, RightCornerCutEffect.CutCorner corner)
        {
            if (fillImage == null) return;
            var panelRt = transform as RectTransform;
            var trackRt = fillImage.transform.parent as RectTransform;
            ApplyCornerCutToGraphic(fillImage, corner);
            ApplyCornerCutToGraphic(trackRt != null ? trackRt.GetComponent<Graphic>() : null, corner);

            var outerRt = trackRt != null ? trackRt.parent as RectTransform : null;
            if (outerRt != null && outerRt != panelRt)
                ApplyCornerCutToGraphic(outerRt.GetComponent<Graphic>(), corner);
        }

        private static void ApplyCornerCutToGraphic(Graphic graphic, RightCornerCutEffect.CutCorner corner)
        {
            if (graphic == null) return;
            var effect = graphic.GetComponent<RightCornerCutEffect>();
            if (effect == null) effect = graphic.gameObject.AddComponent<RightCornerCutEffect>();
            effect.Configure(corner, BarCornerCutRatio);
        }

        private void EnsureCombatStatsFrameSize()
        {
            if (combatStatsText == null) return;
            var frameRt = combatStatsText.transform.parent as RectTransform;
            if (frameRt == null) return;

            LayoutRebuilder.ForceRebuildLayoutImmediate(combatStatsText.rectTransform);
            var requiredHeight = Mathf.Max(CombatStatsMinHeight, combatStatsText.preferredHeight + CombatStatsPadding);
            var currentHeight = frameRt.rect.height;
            if (requiredHeight <= currentHeight + 0.5f) return;

            var delta = requiredHeight - currentHeight;
            frameRt.offsetMin = new Vector2(frameRt.offsetMin.x, frameRt.offsetMin.y - delta);
        }
    }
}
