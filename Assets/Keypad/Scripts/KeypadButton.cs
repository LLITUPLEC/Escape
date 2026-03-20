using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
namespace NavKeypad
{
    public class KeypadButton : MonoBehaviour
    {
        [Header("Value")]
        [SerializeField] private string value;
        [Header("Button Animation Settings")]
        [SerializeField] private float bttnspeed = 0.1f;
        [SerializeField] private float moveDist = 0.0025f;
        [SerializeField] private float buttonPressedTime = 0.1f;
        [Tooltip("Если true, кнопка уходит внутрь по направлению к центру клавиатуры (устойчиво для зеркальных/поворотных инстансов в build).")]
        [SerializeField] private bool pressTowardsKeypadCenter = true;
        [Header("Dynamic Digit Label")]
        [SerializeField] private bool useDynamicDigitLabel = true;
        [SerializeField] private string dynamicLabelTextOverride = "";
        [SerializeField] private Color digitLabelColor = new(1f, 0.78431374f, 0f, 1f);
        [SerializeField] private float digitLabelFontSize = 9f;
        [SerializeField] private Vector3 digitLabelLocalOffset = new(0f, 0f, -0.00043f);
        [SerializeField] private float digitLabelLocalScale = 0.001900739f;
        [Header("Enter Label Override")]
        [SerializeField] private Vector3 enterLabelLocalOffset = new(-0.0002f, 0.00045f, -0.00046f);
        [SerializeField] private float enterLabelLocalScale = 0.001195128f;
        [Header("Component References")]
        [SerializeField] private Keypad keypad;

        private const string DynamicLabelName = "DynamicDigitLabel";
        private TextMeshPro _digitLabel;
        private string _cachedLabelText;

        public void PressButton()
        {
            if (!moving)
            {
                keypad.AddInput(value);
                StartCoroutine(MoveSmooth());
            }
        }
        private bool moving;

        private void Awake()
        {
            EnsureDynamicDigitLabel();
        }

        private void OnValidate()
        {
            _cachedLabelText = ResolveLabelText();
            var existing = transform.Find(DynamicLabelName);
            _digitLabel = existing != null ? existing.GetComponent<TextMeshPro>() : null;
            ApplyDigitLabelStyle();
        }

        public void SetDigitLabelColor(Color color)
        {
            digitLabelColor = color;
            ApplyDigitLabelStyle();
        }

        private void EnsureDynamicDigitLabel()
        {
            _cachedLabelText = ResolveLabelText();

            var existing = transform.Find(DynamicLabelName);
            if (!useDynamicDigitLabel || string.IsNullOrEmpty(_cachedLabelText))
            {
                if (existing != null)
                {
                    if (Application.isPlaying) Destroy(existing.gameObject);
                    else DestroyImmediate(existing.gameObject);
                }
                _digitLabel = null;
                return;
            }

            if (existing == null)
            {
                var go = new GameObject(DynamicLabelName, typeof(TextMeshPro));
                go.transform.SetParent(transform, false);
                existing = go.transform;
            }

            _digitLabel = existing.GetComponent<TextMeshPro>();
            if (_digitLabel == null)
                _digitLabel = existing.gameObject.AddComponent<TextMeshPro>();
            ApplyDigitLabelStyle();
        }

        private void ApplyDigitLabelStyle()
        {
            if (_digitLabel == null) return;
            if (string.IsNullOrEmpty(_cachedLabelText)) return;

            var tr = _digitLabel.transform;
            var token = value == null ? "" : value.Trim().ToLowerInvariant();
            var isEnter = token == "enter";
            tr.localPosition = isEnter ? enterLabelLocalOffset : digitLabelLocalOffset;
            tr.localRotation = Quaternion.identity;
            tr.localScale = Vector3.one * Mathf.Max(0.00001f, isEnter ? enterLabelLocalScale : digitLabelLocalScale);

            _digitLabel.text = _cachedLabelText;
            _digitLabel.fontSize = digitLabelFontSize;
            _digitLabel.enableAutoSizing = true;
            _digitLabel.fontSizeMin = Mathf.Max(2f, digitLabelFontSize * 0.45f);
            _digitLabel.fontSizeMax = digitLabelFontSize;
            _digitLabel.color = digitLabelColor;
            _digitLabel.alignment = TextAlignmentOptions.Center;
            _digitLabel.textWrappingMode = TextWrappingModes.NoWrap;
            _digitLabel.raycastTarget = false;
            _digitLabel.outlineWidth = 0f;
        }

        private static bool IsSingleDigit(string s)
        {
            return !string.IsNullOrEmpty(s) && s.Length == 1 && s[0] >= '0' && s[0] <= '9';
        }

        private string ResolveLabelText()
        {
            if (!string.IsNullOrWhiteSpace(dynamicLabelTextOverride))
                return dynamicLabelTextOverride.Trim();
            if (IsSingleDigit(value))
                return value;

            var token = value == null ? "" : value.Trim().ToLowerInvariant();
            if (token == "enter")
                return "Вход";
            if (token == "del" || token == "delete" || token == "back" || token == "<-")
                return "<-";
            return null;
        }

        private IEnumerator MoveSmooth()
        {
            moving = true;
            var startPos = transform.position;
            var pressDir = ResolvePressDirectionWorld();
            var endPos = startPos + pressDir * moveDist;

            float elapsedTime = 0;
            while (elapsedTime < bttnspeed)
            {
                elapsedTime += Time.deltaTime;
                float t = Mathf.Clamp01(elapsedTime / bttnspeed);

                transform.position = Vector3.Lerp(startPos, endPos, t);

                yield return null;
            }
            transform.position = endPos;
            yield return new WaitForSeconds(buttonPressedTime);
            startPos = transform.position;
            endPos = startPos - pressDir * moveDist;

            elapsedTime = 0;
            while (elapsedTime < bttnspeed)
            {
                elapsedTime += Time.deltaTime;
                float t = Mathf.Clamp01(elapsedTime / bttnspeed);

                transform.position = Vector3.Lerp(startPos, endPos, t);

                yield return null;
            }
            transform.position = endPos;

            moving = false;
        }

        private Vector3 ResolvePressDirectionWorld()
        {
            if (pressTowardsKeypadCenter && keypad != null)
            {
                var toCenter = keypad.transform.position - transform.position;
                if (toCenter.sqrMagnitude > 0.0000001f)
                    return toCenter.normalized;
            }

            return -transform.forward;
        }
    }
}