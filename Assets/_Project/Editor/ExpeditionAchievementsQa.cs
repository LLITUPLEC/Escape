using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Project.Achievements;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Editor
{
    /// <summary>Real achievement renderer/events with temporary in-memory cache; no claim, RPC or PlayerPrefs writes.</summary>
    public static class ExpeditionAchievementsQa
    {
        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        public static string Run(string mode = "all")
        {
            if (mode != "all") throw new ArgumentException("Use all: fixtures always restore in the same call.");
            if (!Application.isPlaying) throw new InvalidOperationException("Play Mode required; this API does not start Play.");
            var panel = UnityEngine.Object.FindFirstObjectByType<AchievementsPanelController>();
            if (panel == null || !panel.IsVisible) throw new InvalidOperationException("Open achievements on the Obsession tab first.");
            var chain = AchievementCatalog.FindChain("obs.cross");
            if (chain?.Thresholds == null || chain.Thresholds.Length < 3
                || chain.Thresholds[0] != 10 || chain.Thresholds[1] != 50 || chain.Thresholds[2] != 250)
                throw new InvalidOperationException("Current real obs.cross catalog must contain delta steps 10,50,250; no fabricated definition is substituted.");
            var rowList = Read<List<AchievementChainRowView>>(panel, "_spawnedRows");
            var row = rowList.FirstOrDefault(r => r != null && r.Slots != null && r.Slots.Length > 0
                && r.Slots[0].gameObject.name.EndsWith("_" + chain.ChainId, StringComparison.Ordinal));
            if (row == null) throw new InvalidOperationException("The actual obs.cross row is not rendered. Select Obsession first.");
            var card = row.GetComponent<ExpeditionAchievementCard>();
            if (card == null) throw new InvalidOperationException("Expedition achievement card missing.");
            var nav = GameObject.Find("ExpeditionNav1")?.GetComponent<Button>();
            var badge = nav != null ? nav.GetComponent<AchievementNavigationBadge>() : null;
            if (badge == null) throw new InvalidOperationException("Root must call AchievementNavigationBadge.Bind(ExpeditionNav1) first.");
            AchievementProgressStorage.EnsureLoaded();
            var statsField = typeof(AchievementProgressStorage).GetField("_stats", PrivateStatic);
            var claimsField = typeof(AchievementProgressStorage).GetField("_claimedStepTokens", PrivateStatic);
            var revisionField = typeof(AchievementProgressStorage).GetField("_confirmedClaimRevision", PrivateStatic);
            var originalStats = statsField.GetValue(null);
            var originalClaims = claimsField.GetValue(null);
            var originalRevision = revisionField.GetValue(null);
            var sent = (HashSet<string>)typeof(AchievementUiPending).GetField("AwaitClaimToastSentForStepTokens", PrivateStatic).GetValue(null);
            var sentSnapshot = sent.ToArray();
            var toast = UnityEngine.Object.FindFirstObjectByType<AchievementInGameToastHost>(FindObjectsInactive.Include);
            var hadToast = toast != null;
            if (toast != null && (Read<Coroutine>(toast, "_routine") != null || Read<Queue<AchievementUnlockInfo>>(toast, "_queue").Count > 0))
                throw new InvalidOperationException("Wait for real queued achievement notifications before running a fixture.");
            toast = AchievementInGameToastHost.Ensure();
            var wasToastEnabled = toast.enabled;
            toast.enabled = false;
            var queue = Read<Queue<AchievementUnlockInfo>>(toast, "_queue");
            var toastSeen = Read<HashSet<string>>(toast, "_seen");
            var toastSeenSnapshot = toastSeen.ToArray();
            var queuedAt = Read<Dictionary<string, float>>(toast, "_queuedAt");
            var queuedAtSnapshot = new Dictionary<string, float>(queuedAt);
            toastSeen.Remove(chain.ChainId + "|2|" + (int)AchievementNoticeKind.CriterionMetAwaitClaim);
            var group = Read<CanvasGroup>(panel, "rootCanvasGroup");
            var wasInteractable = group != null && group.interactable;
            var wasBlocking = group != null && group.blocksRaycasts;
            var rowIds = rowList.Select(r => r.GetInstanceID()).ToArray();
            var fixtures = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var definition in AchievementCatalog.Chains) if (definition != null) fixtures[definition.StatKey] = 0;
            var claims = new HashSet<string>(StringComparer.Ordinal);
            var notices = new List<AchievementUnlockInfo>();
            Action<AchievementUnlockInfo> onNotice = info => notices.Add(info);
            try
            {
                if (group != null) { group.interactable = false; group.blocksRaycasts = false; }
                statsField.SetValue(null, fixtures); claimsField.SetValue(null, claims);
                fixtures[chain.StatKey] = 309;
                var before = AchievementUiPending.CaptureReachedUnclaimedStepTokens();
                Check(AchievementUiRules.CumulativeRequired(chain, 0) == 10
                    && AchievementUiRules.CumulativeRequired(chain, 1) == 60
                    && AchievementUiRules.CumulativeRequired(chain, 2) == 310, "Server eligibility totals changed.");
                Check(AchievementUiRules.StepProgress(chain, 2, 309) == 249, "Third step must show 249/250 at total309.");
                sent.Remove(chain.ChainId + "|2");
                AchievementLifecycle.OnAwaitingClaim += onNotice;
                fixtures[chain.StatKey] = 310;
                var raised = AchievementUiPending.RaiseNewCompletedSince(before);
                Check(raised == 1 && notices.Count == 1 && notices[0].ChainId == chain.ChainId && notices[0].StepIndex == 2,
                    "Third completion must emit exactly one actual lifecycle event at310, even without prior claims.");
                Check(queue.Count == 1 && queue.Peek().StepIndex == 2, "Production in-game toast host did not receive the third-step completion.");
                Check(AchievementUiPending.RaiseNewCompletedSince(before) == 0, "Repeated snapshots duplicated completion toast.");
                Check(!AchievementRewardClaim.CanClaimStep(chain, 2), "Fixture incorrectly bypassed ordered reward claims.");
                AchievementLifecycle.NotifyDataChanged();
                Check(badge.PendingCount == 1, "Unclaimed first reward must produce navigation badge1.");
                Check(badge.transform.Find("ExpeditionAchievementBadge").GetComponent<CanvasGroup>().blocksRaycasts == false,
                    "Badge intercepts navigation raycasts.");
                claims.Add(chain.ChainId + ":0"); claims.Add(chain.ChainId + ":1");
                AchievementLifecycle.NotifyDataChanged();
                Check(AchievementRewardClaim.CanClaimStep(chain, 2), "Third reward not claimable after actual preceding claim tokens.");
                Check(card.CurrentStep == 2 && card.ProgressLabel.text.Replace(" ", "") == "250/250", "Current card does not show delta250/250.");
                Check(card.TargetLabels[0].text == "10" && card.TargetLabels[1].text == "50" && card.TargetLabels[2].text == "250", "Full chain displays cumulative targets.");
                Check(card.DescriptionLabel.fontSizeMin >= 26, "Main criterion text became too small.");
                Canvas.ForceUpdateCanvases();
                var scroll = row.GetComponentInParent<ScrollRect>();
                var rowHeight = (row.transform as RectTransform).rect.height;
                var visible = scroll.viewport.rect.height / Mathf.Max(1, rowHeight);
                Check(visible >= 2 && visible <= 3.3f, "Expected about2–3 readable cards per viewport, actual ratio=" + visible);
                for (var i = 0; i < chain.Thresholds.Length && i < row.Slots.Length; i++)
                {
                    var rect = row.Slots[i].transform as RectTransform;
                    Check(rect.anchorMin.x >= 0 && rect.anchorMax.x <= 1 && rect.anchorMin.y >= 0 && rect.anchorMax.y <= .30f,
                        "Step chain leaves the lower full-width row.");
                }
                fixtures[chain.StatKey] = 60; AchievementLifecycle.NotifyDataChanged();
                Check(card.ProgressLabel.text.Replace(" ", "") == "0/250", "New third step must start0/250 at total60.");
                fixtures[chain.StatKey] = 309; AchievementLifecycle.NotifyDataChanged();
                Check(card.ProgressLabel.text.Replace(" ", "") == "249/250", "In-progress card249/250 incorrect.");
                for (var i = 0; i < chain.Thresholds.Length; i++) claims.Add(chain.ChainId + ":" + i);
                fixtures[chain.StatKey] = AchievementUiRules.CumulativeRequired(chain, chain.Thresholds.Length - 1);
                AchievementLifecycle.NotifyDataChanged();
                Check(badge.PendingCount == 0, "Navigation badge did not clear after all fixture rewards claimed.");
                Check(rowIds.SequenceEqual(rowList.Select(r => r.GetInstanceID())), "Live progress destroys/recreates all rendered cards.");
                return "PASS: actual catalog deltas10/50/250 retain totals10/60/310; current0/250,249/250,250/250; completion310 emits one lifecycle event and reaches production toast without prior claims; dedup and ordered claim eligibility preserved; nav badge1→0; about" + visible.ToString("0.0") + " cards, full horizontal chain; rows reused. All cache/UI/toast state restored, no RPC/claim/PlayerPrefs writes.";
            }
            finally
            {
                AchievementLifecycle.OnAwaitingClaim -= onNotice;
                statsField.SetValue(null, originalStats); claimsField.SetValue(null, originalClaims); revisionField.SetValue(null, originalRevision);
                sent.Clear(); foreach (var token in sentSnapshot) sent.Add(token);
                queue.Clear();
                toastSeen.Clear(); foreach (var token in toastSeenSnapshot) toastSeen.Add(token);
                queuedAt.Clear(); foreach (var entry in queuedAtSnapshot) queuedAt.Add(entry.Key, entry.Value);
                if (hadToast) toast.enabled = wasToastEnabled;
                else if (toast != null) UnityEngine.Object.DestroyImmediate(toast.gameObject);
                if (group != null) { group.interactable = wasInteractable; group.blocksRaycasts = wasBlocking; }
                AchievementLifecycle.NotifyDataChanged();
            }
        }

        private static T Read<T>(object target, string field) => (T)target.GetType().GetField(field, PrivateInstance).GetValue(target);
        private static void Check(bool result, string message) { if (!result) throw new InvalidOperationException(message); }
    }
}
