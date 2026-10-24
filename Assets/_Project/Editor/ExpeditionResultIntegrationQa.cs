using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Project.Match3;
using TMPro;
using UnityEngine;

namespace Project.Editor
{
    /// <summary>Exercise real manager reward rows and real result panel together, without ShowGameOver's gameplay side effects.</summary>
    public static class ExpeditionResultIntegrationQa
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static DuelMatch3Manager _manager;
        private static Match3GameOverPanel _panel;
        private static GameObject _selector;
        private static bool _enabled, _visible, _selectorActive, _refund;
        private static readonly Dictionary<FieldInfo, object> Saved = new Dictionary<FieldInfo, object>();

        public static string Run(string target)
        {
            if (target == "restore")
            {
                if (_manager == null) return "No result integration fixture retained";
                Call("ClearRewardRows");
                foreach (var pair in Saved) pair.Key.SetValue(_manager, pair.Value);
                Saved.Clear();
                _panel.gameObject.SetActive(_visible);
                if (_selector != null) _selector.SetActive(_selectorActive);
                _manager.enabled = _enabled; _manager = null; _panel = null;
                return "Real result panel/manager reward fields restored; no ShowGameOver, achievement, RPC or profile actions";
            }
            if (target == "verify")
            {
                Check(_manager != null, "Show an integration fixture first");
                var rows = (RectTransform)Read("_gameOverRewardRowsRoot");
                var viewport = (RectTransform)typeof(Match3GameOverPanel).GetField("_rewardViewport", Private).GetValue(_panel);
                Check(rows != null && rows.gameObject.activeInHierarchy && rows.IsChildOf(viewport), "Actual manager rows are outside result viewport");
                var values = rows.GetComponentsInChildren<TMP_Text>().Where(t => t.name == "Value").Select(t => t.text).OrderBy(s => s).ToArray();
                var expected = (_refund ? new[] { "+2" } : new[] { "+120", "+240", "+85" }).OrderBy(s => s).ToArray();
                Check(values.SequenceEqual(expected), "Actual manager reward values differ: " + string.Join(",", values));
                Check(!_panel.rewardText.gameObject.activeSelf, "Manager did not hide its fallback text");
                if (_refund)
                    Check(_panel.GetComponentsInChildren<TMP_Text>().Any(t => t.text.Contains("взнос возвращён")), "Refund notice disappeared after manager hid fallback text");
                return "PASS actual manager RefreshGameOverRewardRows -> production result viewport, original reward sprites/values " + string.Join(",", values) + ", refund=" + _refund + "; no gameplay/RPC invocation";
            }
            if (_manager != null) Run("restore");
            _manager = UnityEngine.Object.FindFirstObjectByType<DuelMatch3Manager>();
            Check(Application.isPlaying && _manager != null && Read("_match") == null && !(bool)Read("_hasInitialBoardSync"), "Use pre-match Solo lobby only");
            _enabled = _manager.enabled; _manager.enabled = false;
            _panel = (Match3GameOverPanel)Read("_gameOverPanel"); _visible = _panel.gameObject.activeSelf;
            _selector = (GameObject)Read("_pveSelectorRoot"); _selectorActive = _selector != null && _selector.activeSelf;
            if (_selector != null) _selector.SetActive(false);
            foreach (var field in typeof(DuelMatch3Manager).GetFields(Private).Where(f => f.Name.StartsWith("_lastReward", StringComparison.Ordinal))) Saved[field] = field.GetValue(_manager);
            Call("ClearLastPveRewards");
            _refund = target == "refund";
            Write("_lastRewardXp", _refund ? 0 : 120); Write("_lastRewardGold", _refund ? 0 : 240); Write("_lastRewardOre", _refund ? 0 : 85); Write("_lastRewardMatter", _refund ? 2 : 0);
            var text = _refund ? "Бой с ботом · взнос возвращён\n+2 материи" : "+120 опыта\n+240 золота\n+85 руды";
            Write("_lastRewardText", text);
            _panel.Show(!_refund, text, null);
            Call("RefreshGameOverRewardRows", !_refund);
            _panel.RequestRewardRefresh();
            return "Actual manager/result fixture shown; verify after one rendered frame, then restore. Never calls gameplay ShowGameOver/RPC.";
        }
        private static object Read(string name) => typeof(DuelMatch3Manager).GetField(name, Private).GetValue(_manager);
        private static void Write(string name, object value) => typeof(DuelMatch3Manager).GetField(name, Private).SetValue(_manager, value);
        private static void Call(string name, params object[] args) => typeof(DuelMatch3Manager).GetMethod(name, Private).Invoke(_manager, args);
        private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
