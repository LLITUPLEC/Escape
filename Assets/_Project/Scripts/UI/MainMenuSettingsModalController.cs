using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Nakama;
using Project.Nakama;
using Project.Utils;
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
        private const string PrefKnownLinkedEmail = "nakama.ui.known_linked_email";

        [Header("Окно (если пусто — ищется дочерний объект SettingsModal)")]
        [SerializeField] private GameObject settingsModalRoot;

        [Header("Необязательно: назначьте в инспекторе, иначе ищутся по имени под Panel")]
        [SerializeField] private RectTransform modalPanelRect;
        [SerializeField] private CanvasGroup modalPanelGroup;
        [SerializeField] private Text statusText;
        [SerializeField] private Text linkedEmailLineText;
        [SerializeField] private InputField emailInput;
        [SerializeField] private InputField passwordInput;
        [SerializeField] private RectTransform emailInputSlot;
        [SerializeField] private RectTransform passwordInputSlot;
        [SerializeField] private Button linkButton;
        [SerializeField] private Button loginButton;
        [SerializeField] private Button logoutAccountButton;
        [SerializeField] private Button closeButton;

        private Button _settingsButton;
        private GameObject _modalGo;
        private bool _busy;

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
                statusText = panel.Find("Status")?.GetComponent<Text>();
            if (linkedEmailLineText == null)
                linkedEmailLineText = panel.Find("LinkedEmailLine")?.GetComponent<Text>();

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
            if (closeButton == null)
                closeButton = panel.Find("CloseButton")?.GetComponent<Button>();
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
            if (closeButton != null) closeButton.onClick.AddListener(CloseModal);
        }

        private void UnwireModalButtons()
        {
            if (linkButton != null) linkButton.onClick.RemoveListener(OnLinkClicked);
            if (loginButton != null) loginButton.onClick.RemoveListener(OnLoginClicked);
            if (logoutAccountButton != null) logoutAccountButton.onClick.RemoveListener(OnLogoutClicked);
            if (closeButton != null) closeButton.onClick.RemoveListener(CloseModal);
        }

        private void OpenModal()
        {
            if (_modalGo == null) return;
            _modalGo.SetActive(true);
            if (modalPanelGroup != null)
                modalPanelGroup.alpha = 1f;
            _ = RefreshStatusAsync(CancellationToken.None);
        }

        private void CloseModal()
        {
            if (_modalGo != null)
                _modalGo.SetActive(false);
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            if (linkButton != null) linkButton.interactable = !busy;
            if (loginButton != null) loginButton.interactable = !busy;
            if (logoutAccountButton != null) logoutAccountButton.interactable = !busy;
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
                var mailFromApi = TryGetUserEmail(acc.User);
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
                    linkedBody = $"Привязанный e-mail: {mailKnown}";
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
                            "Привязка выполнена. Профиль на сервере тот же; вход с другого устройства — «Войти по e-mail».";
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
