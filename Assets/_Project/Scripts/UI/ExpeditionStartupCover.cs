using System;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>Scene-loaded runs before the first render; legacy prefabs never flash while their adapters settle.</summary>
    public static class ExpeditionStartupCover
    {
        public static GameObject Show(Scene scene)
        {
            var root = new GameObject("ExpeditionStartupCover", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            SceneManager.MoveGameObjectToScene(root, scene);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 4500; // Authentication/session UI retains its higher independent ordering.
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = .5f;
            ExpeditionUi.Fill(root.transform, "OpaqueCover", 0, 0, 1, 1, new Color32(7, 20, 24, 255)).raycastTarget = true;
            var image = ExpeditionUi.Rect(root.transform, "CurrentWorld", 0, 0, 1, 1).gameObject.AddComponent<Image>();
            var art = scene.name == "MainMenu" ? "shelter-mine-light" : scene.name == "MineScene" ? "mine-backdrop" :
                scene.name == "WorkshopScene" ? "workshop-backdrop" : scene.name == "DuelMatch3" ? "battle-world-v4" : "arena-backdrop";
            image.sprite = Resources.Load<Sprite>("Expedition/Art/" + art);
            image.raycastTarget = false;
            if (image.sprite != null)
            {
                var fit = image.gameObject.AddComponent<AspectRatioFitter>();
                fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                fit.aspectRatio = image.sprite.rect.width / image.sprite.rect.height;
            }
            if (scene.name != "DuelMatch3") SetLoadingState(root, false, null);
            return root;
        }
        public static void SetLoadingState(GameObject cover, bool failed, Action retry)
        {
            if (cover == null) return;
            ExpeditionUi.Ensure<ExpeditionStartupLoadingView>(cover).Bind(failed, retry);
        }
    }

    public sealed class ExpeditionStartupLoadingView : MonoBehaviour
    {
        private TMP_Text _status;
        private Button _retry;
        private Action _onRetry;
        public void Bind(bool failed, Action retry)
        {
            if (_status == null)
            {
                var safe = ExpeditionUi.Rect(transform, "LoadingSafeArea", 0, 0, 1, 1);
                safe.gameObject.AddComponent<ExpeditionSafeArea>();
                var plate = ExpeditionUi.Panel(safe, "LoadingPanel", .12f, .12f, .88f, .29f);
                _status = ExpeditionUi.Text(plate.transform, "LoadingStatus", "", 32, .06f, .43f, .94f, .92f, TextAlignmentOptions.Center);
                _status.enableAutoSizing = true; _status.fontSizeMin = 24; _status.fontSizeMax = 32;
                _retry = ExpeditionUi.Button(plate.transform, "RetryInitialData", "Повторить", .20f, .06f, .80f, .37f, null, true, 28);
            }
            _status.text = failed ? "Не удалось загрузить данные героя.\nПроверьте подключение и повторите." : "Загружаем данные героя…";
            _status.color = failed ? ExpeditionTheme.Coral : ExpeditionTheme.Ivory;
            _retry.gameObject.SetActive(failed && retry != null);
            if (_onRetry != retry)
            {
                _onRetry = retry; _retry.onClick.RemoveAllListeners();
                if (retry != null) _retry.onClick.AddListener(() => _onRetry?.Invoke());
            }
        }
    }
}
