using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>One small overlay for transitions; no render textures, post-processing or persistent polling.</summary>
    public sealed class ExpeditionSceneTransition : MonoBehaviour
    {
        private static ExpeditionSceneTransition _instance;
        private CanvasGroup _group;
        private bool _busy;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState() => _instance = null;

        public static void Load(string scene)
        {
            if (!Application.CanStreamedLevelBeLoaded(scene)) return;
            if (_instance == null)
            {
                var root = new GameObject("ExpeditionSceneTransition", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup), typeof(GraphicRaycaster));
                DontDestroyOnLoad(root);
                var canvas = root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 32700;
                _instance = root.AddComponent<ExpeditionSceneTransition>();
                _instance._group = root.GetComponent<CanvasGroup>();
                _instance._group.alpha = 0;
                _instance._group.blocksRaycasts = false;
                var tint = ExpeditionUi.Rect(root.transform, "Fade", 0, 0, 1, 1).gameObject.AddComponent<Image>();
                tint.color = new Color32(7, 20, 25, 255);
            }
            if (!_instance._busy) _instance.StartCoroutine(_instance.Change(scene));
        }

        private IEnumerator Change(string scene)
        {
            _busy = true;
            _group.blocksRaycasts = true;
            for (float t = 0; t < .12f; t += Time.unscaledDeltaTime)
            {
                _group.alpha = Mathf.SmoothStep(0, 1, t / .12f);
                yield return null;
            }
            _group.alpha = 1;
            var operation = SceneManager.LoadSceneAsync(scene);
            while (operation != null && !operation.isDone) yield return null;
            yield return null; // Allow the new scene's presentation to finish binding before revealing it.
            yield return null;
            for (float t = 0; t < .18f; t += Time.unscaledDeltaTime)
            {
                _group.alpha = 1 - Mathf.SmoothStep(0, 1, t / .18f);
                yield return null;
            }
            _group.alpha = 0;
            _group.blocksRaycasts = false;
            _busy = false;
        }
    }

    [DisallowMultipleComponent]
    public sealed class ExpeditionPanelArrival : MonoBehaviour
    {
        private CanvasGroup _group;
        private float _started;
        private bool _running;
        private void OnEnable() => Play();
        public void Play()
        {
            _group = ExpeditionUi.Ensure<CanvasGroup>(gameObject);
            _started = Time.unscaledTime;
            _running = true;
            _group.alpha = 0;
        }
        private void Update()
        {
            if (!_running) return;
            var t = Mathf.Clamp01((Time.unscaledTime - _started) / .17f);
            _group.alpha = Mathf.SmoothStep(0, 1, t);
            _running = t < 1;
        }
        private void OnDisable() { _running = false; if (_group != null) _group.alpha = 1; }
    }
}
