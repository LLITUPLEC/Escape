using System;
using Project.Utils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>Блокирующее окно: вход с другого устройства, подтверждение и сброс локальной сессии.</summary>
    public sealed class SessionReplacedModal : MonoBehaviour
    {
        public static void Show(string message, Action onOk)
        {
            if (onOk == null) return;
            MainThreadDispatcher.Enqueue(() => ShowInternal(message, onOk));
        }

        private static void ShowInternal(string message, Action onOk)
        {
            if (FindFirstObjectByType<SessionReplacedModal>() != null) return;

            EnsureEventSystem();

            var root = new GameObject("SessionReplacedModal");
            DontDestroyOnLoad(root);
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = short.MaxValue;
            root.AddComponent<GraphicRaycaster>();
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            root.AddComponent<SessionReplacedModal>();

            var dim = new GameObject("Dim");
            dim.transform.SetParent(root.transform, false);
            var dimRt = dim.AddComponent<RectTransform>();
            dimRt.anchorMin = Vector2.zero;
            dimRt.anchorMax = Vector2.one;
            dimRt.offsetMin = dimRt.offsetMax = Vector2.zero;
            var dimImg = dim.AddComponent<Image>();
            dimImg.color = new Color(0f, 0f, 0f, 0.72f);

            var panel = new GameObject("Panel");
            panel.transform.SetParent(root.transform, false);
            var panelRt = panel.AddComponent<RectTransform>();
            panelRt.anchorMin = new Vector2(0.1f, 0.26f);
            panelRt.anchorMax = new Vector2(0.9f, 0.74f);
            panelRt.offsetMin = panelRt.offsetMax = Vector2.zero;
            var panelImg = panel.AddComponent<Image>();
            panelImg.color = new Color(0.15f, 0.15f, 0.18f, 1f);

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(panel.transform, false);
            var textRt = textGo.AddComponent<RectTransform>();
            textRt.anchorMin = new Vector2(0.06f, 0.22f);
            textRt.anchorMax = new Vector2(0.94f, 0.92f);
            textRt.offsetMin = textRt.offsetMax = Vector2.zero;
            var txt = textGo.AddComponent<Text>();
            txt.text = message;
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.fontSize = 26;
            txt.color = Color.white;
            txt.alignment = TextAnchor.MiddleCenter;

            var btnGo = new GameObject("OkButton");
            btnGo.transform.SetParent(panel.transform, false);
            var btnRt = btnGo.AddComponent<RectTransform>();
            btnRt.anchorMin = new Vector2(0.28f, 0.06f);
            btnRt.anchorMax = new Vector2(0.72f, 0.19f);
            btnRt.offsetMin = btnRt.offsetMax = Vector2.zero;
            btnGo.AddComponent<Image>().color = new Color(0.25f, 0.45f, 0.75f, 1f);
            var btn = btnGo.AddComponent<Button>();
            var colors = btn.colors;
            colors.highlightedColor = new Color(0.35f, 0.55f, 0.9f);
            btn.colors = colors;

            var btnLabelGo = new GameObject("Label");
            btnLabelGo.transform.SetParent(btnGo.transform, false);
            var btnLabelRt = btnLabelGo.AddComponent<RectTransform>();
            btnLabelRt.anchorMin = Vector2.zero;
            btnLabelRt.anchorMax = Vector2.one;
            btnLabelRt.offsetMin = btnLabelRt.offsetMax = Vector2.zero;
            var btnTxt = btnLabelGo.AddComponent<Text>();
            btnTxt.text = "ОК";
            btnTxt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            btnTxt.fontSize = 30;
            btnTxt.color = Color.white;
            btnTxt.alignment = TextAnchor.MiddleCenter;
            btnTxt.raycastTarget = false;

            btn.onClick.AddListener(() =>
            {
                onOk.Invoke();
                Destroy(root);
            });
        }

        private static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem");
            DontDestroyOnLoad(es);
            es.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
            es.AddComponent<InputSystemUIInputModule>();
#else
            es.AddComponent<StandaloneInputModule>();
#endif
        }
    }
}
