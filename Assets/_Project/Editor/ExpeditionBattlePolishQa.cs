using System;
using System.Linq;
using System.Reflection;
using Project.Match3;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Editor
{
    /// <summary>Pre-match production UI fixture. Never creates/joins a match, spends or changes account data.</summary>
    public static class ExpeditionBattlePolishQa
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static DuelMatch3Manager _manager;
        private static bool _enabled;
        private static string _mode;
        private static int _bank;
        private static LabelTransform _oldBank;

        public static string Run(string mode)
        {
            if (mode == "restore")
            {
                if (_manager == null) return "No battle fixture retained";
                ExpeditionBattleTopQa.Run("restore");
                var ability = UnityEngine.Object.FindFirstObjectByType<Match3AbilityPanel>();
                var bank = ability.petardButton.transform.Find("RacePetardBank");
                if (_oldBank != null) _oldBank.Restore();
                else if (bank != null) UnityEngine.Object.DestroyImmediate(bank.gameObject);
                _manager.enabled = _enabled;
                typeof(DuelMatch3Manager).GetMethod("RefreshStatsUI", Private).Invoke(_manager, null);
                typeof(DuelMatch3Manager).GetMethod("RefreshAbilityUi", Private).Invoke(_manager, new object[] { false, false });
                var board = UnityEngine.Object.FindFirstObjectByType<Match3BoardView>();
                board.RefreshAll((Match3BoardLogic)typeof(DuelMatch3Manager).GetField("_board", Private).GetValue(_manager));
                _manager = null; _oldBank = null;
                return "UI-only battle fixture restored; no RPC, match, ability use or profile mutation";
            }
            var manager = UnityEngine.Object.FindFirstObjectByType<DuelMatch3Manager>();
            Check(Application.isPlaying && manager != null, "Open pre-match Solo lobby");
            Check(typeof(DuelMatch3Manager).GetField("_match", Private).GetValue(manager) == null &&
                !(bool)typeof(DuelMatch3Manager).GetField("_hasInitialBoardSync", Private).GetValue(manager), "Never fixture a live match");
            var abilities = UnityEngine.Object.FindFirstObjectByType<Match3AbilityPanel>();
            var hud = UnityEngine.Object.FindFirstObjectByType<Match3GameHUD>();
            if (mode == "verify") return Verify(abilities, hud);
            if (_manager == null)
            {
                _manager = manager; _enabled = manager.enabled; manager.enabled = false;
                _oldBank = abilities.petardCooldownText != null ? new LabelTransform(abilities.petardCooldownText) : null;
            }
            _mode = mode;
            var race = mode.StartsWith("race", StringComparison.Ordinal);
            _bank = race ? int.Parse(mode.Substring(4)) : -1;
            ExpeditionBattleTopQa.Run(race ? "race" : "popover-open");
            var panels = UnityEngine.Object.FindObjectsByType<Match3PlayerPanel>(FindObjectsSortMode.None).OrderBy(p => p.transform.position.x).ToArray();
            foreach (var panel in panels)
            {
                panel.SetRaceHudMode(race);
                panel.UpdateStats(820, 1000, race ? 170 : 80, race ? 240 : 100);
                panel.UpdateCombatStats(42, 18, 7, 12.5f);
                panel.UpdateBuffState(race ? 0 : 1, 3);
            }
            hud.SetTurn("Ваш ход!"); hud.SetTimerPhase(false); hud.SetTimer("18", 18);
            abilities.Refresh(new PlayerStats { mana = 170, crossCooldown = 2, furyCooldown = 3 }, true, false, 20, 20, 30, 40, 30, race, _bank);
            var view = UnityEngine.Object.FindFirstObjectByType<Match3BoardView>();
            var local = new Match3BoardLogic(); local.Init(3400);
            view.RefreshAll(local);
            Canvas.ForceUpdateCanvases();
            return Verify(abilities, hud) + "; retained for capture; call restore";
        }

        private static string Verify(Match3AbilityPanel abilities, Match3GameHUD hud)
        {
            Canvas.ForceUpdateCanvases();
            var frame = UnityEngine.Object.FindFirstObjectByType<Match3BoardView>().transform.Find("Frame") as RectTransform;
            var timer = WorldRect(hud.timerText.rectTransform);
            var turn = WorldRect(hud.turnText.rectTransform);
            var board = WorldRect(frame);
            Check(hud.timerText.fontSize >= 59 && hud.timerText.alignment == TextAlignmentOptions.Center, "Large centered countdown missing");
            Check(timer.yMin >= board.yMax && timer.yMin - board.yMax < Screen.height * .04f, "Countdown is not directly above actual square board");
            hud.timerText.ForceMeshUpdate(); hud.turnText.ForceMeshUpdate();
            var timerGlyphTop = hud.timerText.rectTransform.TransformPoint(hud.timerText.textBounds.max).y;
            var turnGlyphBottom = hud.turnText.rectTransform.TransformPoint(hud.turnText.textBounds.min).y;
            Check(turnGlyphBottom > timerGlyphTop && Mathf.Abs(timer.center.x - board.center.x) < 2, "Whose-turn overlaps or is not above countdown");
            Check(hud.affixButton == null || WorldRect(hud.affixButton.transform as RectTransform).xMin > timer.xMax, "Affix overlaps countdown");
            var cooldown = abilities.crossButton.transform.Find("CooldownCenterText").GetComponent<TMP_Text>();
            Check(cooldown.gameObject.activeInHierarchy && cooldown.text == "2", "Cooldown must contain only2");
            var bank = abilities.petardButton.transform.Find("RacePetardBank")?.GetComponent<TMP_Text>();
            if (_bank >= 0)
            {
                Check(bank != null && bank.gameObject.activeInHierarchy && bank.text == "−" + _bank, "Actual Race bank text missing");
                Check(bank.rectTransform.parent == abilities.petardButton.transform && bank.transform.GetSiblingIndex() == bank.transform.parent.childCount - 1, "Bank hidden behind icon/cooldown or has legacy parent");
                var cost = abilities.petardButton.transform.Find("AbilityCost").GetComponent<TMP_Text>();
                Check(!WorldRect(bank.rectTransform).Overlaps(WorldRect(cost.rectTransform)), "Bank overlaps dynamic mana cost");
                Check(bank.fontSizeMin >= 22 && bank.color.a > .99f, "Bank is unreadably small or transparent");
            }
            else
            {
                foreach (var panel in UnityEngine.Object.FindObjectsByType<Match3PlayerPanel>(FindObjectsSortMode.None))
                {
                    var badge = panel.transform.Find("ExpeditionShieldBadge") as RectTransform;
                    var popover = panel.transform.Find("ExpeditionCombatStatsPopover") as RectTransform;
                    Check(badge.gameObject.activeInHierarchy && panel.buffStateText.fontSizeMin >= 18, "Visible larger shield status missing");
                    Check(popover == null || !popover.gameObject.activeInHierarchy || !WorldRect(badge).Overlaps(WorldRect(popover)), "Combat stats cover shield status");
                }
            }
            var source = new SerializedObject(_manager);
            Check(AssetDatabase.GetAssetPath(source.FindProperty("sfxAbilityFury").objectReferenceValue) == "Assets/_Project/Audio/SFX/Bloodlust_cast.ogg", "Wrong Fury clip");
            Check(AssetDatabase.GetAssetPath(source.FindProperty("sfxAbilityShield").objectReferenceValue) == "Assets/_Project/Audio/SFX/Heroism_Cast.ogg", "Wrong Shield clip");
            return "PASS " + _mode + ": actual timer60/turn-above/board gap, shield-popover separation, cooldown2, Race bank" + _bank + " and authored OGG references";
        }

        private static Rect WorldRect(RectTransform rect)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners.Min(p => p.x), corners.Min(p => p.y), corners.Max(p => p.x), corners.Max(p => p.y));
        }
        private static void Check(bool test, string message) { if (!test) throw new InvalidOperationException(message); }

        private sealed class LabelTransform
        {
            private readonly TMP_Text _label;
            private readonly Transform _parent;
            private readonly string _name;
            private readonly Vector2 _min, _max, _pivot, _size, _position;
            private readonly int _index;
            public LabelTransform(TMP_Text label)
            {
                _label = label; _parent = label.transform.parent; _name = label.name; _index = label.transform.GetSiblingIndex();
                var r = label.rectTransform; _min = r.anchorMin; _max = r.anchorMax; _pivot = r.pivot; _size = r.sizeDelta; _position = r.anchoredPosition;
            }
            public void Restore()
            {
                _label.transform.SetParent(_parent, false); _label.name = _name; _label.transform.SetSiblingIndex(_index);
                var r = _label.rectTransform; r.anchorMin = _min; r.anchorMax = _max; r.pivot = _pivot; r.sizeDelta = _size; r.anchoredPosition = _position;
            }
        }
    }
}
