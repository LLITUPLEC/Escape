using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Project.Match3;
using Project.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Editor
{
    /// <summary>Local clones of the real state prefabs, real view methods, fixture values; no match or account calls.</summary>
    public static class ExpeditionMatchStatesQa
    {
        private static Match3SearchingPanel _search;
        private static Match3GameOverPanel _result;
        private static string _state;
        private static int _frame, _cancelCalls, _backCalls;
        private static bool _pending;
        private static readonly List<string> Checks = new List<string>();

        [Serializable]
        private sealed class Report
        {
            public string state, fixture, error, screenshot;
            public int frame, cancelCallbacks, backCallbacks;
            public bool completed, passed, rpcOrAccountActions;
            public string[] checks;
        }

        public static string Run(string state)
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Play mode required");
            if (state == "close")
            {
                EditorApplication.update -= Validate;
                _pending = false;
                if (_search != null) UnityEngine.Object.Destroy(_search.gameObject);
                if (_result != null) UnityEngine.Object.Destroy(_result.gameObject);
                _search = null; _result = null;
                return "Closed only local state fixtures; actual match panels and account state unchanged";
            }
            if (_pending) throw new InvalidOperationException("Wait for the previous fixture report");
            Checks.Clear(); _state = state;
            if (_search != null) _search.Hide();
            if (_result != null) _result.Hide();
            if (state == "search" || state == "search-error")
            {
                if (_search == null)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<Match3SearchingPanel>("Assets/_Project/Prefabs/Match3/Match3SearchingPanel.prefab");
                    if (prefab == null) throw new InvalidOperationException("Actual search prefab unavailable");
                    _search = UnityEngine.Object.Instantiate(prefab);
                    _search.name = "ExpeditionMatchStatesQa_Search";
                    _search.OnCancelClicked += () => _cancelCalls++;
                }
                _search.UseExpeditionStyle("Классика");
                _search.UseExpeditionStyle();
                _search.Show(state == "search" ? "Поиск соперника…" : "Не удалось подключиться. Проверьте соединение и повторите попытку.");
            }
            else
            {
                if (state != "victory" && state != "defeat" && state != "draw" && state != "refund" && state != "many-rewards")
                    throw new ArgumentException("Use search, search-error, victory, defeat, draw, refund, many-rewards or close");
                if (_result == null)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<Match3GameOverPanel>("Assets/_Project/Prefabs/Match3/Match3GameOverPanel.prefab");
                    if (prefab == null) throw new InvalidOperationException("Actual result prefab unavailable");
                    _result = UnityEngine.Object.Instantiate(prefab);
                    _result.name = "ExpeditionMatchStatesQa_Result";
                    _result.OnBackClicked += () => _backCalls++;
                }
                _result.UseExpeditionStyle(); _result.UseExpeditionStyle();
                var refund = state == "refund";
                _result.Show(state == "victory" || state == "many-rewards",
                    refund ? "Бой с ботом · взнос возвращён\nМатерия +2" : null,
                    state == "draw" ? "Ничья!" : null);
                BuildRows(state);
            }
            _frame = Time.frameCount; _pending = true;
            EditorApplication.update -= Validate;
            EditorApplication.update += Validate;
            Write("running", null, null);
            return "Local production-renderer fixture started; poll .utmp/expedition/match-states-qa.json. No matchmaking/RPC/account actions.";
        }

        private static void BuildRows(string state)
        {
            var parent = _result.rewardText.transform.parent;
            var old = parent.Find("RewardRows");
            if (old != null) { old.gameObject.SetActive(false); UnityEngine.Object.Destroy(old.gameObject); }
            var rows = ExpeditionUi.Rect(parent, "RewardRows", 0, 0, 1, 1);
            rows.gameObject.AddComponent<VerticalLayoutGroup>();
            rows.gameObject.AddComponent<ContentSizeFitter>();
            var fields = state == "refund" ? new[] { "rewardMatterSprite" } :
                state == "defeat" ? new[] { "rewardExpSprite" } :
                state == "draw" ? new[] { "rewardExpSprite", "rewardMatterSprite" } :
                new[] { "rewardGoldSprite", "rewardOreSprite", "rewardExpSprite" };
            var values = state == "refund" ? new[] { "+2" } : state == "defeat" ? new[] { "+10" } :
                state == "draw" ? new[] { "+200", "+2" } : new[] { "+240", "+85", "+120" };
            var count = state == "many-rewards" ? 12 : fields.Length;
            RectTransform line = null;
            for (var i = 0; i < count; i++)
            {
                if (i % _result.RewardCellsPerLine == 0)
                {
                    line = ExpeditionUi.Rect(rows, "RewardLine", 0, 0, 1, 1);
                    line.gameObject.AddComponent<HorizontalLayoutGroup>(); line.gameObject.AddComponent<ContentSizeFitter>();
                }
                var cell = ExpeditionUi.Rect(line, "RewardRow", 0, 0, 1, 1);
                cell.gameObject.AddComponent<HorizontalLayoutGroup>(); cell.gameObject.AddComponent<ContentSizeFitter>();
                var icon = ExpeditionUi.Fill(cell, "Icon", 0, 0, 1, 1, Color.white);
                icon.sprite = RewardSprite(fields[i % fields.Length]);
                var value = ExpeditionUi.Text(cell, "Value", state == "many-rewards" && i == 11 ? "Рецепт оружия ×1" : values[i % values.Length],
                    _result.RewardValueFontSize, 0, 0, 1, 1, TextAlignmentOptions.Center);
                icon.gameObject.AddComponent<LayoutElement>(); value.gameObject.AddComponent<LayoutElement>();
            }
            _result.rewardText.gameObject.SetActive(false); // Actual manager hides this fallback when icons are available.
            _result.RequestRewardRefresh();
        }

        private static Sprite RewardSprite(string field)
        {
            var manager = UnityEngine.Object.FindFirstObjectByType<DuelMatch3Manager>();
            var sprite = manager != null ? typeof(DuelMatch3Manager).GetField(field,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(manager) as Sprite : null;
            if (sprite == null) throw new InvalidOperationException("Existing manager reward sprite not assigned: " + field);
            return sprite;
        }

        private static void Validate()
        {
            if (!_pending || Time.frameCount - _frame < 3) return;
            EditorApplication.update -= Validate; _pending = false;
            try
            {
                if (!EditorApplication.isPlaying) throw new InvalidOperationException("Play stopped during fixture");
                Canvas.ForceUpdateCanvases();
                var panel = _state.StartsWith("search", StringComparison.Ordinal) ? (Component)_search : _result;
                var canvas = panel.GetComponent<Canvas>();
                Require(canvas != null && canvas.isRootCanvas && canvas.renderMode == RenderMode.ScreenSpaceOverlay,
                    "state uses a real root overlay canvas above battle and shell");
                var safe = panel.transform.Find("ExpeditionMatchStateSafeArea");
                Require(safe != null && safe.GetComponent<ExpeditionSafeArea>() != null, "content has safe-area container");
                Require(panel.transform.Find("ExpeditionMatchStateBackdrop")?.GetComponent<Image>()?.sprite != null,
                    "actual full-screen environment sprite present");
                if (_state.StartsWith("search", StringComparison.Ordinal))
                {
                    var expected = _state == "search" ? "Поиск соперника…" : "Не удалось подключиться. Проверьте соединение и повторите попытку.";
                    Require(_search.statusText.text == expected, "server-provided status is not replaced by a fabricated found-player state");
                    Require(safe.Find("UnknownOpponentPortrait") != null, "opponent remains explicitly unknown");
                    var mode = safe.Find("SearchHeadingFrame/ActualSearchMode")?.GetComponent<TMP_Text>();
                    Require(mode != null && mode.text == "Режим: Классика", "explicit real mode label survives repeated styling");
                    Require(_search.cancelButton.onClick.GetPersistentEventCount() == 0, "fixture cancel has no external persistent callbacks");
                    var before = _cancelCalls; _search.cancelButton.onClick.Invoke();
                    Require(_cancelCalls == before + 1, "repeated theme application retains exactly one cancel callback");
                    InsideSafe(_search.cancelButton.transform as RectTransform);
                    _search.statusText.ForceMeshUpdate();
                    Require(!_search.statusText.isTextOverflowing, "search status fits its readable area");
                }
                else
                {
                    var expected = _state == "draw" ? "Ничья!" : _state == "victory" || _state == "many-rewards" ? "ПОБЕДА" : "ПОРАЖЕНИЕ";
                    Require(_result.titleText.text == expected, "victory/defeat/draw heading follows actual Show parameters");
                    _result.titleText.ForceMeshUpdate();
                    Require(!_result.titleText.isTextOverflowing, "outcome heading fits its frame");
                    var notice = safe.Find("ActualOutcomeNotice");
                    Require(notice != null && notice.gameObject.activeSelf == (_state == "refund"), "refund notice follows real context only");
                    if (_state == "refund") Require(notice.GetComponentInChildren<TMP_Text>().text.Contains("взнос возвращён"),
                        "actual refund message survives hidden rewardText fallback");
                    var rows = ExpeditionUi.Find(_result.transform, "RewardRows") as RectTransform;
                    var scroll = rows != null ? rows.GetComponentInParent<ScrollRect>() : null;
                    Require(scroll != null, "actual reward rows are in a scrollable viewport");
                    foreach (var text in rows.GetComponentsInChildren<TMP_Text>())
                    {
                        text.ForceMeshUpdate();
                        Require(!text.isTextOverflowing, "true reward value fits its padded cell: " + text.text);
                    }
                    if (_state == "many-rewards")
                    {
                        Require(scroll.vertical && rows.rect.height > scroll.viewport.rect.height, "twelve rewards create real vertical overflow");
                        scroll.verticalNormalizedPosition = 0;
                        Canvas.ForceUpdateCanvases();
                        Require(scroll.verticalNormalizedPosition < .001f, "actual reward scroll reaches last line");
                    }
                    Require(_result.backButton.onClick.GetPersistentEventCount() == 0, "fixture continue has no external persistent callbacks");
                    var before = _backCalls; _result.backButton.onClick.Invoke();
                    Require(_backCalls == before + 1, "repeated theme application retains exactly one continue callback");
                    InsideSafe(_result.backButton.transform as RectTransform);
                }
                var screenshot = Path.GetFullPath(Path.Combine(Application.dataPath, "../.utmp/expedition/screenshots/match-state-" + _state + ".png"));
                Directory.CreateDirectory(Path.GetDirectoryName(screenshot));
                ScreenCapture.CaptureScreenshot(screenshot);
                Write("passed", null, screenshot);
            }
            catch (Exception error) { Write("failed", error.ToString(), null); }
        }

        private static void InsideSafe(RectTransform rect)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            var safe = Screen.safeArea;
            Require(corners[0].x >= safe.xMin - 2 && corners[0].y >= safe.yMin - 2 &&
                corners[2].x <= safe.xMax + 2 && corners[2].y <= safe.yMax + 2, "primary action remains inside physical safe area");
        }
        private static void Require(bool value, string text) { if (!value) throw new InvalidOperationException(text); Checks.Add(text); }
        private static void Write(string status, string error, string screenshot)
        {
            var report = new Report { state = status, fixture = _state, completed = status != "running", passed = status == "passed",
                error = error, screenshot = screenshot, frame = Time.frameCount, cancelCallbacks = _cancelCalls,
                backCallbacks = _backCalls, rpcOrAccountActions = false, checks = Checks.ToArray() };
            var path = Path.GetFullPath(Path.Combine(Application.dataPath, "../.utmp/expedition/match-states-qa.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
        }
    }
}
