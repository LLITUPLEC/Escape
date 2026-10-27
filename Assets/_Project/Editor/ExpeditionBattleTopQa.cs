using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Project.Match3;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Project.Editor
{
    /// <summary>Pre-match, UI-only fixtures using the production battle views. Never calls match/RPC methods.</summary>
    public static class ExpeditionBattleTopQa
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static Snapshot _displaySnapshot;
        private static readonly List<Check> Checks = new List<Check>();

        public static string VerifyHeaderRatiosAndBounds() => Run("verify");

        public static string Run(string target)
        {
            try
            {
                var result = RunCore(target);
                Record(target, true, result);
                return result;
            }
            catch (Exception error)
            {
                if (target == "race" || target == "popover-open") RestoreDisplay();
                Record(target, false, error.ToString());
                throw;
            }
        }

        private static string RunCore(string target)
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Play mode required.");
            if (target == "restore") { RestoreDisplay(); return "Original battle presentation restored; no match or account data changed."; }
            var manager = UnityEngine.Object.FindFirstObjectByType<DuelMatch3Manager>();
            if (manager == null || Read<bool>(manager, "_hasInitialBoardSync") || Read<object>(manager, "_match") != null)
                throw new InvalidOperationException("Use the UI-only fixture before starting Solo/a match.");
            var panels = UnityEngine.Object.FindObjectsByType<Match3PlayerPanel>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(p => p.gameObject.scene == manager.gameObject.scene).OrderBy(p => p.transform.position.x).ToArray();
            var hud = UnityEngine.Object.FindFirstObjectByType<Match3GameHUD>();
            if (panels.Length != 2 || hud == null) throw new InvalidOperationException("Two battle headers and a HUD are required.");
            switch (target)
            {
                case "race":
                    _displaySnapshot ??= new Snapshot(manager, panels, hud);
                    _displaySnapshot.HideSelector();
                    foreach (var panel in panels)
                    {
                        panel.SetRaceHudMode(true);
                        panel.SetRaceGoalMana(200);
                        panel.UpdateStats(100, 100, 60, 240);
                    }
                    hud.SetRaceGoalBanner(true, "Цель: 200 маны");
                    hud.SetRaceLastTurnBanner(true, "Последний ход! Соперник достиг цели.");
                    hud.SetRaceManaBonusBanner(true, "+3 к мане с камней");
                    hud.SetTurn("Ваш ход!");
                    hud.SetTimerPhase(false);
                    hud.SetTimer("18", 18);
                    Canvas.ForceUpdateCanvases();
                    foreach (var panel in panels)
                    {
                        Require(!Find(panel, "HpBarTrack").gameObject.activeSelf, "Race HP bar must be hidden.");
                        Require(panel.manaText.text == "60/240", "Race MP must be a numeric ratio.");
                        VerifyFill(panel.manaFill, .25f);
                        var track = panel.manaFill.transform.parent as RectTransform;
                        var marker = track.Find("RaceGoalMarker") as RectTransform;
                        Require(marker != null && marker.gameObject.activeInHierarchy, "Race goal marker missing.");
                        var markerPoint = track.InverseTransformPoint(marker.TransformPoint(marker.rect.center));
                        var expectedX = track.rect.xMin + 7 + (track.rect.width - 14) * (200f / 240f);
                        Require(Mathf.Abs(markerPoint.x - expectedX) < .25f, "Race marker does not follow the inset track.");
                        Require(!panel.GetComponent<ExpeditionCombatStatsPopover>().Expanded, "Combat stats should stay hidden in Race.");
                    }
                    Require(hud.raceGoalText.gameObject.activeInHierarchy && hud.raceLastTurnText.gameObject.activeInHierarchy &&
                        hud.raceManaBonusText.gameObject.activeInHierarchy, "Actual race banners must be visible.");
                    return "Race display retained: actual SetRaceHudMode, MP 60/240, goal 200 at 83.33%, last-turn and +3 bonus; call restore after capture.";
                case "verify":
                case "bars":
                    return VerifyHeaderRatiosAndBounds(manager, panels, hud);
                case "popover":
                    using (var saved = new Snapshot(manager, panels, hud))
                    {
                        saved.HideSelector();
                        foreach (var panel in panels)
                        {
                            panel.SetRaceHudMode(false);
                            var popover = panel.GetComponent<ExpeditionCombatStatsPopover>();
                            var original = popover.Expanded;
                            TapHeader(panel);
                            Require(popover.Expanded != original, "Actual raycast tap did not toggle the combat stats.");
                            TapHeader(panel);
                            Require(popover.Expanded == original, "Second header tap did not restore the popover.");
                        }
                    }
                    return "Both headers: actual EventSystem raycast -> production IPointerClickHandler opens/closes stats; original presentation restored.";
                case "popover-open":
                    _displaySnapshot ??= new Snapshot(manager, panels, hud);
                    _displaySnapshot.HideSelector();
                    panels[0].SetRaceHudMode(false);
                    if (!panels[0].GetComponent<ExpeditionCombatStatsPopover>().Expanded) TapHeader(panels[0]);
                    Require(panels[0].GetComponent<ExpeditionCombatStatsPopover>().Expanded, "Left header popover did not open.");
                    return "Left header stats retained after an actual raycast tap; call restore after capture.";
                default: throw new ArgumentException("Unknown battle-top fixture. Use race, verify, popover, popover-open or restore.");
            }
        }

        private static string VerifyHeaderRatiosAndBounds(DuelMatch3Manager manager, Match3PlayerPanel[] panels, Match3GameHUD hud)
        {
            using (var saved = new Snapshot(manager, panels, hud))
            {
                foreach (var panel in panels)
                {
                    panel.SetRaceHudMode(false);
                    foreach (var amount in new[] { 0, 50, 100 })
                    {
                        panel.UpdateStats(amount, 100, amount, 100);
                        Canvas.ForceUpdateCanvases();
                        Require(panel.hpText.text == amount + "/100" && panel.manaText.text == amount + "/100", "HP/MP labels contain a prefix or stale value.");
                        VerifyFill(panel.hpFill, amount / 100f);
                        VerifyFill(panel.manaFill, amount / 100f);
                        VerifyValueInsideTrack(panel.hpText, panel.hpFill.transform.parent as RectTransform);
                        VerifyValueInsideTrack(panel.manaText, panel.manaFill.transform.parent as RectTransform);
                    }
                }
            }
            return "Both headers at 0%, 50%, 100%: numeric-only labels, visible width ratio*(trackWidth-14), 7/5 insets, glyph bounds inside tracks; original data/modes restored.";
        }

        private static void VerifyFill(Image fill, float ratio)
        {
            var track = fill.transform.parent as RectTransform;
            Require(track != null && track.rect.width > 14 && track.rect.height > 10, "Bar track has no usable size.");
            Require(Mathf.Abs(fill.rectTransform.rect.width - ratio * (track.rect.width - 14)) < .25f, "Visible bar ratio is wrong.");
            var corners = new Vector3[4];
            fill.rectTransform.GetWorldCorners(corners);
            foreach (var corner in corners)
            {
                var point = track.InverseTransformPoint(corner);
                Require(point.x >= track.rect.xMin + 6.75f && point.x <= track.rect.xMax - 6.75f &&
                    point.y >= track.rect.yMin + 4.75f && point.y <= track.rect.yMax - 4.75f, "Bar fill extends outside its 7/5 inset.");
            }
        }

        private static void VerifyValueInsideTrack(TMP_Text text, RectTransform track)
        {
            text.ForceMeshUpdate(true, true);
            var bounds = text.textBounds;
            foreach (var local in new[] { bounds.min, bounds.max })
            {
                var point = track.InverseTransformPoint(text.rectTransform.TransformPoint(local));
                Require(point.x >= track.rect.xMin && point.x <= track.rect.xMax && point.y >= track.rect.yMin && point.y <= track.rect.yMax,
                    "Numeric text glyphs extend outside the bar frame.");
            }
        }

        private static void TapHeader(Match3PlayerPanel panel)
        {
            Require(EventSystem.current != null, "EventSystem missing.");
            Canvas.ForceUpdateCanvases();
            var target = panel.transform.Find("ExpeditionHeaderInfo") as RectTransform;
            Require(target != null, "Header info marker missing.");
            var canvas = panel.GetComponentInParent<Canvas>();
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            var point = RectTransformUtility.WorldToScreenPoint(camera, target.TransformPoint(target.rect.center));
            var pointer = new PointerEventData(EventSystem.current) { position = point, button = PointerEventData.InputButton.Left, pointerId = -1 };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            var handler = hits.Count > 0 ? ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject) : null;
            Require(handler == panel.gameObject, "Header is obscured: " + (hits.Count > 0 ? hits[0].gameObject.name : "no raycast hit"));
            pointer.pointerCurrentRaycast = hits[0];
            ExecuteEvents.Execute(handler, pointer, ExecuteEvents.pointerClickHandler);
            Canvas.ForceUpdateCanvases();
        }

        private static void RestoreDisplay()
        {
            var saved = _displaySnapshot;
            _displaySnapshot = null;
            saved?.Dispose();
        }

        private sealed class Snapshot : IDisposable
        {
            private readonly PanelState[] _panels;
            private readonly TextState[] _texts;
            private readonly ActiveState[] _active;
            private readonly Match3GameHUD _hud;
            private readonly bool _resolving;
            private readonly GameObject _selector;
            private readonly bool _selectorActive;
            private bool _restored;

            public Snapshot(DuelMatch3Manager manager, Match3PlayerPanel[] panels, Match3GameHUD hud)
            {
                _panels = panels.Select(p => new PanelState(p)).ToArray();
                _hud = hud;
                _resolving = Read<bool>(hud, "_moveResolving");
                var roots = panels.Select(p => p.transform).Concat(new[] { hud.transform, hud.transform.parent }).ToArray();
                var transforms = roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Distinct().ToArray();
                _texts = transforms.Select(t => t.GetComponent<TMP_Text>()).Where(t => t != null).Select(t => new TextState(t)).ToArray();
                _active = transforms.Select(t => new ActiveState(t.gameObject)).ToArray();
                _selector = Read<GameObject>(manager, "_pveSelectorRoot");
                _selectorActive = _selector != null && _selector.activeSelf;
            }

            public void HideSelector() { if (_selector != null) _selector.SetActive(false); }

            public void Dispose()
            {
                if (_restored) return;
                _restored = true;
                foreach (var panel in _panels) panel.Restore();
                if (_hud != null) _hud.SetTimerPhase(_resolving);
                foreach (var text in _texts) text.Restore();
                foreach (var active in _active) active.Restore();
                if (_selector != null) _selector.SetActive(_selectorActive);
                Canvas.ForceUpdateCanvases();
            }
        }

        private sealed class PanelState
        {
            private readonly Match3PlayerPanel _panel;
            private readonly bool _race, _expanded;
            private readonly int _goal, _max;
            private readonly Image _hp, _mana;
            private readonly TMP_Text _hpText, _manaText;
            private readonly float _hpFill, _manaFill;
            private readonly FillState[] _allFills;
            private readonly RectTransform _marker;
            private readonly Transform _raceTrack;

            public PanelState(Match3PlayerPanel panel)
            {
                _panel = panel;
                _race = Read<bool>(panel, "_raceHudMode");
                _goal = Read<int>(panel, "_raceGoalMana");
                _max = Read<int>(panel, "_lastDisplayedMaxMana");
                _marker = Read<RectTransform>(panel, "_raceGoalMarker");
                _raceTrack = Read<Transform>(panel, "_raceTrack");
                _hp = panel.hpFill; _mana = panel.manaFill;
                _hpText = panel.hpText; _manaText = panel.manaText;
                _hpFill = _hp.fillAmount; _manaFill = _mana.fillAmount;
                _allFills = panel.GetComponentsInChildren<Image>(true)
                    .Where(i => i.name == "HpBarFill" || i.name == "MpBarFill").Select(i => new FillState(i)).ToArray();
                var popover = panel.GetComponent<ExpeditionCombatStatsPopover>();
                _expanded = popover != null && Read<bool>(popover, "_expanded");
            }

            public void Restore()
            {
                if (_panel == null) return;
                _panel.SetRaceHudMode(_race);
                _panel.SetRaceGoalMana(_goal);
                _panel.hpFill = _hp; _panel.manaFill = _mana;
                _panel.hpText = _hpText; _panel.manaText = _manaText;
                _panel.UpdateStats(Mathf.RoundToInt(_hpFill * 10000), 10000, Mathf.RoundToInt(_manaFill * 10000), 10000);
                Write(_panel, "_lastDisplayedMaxMana", _max);
                _panel.SetRaceGoalMana(_goal);
                Write(_panel, "_raceTrack", _raceTrack);
                foreach (var fill in _allFills) fill.Restore();
                var createdMarker = Read<RectTransform>(_panel, "_raceGoalMarker");
                if (_marker == null && createdMarker != null)
                {
                    createdMarker.gameObject.SetActive(false);
                    UnityEngine.Object.Destroy(createdMarker.gameObject);
                    Write(_panel, "_raceGoalMarker", null);
                }
                var popover = _panel.GetComponent<ExpeditionCombatStatsPopover>();
                if (popover != null) Write(popover, "_expanded", _expanded);
            }
        }

        private sealed class FillState
        {
            private readonly Image _image;
            private readonly float _amount;
            private readonly Image.Type _type;
            private readonly Vector2 _anchorMin, _anchorMax, _offsetMin, _offsetMax;
            public FillState(Image image)
            {
                _image = image; _amount = image.fillAmount; _type = image.type;
                _anchorMin = image.rectTransform.anchorMin; _anchorMax = image.rectTransform.anchorMax;
                _offsetMin = image.rectTransform.offsetMin; _offsetMax = image.rectTransform.offsetMax;
            }
            public void Restore()
            {
                if (_image == null) return;
                _image.type = _type; _image.fillAmount = _amount;
                _image.rectTransform.anchorMin = _anchorMin; _image.rectTransform.anchorMax = _anchorMax;
                _image.rectTransform.offsetMin = _offsetMin; _image.rectTransform.offsetMax = _offsetMax;
            }
        }

        private sealed class TextState
        {
            private readonly TMP_Text _text;
            private readonly string _value;
            private readonly Color _color;
            private readonly float _fontSize, _min, _max;
            private readonly bool _auto;
            public TextState(TMP_Text text) { _text = text; _value = text.text; _color = text.color; _fontSize = text.fontSize; _auto = text.enableAutoSizing; _min = text.fontSizeMin; _max = text.fontSizeMax; }
            public void Restore() { if (_text == null) return; _text.text = _value; _text.color = _color; _text.fontSize = _fontSize; _text.enableAutoSizing = _auto; _text.fontSizeMin = _min; _text.fontSizeMax = _max; }
        }

        private sealed class ActiveState
        {
            private readonly GameObject _object;
            private readonly bool _active;
            public ActiveState(GameObject value) { _object = value; _active = value.activeSelf; }
            public void Restore() { if (_object != null) _object.SetActive(_active); }
        }

        [Serializable] private sealed class Check { public string target; public bool passed; public string details; public string utc; }
        [Serializable] private sealed class Report { public string scope = "UI-only production-view checks before a match; no RPC"; public int width; public int height; public List<Check> checks; }
        private static void Record(string target, bool passed, string details)
        {
            Checks.Add(new Check { target = target, passed = passed, details = details, utc = DateTime.UtcNow.ToString("O") });
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, "../.cache/qa/bridge/validation-battle-top-v3"));
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "unity-fixture-report.json"), JsonUtility.ToJson(new Report { width = Screen.width, height = Screen.height, checks = Checks }, true));
        }

        private static Transform Find(Component root, string name) => root.GetComponentsInChildren<Transform>(true).First(t => t.name == name);
        private static T Read<T>(object owner, string name) => (T)owner.GetType().GetField(name, Private).GetValue(owner);
        private static void Write(object owner, string name, object value) => owner.GetType().GetField(name, Private).SetValue(owner, value);
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
