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
    /// Полноэкранный гейт первого входа после удаления аккаунта: e-mail + пароль → create+link к текущему device user.
    /// </summary>
    public static class AuthSetupGate
    {
        public static async Task ShowAndWaitAsync(NakamaBootstrap bootstrap, CancellationToken ct)
        {
            if (bootstrap == null) throw new ArgumentNullException(nameof(bootstrap));

            var tcs = new TaskCompletionSource<bool>();
            await MainThreadDispatcher.RunAsync(() =>
            {
                BuildUi(bootstrap, ct, tcs);
            }).ConfigureAwait(true);

            await tcs.Task.ConfigureAwait(true);
        }

        private static void BuildUi(NakamaBootstrap bootstrap, CancellationToken ct, TaskCompletionSource<bool> tcs)
        {
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
            panelRt.anchorMin = new Vector2(0.5f, 0.5f);
            panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.sizeDelta = new Vector2(520f, 420f);
            panel.GetComponent<Image>().color = new Color(0.12f, 0.14f, 0.2f, 1f);
            var v = panel.GetComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(24, 24, 24, 24);
            v.spacing = 12f;
            v.childControlHeight = true;
            v.childControlWidth = true;
            v.childForceExpandWidth = true;

            var title = NewText(panel.transform, "Title", "Добро пожаловать", 28, FontStyle.Bold);
            title.alignment = TextAnchor.MiddleCenter;
            NewText(panel.transform, "Hint",
                "Создайте аккаунт: укажите e-mail и пароль.\nОни будут привязаны к этому устройству.",
                18, FontStyle.Normal).alignment = TextAnchor.MiddleCenter;

            var email = CreateInput(panel.transform, "Email", "E-mail", false);
            var pass = CreateInput(panel.transform, "Pass", "Пароль (мин. 8)", true);
            var status = NewText(panel.transform, "Status", "", 16, FontStyle.Normal);
            status.alignment = TextAnchor.MiddleCenter;
            status.color = new Color(1f, 0.85f, 0.7f);

            var btnGo = new GameObject("Submit", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            btnGo.transform.SetParent(panel.transform, false);
            btnGo.GetComponent<LayoutElement>().minHeight = 48f;
            btnGo.GetComponent<Image>().color = new Color(0.25f, 0.55f, 0.35f, 1f);
            NewText(btnGo.transform, "L", "Продолжить", 22, FontStyle.Bold).alignment = TextAnchor.MiddleCenter;

            var busy = false;
            btnGo.GetComponent<Button>().onClick.AddListener(async () =>
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
                status.text = "Регистрация…";
                try
                {
                    await bootstrap.EnsureConnectedAsync(ct);
                    // Сначала привязываем e-mail к текущему device-аккаунту; если почта уже занята — вход в неё.
                    try
                    {
                        await bootstrap.LinkEmailAsync(em, pw, ct);
                    }
                    catch
                    {
                        await bootstrap.LoginWithEmailAsync(em, pw, create: false, ct);
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
            });
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
