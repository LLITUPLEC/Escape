using System.Threading;
using System.Threading.Tasks;
using Project.Character;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>
    /// XP-бар на главном меню: серый фон, золотое заполнение, по центру — сколько XP до следующего уровня.
    /// Вешается на пустой <c>progress_bar</c> (сохраняет sizeDelta).
    /// Заполнение через anchorMax (без спрайта) — Image.Type.Filled без Source Image всегда рисует полный прямоугольник.
    /// </summary>
    public sealed class MainMenuXpProgressBar : MonoBehaviour
    {
        private static readonly Color TrackColor = new Color(0.42f, 0.42f, 0.45f, 1f);
        private static readonly Color FillColor = new Color(0.83f, 0.65f, 0.18f, 1f);
        private static readonly Color LabelColor = new Color(1f, 1f, 1f, 0.95f);

        [SerializeField] private Image trackImage;
        [SerializeField] private Image fillImage;
        [SerializeField] private TMP_Text label;

        private RectTransform _fillRt;
        private bool _built;
        private int _lastXp = int.MinValue;

        private void Awake() => EnsureBuilt();

        public void ApplyXp(int xp)
        {
            EnsureBuilt();
            if (xp == _lastXp) return;
            _lastXp = xp;

            PlayerLevelXpTable.GetBarState(xp, out _, out var fill01, out var remaining);
            SetFillRatio(fill01);

            if (label != null)
            {
                label.text = remaining <= 0 && fill01 >= 0.999f
                    ? "MAX"
                    : remaining.ToString();
            }
        }

        public async Task RefreshFromServerAsync(CancellationToken ct)
        {
            try
            {
                var profile = await CharacterProfileService.GetAsync(ct).ConfigureAwait(true);
                if (ct.IsCancellationRequested) return;
                if (profile == null || !profile.ok || profile.progression == null)
                {
                    ApplyXp(0);
                    return;
                }

                ApplyXp(Mathf.Max(0, profile.progression.xp));
            }
            catch
            {
                // Не шумим в UI: бар просто останется с последним значением / 0.
            }
        }

        private void SetFillRatio(float fill01)
        {
            fill01 = Mathf.Clamp01(fill01);
            if (_fillRt == null && fillImage != null)
                _fillRt = fillImage.rectTransform;
            if (_fillRt == null) return;

            // Ширина = доля прогресса; спрайт не нужен.
            _fillRt.anchorMin = new Vector2(0f, 0f);
            _fillRt.anchorMax = new Vector2(fill01, 1f);
            _fillRt.offsetMin = Vector2.zero;
            _fillRt.offsetMax = Vector2.zero;
            _fillRt.pivot = new Vector2(0f, 0.5f);
        }

        private void EnsureBuilt()
        {
            if (_built) return;
            _built = true;

            var root = transform as RectTransform;
            if (root == null) return;

            // Сохраняем заданные 162×15 (и любые другие sizeDelta из сцены).
            var size = root.sizeDelta;
            if (size.x < 1f) size.x = 162f;
            if (size.y < 1f) size.y = 15f;
            root.sizeDelta = size;

            trackImage = GetComponent<Image>();
            if (trackImage == null) trackImage = gameObject.AddComponent<Image>();
            trackImage.color = TrackColor;
            trackImage.raycastTarget = false;
            trackImage.type = Image.Type.Simple;

            var fillTr = transform.Find("Fill") as RectTransform;
            if (fillTr == null)
            {
                var go = new GameObject("Fill", typeof(RectTransform));
                fillTr = go.GetComponent<RectTransform>();
                fillTr.SetParent(root, false);
            }

            _fillRt = fillTr;
            fillImage = fillTr.GetComponent<Image>();
            if (fillImage == null) fillImage = fillTr.gameObject.AddComponent<Image>();
            fillImage.color = FillColor;
            fillImage.raycastTarget = false;
            fillImage.type = Image.Type.Simple;
            fillImage.fillAmount = 1f;
            SetFillRatio(0f);

            var labelTr = transform.Find("Label") as RectTransform;
            if (labelTr == null)
            {
                var go = new GameObject("Label", typeof(RectTransform));
                labelTr = go.GetComponent<RectTransform>();
                labelTr.SetParent(root, false);
            }

            labelTr.anchorMin = Vector2.zero;
            labelTr.anchorMax = Vector2.one;
            labelTr.offsetMin = Vector2.zero;
            labelTr.offsetMax = Vector2.zero;
            labelTr.SetAsLastSibling();

            label = labelTr.GetComponent<TMP_Text>();
            if (label == null) label = labelTr.gameObject.AddComponent<TextMeshProUGUI>();
            label.raycastTarget = false;
            label.alignment = TextAlignmentOptions.Center;
            label.color = LabelColor;
            label.fontStyle = FontStyles.Bold;
            label.enableAutoSizing = true;
            label.fontSizeMin = 8f;
            label.fontSizeMax = Mathf.Max(10f, size.y - 1f);
            label.overflowMode = TextOverflowModes.Overflow;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            if (string.IsNullOrEmpty(label.text) || label.text == "+12" || label.text == "—")
                label.text = "—";

            if (TMP_Settings.defaultFontAsset != null)
                label.font = TMP_Settings.defaultFontAsset;
        }
    }
}
