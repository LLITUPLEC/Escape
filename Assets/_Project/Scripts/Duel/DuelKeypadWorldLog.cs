using System.Collections.Generic;
using System.Text;
using NavKeypad;
using TMPro;
using UnityEngine;

namespace Project.Duel
{
    /// <summary>
    /// Панель попыток «быки : коровы» рядом с 3D-клавиатурой (World Space).
    /// </summary>
    public sealed class DuelKeypadWorldLog : MonoBehaviour
    {
        public const int MaxAttempts = 20;

        [SerializeField] private float panelWidth = 0.40f;
        [SerializeField] private float panelHeight = 0.55f;
        [SerializeField] private float offsetRightLocal = 0.237f;
        [SerializeField] private float offsetUpLocal = -0.02f;
        [SerializeField] private float offsetForwardLocal;

        private readonly List<string> _lines = new();
        private int _attemptSerial;
        private Keypad _keypad;
        private TextMeshProUGUI _body;
        private TextMeshProUGUI _header;
        private Camera _faceCamera;
        private Transform _keypadRoot;
        private IReadOnlyList<string> _initialLines;

        public void Initialize(Keypad keypad, Transform keypadRoot, Camera faceCamera, IReadOnlyList<string> initialLines = null)
        {
            _keypad = keypad;
            _keypadRoot = keypadRoot;
            _faceCamera = faceCamera;
            _initialLines = initialLines;

            if (_keypad != null)
                _keypad.WrongGuessSubmitted += OnWrongGuess;

            BuildVisuals();
        }

        private void OnWrongGuess(string guess, int bulls, int cows)
        {
            if (_lines.Count >= MaxAttempts) return;
            _attemptSerial++;
            _lines.Add(BullsCowsScoring.FormatAttemptLine(_attemptSerial, guess, bulls, cows));
            RefreshText();
        }

        private void LateUpdate()
        {
            if (_faceCamera == null || _keypadRoot == null) return;
            var toCam = _faceCamera.transform.position - transform.position;
            if (toCam.sqrMagnitude < 0.0001f) return;
            transform.rotation = Quaternion.LookRotation(-toCam.normalized, Vector3.up);
        }

        private void BuildVisuals()
        {
            transform.SetParent(_keypadRoot, false);
            transform.localPosition = new Vector3(offsetRightLocal, offsetUpLocal, offsetForwardLocal);
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;

            if (_initialLines != null && _initialLines.Count > 0)
            {
                _lines.Clear();
                _lines.AddRange(_initialLines);
                _attemptSerial = _lines.Count;
            }

            var canvasGo = new GameObject("Canvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 200;
            var scaler = canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 100f;
            canvasGo.AddComponent<UnityEngine.UI.GraphicRaycaster>();

            var rt = canvas.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(panelWidth * 1000f, panelHeight * 1000f);
            canvasGo.transform.localScale = new Vector3(0.0004f, 0.0004f, 0.0004f);

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            panel.transform.SetParent(canvasGo.transform, false);
            var prt = panel.GetComponent<RectTransform>();
            prt.anchorMin = Vector2.zero;
            prt.anchorMax = Vector2.one;
            prt.offsetMin = Vector2.zero;
            prt.offsetMax = Vector2.zero;
            var img = panel.GetComponent<UnityEngine.UI.Image>();
            img.color = new Color(0.04f, 0.06f, 0.05f, 0.92f);
            img.raycastTarget = false;

            var cg = panel.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false;
            cg.interactable = false;

            _header = CreateTmp("Header", panel.transform, 22);
            var hrt = _header.rectTransform;
            hrt.anchorMin = new Vector2(0f, 1f);
            hrt.anchorMax = new Vector2(1f, 1f);
            hrt.pivot = new Vector2(0.5f, 1f);
            hrt.sizeDelta = new Vector2(-20f, 32f);
            hrt.anchoredPosition = new Vector2(0f, -6f);
            _header.alignment = TextAlignmentOptions.TopLeft;
            _header.margin = new Vector4(14f, 0f, 14f, 0f);
            _header.fontStyle = FontStyles.Bold;

            _body = CreateTmp("Body", panel.transform, 32);
            var brt = _body.rectTransform;
            brt.anchorMin = Vector2.zero;
            brt.anchorMax = Vector2.one;
            brt.offsetMin = new Vector2(10f, 10f);
            brt.offsetMax = new Vector2(-10f, -46f);
            _body.alignment = TextAlignmentOptions.TopLeft;
            _body.lineSpacing = -2f;

            RefreshText();
        }

        private static TextMeshProUGUI CreateTmp(string name, Transform parent, float fontSize)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.enableAutoSizing = false;
            tmp.fontSize = fontSize;
            tmp.color = new Color(0.75f, 0.68f, 0.52f, 1f);
            tmp.richText = true;
            tmp.raycastTarget = false;
            if (TMP_Settings.defaultFontAsset != null)
                tmp.font = TMP_Settings.defaultFontAsset;
            return tmp;
        }

        private void RefreshText()
        {
            if (_body == null) return;
            var visibleRows = Mathf.Min(10, MaxAttempts);
            var sb = new StringBuilder();
            var start = Mathf.Max(0, _lines.Count - visibleRows);
            for (var i = start; i < _lines.Count; i++)
            {
                sb.AppendLine(_lines[i]);
            }
            for (var pad = _lines.Count - start; pad < visibleRows; pad++)
                sb.AppendLine("<color=#2A2118>\u00B7 \u00B7 \u00B7 \u00B7 \u00B7</color>");
            _body.text = sb.ToString().TrimEnd();

            if (_header != null)
            {
                _header.text =
                    $"<color=#A8A29E>Попытки:</color>  <color=#F59E0B>{_lines.Count}</color> / {MaxAttempts}";
            }
        }

        private void OnDestroy()
        {
            if (_keypad != null)
                _keypad.WrongGuessSubmitted -= OnWrongGuess;
        }
    }
}
