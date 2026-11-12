using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Project.Achievements;
using Project.Leaderboard;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Editor
{
    /// <summary>Production prefabs/renderers with temporary UI or in-memory achievement state. No RPC or claims.</summary>
    public static class ExpeditionMetaRowsQa
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;
        private static readonly List<LeaderboardRowView> FixtureRows = new List<LeaderboardRowView>();
        private static readonly List<GameObject> HiddenRows = new List<GameObject>();
        private static ScrollRect _scroll;
        private static float _scrollPosition;

        public static string ShowLeaderboard()
        {
            RequirePlay();
            if (FixtureRows.Count > 0) throw new InvalidOperationException("Restore the previous row fixture first.");
            var controller = UnityEngine.Object.FindFirstObjectByType<LeaderboardPanelController>();
            Check(controller != null && controller.IsVisible, "Open the actual leaderboard first.");
            _scroll = Read<ScrollRect>(controller, "scrollRect");
            var content = Read<RectTransform>(controller, "scrollContent");
            Check(_scroll != null && content != null, "Actual list and ScrollRect are required.");
            // Cancel an in-flight read so its completion cannot overwrite a screenshot fixture. No request is made.
            Read<System.Threading.CancellationTokenSource>(controller, "_refreshCts")?.Cancel();
            _scrollPosition = _scroll.verticalNormalizedPosition;
            foreach (Transform child in content)
                if (child.gameObject.activeSelf) { HiddenRows.Add(child.gameObject); child.gameObject.SetActive(false); }
            var empty = Project.UI.ExpeditionUi.Find(_scroll.viewport, "ExpeditionEmptyState");
            if (empty != null && empty.gameObject.activeSelf) { HiddenRows.Add(empty.gameObject); empty.gameObject.SetActive(false); }
            var fields = new[] { "podiumGoldPrefab", "podiumSilverPrefab", "podiumBronzePrefab", "standardRowPrefab" };
            try
            {
                for (var i = 0; i < fields.Length; i++)
                {
                    var prefab = Read<LeaderboardRowView>(controller, fields[i]);
                    Check(prefab != null, "Actual row prefab is missing: " + fields[i]);
                    var row = UnityEngine.Object.Instantiate(prefab, content);
                    FixtureRows.Add(row);
                    row.Bind(new LeaderboardEntry
                    {
                        Rank = i + 1, Nickname = "UI-фикстура «Строка " + (i + 1) + "»", UserId = "ui-only-" + i,
                        Score = 1885 - i * 317, SecondaryScore = 629 - i * 73, Delta = LeaderboardRankDelta.None
                    });
                }
                Canvas.ForceUpdateCanvases();
                _scroll.verticalNormalizedPosition = 1;
                return "SHOWN: four actual row prefabs with explicitly labelled UI-only entries, no podium or player figures. VerifyLeaderboard(), then RestoreLeaderboard(); no RPC, rewards or account writes.";
            }
            catch { RestoreLeaderboard(); throw; }
        }

        public static string VerifyLeaderboard()
        {
            RequirePlay();
            Check(FixtureRows.Count == 4, "Call ShowLeaderboard() first.");
            Canvas.ForceUpdateCanvases();
            var expected = new[] { "leaderboard-gold", "leaderboard-silver", "leaderboard-bronze" };
            for (var i = 0; i < FixtureRows.Count; i++)
            {
                var row = FixtureRows[i];
                var image = Read<Image>(row, "rowBackground");
                var name = Read<TMP_Text>(row, "nicknameText");
                var score = Read<TMP_Text>(row, "scoreText");
                var rank = Read<TMP_Text>(row, "rankText");
                var secondary = Read<TMP_Text>(row, "secondaryStatText");
                Check(row.BoundRank == i + 1 && rank.text == (i + 1).ToString(), "Actual rank writer changed.");
                Check(score.text == LeaderboardRowView.FormatScore(1885 - i * 317), "Actual bound score changed.");
                Check(secondary.text == (629 - i * 73).ToString("N0", System.Globalization.CultureInfo.InvariantCulture), "Secondary value changed.");
                Check(!Read<Image>(row, "avatarImage").gameObject.activeInHierarchy
                    && !Read<Image>(row, "avatarFrame").gameObject.activeInHierarchy, "Player portrait/frame is still visible.");
                Check(name.alignment == TextAlignmentOptions.MidlineLeft && score.alignment == TextAlignmentOptions.MidlineLeft,
                    "Main row values do not align on horizontal baselines.");
                Check(name.rectTransform.position.x > rank.rectTransform.position.x && name.rectTransform.position.y > score.rectTransform.position.y,
                    "Rank/name/score geometry is not a readable horizontal structure.");
                Check(secondary.rectTransform.position.x > score.rectTransform.position.x, "Secondary value overlaps the main score.");
                Check(name.textInfo.characterCount > 0 && name.fontSize >= 30 && score.fontSize >= 29, "Row text is absent or too small.");
                if (i < 3)
                    Check(image.sprite != null && image.sprite.name == expected[i] && image.type == Image.Type.Simple && image.preserveAspect,
                        "Top-three ornamental frame missing or stretched: " + expected[i]);
                else Check(image.sprite != null && !expected.Contains(image.sprite.name), "Ordinary rank uses a top-three frame.");
            }
            var root = _scroll.transform.root;
            var podium = Project.UI.ExpeditionUi.Find(root, "ExpeditionLeaderboardPodium");
            Check(podium == null || !podium.gameObject.activeInHierarchy, "Podium stage still visible.");
            return "PASS: actual four row prefabs bind correct rank/score/secondary values; distinct gold/silver/bronze imported frames; ordinary frame; no avatar/podium; readable horizontal text. UI-only fixture still shown; RestoreLeaderboard() required.";
        }

        public static string RestoreLeaderboard()
        {
            foreach (var row in FixtureRows) if (row != null) UnityEngine.Object.DestroyImmediate(row.gameObject);
            FixtureRows.Clear();
            foreach (var row in HiddenRows) if (row != null) row.SetActive(true);
            HiddenRows.Clear();
            Canvas.ForceUpdateCanvases();
            if (_scroll != null) _scroll.verticalNormalizedPosition = _scrollPosition;
            _scroll = null;
            return "RESTORED: temporary UI rows removed, actual rows and scroll restored. Open/refresh retains the controller's existing server-read behavior.";
        }

        public static string RunAchievementStates()
        {
            RequirePlay();
            var panel = UnityEngine.Object.FindFirstObjectByType<AchievementsPanelController>();
            Check(panel != null && panel.IsVisible, "Open achievements on Obsession first.");
            var chain = AchievementCatalog.FindChain("obs.cross");
            Check(chain?.Thresholds != null && chain.Thresholds.Length >= 3, "Actual obs.cross catalog missing.");
            var row = Read<List<ExpeditionAchievementJournalRow>>(panel, "_spawnedRows")
                .FirstOrDefault(r => r != null && r.ChainId == chain.ChainId);
            Check(row != null, "Actual obs.cross row missing; select Obsession.");
            var progress = row.transform.Find("MedalProgress")?.GetComponent<TMP_Text>();
            var status = row.transform.Find("MedalStatus")?.GetComponent<TMP_Text>();
            var reward = row.transform.Find("MedalReward")?.GetComponent<TMP_Text>();
            var art = row.transform.Find("MedalIcon")?.GetComponent<Image>();
            var button = row.GetComponent<Button>();
            var journal = row.GetComponentInParent<ScrollRect>();
            Check(progress != null && status != null && reward != null && art != null && button != null && journal != null,
                "Production Journal row is missing progress, status, reward, artwork, or its click target.");
            AchievementProgressStorage.EnsureLoaded();
            var stats = typeof(AchievementProgressStorage).GetField("_stats", StaticPrivate);
            var claims = typeof(AchievementProgressStorage).GetField("_claimedStepTokens", StaticPrivate);
            var oldStats = stats.GetValue(null); var oldClaims = claims.GetValue(null);
            var counters = new Dictionary<string, int>(StringComparer.Ordinal) { [chain.StatKey] = 5 };
            var claimed = new HashSet<string>(StringComparer.Ordinal);
            var group = Read<CanvasGroup>(panel, "rootCanvasGroup");
            var interactive = group != null && group.interactable;
            var blocks = group != null && group.blocksRaycasts;
            var scrollPosition = journal.verticalNormalizedPosition;
            try
            {
                if (group != null) { group.interactable = false; group.blocksRaycasts = false; }
                stats.SetValue(null, counters); claims.SetValue(null, claimed);
                AchievementLifecycle.NotifyDataChanged(); Canvas.ForceUpdateCanvases();
                journal.verticalNormalizedPosition = 1f;
                Canvas.ForceUpdateCanvases();
                Check(row.CurrentStep == 0 && progress.text.Replace(" ", "") == "5/10"
                    && status.text == $"Ступень 1 из {chain.Thresholds.Length}" && !AchievementRewardClaim.CanClaimStep(chain, 0),
                    "In-progress Journal step or eligibility changed.");
                counters[chain.StatKey] = 310;
                claimed.Add(chain.ChainId + ":0"); claimed.Add(chain.ChainId + ":1");
                AchievementLifecycle.NotifyDataChanged(); Canvas.ForceUpdateCanvases();
                Check(row.CurrentStep == 2 && progress.text.Replace(" ", "") == "250/250", "Delta progress or ordered current step changed.");
                Check(status.text == "Награда готова" && !string.IsNullOrWhiteSpace(reward.text)
                    && AchievementRewardClaim.CanClaimStep(chain, 2), "Eligible unclaimed reward lacks a clear Journal status or reward preview.");
                var catalog = Read<AchievementIconCatalog>(panel, "iconCatalog");
                Check(catalog != null && art.sprite == catalog.GetDisplayIcon(chain.ChainId), "Original achievement artwork was replaced.");
                CheckDrawnAspect(art);
                claimed.Clear(); AchievementLifecycle.NotifyDataChanged();
                Check(!AchievementRewardClaim.CanClaimStep(chain, 2) && AchievementRewardClaim.CanClaimStep(chain, 0)
                    && row.CurrentStep == 0 && progress.text.Replace(" ", "") == "10/10" && status.text == "Награда готова",
                    "Completed later step incorrectly bypasses the earliest unclaimed reward.");
                for (var i = 0; i < chain.Thresholds.Length; i++) claimed.Add(chain.ChainId + ":" + i);
                var lastStep = chain.Thresholds.Length - 1;
                var lastTarget = AchievementUiRules.StepTarget(chain, lastStep);
                counters[chain.StatKey] = AchievementUiRules.CumulativeRequired(chain, lastStep);
                AchievementLifecycle.NotifyDataChanged();
                Check(row.CurrentStep == lastStep && progress.text.Replace(" ", "") == $"{lastTarget}/{lastTarget}"
                    && status.text == "Получено" && !AchievementRewardClaim.CanClaimStep(chain, lastStep),
                    "Completed Journal row loses its final progress or incorrectly signals a pending reward.");
                Check(button.interactable && button.targetGraphic != null && button.targetGraphic.raycastTarget
                    && row.GetComponentsInChildren<Button>(true).Length == 1,
                    "Completed medal should retain one full-row detail action.");
                return "PASS: actual Journal row shows current5/10, claimable250/250 at310, earliest unclaimed reward, and all-claimed states; ordered claims retained; the original artwork keeps its mesh aspect and completed medals retain a detail action. Cache/UI restored; no RPC/claim/PlayerPrefs writes.";
            }
            finally
            {
                stats.SetValue(null, oldStats); claims.SetValue(null, oldClaims);
                if (group != null) { group.interactable = interactive; group.blocksRaycasts = blocks; }
                AchievementLifecycle.NotifyDataChanged();
                Canvas.ForceUpdateCanvases();
                if (journal != null) journal.verticalNormalizedPosition = scrollPosition;
            }
        }

        private static void CheckDrawnAspect(Image image)
        {
            Check(image.enabled && image.sprite != null && image.type == Image.Type.Simple && image.preserveAspect,
                "Image must use original-aspect Simple rendering.");
            // Unity 6 returns its current renderer mesh; read it without changing or destroying UI-owned data.
            var mesh = image.canvasRenderer.GetMesh();
            Check(mesh != null, "Actual image renderer mesh is missing.");
            var dimensions = mesh.bounds.size;
            Check(dimensions.x > 0 && dimensions.y > 0, "Actual image renderer mesh is empty.");
            var expected = image.sprite.rect.width / image.sprite.rect.height;
            Check(Mathf.Abs(dimensions.x / dimensions.y - expected) <= expected * .025f,
                "Actual rendered image mesh squashes the source artwork.");
        }

        private static T Read<T>(object obj, string field) => (T)obj.GetType().GetField(field, Private).GetValue(obj);
        private static void RequirePlay() => Check(Application.isPlaying, "Play Mode required; helper does not start Unity or Play.");
        private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
