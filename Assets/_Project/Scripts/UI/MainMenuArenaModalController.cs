using UnityEngine;
using UnityEngine.UI;
using System.Collections;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Project.UI
{
    [DisallowMultipleComponent]
    public sealed class MainMenuArenaModalController : MonoBehaviour
    {
        [SerializeField] private string panelPath = "MainMenuScreen/Canvas/Panel";
        [SerializeField] private string modalWindowPath = "MainMenuScreen/Canvas/Panel/ModalWindow";
        [SerializeField] private string closeButtonPath = "MainMenuScreen/Canvas/Panel/ModalWindow/CloseButton";
        [SerializeField] private bool toggleOnArenaButton = true;
        [SerializeField] private bool hidePanelOnStart = true;
        [SerializeField] private bool useFadeAnimation = true;
        [SerializeField, Min(0f)] private float fadeDuration = 0.18f;

        private Button _arenaButton;
        private Button _closeButton;
        private GameObject _panel;
        private RectTransform _modalWindow;
        private CanvasGroup _panelCanvasGroup;
        private Coroutine _fadeRoutine;
        private int _openedFrame = -1000;

        private void Awake()
        {
            _arenaButton = GetComponent<Button>();
            if (_arenaButton != null)
                _arenaButton.onClick.AddListener(OnArenaButtonClicked);

            ResolvePanel();
            if (hidePanelOnStart && _panel != null)
            {
                _panel.SetActive(false);
                if (_panelCanvasGroup != null)
                    _panelCanvasGroup.alpha = 0f;
            }
        }

        private void OnDestroy()
        {
            if (_arenaButton != null)
                _arenaButton.onClick.RemoveListener(OnArenaButtonClicked);
            if (_closeButton != null)
                _closeButton.onClick.RemoveListener(ClosePanel);
            if (_fadeRoutine != null)
                StopCoroutine(_fadeRoutine);
        }

        private void Update()
        {
            ResolvePanel();
            if (_panel == null || !_panel.activeSelf)
                return;

            if (TryGetPressPositionThisFrame(out var pressPos) &&
                Time.frameCount > _openedFrame &&
                _modalWindow != null &&
                !RectTransformUtility.RectangleContainsScreenPoint(_modalWindow, pressPos, null))
            {
                ClosePanel();
                return;
            }

            if (IsBackPressed())
                ClosePanel();
        }

        private void OnArenaButtonClicked()
        {
            ResolvePanel();
            if (_panel == null)
                return;

            if (toggleOnArenaButton)
            {
                var nextState = !_panel.activeSelf;
                if (nextState)
                {
                    OpenPanel();
                    _openedFrame = Time.frameCount;
                }
                else
                {
                    ClosePanel();
                }
            }
            else
            {
                OpenPanel();
                _openedFrame = Time.frameCount;
            }
        }

        private void ResolvePanel()
        {
            if (_panel == null && !string.IsNullOrWhiteSpace(panelPath))
            {
                _panel = FindByPath(panelPath);
                if (_panel != null)
                {
                    _panelCanvasGroup = _panel.GetComponent<CanvasGroup>();
                    if (_panelCanvasGroup == null)
                        _panelCanvasGroup = _panel.AddComponent<CanvasGroup>();
                }
            }

            if (_modalWindow == null)
                _modalWindow = FindRectTransformByPath(modalWindowPath, "ModalWindow");

            if (_closeButton == null)
            {
                _closeButton = FindButtonByPath(closeButtonPath, "ModalWindow/CloseButton");
                if (_closeButton != null)
                    _closeButton.onClick.AddListener(ClosePanel);
            }
        }

        private void ClosePanel()
        {
            if (_panel != null)
            {
                if (!useFadeAnimation || fadeDuration <= 0f)
                {
                    _panel.SetActive(false);
                    if (_panelCanvasGroup != null)
                        _panelCanvasGroup.alpha = 0f;
                    return;
                }
                StartFade(false);
            }
        }

        private void OpenPanel()
        {
            if (_panel == null)
                return;

            if (!useFadeAnimation || fadeDuration <= 0f || _panelCanvasGroup == null)
            {
                _panel.SetActive(true);
                if (_panelCanvasGroup != null)
                    _panelCanvasGroup.alpha = 1f;
                return;
            }

            _panel.SetActive(true);
            StartFade(true);
        }

        private void StartFade(bool toVisible)
        {
            if (_fadeRoutine != null)
                StopCoroutine(_fadeRoutine);
            _fadeRoutine = StartCoroutine(FadeRoutine(toVisible));
        }

        private IEnumerator FadeRoutine(bool toVisible)
        {
            if (_panel == null || _panelCanvasGroup == null)
                yield break;

            float start = _panelCanvasGroup.alpha;
            float end = toVisible ? 1f : 0f;
            float duration = Mathf.Max(0.01f, fadeDuration);
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                _panelCanvasGroup.alpha = Mathf.Lerp(start, end, t / duration);
                yield return null;
            }
            _panelCanvasGroup.alpha = end;
            if (!toVisible)
                _panel.SetActive(false);
            _fadeRoutine = null;
        }

        private GameObject FindByPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            var direct = GameObject.Find(path);
            if (direct != null)
                return direct;

            // GameObject.Find не находит неактивные объекты, поэтому ищем через активный корень.
            var split = path.Split('/');
            if (split.Length == 0)
                return null;

            var root = GameObject.Find(split[0]);
            if (root == null)
                return null;

            var tr = root.transform;
            for (var i = 1; i < split.Length && tr != null; i++)
                tr = tr.Find(split[i]);

            return tr != null ? tr.gameObject : null;
        }

        private RectTransform FindRectTransformByPath(string fullPath, string fallbackRelativePath)
        {
            var go = FindByPath(fullPath);
            if (go != null)
                return go.transform as RectTransform;

            if (_panel != null && !string.IsNullOrWhiteSpace(fallbackRelativePath))
            {
                var tr = _panel.transform.Find(fallbackRelativePath);
                if (tr != null)
                    return tr as RectTransform;
            }
            return null;
        }

        private Button FindButtonByPath(string fullPath, string fallbackRelativePath)
        {
            var go = FindByPath(fullPath);
            if (go != null)
                return go.GetComponent<Button>();

            if (_panel != null && !string.IsNullOrWhiteSpace(fallbackRelativePath))
            {
                var tr = _panel.transform.Find(fallbackRelativePath);
                if (tr != null)
                    return tr.GetComponent<Button>();
            }
            return null;
        }

        private static bool IsBackPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
                return true;

            var gp = Gamepad.current;
            if (gp != null && gp.buttonEast.wasPressedThisFrame)
                return true;
            return false;
#else
            return Input.GetKeyDown(KeyCode.Escape);
#endif
        }

        private static bool TryGetPressPositionThisFrame(out Vector2 pointerPos)
        {
#if ENABLE_INPUT_SYSTEM
            var ts = Touchscreen.current;
            if (ts != null)
            {
                var t = ts.primaryTouch;
                if (t.press.wasPressedThisFrame)
                {
                    pointerPos = t.position.ReadValue();
                    return true;
                }
            }

            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                pointerPos = mouse.position.ReadValue();
                return true;
            }
#else
            if (Input.GetMouseButtonDown(0))
            {
                pointerPos = Input.mousePosition;
                return true;
            }
            if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)
            {
                pointerPos = Input.GetTouch(0).position;
                return true;
            }
#endif
            pointerPos = default;
            return false;
        }
    }
}

