using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace NavKeypad
{
    public class Keypad : MonoBehaviour
    {
        [Header("Events")]
        [SerializeField] private UnityEvent onAccessGranted;
        [SerializeField] private UnityEvent onAccessDenied;
        [Header("Combination Code (9 Numbers Max)")]
        [SerializeField] private int keypadCombo = 12345;

        public UnityEvent OnAccessGranted => onAccessGranted;
        public UnityEvent OnAccessDenied => onAccessDenied;

        /// <summary>Событие после неверного ввода (угадайка «быки : коровы»). Только при полной длине кода.</summary>
        public event Action<string, int, int> WrongGuessSubmitted;

        [Header("Optional — оттенок корпуса кнопок (не трогает общий материал в проекте)")]
        [Tooltip("Белый = без изменений. Иначе умножается с исходным _BaseColor через MaterialPropertyBlock.")]
        [SerializeField] private Color buttonBodyTint = Color.white;

        private string _pinString = "";
        private int _expectedCodeLength;
        private Action<string> _serverGuessInvoker;
        private bool _guessPending;
        private readonly List<MeshRenderer> _buttonRenderers = new();
        private MaterialPropertyBlock _buttonMpb;

        [Header("Component References")]
        [SerializeField] private Renderer panelMesh;
        [SerializeField] private TMP_Text keypadDisplayText;
        [Tooltip("Цвет цифр на экране клавиатуры (TMP). Белый = как в префабе.")]
        [SerializeField] private Color displayDigitsColor = Color.white;
        [SerializeField] private AudioSource audioSource;

        [Header("Settings")]
        [SerializeField] private string accessGrantedText = "Granted";
        [SerializeField] private string accessDeniedText = "Denied";

        [Header("Visuals")]
        [SerializeField] private float displayResultTime = 1f;
        [Range(0, 5)]
        [SerializeField] private float screenIntensity = 2.5f;
        [Header("Colors")]
        [SerializeField] private Color screenNormalColor = new Color(0.98f, 0.50f, 0.032f, 1f);
        [SerializeField] private Color screenDeniedColor = new Color(1f, 0f, 0f, 1f);
        [SerializeField] private Color screenGrantedColor = new Color(0f, 0.62f, 0.07f, 1f);
        [Header("SoundFx")]
        [SerializeField] private AudioClip buttonClickedSfx;
        [SerializeField] private AudioClip accessDeniedSfx;
        [SerializeField] private AudioClip accessGrantedSfx;

        private string currentInput;
        private bool displayingResult = false;
        private bool accessWasGranted = false;

        /// <summary>Локальный режим (префаб / тест): задать код и длину.</summary>
        public void ApplyCombinationAndReset(int combo, int codeLength = 0)
        {
            StopAllCoroutines();
            _guessPending = false;
            _serverGuessInvoker = null;
            keypadCombo = combo;
            _expectedCodeLength = codeLength > 0 ? codeLength : 0;
            _pinString = codeLength > 0 ? combo.ToString().PadLeft(codeLength, '0') : combo.ToString();
            accessWasGranted = false;
            displayingResult = false;
            currentInput = "";
            if (panelMesh != null)
                panelMesh.material.SetVector("_EmissionColor", screenNormalColor * screenIntensity);
            ApplyButtonBodyTintIfNeeded();
            ApplyDisplayDigitsColor();
            UpdateDisplayText();
        }

        /// <summary>Дуэль: длина кода + проверка через Nakama RPC (без локального PIN).</summary>
        public void ConfigureDuelSession(int codeLength)
        {
            StopAllCoroutines();
            _guessPending = false;
            _expectedCodeLength = Mathf.Max(1, codeLength);
            _pinString = "";
            keypadCombo = 0;
            accessWasGranted = false;
            displayingResult = false;
            currentInput = "";
            if (panelMesh != null)
                panelMesh.material.SetVector("_EmissionColor", screenNormalColor * screenIntensity);
            ApplyButtonBodyTintIfNeeded();
            ApplyDisplayDigitsColor();
            UpdateDisplayText();
        }

        public void SetServerGuessInvoker(Action<string> invoker) => _serverGuessInvoker = invoker;

        public void ClearDuelNetworking()
        {
            _serverGuessInvoker = null;
            _guessPending = false;
        }

        public void AbortPendingGuess() => _guessPending = false;

        /// <summary>Ответ RPC <see cref="Project.Networking.DuelKeypadGuessResult"/>.</summary>
        public void ApplyServerGuessOutcome(Project.Networking.DuelKeypadGuessResult r, string guessSubmitted)
        {
            _guessPending = false;
            if (displayingResult || accessWasGranted) return;

            if (!r.ok)
            {
                Debug.LogWarning("[Keypad] duel_keypad_guess: " + r.err);
                if (audioSource != null && accessDeniedSfx != null)
                    audioSource.PlayOneShot(accessDeniedSfx);
                return;
            }

            if (r.granted)
            {
                StartCoroutine(GrantFromServerRoutine());
                return;
            }

            WrongGuessSubmitted?.Invoke(guessSubmitted, r.bulls, r.cows);
            StartCoroutine(DisplayResultRoutine(false));
        }

        private IEnumerator GrantFromServerRoutine()
        {
            displayingResult = true;
            AccessGranted();
            displayingResult = false;
            yield break;
        }

        public void EnsureDisplayReference()
        {
            if (keypadDisplayText != null) return;
            foreach (var t in GetComponentsInChildren<TMP_Text>(true))
            {
                if (t.gameObject.name == "DisplayText")
                {
                    keypadDisplayText = t;
                    return;
                }
            }
            foreach (var t in GetComponentsInChildren<TMP_Text>(true))
            {
                keypadDisplayText = t;
                return;
            }
        }

        private void Awake()
        {
            EnsureDisplayReference();
            CacheButtonRenderers();
            ClearInput();
            if (panelMesh != null)
                panelMesh.material.SetVector("_EmissionColor", screenNormalColor * screenIntensity);
            ApplyButtonBodyTintIfNeeded();
            ApplyDisplayDigitsColor();
        }

        private void ApplyDisplayDigitsColor()
        {
            if (keypadDisplayText == null) return;
            if (IsEffectivelyWhite(displayDigitsColor)) return;
            keypadDisplayText.color = displayDigitsColor;
        }

        private void CacheButtonRenderers()
        {
            _buttonRenderers.Clear();
            foreach (var r in GetComponentsInChildren<MeshRenderer>(true))
            {
                var n = r.gameObject.name;
                if (n.StartsWith("bttn", StringComparison.OrdinalIgnoreCase))
                    _buttonRenderers.Add(r);
            }
        }

        public void SetButtonBodyTint(Color tint)
        {
            buttonBodyTint = tint;
            ApplyButtonBodyTintIfNeeded();
        }

        private void ApplyButtonBodyTintIfNeeded()
        {
            if (_buttonRenderers.Count == 0) return;
            if (_buttonMpb == null)
                _buttonMpb = new MaterialPropertyBlock();
            if (IsEffectivelyWhite(buttonBodyTint))
            {
                foreach (var r in _buttonRenderers)
                    r.SetPropertyBlock(null);
                return;
            }

            foreach (var r in _buttonRenderers)
            {
                var mat = r.sharedMaterial;
                if (mat == null) continue;
                _buttonMpb.Clear();
                var baseCol = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : Color.white;
                _buttonMpb.SetColor("_BaseColor", baseCol * buttonBodyTint);
                if (mat.HasProperty("_Color"))
                    _buttonMpb.SetColor("_Color", baseCol * buttonBodyTint);
                r.SetPropertyBlock(_buttonMpb);
            }
        }

        private static bool IsEffectivelyWhite(Color c) =>
            Mathf.Approximately(c.r, 1f) && Mathf.Approximately(c.g, 1f) &&
            Mathf.Approximately(c.b, 1f) && Mathf.Approximately(c.a, 1f);

        private void UpdateDisplayText()
        {
            if (keypadDisplayText == null) return;

            if (_expectedCodeLength <= 0)
            {
                keypadDisplayText.text = currentInput ?? "";
                return;
            }

            var parts = new List<string>(_expectedCodeLength);
            for (var i = 0; i < _expectedCodeLength; i++)
            {
                parts.Add(i < currentInput.Length ? currentInput[i].ToString() : "_");
            }
            keypadDisplayText.text = string.Join(" ", parts);
        }

        public void AddInput(string input)
        {
            if (audioSource != null && buttonClickedSfx != null)
                audioSource.PlayOneShot(buttonClickedSfx);
            if (displayingResult || accessWasGranted) return;

            switch (input)
            {
                case "enter":
                    if (_expectedCodeLength > 0 &&
                        (currentInput == null || currentInput.Length != _expectedCodeLength))
                        return;
                    CheckCombo();
                    break;
                case "del":
                case "delete":
                    if (string.IsNullOrEmpty(currentInput)) return;
                    currentInput = currentInput.Substring(0, currentInput.Length - 1);
                    UpdateDisplayText();
                    break;
                default:
                    if (string.IsNullOrEmpty(input) || input.Length != 1 || input[0] < '0' || input[0] > '9')
                        return;
                    if (_expectedCodeLength > 0 && currentInput != null &&
                        currentInput.Length >= _expectedCodeLength)
                        return;
                    if (currentInput != null && currentInput.Length >= 9)
                        return;
                    currentInput += input;
                    UpdateDisplayText();
                    break;
            }
        }

        public void CheckCombo()
        {
            if (displayingResult || accessWasGranted) return;
            if (_expectedCodeLength > 0 &&
                (currentInput == null || currentInput.Length != _expectedCodeLength))
                return;

            if (_serverGuessInvoker != null)
            {
                if (_guessPending) return;
                _guessPending = true;
                _serverGuessInvoker.Invoke(currentInput);
                return;
            }

            if (_expectedCodeLength > 0 && string.IsNullOrEmpty(_pinString) && keypadCombo == 0)
            {
                Debug.LogWarning("[Keypad] Нет локального PIN и не назначен серверный обработчик.");
                return;
            }

            if (!int.TryParse(currentInput, out var currentKombo))
            {
                Debug.LogWarning("Couldn't process input for some reason..");
                return;
            }

            var granted = currentKombo == keypadCombo;
            if (!granted && !string.IsNullOrEmpty(_pinString) && currentInput != null &&
                currentInput.Length == _pinString.Length)
            {
                var guess = currentInput;
                var (bulls, cows) = Project.Duel.BullsCowsScoring.Score(_pinString, guess);
                WrongGuessSubmitted?.Invoke(guess, bulls, cows);
            }

            StartCoroutine(DisplayResultRoutine(granted));
        }

        private IEnumerator DisplayResultRoutine(bool granted)
        {
            displayingResult = true;

            if (granted) AccessGranted();
            else AccessDenied();

            yield return new WaitForSeconds(displayResultTime);
            displayingResult = false;
            if (granted) yield break;
            currentInput = "";
            if (panelMesh != null)
                panelMesh.material.SetVector("_EmissionColor", screenNormalColor * screenIntensity);
            UpdateDisplayText();
        }

        private void AccessDenied()
        {
            if (keypadDisplayText != null)
                keypadDisplayText.text = accessDeniedText;
            onAccessDenied?.Invoke();
            if (panelMesh != null)
                panelMesh.material.SetVector("_EmissionColor", screenDeniedColor * screenIntensity);
            if (audioSource != null && accessDeniedSfx != null)
                audioSource.PlayOneShot(accessDeniedSfx);
        }

        private void ClearInput()
        {
            currentInput = "";
            UpdateDisplayText();
        }

        private void AccessGranted()
        {
            accessWasGranted = true;
            if (keypadDisplayText != null)
                keypadDisplayText.text = accessGrantedText;
            onAccessGranted?.Invoke();
            if (panelMesh != null)
                panelMesh.material.SetVector("_EmissionColor", screenGrantedColor * screenIntensity);
            if (audioSource != null && accessGrantedSfx != null)
                audioSource.PlayOneShot(accessGrantedSfx);
        }
    }
}
