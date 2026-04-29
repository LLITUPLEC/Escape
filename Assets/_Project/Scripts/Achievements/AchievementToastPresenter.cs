using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Project.Achievements
{
    /// <summary>Всплывающее уведомление о получении шага цепочки.</summary>
    public sealed class AchievementToastPresenter : MonoBehaviour
    {
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private RectTransform panelRect;
        [SerializeField] private TMP_Text titleTmp;
        [SerializeField] private TMP_Text rewardTmp;
        [SerializeField] private float showSeconds = 4f;
        [SerializeField] private float slidePixels = 120f;

        private readonly Queue<AchievementUnlockInfo> _queue = new Queue<AchievementUnlockInfo>();
        private Coroutine _routine;

        private TMP_FontAsset ResolvePreferredFont()
        {
            var panel = GetComponentInParent<AchievementsPanelController>();
            return panel != null ? panel.AchievementUiFontReference : null;
        }

        private void Awake()
        {
            AchievementsTmpMaterialRepair.RepairHierarchy(transform, ResolvePreferredFont());

            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.blocksRaycasts = false;
                canvasGroup.interactable = false;
            }

            if (panelRect != null)
                panelRect.anchoredPosition -= new Vector2(0f, slidePixels);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (Application.isPlaying) return;
            AchievementsTmpMaterialRepair.RepairHierarchy(transform, ResolvePreferredFont());
        }
#endif

        private void OnEnable()
        {
            AchievementLifecycle.OnRewardClaimed += Enqueue;
        }

        private void OnDisable()
        {
            AchievementLifecycle.OnRewardClaimed -= Enqueue;
        }

        private void Enqueue(AchievementUnlockInfo info)
        {
            if (info == null) return;
            _queue.Enqueue(info);
            if (_routine == null && isActiveAndEnabled)
                _routine = StartCoroutine(RunQueue());
        }

        private IEnumerator RunQueue()
        {
            while (_queue.Count > 0)
            {
                var info = _queue.Dequeue();
                if (titleTmp != null)
                    titleTmp.text = info.Title ?? string.Empty;
                if (rewardTmp != null)
                    rewardTmp.text = info.RewardLine ?? string.Empty;

                if (canvasGroup != null && panelRect != null)
                {
                    yield return AnimateIn();
                    yield return new WaitForSecondsRealtime(showSeconds);
                    yield return AnimateOut();
                }
                else
                {
                    yield return null;
                }
            }

            _routine = null;
        }

        private IEnumerator AnimateIn()
        {
            var fromPos = panelRect.anchoredPosition;
            var toPos = fromPos + new Vector2(0f, slidePixels);
            float t = 0f;
            const float dur = 0.35f;
            canvasGroup.alpha = 0f;
            panelRect.anchoredPosition = fromPos;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                var k = Mathf.Clamp01(t / dur);
                var ease = 1f - Mathf.Pow(1f - k, 3f);
                canvasGroup.alpha = ease;
                panelRect.anchoredPosition = Vector2.Lerp(fromPos, toPos, ease);
                yield return null;
            }

            canvasGroup.alpha = 1f;
            panelRect.anchoredPosition = toPos;
        }

        private IEnumerator AnimateOut()
        {
            var fromPos = panelRect.anchoredPosition;
            var toPos = fromPos - new Vector2(0f, slidePixels * 0.6f);
            float t = 0f;
            const float dur = 0.28f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                var k = Mathf.Clamp01(t / dur);
                canvasGroup.alpha = 1f - k;
                panelRect.anchoredPosition = Vector2.Lerp(fromPos, toPos, k);
                yield return null;
            }

            canvasGroup.alpha = 0f;
            panelRect.anchoredPosition = fromPos - new Vector2(0f, slidePixels);
        }
    }
}
