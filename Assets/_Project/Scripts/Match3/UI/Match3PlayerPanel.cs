using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
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
        [SerializeField] public TMP_Text  avatarLevelText;        // child "lvl" under Avatar — уровень

        [Header("Name")]
        [SerializeField] public TMP_Text nameText;

        [Header("HP")]
        [SerializeField] public Image hpFill;   // Image.Type = Filled, Horizontal
        [SerializeField] public TMP_Text  hpText;

        [Header("Mana")]
        [SerializeField] public Image manaFill;
        [SerializeField] public TMP_Text  manaText;

        [Header("Combat Stats")]
        [FormerlySerializedAs("combatStatsText")]
        [SerializeField] public TMP_Text combatStatsName;
        [SerializeField] public TMP_Text combatStatsValue;
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
            if (avatarLevelText == null && avatarImage != null)
            {
                var lvlTr = avatarImage.transform.Find("lvl");
                if (lvlTr != null) avatarLevelText = lvlTr.GetComponent<TMP_Text>();
            }
            avatarLevelText ??= FindTmpText("lvl");
            nameText ??= FindTmpText("NameText");
            hpText ??= FindTmpText("HpValue") ?? FindTmpText("HpVal");
            manaText ??= FindTmpText("MpValue") ?? FindTmpText("MpVal");

            // Optional widgets (may be created procedurally by DuelMatch3Manager)
            combatStatsName ??= FindTmpText("CombatStatsName") ?? FindTmpText("CombatStatsText");
            combatStatsValue ??= FindTmpText("CombatStatsValue");
            buffStateText ??= FindTmpText("BuffStateText");

            ResolveDamagePopupFromHierarchy();
        }

        /// <summary>
        /// Подхватывает <see cref="DamagePopupView"/> из иерархии под Avatar (как в сцене/префабе),
        /// чтобы рантайм не дублировал виджет инстанцированием префаба.
        /// </summary>
        public void ResolveDamagePopupFromHierarchy()
        {
            if (damagePopup != null)
            {
                EnsureDamagePopupAnchorAssigned();
                return;
            }

            if (avatarImage == null)
                avatarImage = transform.Find("Avatar")?.GetComponent<Image>();
            if (avatarImage == null) return;

            var found = avatarImage.GetComponentInChildren<DamagePopupView>(true);
            if (found == null) return;

            damagePopup = found;
            EnsureDamagePopupAnchorAssigned();
        }

        private void EnsureDamagePopupAnchorAssigned()
        {
            if (damagePopup == null) return;
            if (damagePopupAnchor != null) return;

            var parentRt = damagePopup.transform.parent as RectTransform;
            if (parentRt != null)
                damagePopupAnchor = parentRt;
            else if (avatarImage != null)
                damagePopupAnchor = avatarImage.rectTransform;
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

        public void UpdateAvatarLevel(int level)
        {
            if (avatarLevelText == null)
                ResolveReferences();
            if (avatarLevelText == null) return;
            avatarLevelText.text = level < 0 ? "—" : Mathf.Max(1, level).ToString();
        }

        public void UpdateCombatStats(int damageBonus, int armor, int healBonus, int critChancePercent)
        {
            var dmg = Mathf.Max(0, damageBonus);
            var arm = Mathf.Max(0, armor);
            var heal = Mathf.Max(0, healBonus);
            var crit = Mathf.Max(0, critChancePercent);

            if (combatStatsName != null && combatStatsValue != null)
            {
                combatStatsName.fontSize = CombatStatsFontSize;
                combatStatsValue.fontSize = CombatStatsFontSize;
                combatStatsName.text =
                    "Урон:\n" +
                    "Броня:\n" +
                    "Лечение:\n" +
                    "Крит:";
                combatStatsValue.text =
                    $"{dmg}\n" +
                    $"{arm}\n" +
                    $"{heal}\n" +
                    $"{crit}%";
            }
            else if (combatStatsName != null)
            {
                combatStatsName.fontSize = CombatStatsFontSize;
                combatStatsName.text =
                    $"Урон:   {dmg}\n" +
                    $"Броня:  {arm}\n" +
                    $"Лечение: {heal}\n" +
                    $"Крит:   {crit}%";
            }
            else return;

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

            if (combatStatsName != null)
            {
                combatStatsName.fontSize = CombatStatsFontSize;
                combatStatsName.enableAutoSizing = false;
            }
            if (combatStatsValue != null)
            {
                combatStatsValue.fontSize = CombatStatsFontSize;
                combatStatsValue.enableAutoSizing = false;
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
            if (combatStatsName == null && combatStatsValue == null) return;
            var frameRt = (combatStatsName != null ? combatStatsName.transform.parent : combatStatsValue.transform.parent) as RectTransform;
            if (frameRt == null) return;

            float maxH = 0f;
            if (combatStatsName != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(combatStatsName.rectTransform);
                maxH = Mathf.Max(maxH, combatStatsName.preferredHeight);
            }
            if (combatStatsValue != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(combatStatsValue.rectTransform);
                maxH = Mathf.Max(maxH, combatStatsValue.preferredHeight);
            }
            var requiredHeight = Mathf.Max(CombatStatsMinHeight, maxH + CombatStatsPadding);
            var currentHeight = frameRt.rect.height;
            if (requiredHeight <= currentHeight + 0.5f) return;

            var delta = requiredHeight - currentHeight;
            frameRt.offsetMin = new Vector2(frameRt.offsetMin.x, frameRt.offsetMin.y - delta);
        }
    }
}
