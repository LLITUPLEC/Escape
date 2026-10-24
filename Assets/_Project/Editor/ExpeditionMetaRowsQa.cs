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
            var row = Read<List<AchievementChainRowView>>(panel, "_spawnedRows").FirstOrDefault(r => r != null
                && r.Slots != null && r.Slots.Length > 0 && r.Slots[0].name.EndsWith("_obs.cross", StringComparison.Ordinal));
            Check(row != null, "Actual obs.cross row missing; select Obsession.");
            var card = row.GetComponent<ExpeditionAchievementCard>();
            Check(card != null, "Production card missing.");
            AchievementProgressStorage.EnsureLoaded();
            var stats = typeof(AchievementProgressStorage).GetField("_stats", StaticPrivate);
            var claims = typeof(AchievementProgressStorage).GetField("_claimedStepTokens", StaticPrivate);
            var oldStats = stats.GetValue(null); var oldClaims = claims.GetValue(null);
            var counters = new Dictionary<string, int>(StringComparer.Ordinal) { [chain.StatKey] = 5 };
            var claimed = new HashSet<string>(StringComparer.Ordinal);
            var group = Read<CanvasGroup>(panel, "rootCanvasGroup");
            var interactive = group != null && group.interactable;
            var blocks = group != null && group.blocksRaycasts;
            try
            {
                if (group != null) { group.interactable = false; group.blocksRaycasts = false; }
                stats.SetValue(null, counters); claims.SetValue(null, claimed);
                AchievementLifecycle.NotifyDataChanged(); Canvas.ForceUpdateCanvases();
                Check(card.CurrentStep == 0 && card.ProgressLabel.text.Replace(" ", "") == "5/10"
                    && card.StateLabels[0].text == "Текущая" && !card.ClaimButton.gameObject.activeSelf,
                    "In-progress current step/claim state changed.");
                CheckStepChain(row, card);
                counters[chain.StatKey] = 310;
                claimed.Add(chain.ChainId + ":0"); claimed.Add(chain.ChainId + ":1");
                AchievementLifecycle.NotifyDataChanged(); Canvas.ForceUpdateCanvases();
                Check(card.CurrentStep == 2 && card.ProgressLabel.text.Replace(" ", "") == "250/250", "Delta progress or ordered current step changed.");
                for (var i = 0; i < 2; i++)
                    Check(card.StateLabels[i].text == "Получено" && card.StateLabels[i].color.g > card.StateLabels[i].color.r
                        && card.ClaimedChecks[i].activeInHierarchy, "Claimed step lacks visible green tick/Получено.");
                Check(card.StateLabels[2].text == "Получить" && card.ClaimButton.gameObject.activeInHierarchy && card.ClaimButton.interactable,
                    "Eligible unclaimed reward lacks clear claim action.");
                var catalog = Read<AchievementIconCatalog>(panel, "iconCatalog");
                var art = card.transform.Find("ExpeditionChainIllustration").GetComponent<Image>();
                Check(catalog != null && art.sprite == catalog.GetDisplayIcon(chain.ChainId), "Original achievement artwork was replaced.");
                CheckDrawnAspect(art);
                CheckStepChain(row, card);
                claimed.Clear(); AchievementLifecycle.NotifyDataChanged();
                Check(!AchievementRewardClaim.CanClaimStep(chain, 2) && card.StateLabels[2].text == "Выполнено",
                    "Completed later step incorrectly bypasses ordered claims.");
                for (var i = 0; i < chain.Thresholds.Length; i++) claimed.Add(chain.ChainId + ":" + i);
                counters[chain.StatKey] = AchievementUiRules.CumulativeRequired(chain, chain.Thresholds.Length - 1);
                AchievementLifecycle.NotifyDataChanged();
                Check(!card.ClaimButton.gameObject.activeSelf && card.StateLabels.All(label => label.text == "Получено"),
                    "All-claimed chain retains a claim action or loses its labels.");
                Canvas.ForceUpdateCanvases(); CheckStepChain(row, card);
                return "PASS: actual catalog current5/10, claimed+eligible250/250 at310, and all-claimed states; green ticks/Получено and explicit claim action; ordered claims retained; main artwork keeps original mesh aspect; mini-icons remain absent after repeated binds, three clear arrow connectors stay between four target/state cells. Cache/UI restored; no RPC/claim/PlayerPrefs writes.";
            }
            finally
            {
                stats.SetValue(null, oldStats); claims.SetValue(null, oldClaims);
                if (group != null) { group.interactable = interactive; group.blocksRaycasts = blocks; }
                AchievementLifecycle.NotifyDataChanged();
            }
        }

        private static void CheckStepChain(AchievementChainRowView row, ExpeditionAchievementCard card)
        {
            var count = row.Slots.Length;
            Check(card.StepConnectors.Length == count - 1, "Expected one directional connector between every adjacent step.");
            for (var i = 0; i < count; i++)
            {
                var icon = row.Slots[i].transform.Find("Icon")?.GetComponent<Image>();
                Check(icon == null || (!icon.enabled && !icon.gameObject.activeInHierarchy), "A bind resurrected mini achievement artwork.");
                Check(!string.IsNullOrWhiteSpace(card.TargetLabels[i].text) && !string.IsNullOrWhiteSpace(card.StateLabels[i].text),
                    "Step target/state missing.");
                if (i >= count - 1) continue;
                var connector = card.StepConnectors[i];
                Check(connector.gameObject.activeInHierarchy && !connector.raycastTarget, "Step connector absent or intercepts taps.");
                var shaft = connector.transform.Find("Shaft").GetComponent<Image>();
                var mesh = shaft.canvasRenderer.GetMesh();
                Check(shaft.enabled && !shaft.raycastTarget && mesh != null && mesh.bounds.size.x > 0,
                    "Directional connector has no actual rendered shaft.");
                var left = new Vector3[4]; var right = new Vector3[4];
                (row.Slots[i].transform as RectTransform).GetWorldCorners(left);
                (row.Slots[i + 1].transform as RectTransform).GetWorldCorners(right);
                Check(connector.transform.position.x > left[3].x && connector.transform.position.x < right[0].x,
                    "Arrow is not inside the gap between adjacent steps.");
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
