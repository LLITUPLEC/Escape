using System;
using System.Threading;
using System.Threading.Tasks;
using Project.Nakama;
using Project.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>
    /// Полноэкранный гейт: привязка e-mail к device-аккаунту или вход в существующий (восстановление прогресса на новом телефоне).
    /// Закрыть без успешного submit нельзя — иначе при каждом запуске снова покажется то же окно (тот же DeviceID).
    /// </summary>
    public static class AuthSetupGate
    {
        private static GameObject _blockingOverlay;

        /// <summary>Сразу затемнить экран (до сети), чтобы MainMenu не мелькал с прочерками.</summary>
        public static void EnsureBlockingOverlayVisible()
        {
            if (_blockingOverlay != null) return;
            var root = new GameObject("AuthSetupGateOverlay", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            UnityEngine.Object.DontDestroyOnLoad(root);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 4990;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);

            var dim = new GameObject("Dim", typeof(RectTransform), typeof(Image));
            var dimRt = dim.GetComponent<RectTransform>();
            dimRt.SetParent(root.transform, false);
            Stretch(dimRt);
            dim.GetComponent<Image>().color = new Color(0.05f, 0.06f, 0.09f, 0.96f);
            dim.GetComponent<Image>().raycastTarget = true;

            var label = NewText(root.transform, "Wait", "Подключение…", 22, FontStyle.Normal);
            var labelRt = label.GetComponent<RectTransform>();
            labelRt.anchorMin = new Vector2(0.1f, 0.45f);
            labelRt.anchorMax = new Vector2(0.9f, 0.55f);
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;
            label.alignment = TextAnchor.MiddleCenter;

            _blockingOverlay = root;
        }

        public static void HideBlockingOverlayIfAny()
        {
            if (_blockingOverlay == null) return;
            UnityEngine.Object.Destroy(_blockingOverlay);
            _blockingOverlay = null;
        }

        public static async Task ShowAndWaitAsync(NakamaBootstrap bootstrap, CancellationToken ct)
        {
            if (bootstrap == null) throw new ArgumentNullException(nameof(bootstrap));

            var tcs = new TaskCompletionSource<bool>();
            await MainThreadDispatcher.RunAsync(() =>
            {
                EnsureBlockingOverlayVisible();
                BuildUi(bootstrap, ct, tcs);
            }).ConfigureAwait(true);

            await tcs.Task.ConfigureAwait(true);
        }

        private static void BuildUi(NakamaBootstrap bootstrap, CancellationToken ct, TaskCompletionSource<bool> tcs)
        {
            HideBlockingOverlayIfAny();

            var root = new GameObject("AuthSetupGate", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            UnityEngine.Object.DontDestroyOnLoad(root);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5000;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);

            var dim = new GameObject("Dim", typeof(RectTransform), typeof(Image));
            var dimRt = dim.GetComponent<RectTransform>();
            dimRt.SetParent(root.transform, false);
            Stretch(dimRt);
            dim.GetComponent<Image>().color = new Color(0.05f, 0.06f, 0.09f, 0.96f);
            dim.GetComponent<Image>().raycastTarget = true;

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            var panelRt = panel.GetComponent<RectTransform>();
            panelRt.SetParent(root.transform, false);
            // middle-stretch, left/right 50, height 800
            panelRt.anchorMin = new Vector2(0f, 0.5f);
            panelRt.anchorMax = new Vector2(1f, 0.5f);
            panelRt.pivot = new Vector2(0.5f, 0.5f);
            panelRt.anchoredPosition = Vector2.zero;
            panelRt.sizeDelta = new Vector2(-100f, 800f); // left/right 50 → width = parent - 100
            panel.GetComponent<Image>().color = new Color(0.12f, 0.14f, 0.2f, 1f);
            var v = panel.GetComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(24, 24, 24, 24);
            v.spacing = 10f;
            v.childControlHeight = true;
            v.childControlWidth = true;
            v.childForceExpandWidth = true;

            NewText(panel.transform, "Title", "Сохранение прогресса", 40, FontStyle.Bold)
                .alignment = TextAnchor.MiddleCenter;
            NewText(panel.transform, "Hint",
                "Укажите e-mail и пароль.\n" +
                "• Новый аккаунт — привяжем к этому устройству.\n" +
                "• Уже играли на другом телефоне — войдёте и восстановите прогресс.",
                30, FontStyle.Normal).alignment = TextAnchor.MiddleCenter;

            var email = CreateInput(panel.transform, "Email", "E-mail", false);
            var pass = CreateInput(panel.transform, "Pass", "Пароль (мин. 8)", true);
            var status = NewText(panel.transform, "Status", "", 15, FontStyle.Normal);
            status.alignment = TextAnchor.MiddleCenter;
            status.color = new Color(1f, 0.85f, 0.7f);

            var busy = false;

            async Task SubmitAsync(bool preferRestore)
            {
                if (busy) return;
                var em = (email.text ?? "").Trim();
                var pw = pass.text ?? "";
                if (string.IsNullOrEmpty(em) || pw.Length < 8)
                {
                    status.text = "Введите e-mail и пароль не короче 8 символов.";
                    return;
                }

                busy = true;
                status.text = preferRestore ? "Вход и восстановление…" : "Регистрация…";
                try
                {
                    await bootstrap.EnsureConnectedAsync(ct);
                    if (preferRestore)
                    {
                        // Явный вход в существующий аккаунт + LinkDevice текущего телефона.
                        await bootstrap.LoginWithEmailAsync(em, pw, create: false, ct);
                    }
                    else
                    {
                        // Сначала привязка к текущему device-user; если почта занята — вход в неё (перенос прогресса).
                        try
                        {
                            await bootstrap.LinkEmailAsync(em, pw, ct);
                        }
                        catch
                        {
                            await bootstrap.LoginWithEmailAsync(em, pw, create: false, ct);
                        }
                    }

                    PlayerPrefs.SetString(NakamaBootstrap.PrefKnownLinkedEmail, em);
                    PlayerPrefs.DeleteKey(NakamaBootstrap.PrefForceEmailSetup);
                    PlayerPrefs.Save();
                    UnityEngine.Object.Destroy(root);
                    tcs.TrySetResult(true);
                }
                catch (Exception e)
                {
                    status.text = "Ошибка: " + e.Message;
                    busy = false;
                }
            }

            AddActionButton(panel.transform, "Создать / привязать", new Color(0.25f, 0.55f, 0.35f, 1f),
                () => _ = SubmitAsync(preferRestore: false));
            AddActionButton(panel.transform, "У меня уже есть аккаунт", new Color(0.28f, 0.35f, 0.55f, 1f),
                () => _ = SubmitAsync(preferRestore: true));
        }

        private static void AddActionButton(Transform parent, string label, Color color, Action onClick)
        {
            var btnGo = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            btnGo.transform.SetParent(parent, false);
            var le = btnGo.GetComponent<LayoutElement>();
            le.preferredHeight = 80f;
            le.minHeight = 80f;
            btnGo.GetComponent<Image>().color = color;

            var labelTx = NewText(btnGo.transform, "L", label, 30, FontStyle.Bold);
            labelTx.alignment = TextAnchor.MiddleCenter;
            var labelRt = labelTx.GetComponent<RectTransform>();
            // middle-stretch
            labelRt.anchorMin = new Vector2(0f, 0.5f);
            labelRt.anchorMax = new Vector2(1f, 0.5f);
            labelRt.pivot = new Vector2(0.5f, 0.5f);
            labelRt.anchoredPosition = Vector2.zero;
            labelRt.sizeDelta = new Vector2(0f, 80f);
            var labelLe = labelTx.GetComponent<LayoutElement>();
            if (labelLe != null)
            {
                labelLe.ignoreLayout = true;
                labelLe.preferredHeight = 80f;
            }

            btnGo.GetComponent<Button>().onClick.AddListener(() => onClick());
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static Text NewText(Transform parent, string name, string s, int size, FontStyle fs)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().minHeight = size + 12;
            var t = go.GetComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.text = s;
            t.fontSize = size;
            t.fontStyle = fs;
            t.color = new Color(0.94f, 0.92f, 0.86f);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        private static InputField CreateInput(Transform parent, string name, string placeholder, bool password)
        {
            var slot = new GameObject(name + "Slot", typeof(RectTransform), typeof(LayoutElement));
            slot.transform.SetParent(parent, false);
            slot.GetComponent<LayoutElement>().minHeight = 44f;
            return SettingsModalUiHelper.CreateInputField(slot.GetComponent<RectTransform>(), name, placeholder, password,
                SettingsModalUiHelper.GetDefaultUIFont());
        }
    }
}
