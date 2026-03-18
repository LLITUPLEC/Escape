using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace Project.Duel
{
    public sealed class DuelKeypadModal : MonoBehaviour
    {
        private const int DigitCount = 10;

        [Header("Layout (runtime UI)")]
        [SerializeField] private int panelWidth = 420;
        [SerializeField] private int panelHeight = 560;
        [SerializeField] private int fontSize = 28;
        [SerializeField] private int smallFontSize = 20;
        [SerializeField] private float backdropAlpha = 0.6f;

        private Canvas _canvas;
        private Image _backdrop;
        private Text _inputText;
        private Text _attemptsText;

        private Action _onCorrect;
        private string _pinCode;
        private int _codeLength;
        private string _currentInput;
        private readonly List<string> _attemptLines = new();
        private bool _isOpen;

        public bool IsOpen => _isOpen;

        private void Awake()
        {
            EnsureCanvasAndEventSystem();
            BuildUi();
            gameObject.SetActive(false);
        }

        public void Show(string pinCode, int codeLength, Action onCorrect)
        {
            _pinCode = pinCode;
            _codeLength = Mathf.Max(1, codeLength);
            _onCorrect = onCorrect;

            _currentInput = string.Empty;
            _attemptLines.Clear();
            _inputText.text = string.Empty;
            _attemptsText.text = string.Empty;

            _isOpen = true;
            gameObject.SetActive(true);
        }

        public void Close()
        {
            _isOpen = false;
            _onCorrect = null;
            gameObject.SetActive(false);
        }

        private void BuildUi()
        {
            Font font = null;
            // В новых версиях Unity Arial.ttf может быть недоступен, используем LegacyRuntime.ttf.
            try
            {
                font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            catch
            {
                // ignore
            }

            if (font == null)
            {
                try
                {
                    font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                }
                catch
                {
                    // ignore
                }
            }

            // Если шрифт всё равно не удалось получить — просто не задаём его явно.

            _canvas = gameObject.GetComponent<Canvas>();
            if (_canvas == null)
            {
                _canvas = gameObject.AddComponent<Canvas>();
                _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _canvas.overrideSorting = true;
                _canvas.sortingOrder = 2000;
            }

            gameObject.AddComponent<GraphicRaycaster>();

            var panelGO = new GameObject("KeypadPanel");
            panelGO.transform.SetParent(transform, false);

            var panelRect = panelGO.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = Vector2.zero;
            panelRect.sizeDelta = new Vector2(panelWidth, panelHeight);

            _backdrop = panelGO.AddComponent<Image>();
            _backdrop.color = new Color(0f, 0f, 0f, backdropAlpha);

            var vlg = panelGO.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.spacing = 12;
            vlg.padding = new RectOffset(20, 20, 20, 20);

            var titleGO = new GameObject("Title");
            titleGO.transform.SetParent(panelGO.transform, false);
            var titleText = titleGO.AddComponent<Text>();
            if (font != null) titleText.font = font;
            titleText.fontSize = fontSize;
            titleText.alignment = TextAnchor.MiddleCenter;
            titleText.color = Color.white;
            titleText.text = "Введите код";

            var inputGO = new GameObject("Input");
            inputGO.transform.SetParent(panelGO.transform, false);
            var inputRect = inputGO.AddComponent<RectTransform>();
            inputRect.sizeDelta = new Vector2(panelWidth - 40, 48);
            _inputText = inputGO.AddComponent<Text>();
            if (font != null) _inputText.font = font;
            _inputText.fontSize = fontSize;
            _inputText.alignment = TextAnchor.MiddleCenter;
            _inputText.color = Color.white;
            _inputText.text = string.Empty;

            var attemptsGO = new GameObject("Attempts");
            attemptsGO.transform.SetParent(panelGO.transform, false);
            var attemptsRect = attemptsGO.AddComponent<RectTransform>();
            attemptsRect.sizeDelta = new Vector2(panelWidth - 40, 160);
            _attemptsText = attemptsGO.AddComponent<Text>();
            if (font != null) _attemptsText.font = font;
            _attemptsText.fontSize = smallFontSize;
            _attemptsText.alignment = TextAnchor.UpperLeft;
            _attemptsText.color = Color.white;
            _attemptsText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _attemptsText.verticalOverflow = VerticalWrapMode.Overflow;
            _attemptsText.text = string.Empty;

            var gridGO = new GameObject("DigitsGrid");
            gridGO.transform.SetParent(panelGO.transform, false);
            var gridRect = gridGO.AddComponent<RectTransform>();
            gridRect.sizeDelta = new Vector2(panelWidth - 40, 220);

            var grid = gridGO.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(70, 70);
            grid.spacing = new Vector2(10, 10);
            grid.constraint = GridLayoutGroup.Constraint.FixedRowCount;
            grid.constraintCount = 2;

            for (var d = 0; d < DigitCount; d++)
            {
                var digitCopy = d;
                var btnGO = new GameObject($"Digit_{digitCopy}");
                btnGO.transform.SetParent(gridGO.transform, false);

                var btnRect = btnGO.AddComponent<RectTransform>();
                btnRect.sizeDelta = new Vector2(70, 70);

                var btnImage = btnGO.AddComponent<Image>();
                btnImage.color = new Color(1f, 1f, 1f, 0.15f);

                var btn = btnGO.AddComponent<Button>();
                var textGO = new GameObject("Text");
                textGO.transform.SetParent(btnGO.transform, false);

                var textRect = textGO.AddComponent<RectTransform>();
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.offsetMin = Vector2.zero;
                textRect.offsetMax = Vector2.zero;

                var btnText = textGO.AddComponent<Text>();
                if (font != null) btnText.font = font;
                btnText.fontSize = fontSize;
                btnText.alignment = TextAnchor.MiddleCenter;
                btnText.color = Color.white;
                btnText.text = digitCopy.ToString();

                btn.onClick.AddListener(() => OnDigitPressed(digitCopy));
            }
        }

        private void OnDigitPressed(int digit)
        {
            if (!_isOpen) return;
            if (_currentInput.Length >= _codeLength) return;

            _currentInput += digit.ToString();
            _inputText.text = _currentInput;

            if (_currentInput.Length == _codeLength)
            {
                SubmitAttempt();
            }
        }

        private void SubmitAttempt()
        {
            var guess = _currentInput;
            var (x, y) = Score(_pinCode, guess);
            _attemptLines.Add($"{guess} - {x} : {y}");
            _attemptsText.text = FormatAttempts(_attemptLines);

            if (string.Equals(guess, _pinCode, StringComparison.Ordinal))
            {
                _onCorrect?.Invoke();
                Close();
                return;
            }

            // Неправильно — очищаем поле и позволяем ввести заново.
            _currentInput = string.Empty;
            _inputText.text = string.Empty;
        }

        private static (int guessedDigits, int correctPlaceDigits) Score(string pin, string guess)
        {
            var n = pin.Length;
            if (guess.Length != n) return (0, 0);

            var code = pin.ToCharArray();
            var g = guess.ToCharArray();

            var correctPlace = 0;
            var codeCounts = new int[10];
            var guessCounts = new int[10];

            for (var i = 0; i < n; i++)
            {
                if (code[i] == g[i]) correctPlace++;
                codeCounts[code[i] - '0']++;
                guessCounts[g[i] - '0']++;
            }

            var guessed = 0;
            for (var d = 0; d < 10; d++)
            {
                guessed += Math.Min(codeCounts[d], guessCounts[d]);
            }

            return (guessed, correctPlace);
        }

        private static string FormatAttempts(IReadOnlyList<string> lines)
        {
            if (lines.Count == 0) return string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine("[");
            for (var i = 0; i < lines.Count; i++)
            {
                sb.AppendLine($"{i + 1}) {lines[i]}");
            }
            sb.Append("]");
            return sb.ToString();
        }

        private static void EnsureCanvasAndEventSystem()
        {
            // EventSystem нужен для работы Button.onClick.
            var es = UnityEngine.Object.FindFirstObjectByType<EventSystem>();
            if (es == null)
            {
                var esGO = new GameObject("EventSystem");
                es = esGO.AddComponent<EventSystem>();
            }

#if ENABLE_INPUT_SYSTEM
            // Для Input System нужен InputSystemUIInputModule, иначе кнопки могут не реагировать.
            if (es.GetComponent<InputSystemUIInputModule>() == null)
            {
                es.gameObject.AddComponent<InputSystemUIInputModule>();
            }
#else
            if (es.GetComponent<StandaloneInputModule>() == null)
            {
                es.gameObject.AddComponent<StandaloneInputModule>();
            }
#endif
        }
    }
}

