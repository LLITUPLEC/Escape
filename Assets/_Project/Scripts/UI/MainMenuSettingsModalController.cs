using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Nakama;
using Project.Nakama;
using Project.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Project.UI
{
    /// <summary>
    /// Окно настроек из префаба <c>SettingsModal</c> (дочерний к HUD). Привязка и вход по e-mail через Nakama.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MainMenuSettingsModalController : MonoBehaviour
    {
        private const string PrefKnownLinkedEmail = NakamaBootstrap.PrefKnownLinkedEmail;

        [Header("Окно (если пусто — ищется дочерний объект SettingsModal)")]
        [SerializeField] private GameObject settingsModalRoot;

        [Header("Необязательно: назначьте в инспекторе, иначе ищутся по имени под Panel")]
        [SerializeField] private RectTransform modalPanelRect;
        [SerializeField] private CanvasGroup modalPanelGroup;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private TMP_Text linkedEmailLineText;
        [SerializeField] private InputField emailInput;
        [SerializeField] private InputField passwordInput;
        [SerializeField] private RectTransform emailInputSlot;
        [SerializeField] private RectTransform passwordInputSlot;
        [SerializeField] private Button linkButton;
        [SerializeField] private Button loginButton;
        [SerializeField] private Button logoutAccountButton;
        [SerializeField] private Button deleteAccountButton;
        [SerializeField] private Button closeButton;

        private Button _settingsButton;
        private GameObject _modalGo;
        private GameObject _deleteConfirmRoot;
        private bool _busy;

        /// <summary>Исходная позиция Panel до сдвига от IME (мобильная клавиатура).</summary>
        private Vector2 _modalPanelBaseAnchoredPos;
        private bool _hasStoredModalPanelBasePos;

        private void Awake()
        {
            var tr = transform.Find("SettingsButton");
            if (tr != null)
            {
                _settingsButton = tr.GetComponent<Button>();
                if (_settingsButton != null)
                    _settingsButton.onClick.AddListener(OpenModal);
            }

            ResolveUiReferences();
            EnsureInputsUnderSlots();
            WireModalButtons();

            if (_modalGo != null)
                _modalGo.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_settingsButton != null)
                _settingsButton.onClick.RemoveListener(OpenModal);
            UnwireModalButtons();
        }

        private void OnEnable()
        {
            if (_modalGo != null && _modalGo.activeSelf)
                _ = RefreshStatusAsync(CancellationToken.None);
        }

        private void Update()
        {
            if (_modalGo == null || !_modalGo.activeSelf) return;

            if (TryBackPressed())
                CloseModal();

#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame &&
                modalPanelRect != null &&
                !RectTransformUtility.RectangleContainsScreenPoint(modalPanelRect, mouse.position.ReadValue(), null))
            {
                CloseModal();
            }
#else
            if (Input.GetMouseButtonDown(0) &&
                modalPanelRect != null &&
                !RectTransformUtility.RectangleContainsScreenPoint(modalPanelRect, Input.mousePosition, null))
            {
                CloseModal();
            }
#endif
        }

        private void LateUpdate()
        {
            if (_modalGo == null || !_modalGo.activeSelf || modalPanelRect == null || !_hasStoredModalPanelBasePos)
                return;

            float obscuringPx = ComputeKeyboardObscuringHeightPx();
            float lift = KeyboardObscuringPixelsToPanelLift(obscuringPx);
            modalPanelRect.anchoredPosition = _modalPanelBaseAnchoredPos + new Vector2(0f, lift);
        }

        private bool IsAnySettingsInputFocused()
        {
            return (emailInput != null && emailInput.isFocused) ||
                   (passwordInput != null && passwordInput.isFocused);
        }

        /// <summary>Высота области экрана, перекрываемой клавиатурой (нижний край), в пикселях.</summary>
        private float ComputeKeyboardObscuringHeightPx()
        {
            bool focused = IsAnySettingsInputFocused();
            bool kbdVisible = TouchScreenKeyboard.visible;
            if (!focused && !kbdVisible)
                return 0f;

            float h = 0f;

            if (kbdVisible)
            {
                var area = TouchScreenKeyboard.area;
                if (area.height > 1f)
                    h = Mathf.Max(h, area.height);
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            if ((focused || kbdVisible) && TryGetAndroidKeyboardOverlapPx(out var androidH))
                h = Mathf.Max(h, androidH);
#elif UNITY_IOS || UNITY_IPHONE
            if (focused || kbdVisible)
            {
                float inset = Screen.height - Screen.safeArea.yMax;
                if (inset > 10f)
                    h = Mathf.Max(h, inset);
            }
#endif

            if ((focused || kbdVisible) && h < 80f)
                h = Mathf.Max(h, Screen.height * 0.4f);

            return h;
        }

        private float KeyboardObscuringPixelsToPanelLift(float obscuringPx)
        {
            if (obscuringPx <= 0f)
                return 0f;

            var canvas = modalPanelRect != null ? modalPanelRect.GetComponentInParent<Canvas>() : null;
            float scale = canvas != null ? canvas.scaleFactor : 1f;
            if (scale < 0.01f)
                scale = 1f;

            float lift = obscuringPx / scale;
            var parent = modalPanelRect.parent as RectTransform;
            if (parent != null)
            {
                float maxByParent = parent.rect.height * 0.42f;
                if (maxByParent > 1f)
                    lift = Mathf.Min(lift, maxByParent);
            }

            return lift;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static bool TryGetAndroidKeyboardOverlapPx(out float heightPx)
        {
            heightPx = 0f;
            try
            {
                using var unityClass = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                var activity = unityClass.GetStatic<AndroidJavaObject>("currentActivity");
                var window = activity?.Call<AndroidJavaObject>("getWindow");
                var decor = window?.Call<AndroidJavaObject>("getDecorView");
                if (decor == null)
                    return false;

                using var rect = new AndroidJavaObject("android.graphics.Rect");
                decor.Call("getWindowVisibleDisplayFrame", rect);
                int visibleBottom = rect.Call<int>("bottom");
                int screenH = Screen.height;
                heightPx = Mathf.Max(0f, screenH - visibleBottom);
                return heightPx > 2f;
            }
            catch
            {
                return false;
            }
        }
#else
        private static bool TryGetAndroidKeyboardOverlapPx(out float heightPx)
        {
            heightPx = 0f;
            return false;
        }
#endif

        private static bool TryBackPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) return true;
            var gp = Gamepad.current;
            if (gp != null && gp.buttonEast.wasPressedThisFrame) return true;
            return false;
#else
            return Input.GetKeyDown(KeyCode.Escape);
#endif
        }

        private void ResolveUiReferences()
        {
            _modalGo = settingsModalRoot != null ? settingsModalRoot : transform.Find("SettingsModal")?.gameObject;
            if (_modalGo == null)
            {
                Debug.LogError("[SettingsModal] Не найден GameObject SettingsModal. Добавьте префаб дочерним к MainMenuHudOverlay.");
                return;
            }

            var panel = _modalGo.transform.Find("Panel");
            if (panel == null)
            {
                Debug.LogError("[SettingsModal] Нет дочернего Panel.");
                return;
            }

            if (modalPanelRect == null)
                modalPanelRect = panel as RectTransform;
            if (modalPanelGroup == null)
                modalPanelGroup = panel.GetComponent<CanvasGroup>();

            if (statusText == null)
                statusText = panel.Find("Status")?.GetComponent<TMP_Text>();
            if (linkedEmailLineText == null)
                linkedEmailLineText = panel.Find("LinkedEmailLine")?.GetComponent<TMP_Text>();

            if (emailInputSlot == null)
            {
                var s = panel.Find("EmailInputSlot");
                if (s != null) emailInputSlot = s as RectTransform;
            }
            if (passwordInputSlot == null)
            {
                var s = panel.Find("PasswordInputSlot");
                if (s != null) passwordInputSlot = s as RectTransform;
            }

            if (emailInput == null && emailInputSlot != null)
                emailInput = emailInputSlot.GetComponentInChildren<InputField>(true);
            if (passwordInput == null && passwordInputSlot != null)
                passwordInput = passwordInputSlot.GetComponentInChildren<InputField>(true);

            if (linkButton == null)
                linkButton = panel.Find("LinkButton")?.GetComponent<Button>();
            if (loginButton == null)
                loginButton = panel.Find("LoginButton")?.GetComponent<Button>();
            if (logoutAccountButton == null)
                logoutAccountButton = panel.Find("LogoutEmailButton")?.GetComponent<Button>();
            if (deleteAccountButton == null)
                deleteAccountButton = panel.Find("DeleteAccountButton")?.GetComponent<Button>();
            if (closeButton == null)
                closeButton = panel.Find("CloseButton")?.GetComponent<Button>();

            EnsureDeleteAccountButton(panel);
        }

        private void EnsureDeleteAccountButton(Transform panel)
        {
            if (panel == null) return;

            if (deleteAccountButton == null)
            {
                var go = new GameObject("DeleteAccountButton", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
                go.transform.SetParent(panel, false);
                go.GetComponent<Image>().color = new Color(0.55f, 0.12f, 0.14f, 1f);
                var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
                var lrt = labelGo.GetComponent<RectTransform>();
                lrt.SetParent(go.transform, false);
                lrt.anchorMin = Vector2.zero;
                lrt.anchorMax = Vector2.one;
                lrt.offsetMin = Vector2.zero;
                lrt.offsetMax = Vector2.zero;
                var t = labelGo.GetComponent<Text>();
                t.font = SettingsModalUiHelper.GetDefaultUIFont();
                t.text = "Удалить аккаунт";
                t.fontSize = 20;
                t.alignment = TextAnchor.MiddleCenter;
                t.color = Color.white;
                t.raycastTarget = false;
                if (closeButton != null)
                    go.transform.SetSiblingIndex(closeButton.transform.GetSiblingIndex());
                deleteAccountButton = go.GetComponent<Button>();
            }

            ApplyDeleteAccountButtonLayout(deleteAccountButton);
        }

        private static void ApplyDeleteAccountButtonLayout(Button btn)
        {
            if (btn == null) return;
            var rt = btn.GetComponent<RectTransform>();
            if (rt == null) return;
            // bottom-center, 300×50, Pos Y = 10
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 10f);
            rt.sizeDelta = new Vector2(300f, 50f);
            var le = btn.GetComponent<LayoutElement>();
            if (le != null)
            {
                le.ignoreLayout = true;
                le.minWidth = 300f;
                le.preferredWidth = 300f;
                le.minHeight = 50f;
                le.preferredHeight = 50f;
            }
        }

        private void EnsureInputsUnderSlots()
        {
            var font = SettingsModalUiHelper.GetDefaultUIFont();
            if (emailInput == null && emailInputSlot != null)
                emailInput = SettingsModalUiHelper.CreateInputField(emailInputSlot, "EmailInput", "E-mail", false, font);
            if (passwordInput == null && passwordInputSlot != null)
                passwordInput = SettingsModalUiHelper.CreateInputField(passwordInputSlot, "PasswordInput", "Пароль", true, font);
        }

        private void WireModalButtons()
        {
            if (linkButton != null) linkButton.onClick.AddListener(OnLinkClicked);
            if (loginButton != null) loginButton.onClick.AddListener(OnLoginClicked);
            if (logoutAccountButton != null) logoutAccountButton.onClick.AddListener(OnLogoutClicked);
            if (deleteAccountButton != null) deleteAccountButton.onClick.AddListener(OnDeleteAccountClicked);
            if (closeButton != null) closeButton.onClick.AddListener(CloseModal);
        }

        private void UnwireModalButtons()
        {
            if (linkButton != null) linkButton.onClick.RemoveListener(OnLinkClicked);
            if (loginButton != null) loginButton.onClick.RemoveListener(OnLoginClicked);
            if (logoutAccountButton != null) logoutAccountButton.onClick.RemoveListener(OnLogoutClicked);
            if (deleteAccountButton != null) deleteAccountButton.onClick.RemoveListener(OnDeleteAccountClicked);
            if (closeButton != null) closeButton.onClick.RemoveListener(CloseModal);
        }

        private void OpenModal()
        {
            if (_modalGo == null) return;
            _modalGo.SetActive(true);
            if (modalPanelGroup != null)
                modalPanelGroup.alpha = 1f;
            if (modalPanelRect != null)
            {
                _modalPanelBaseAnchoredPos = modalPanelRect.anchoredPosition;
                _hasStoredModalPanelBasePos = true;
            }
            _ = RefreshStatusAsync(CancellationToken.None);
        }

        private void CloseModal()
        {
            if (modalPanelRect != null && _hasStoredModalPanelBasePos)
                modalPanelRect.anchoredPosition = _modalPanelBaseAnchoredPos;
            if (_modalGo != null)
                _modalGo.SetActive(false);
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            if (linkButton != null) linkButton.interactable = !busy;
            if (loginButton != null) loginButton.interactable = !busy;
            if (logoutAccountButton != null) logoutAccountButton.interactable = !busy;
            if (deleteAccountButton != null) deleteAccountButton.interactable = !busy;
        }

        /// <summary>Любые изменения UI после await — только через очередь главного потока.</summary>
        private static void RunOnUiThread(Action action)
        {
            if (action == null) return;
            MainThreadDispatcher.Enqueue(action);
        }

        private async Task RefreshStatusAsync(CancellationToken ct)
        {
            RunOnUiThread(() =>
            {
                if (statusText != null) statusText.text = "Загрузка…";
                if (linkedEmailLineText != null) linkedEmailLineText.text = "";
            });

            try
            {
                if (NakamaBootstrap.Instance == null)
                {
                    RunOnUiThread(() =>
                    {
                        if (statusText != null) statusText.text = "Сеть недоступна (bootstrap).";
                    });
                    return;
                }

                await NakamaBootstrap.Instance.EnsureConnectedAsync(ct);
                if (NakamaBootstrap.Instance.Session == null)
                {
                    RunOnUiThread(() =>
                    {
                        if (statusText != null) statusText.text = "Нет сессии Nakama.";
                    });
                    return;
                }

                var acc = await NakamaBootstrap.Instance.Client.GetAccountAsync(NakamaBootstrap.Instance.Session, canceller: ct);
                var uid = NakamaBootstrap.Instance.Session.UserId ?? "";
                var shortUid = uid.Length > 8 ? uid.Substring(0, 8) + "…" : uid;
                // Nakama returns e-mail on ApiAccount.Email (not on ApiUser).
                // Keep a tolerant fallback for older payloads / custom servers.
                var mailFromApi = !string.IsNullOrWhiteSpace(acc?.Email) ? acc.Email : TryGetUserEmail(acc?.User);
                var mailKnown = await MainThreadDispatcher.RunAsync(() => PlayerPrefs.GetString(PrefKnownLinkedEmail, ""));
                var emailMode = await NakamaBootstrap.Instance.UsesEmailSessionPersistenceAsync();

                var statusBody =
                    $"Режим: {(emailMode ? "на устройстве сохранён вход по e-mail" : "вход по устройству (анонимный id)")}\n" +
                    $"ID: {shortUid}";

                string linkedBody = null;
                Color? linkedColor = null;
                if (!string.IsNullOrWhiteSpace(mailFromApi))
                {
                    linkedBody = $"Привязанный e-mail: {mailFromApi}";
                    linkedColor = new Color(0.55f, 1f, 0.65f);
                }
                else if (!string.IsNullOrWhiteSpace(mailKnown))
                {
                    // This is only a local hint: it does NOT prove that the current user_id has this email linked on server.
                    linkedBody = $"Привязанный e-mail (локально сохранено): {mailKnown}";
                    linkedColor = new Color(1f, 0.92f, 0.6f);
                }
                else
                {
                    linkedBody =
                        "E-mail в ответе API не найден. Нажмите «Привязать», затем здесь появится сохранённый адрес.";
                    linkedColor = Color.white;
                }

                RunOnUiThread(() =>
                {
                    if (statusText != null) statusText.text = statusBody;
                    if (linkedEmailLineText != null)
                    {
                        linkedEmailLineText.text = linkedBody;
                        if (linkedColor.HasValue) linkedEmailLineText.color = linkedColor.Value;
                    }
                });
            }
            catch (Exception e)
            {
                RunOnUiThread(() =>
                {
                    if (statusText != null) statusText.text = "Не удалось получить профиль: " + e.Message;
                });
            }
        }

        private async void OnLinkClicked()
        {
            if (_busy) return;
            var email = emailInput != null ? emailInput.text.Trim() : "";
            var password = passwordInput != null ? passwordInput.text : "";
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                if (statusText != null) statusText.text = "Введите e-mail и пароль.";
                return;
            }

            SetBusy(true);
            try
            {
                if (NakamaBootstrap.Instance == null) throw new InvalidOperationException("NakamaBootstrap отсутствует.");
                await NakamaBootstrap.Instance.EnsureConnectedAsync(CancellationToken.None);
                await NakamaBootstrap.Instance.LinkEmailAsync(email, password, CancellationToken.None);
                await MainThreadDispatcher.RunAsync(() =>
                {
                    PlayerPrefs.SetString(PrefKnownLinkedEmail, email);
                    PlayerPrefs.Save();
                });
                RunOnUiThread(() =>
                {
                    if (statusText != null)
                        statusText.text =
                            "Привязка выполнена. Вход по e-mail сохранён на устройстве; этот телефон привязан к аккаунту.";
                });
                await RefreshStatusAsync(CancellationToken.None);
            }
            catch (Exception e)
            {
                RunOnUiThread(() =>
                {
                    if (statusText != null) statusText.text = "Привязка не удалась: " + e.Message;
                });
            }
            finally
            {
                RunOnUiThread(() => SetBusy(false));
            }
        }

        private async void OnLoginClicked()
        {
            if (_busy) return;
            var email = emailInput != null ? emailInput.text.Trim() : "";
            var password = passwordInput != null ? passwordInput.text : "";
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                if (statusText != null) statusText.text = "Введите e-mail и пароль.";
                return;
            }

            SetBusy(true);
            try
            {
                if (NakamaBootstrap.Instance == null) throw new InvalidOperationException("NakamaBootstrap отсутствует.");
                await NakamaBootstrap.Instance.LoginWithEmailAsync(email, password, false, CancellationToken.None);
                await MainThreadDispatcher.RunAsync(() =>
                {
                    PlayerPrefs.SetString(PrefKnownLinkedEmail, email);
                    PlayerPrefs.Save();
                });
                RunOnUiThread(() =>
                {
                    if (statusText != null)
                        statusText.text = "Вход выполнен. Прогресс на сервере привязан к этому аккаунту.";
                });
                await RefreshStatusAsync(CancellationToken.None);
            }
            catch (Exception e)
            {
                RunOnUiThread(() =>
                {
                    if (statusText != null) statusText.text = "Вход не удался: " + e.Message;
                });
            }
            finally
            {
                RunOnUiThread(() => SetBusy(false));
            }
        }

        private async void OnLogoutClicked()
        {
            if (_busy) return;
            SetBusy(true);
            try
            {
                if (NakamaBootstrap.Instance == null) throw new InvalidOperationException("NakamaBootstrap отсутствует.");
                await NakamaBootstrap.Instance.ClearEmailPersistenceAndReconnectAsync(CancellationToken.None);
                RunOnUiThread(() =>
                {
                    if (statusText != null)
                        statusText.text =
                            "Локально сброшен вход по e-mail; сейчас снова используется профиль устройства.\n" +
                            "В консоли Nakama e-mail по-прежнему привязан к старому user_id — это нормально (сброс только локальных токенов).";
                    if (linkedEmailLineText != null)
                        linkedEmailLineText.text =
                            "Чтобы снова играть под аккаунтом с почтой — нажмите «Войти по e-mail». Удалить почту у пользователя можно только в консоли / отдельным API.";
                });
                await RefreshStatusAsync(CancellationToken.None);
            }
            catch (Exception e)
            {
                RunOnUiThread(() =>
                {
                    if (statusText != null) statusText.text = "Ошибка: " + e.Message;
                });
            }
            finally
            {
                RunOnUiThread(() => SetBusy(false));
            }
        }

        private void OnDeleteAccountClicked()
        {
            if (_busy) return;
            ShowDeleteConfirmDialog();
        }

        private void ShowDeleteConfirmDialog()
        {
            if (_modalGo == null) return;
            if (_deleteConfirmRoot != null)
            {
                _deleteConfirmRoot.SetActive(true);
                _deleteConfirmRoot.transform.SetAsLastSibling();
                return;
            }

            _deleteConfirmRoot = new GameObject("DeleteAccountConfirm", typeof(RectTransform), typeof(Image));
            var rt = _deleteConfirmRoot.GetComponent<RectTransform>();
            rt.SetParent(_modalGo.transform, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            _deleteConfirmRoot.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.72f);
            _deleteConfirmRoot.GetComponent<Image>().raycastTarget = true;

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            var prt = panel.GetComponent<RectTransform>();
            prt.SetParent(rt, false);
            prt.anchorMin = new Vector2(0.5f, 0.5f);
            prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(420f, 260f);
            panel.GetComponent<Image>().color = new Color(0.14f, 0.12f, 0.14f, 1f);
            var v = panel.GetComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(18, 18, 18, 18);
            v.spacing = 12f;
            v.childAlignment = TextAnchor.MiddleCenter;
            v.childControlWidth = true;
            v.childControlHeight = true;

            var msg = new GameObject("Msg", typeof(Text), typeof(LayoutElement));
            msg.transform.SetParent(panel.transform, false);
            msg.GetComponent<LayoutElement>().minHeight = 120f;
            var mt = msg.GetComponent<Text>();
            mt.font = SettingsModalUiHelper.GetDefaultUIFont();
            mt.fontSize = 18;
            mt.color = new Color(1f, 0.9f, 0.9f);
            mt.alignment = TextAnchor.MiddleCenter;
            mt.horizontalOverflow = HorizontalWrapMode.Wrap;
            mt.verticalOverflow = VerticalWrapMode.Overflow;
            mt.text =
                "Удалить аккаунт?\n\nВесь прогресс будет потерян без возможности восстановления.\nПродолжить?";

            var row = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(panel.transform, false);
            row.GetComponent<LayoutElement>().minHeight = 44f;
            var h = row.GetComponent<HorizontalLayoutGroup>();
            h.spacing = 16f;
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlWidth = true;
            h.childControlHeight = true;

            void AddBtn(string label, Color col, UnityEngine.Events.UnityAction act)
            {
                var b = new GameObject(label, typeof(Image), typeof(Button), typeof(LayoutElement));
                b.transform.SetParent(row.transform, false);
                b.GetComponent<LayoutElement>().minWidth = 140f;
                b.GetComponent<LayoutElement>().minHeight = 40f;
                b.GetComponent<Image>().color = col;
                b.GetComponent<Button>().onClick.AddListener(act);
                var lt = new GameObject("L", typeof(Text));
                lt.transform.SetParent(b.transform, false);
                var ltrt = lt.AddComponent<RectTransform>();
                ltrt.anchorMin = Vector2.zero;
                ltrt.anchorMax = Vector2.one;
                ltrt.offsetMin = Vector2.zero;
                ltrt.offsetMax = Vector2.zero;
                var tx = lt.GetComponent<Text>();
                tx.font = SettingsModalUiHelper.GetDefaultUIFont();
                tx.text = label;
                tx.fontSize = 18;
                tx.alignment = TextAnchor.MiddleCenter;
                tx.color = Color.white;
                tx.raycastTarget = false;
            }

            AddBtn("Да", new Color(0.65f, 0.12f, 0.14f, 1f), () =>
            {
                _deleteConfirmRoot.SetActive(false);
                _ = ConfirmDeleteAccountAsync();
            });
            AddBtn("Нет", new Color(0.28f, 0.28f, 0.32f, 1f), () =>
            {
                _deleteConfirmRoot.SetActive(false);
            });
        }

        private async Task ConfirmDeleteAccountAsync()
        {
            if (_busy) return;
            SetBusy(true);
            try
            {
                if (NakamaBootstrap.Instance == null) throw new InvalidOperationException("NakamaBootstrap отсутствует.");
                RunOnUiThread(() =>
                {
                    if (statusText != null) statusText.text = "Удаление аккаунта…";
                });
                await NakamaBootstrap.Instance.WipeAccountAndQuitAsync(CancellationToken.None);
            }
            catch (Exception e)
            {
                RunOnUiThread(() =>
                {
                    if (statusText != null) statusText.text = "Не удалось удалить аккаунт: " + e.Message;
                });
                RunOnUiThread(() => SetBusy(false));
            }
        }

        private static string TryGetUserEmail(IApiUser user)
        {
            if (user == null) return null;
            var t = user.GetType();
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!string.Equals(p.Name, "Email", StringComparison.OrdinalIgnoreCase))
                    continue;
                object v;
                try
                {
                    v = p.GetValue(user);
                }
                catch
                {
                    continue;
                }
                if (v == null) return null;
                if (v is string s) return string.IsNullOrWhiteSpace(s) ? null : s;
                var text = v.ToString();
                return string.IsNullOrWhiteSpace(text) ? null : text;
            }
            return null;
        }
    }

    internal static class SettingsModalUiHelper
    {
        public static Font GetDefaultUIFont()
        {
            try
            {
                var f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (f != null) return f;
            }
            catch { /* ignored */ }
            try
            {
                return Resources.GetBuiltinResource<Font>("Arial.ttf");
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Создаёт поле ввода, растянутое на весь слот.</summary>
        public static InputField CreateInputField(RectTransform slot, string name, string placeholder, bool password, Font font)
        {
            var go = new GameObject(name);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(slot, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.15f, 0.17f, 0.24f, 1f);

            var input = go.AddComponent<InputField>();
            input.contentType = password ? InputField.ContentType.Password : InputField.ContentType.EmailAddress;
            if (password) input.inputType = InputField.InputType.Password;

            var textGo = new GameObject("Text");
            var textRt = textGo.AddComponent<RectTransform>();
            textRt.SetParent(rt, false);
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(12f, 6f);
            textRt.offsetMax = new Vector2(-12f, -6f);
            var text = textGo.AddComponent<Text>();
            text.font = font;
            text.fontSize = 20;
            text.color = Color.white;
            text.supportRichText = false;
            text.alignment = TextAnchor.MiddleLeft;
            input.textComponent = text;

            var phGo = new GameObject("Placeholder");
            var phRt = phGo.AddComponent<RectTransform>();
            phRt.SetParent(rt, false);
            phRt.anchorMin = Vector2.zero;
            phRt.anchorMax = Vector2.one;
            phRt.offsetMin = new Vector2(12f, 6f);
            phRt.offsetMax = new Vector2(-12f, -6f);
            var phText = phGo.AddComponent<Text>();
            phText.font = font;
            phText.fontSize = 20;
            phText.color = new Color(1f, 1f, 1f, 0.45f);
            phText.text = placeholder;
            phText.fontStyle = FontStyle.Italic;
            phText.alignment = TextAnchor.MiddleLeft;
            input.placeholder = phText;

            return input;
        }
    }
}
