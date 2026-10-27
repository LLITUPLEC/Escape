using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Project.Match3;
using Project.Tutorial;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Project.Editor
{
    /// <summary>Explicit offline QA. The parent runner owns entering Play and taking native screenshots.</summary>
    public static class TutorialQa
    {
        private const string Output = ".cache/qa/legacy-0.3.12/tutorial";
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static IEnumerator _flow;
        private static TutorialController _controller;
        private static Result _result;
        private static bool _savedExit;
        private static bool _savedBackground;
        private static int _exitCount;
        private static TutorialExitReason _lastExit;
        private static string _view = "intro";
        private static bool _captureEffects;
        [Serializable] private sealed class Result
        {
            public bool passed, running, offline = true, accountOrRewardActions;
            public int assertions, step, textCount, touchTargets, boardCells, abilityIcons, networkComponents;
            public string stage, error, checkedUtc, canvasViewport;
            public string[] screenshots = Array.Empty<string>();
        }
        public static string Run(string target)
        {
            if (target == "prepare") return TutorialSceneTools.Prepare();
            if (target == "setup")
            {
                var message = TutorialSceneTools.Prepare();
                if (!SessionState.GetBool("TutorialQa.saved", false))
                {
                    SessionState.SetString("TutorialQa.playStart", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
                    SessionState.SetBool("TutorialQa.saved", true);
                }
                EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(TutorialSceneTools.ScenePath);
                return message + " Tutorial is the temporary Play start scene. Run restore after stopping Play.";
            }
            if (target == "restore")
            {
                Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Stop Play before restoring the Play start scene.");
                if (SessionState.GetBool("TutorialQa.saved", false))
                {
                    var previous = SessionState.GetString("TutorialQa.playStart", "");
                    EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(previous) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(previous);
                    SessionState.EraseBool("TutorialQa.saved"); SessionState.EraseString("TutorialQa.playStart");
                }
                return "Previous Play start scene restored; no gameplay/account API was invoked.";
            }
            if (target == "logic") return Logic();
            if (target == "play" || target == "play-fx") return StartFlow(target == "play-fx");
            if (target == "status") return _result == null ? "Tutorial QA has not run." : JsonUtility.ToJson(_result);
            if (target == "verify") return Verify();
            if (target.StartsWith("show-", StringComparison.Ordinal)) return Show(target.Substring(5));
            throw new ArgumentException("Use prepare, setup, logic, play, status, show-intro/resources/cross/square/shield/combo/fury/modes/summary/skip, verify, or restore.");
        }
        private static string Logic()
        {
            var report = new Result { stage = "logic", checkedUtc = DateTime.UtcNow.ToString("O") }; _result = report;
            try
            {
                var config = File.ReadAllText("Server/nakama/modules/duel_match3_config.lua");
                Rule(config, "MAX_HP", TutorialRules.MaxHp); Rule(config, "MAX_MANA", TutorialRules.MaxMana);
                Rule(config, "SKULL_DAMAGE", TutorialRules.BombDamage); Rule(config, "ANKH_HEAL", TutorialRules.CapsuleHeal);
                Rule(config, "ABILITY_BASE_DAMAGE", TutorialRules.AbilityDamage); Rule(config, "PETARD_DAMAGE", TutorialRules.PetardDamage);
                Rule(config, "SHIELD_ARMOR_PER_STACK", TutorialRules.ShieldArmor); Rule(config, "SHIELD_HEAL_PER_STACK", TutorialRules.ShieldHeal);
                Rule(config, "SHIELD_DURATION_TURNS", TutorialRules.ShieldTurns); Rule(config, "SHIELD_MAX_STACKS", TutorialRules.MaxShieldStacks);
                Rule(config, "FURY_CRIT_CHANCE", TutorialRules.FuryCritChance);
                foreach (AbilityType ability in Enum.GetValues(typeof(AbilityType)))
                {
                    var prefix = ability.ToString().ToUpperInvariant() + "_ABILITY_";
                    Rule(config, prefix + "COST", TutorialRules.Cost(ability)); Rule(config, prefix + "COOLDOWN", TutorialRules.Cooldown(ability));
                }
                Check(Regex.IsMatch(config, @"GEM_MANA\s*=\s*\{\s*\[1\]\s*=\s*5,\s*\[2\]\s*=\s*3,\s*\[3\]\s*=\s*1\s*\}"), "Server mana mapping changed.");
                Check(TutorialRules.Mana(PieceType.GemGreen) == 1 && TutorialRules.Mana(PieceType.Skull) == 0, "Backpack/bomb properties differ.");
                var session = new TutorialSession(); Check(session.Step == 0 && session.Passed && session.Next(), "Introduction cannot start.");
                var original = session.Board.ToArray();
                Check(!session.Next() && !session.Swap(0, 0, 5, 5, out _) && original.SequenceEqual(session.Board.ToArray()), "Invalid swap advanced or changed the board.");
                Check(!session.Ability(AbilityType.Petard) && session.Mana == 0, "An unrelated ability changed the mana lesson.");
                Check(session.Swap(2, 3, 2, 2, out var triple) && triple.Count == 1 && triple[0].count == 3 && session.Mana == 15 && !session.ExtraTurn, "Canister triple differs.");
                Check(!session.Swap(2, 3, 2, 2, out _) && session.Mana == 15, "Repeated action duplicated mana.");
                session.Next(); Check(!session.ChooseResource(PieceType.GemGreen) && !session.Passed, "Wrong resource passed.");
                Check(session.ChooseResource(PieceType.GemYellow) && session.Passed, "Compass answer failed.");
                session.Next(); Check(session.Swap(2, 3, 2, 2, out _) && session.EnemyHp == 135, "Bomb raw damage differs.");
                session.Next(); Check(session.Swap(2, 3, 2, 2, out _) && session.Hp == 63, "Capsule healing differs.");
                session.Next(); Check(session.Swap(2, 3, 2, 2, out var five) && five[0].count == 5 && session.ExtraTurn && session.Mana == 35, "Five-in-line extra turn differs.");
                Enter(session, 5); session.Board.FromArray(TutorialSession.DemoBoard(PieceType.GemYellow, 4));
                Check(session.Swap(2, 3, 2, 2, out var four) && four[0].count == 4 && !session.ExtraTurn, "Four incorrectly grants an extra turn.");
                foreach (var ability in new[] { AbilityType.Cross, AbilityType.Square })
                {
                    Enter(session, ability == AbilityType.Cross ? 6 : 7); var before = session.Board.ToArray();
                    Check(!session.Target(2, 2) && session.Mana == 20, "Target accepted before choosing an ability.");
                    Check(session.Ability(ability) && session.Targeting && session.Mana == 20 && !session.Passed, "Targeting spends mana before confirmation.");
                    Check(!session.Target(0, 0) && session.Mana == 20 && !session.Passed, "Wrong target accepted.");
                    Check(session.Target(2, 2) && session.LastArea.Count == 9 && session.LastArea.Distinct().Count() == 9, "Ability shape differs from actual board logic.");
                    var expectedMana = session.LastArea.Sum(c => TutorialRules.Mana((PieceType)before[c.y * 6 + c.x]));
                    Check(session.Mana == expectedMana && session.Passed && !session.Target(2, 2), "Ability effects duplicated or cost differs.");
                }
                Enter(session, 8); Check(session.Ability(AbilityType.Shield) && session.Hp == 39 && session.Mana == 10 && session.ShieldStacks == 1, "Shield demonstration differs.");
                Enter(session, 9); var boardBefore = session.Board.ToArray(); var bombs = session.CountBombs();
                Check(!session.Ability(AbilityType.Petard) && session.Mana == 60, "Petard skipped the fury lesson.");
                Check(session.Ability(AbilityType.Fury) && session.FuryActive && !session.Passed && session.Mana == 30, "Fury chain differs.");
                Check(!session.Ability(AbilityType.Fury) && session.Mana == 30, "Repeated fury changed state.");
                Check(session.Ability(AbilityType.Petard) && session.Mana == 0 && session.EnemyHp == 150 && session.PetardPending && !session.Passed && boardBefore.SequenceEqual(session.Board.ToArray()), "Petard applied damage before contact or changed board.");
                Check(!session.Ability(AbilityType.Petard) && !session.Next() && session.Mana == 0 && session.EnemyHp == 150, "In-flight petard duplicated spending or bypassed the lesson.");
                Check(session.ResolvePetardImpact() && !session.PetardPending && session.Passed && session.EnemyHp == 150 - 15 - bombs && !session.ResolvePetardImpact(), "Petard contact did not apply exactly once.");
                Enter(session, 9); session.Ability(AbilityType.Fury); session.Ability(AbilityType.Petard); session.Restart();
                Check(!session.ResolvePetardImpact() && session.EnemyHp == 150 && !session.PetardPending, "Restart retained pending petard damage.");
                Enter(session, 9); session.Ability(AbilityType.Fury); session.Ability(AbilityType.Petard); session.ResolvePetardImpact();
                session.Next(); Check(!session.ChooseMode(false) && session.ChooseMode(true), "Mode answer was not validated.");
                Check(session.Next() && session.Step == 11 && session.Passed && !session.Next(), "Explicit final step missing.");
                Check(session.Back() && session.Step == 10 && !session.Passed, "Back did not reset the demonstration.");
                session.Restart(); Check(session.Step == 0 && session.Hp == 90 && !session.FuryActive, "Restart retained demo state.");
                foreach (var path in Directory.GetFiles("Assets/_Project/Scripts/Tutorial", "*.cs"))
                {
                    var source = File.ReadAllText(path);
                    Check(!Regex.IsMatch(source, @"\b(Nakama|RpcAsync|DuelMatch3Manager|ProgressionWallet|PlayerPrefs|DailyRewardsService)\b"), "Gameplay/account dependency in " + path);
                }
                report.passed = true;
            }
            catch (Exception ex) { report.error = ex.Message; }
            Write("logic", report); if (!report.passed) throw new InvalidOperationException(report.error); return JsonUtility.ToJson(report);
        }
        private static void Rule(string source, string name, double expected)
        {
            var match = Regex.Match(source, @"\b" + name + @"\s*=\s*(\d+(?:\.\d+)?)\s*,");
            Check(match.Success && Math.Abs(double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) - expected) < .00001, "Tutorial rule differs from server: " + name);
        }
        private static string StartFlow(bool captureEffects)
        {
            Require(_flow == null, "A Tutorial QA flow is already running."); _controller = Find();
            _savedExit = (bool)typeof(TutorialController).GetField("_exitToMenu", PrivateInstance).GetValue(_controller);
            _savedBackground = Application.runInBackground;
            Application.runInBackground = true;
            typeof(TutorialController).GetField("_exitToMenu", PrivateInstance).SetValue(_controller, false);
            _exitCount = 0; _captureEffects = captureEffects; _controller.ExitRequested += ObserveExit;
            _result = new Result { running = true, stage = "intro", checkedUtc = DateTime.UtcNow.ToString("O") };
            _flow = Flow(); EditorApplication.update += Tick;
            return "Started actual Tutorial buttons/board-input flow. Exit is suppressed only for this offline QA instance; poll status.";
        }
        private static IEnumerator Flow()
        {
            _controller.Restart(); yield return null;
            Check(_controller.PlayerPanel != null && _controller.EnemyPanel != null && _controller.AbilityPanel != null && _controller.Effects != null, "Training does not use real battle presentation widgets.");
            Check(_controller.PlayerPanel.damagePopup != null && _controller.PlayerPanel.gainPopup != null && _controller.EnemyPanel.damagePopup != null, "Shipping popup widgets are missing.");
            Check(_controller.Session.Step == 0 && Button("TutorialNext").interactable, "Introduction is not ready.");
            Click("TutorialNext"); Check(_controller.Session.Step == 1 && !Button("TutorialNext").interactable, "First interactive lesson is skippable.");
            Click("TutorialNext", true); Check(_controller.Session.Step == 1, "Disabled Next bypassed the lesson.");
            var before = _controller.Session.Board.ToArray(); CellClick(0, 0); CellClick(5, 5);
            Check(!_controller.Session.Passed && before.SequenceEqual(_controller.Session.Board.ToArray()), "Invalid board click pair advanced.");
            Click("TutorialAbilityPetard", true); Check(!_controller.Session.Passed && _controller.Session.Mana == 0, "Disabled ability bypassed the current lesson.");
            CellClick(2, 3); CellClick(2, 2); while (_controller.Resolving) yield return null;
            Check(_controller.Session.Mana == 15 && Button("TutorialNext").interactable, "Real canister input failed.");
            Check(_controller.DisplayedMana == 15 && VisibleGain(_controller.PlayerPanel, "+15"), "Real mana popup/bar did not update from the resolved board."); Shot("mana-gain");
            Click("TutorialNext"); Click("TutorialResourceGemGreen"); Check(!_controller.Session.Passed, "Wrong resource button passed.");
            Click("TutorialResourceGemYellow"); Check(_controller.Session.Passed, "Compass button failed.");
            Click("TutorialBack"); Check(_controller.Session.Step == 1 && !_controller.Session.Passed && _controller.Session.Mana == 0, "Back retained completed demo.");
            Click("TutorialRestart"); Check(_controller.Session.Step == 0, "Restart button failed.");
            Click("TutorialNext"); CellClick(2, 3); CellClick(2, 2); while (_controller.Resolving) yield return null;
            Click("TutorialNext"); Click("TutorialResourceGemYellow"); Click("TutorialNext");
            for (var step = 3; step <= 5; step++)
            {
                _result.stage = "board-lesson-" + step; Check(_controller.Session.Step == step && !Button("TutorialNext").interactable, "Board lesson is not gated.");
                CellClick(2, 3); CellClick(2, 2); while (_controller.Resolving) yield return null;
                Check(_controller.Session.Passed && (step != 3 || _controller.Session.EnemyHp == 135) && (step != 4 || _controller.Session.Hp == 63) && (step != 5 || _controller.Session.ExtraTurn), "Board outcome differs.");
                if (step == 3) { Check(VisibleDamage(_controller.EnemyPanel, "-15"), "Real bomb damage popup is missing."); Shot("bomb-damage"); }
                if (step == 4) { Check(VisibleGain(_controller.PlayerPanel, "+3"), "Real healing popup is missing."); Shot("healing-gain"); }
                Click("TutorialNext");
                if (step == 3) Check(!VisibleDamage(_controller.EnemyPanel, "-15"), "A previous lesson's damage popup survived Next.");
                if (step == 4) Check(!VisibleGain(_controller.PlayerPanel, "+3"), "A previous lesson's heal popup survived Next.");
            }
            foreach (var ability in new[] { "Cross", "Square" })
            {
                _result.stage = ability; var mana = _controller.Session.Mana; Click("TutorialAbility" + ability);
                Check(_controller.Session.Targeting && !_controller.Session.Passed && _controller.Session.Mana == mana, "Ability target stage was skipped.");
                CellClick(0, 0); Check(_controller.Session.Targeting && !_controller.Session.Passed, "Wrong target passed.");
                CellClick(2, 2); while (_controller.Resolving) yield return null;
                Check(_controller.Session.Passed && _controller.Session.LastArea.Count == 9, "Actual area input failed."); Click("TutorialNext");
            }
            _result.stage = "shield-combo"; var shields = _controller.Effects.ShieldCount; Click("TutorialAbilityShield");
            Check(_controller.Session.Hp == 39 && _controller.Session.ShieldStacks == 1 && _controller.DisplayedHp == 50 && _controller.Resolving, "Shield activation did not preserve the pending demonstration hit.");
            var until = Time.realtimeSinceStartup + .18f; while (Time.realtimeSinceStartup < until) yield return null;
            Check(_controller.Effects.ShieldCount == shields + 1, "Real shield activation was not played once."); Shot("shield-activation");
            Check(BadgeClearsBoard(_controller), "Shield status badge overlaps the training board.");
            while (_controller.Resolving) yield return null;
            Check(_controller.DisplayedHp == 39 && VisibleDamage(_controller.PlayerPanel, "-11"), "Shield did not mitigate the scripted hit or show real damage."); Click("TutorialNext");
            Check(!VisibleDamage(_controller.PlayerPanel, "-11"), "Shield damage popup leaked into the fury lesson.");
            var frozen = _controller.Session.Board.ToArray(); Click("TutorialAbilityPetard", true); Check(!_controller.Session.Passed && _controller.Session.Mana == 60, "Petard bypassed fury.");
            Click("TutorialAbilityFury"); Check(!_controller.Session.Passed && !Button("TutorialNext").interactable, "Fury prematurely completed the combo.");
            until = Time.realtimeSinceStartup + .20f; while (Time.realtimeSinceStartup < until) yield return null;
            var fury = _controller.PlayerPanel.avatarImage.GetComponent<Match3AvatarFuryFx>();
            Check(fury != null && fury.IsActive, "Real avatar flame did not activate."); Shot("fury-avatar");
            var enemyHp = _controller.Session.EnemyHp; var launches = _controller.Effects.LaunchCount; var impacts = _controller.Effects.ImpactCount;
            Click("TutorialAbilityPetard");
            Check(_controller.Session.PetardPending && !_controller.Session.Passed && _controller.Session.Mana == 0 && _controller.Session.EnemyHp == enemyHp && _controller.DisplayedEnemyHp == enemyHp && !Button("TutorialNext").interactable, "Petard damage was applied before flight.");
            Click("TutorialAbilityPetard", true);
            until = Time.realtimeSinceStartup + Match3AbilityFx.FlightSeconds * .35f; while (Time.realtimeSinceStartup < until) yield return null;
            Check(_controller.Effects.PetardFlying && _controller.Session.EnemyHp == enemyHp && _controller.DisplayedEnemyHp == enemyHp && _controller.Effects.ImpactCount == impacts, "HP changed during the actual flight."); Shot("petard-flight");
            while (_controller.Effects.PetardFlying) yield return null;
            Check(_controller.Session.Passed && !_controller.Session.PetardPending && _controller.Effects.LaunchCount == launches + 1 && _controller.Effects.ImpactCount == impacts + 1 && _controller.Session.EnemyHp < enemyHp && _controller.DisplayedEnemyHp == _controller.Session.EnemyHp,
                "Contact did not apply exactly one delayed hit.");
            Check(VisibleDamage(_controller.EnemyPanel, "-" + (enemyHp - _controller.Session.EnemyHp)), "Real damage popup is not visible at petard contact."); Shot("petard-impact");
            while (_controller.Resolving) yield return null;
            Check(_controller.Session.Mana == 0 && frozen.SequenceEqual(_controller.Session.Board.ToArray()), "Combo altered board or failed."); Click("TutorialNext");
            Click("TutorialChooseClassic"); Check(!_controller.Session.Passed, "Wrong mode passed."); Click("TutorialChoosePro"); Click("TutorialNext");
            Check(_controller.Session.Step == 11 && _exitCount == 0, "Training exited without explicit Finish."); Click("TutorialNext");
            Check(_exitCount == 1 && _lastExit == TutorialExitReason.Completed, "Finish did not emit Completed once."); Click("TutorialNext", true); Check(_exitCount == 1, "Finish duplicated the event.");
            _controller.Restart(); Click("TutorialSkip"); yield return null;
            Check(_exitCount == 1 && FindObject("TutorialSkipConfirmation") != null, "Skip lacks confirmation.");
            Click("TutorialNext", true); Check(_controller.Session.Step == 0, "Confirmation allowed a background action.");
            Click("TutorialKeepLearning"); yield return null; Check(FindObject("TutorialSkipConfirmation") == null && _exitCount == 1, "Keep learning exited.");
            Click("TutorialSkip"); Click("TutorialConfirmSkip"); Check(_exitCount == 2 && _lastExit == TutorialExitReason.Skipped, "Confirmed skip did not emit Skipped.");
            Click("TutorialConfirmSkip", true); Check(_exitCount == 2, "Skip duplicated the event.");
            _controller.Restart(); yield return null; _result.passed = true; _result.stage = "complete";
        }
        private static double _deadline;
        private static void Tick()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            try
            {
                if (!EditorApplication.isPlaying || _controller == null) throw new InvalidOperationException("Tutorial Play ended during QA.");
                if (_deadline == 0) _deadline = EditorApplication.timeSinceStartup + 90;
                Require(EditorApplication.timeSinceStartup < _deadline, "Tutorial QA timed out while resolving an animation.");
                if (_flow.MoveNext()) return;
            }
            catch (Exception ex) { _result.error = ex.ToString(); _result.passed = false; }
            _result.running = false; _result.step = _controller != null ? _controller.Session.Step : -1;
            if (_controller != null) { _controller.ExitRequested -= ObserveExit; typeof(TutorialController).GetField("_exitToMenu", PrivateInstance).SetValue(_controller, _savedExit); }
            _flow = null; _deadline = 0; EditorApplication.update -= Tick; Write("play", _result);
            Application.runInBackground = _savedBackground;
        }
        private static void ObserveExit(TutorialExitReason reason) { _exitCount++; _lastExit = reason; }
        private static string Show(string view)
        {
            Require(_flow == null, "Wait for the button QA flow before showing a screenshot state."); var controller = Find(); controller.Restart();
            var step = view == "intro" || view == "skip" ? 0 : view == "resources" ? 2 : view == "cross" ? 6 : view == "square" ? 7 : view == "shield" ? 8 : view == "combo" || view == "fury" ? 9 : view == "modes" ? 10 : view == "summary" ? 11 : -1;
            Require(step >= 0, "Unknown tutorial view: " + view); Enter(controller.Session, step);
            typeof(TutorialController).GetMethod("EnterView", PrivateInstance).Invoke(controller, null);
            _controller = controller; if (view == "cross" || view == "square") Click("TutorialAbility" + (view == "cross" ? "Cross" : "Square"));
            if (view == "fury") Click("TutorialAbilityFury"); if (view == "skip") Click("TutorialSkip");
            _view = view; Canvas.ForceUpdateCanvases(); return "Tutorial presentation: " + view + ". Wait one rendered frame, verify, then capture the native GameView.";
        }
        private static string Verify()
        {
            var report = new Result { stage = _view, checkedUtc = DateTime.UtcNow.ToString("O") };
            try
            {
                var controller = Find(); report.step = controller.Session.Step; var canvas = controller.GetComponentInChildren<Canvas>();
                Require(canvas != null, "Tutorial canvas missing."); Canvas.ForceUpdateCanvases(); var viewport = canvas.pixelRect; report.canvasViewport = viewport.ToString();
                Require(Mathf.Abs(viewport.x) < 1 && Mathf.Abs(viewport.y) < 1 && Mathf.Abs(viewport.width - 1080) < 1 && Mathf.Abs(viewport.height - 1920) < 1, "Native Tutorial viewport must be1080x1920: " + viewport + "; Screen=" + Screen.width + "x" + Screen.height);
                foreach (var text in canvas.GetComponentsInChildren<TMP_Text>())
                {
                    if (!text.isActiveAndEnabled || string.IsNullOrEmpty(text.text)) continue; text.ForceMeshUpdate(); report.textCount++;
                    Require(!text.isTextOverflowing, "Tutorial text overflow: " + text.name + " / " + text.text + " / " + text.rectTransform.rect); Within(text.rectTransform, canvas, viewport);
                }
                foreach (var button in canvas.GetComponentsInChildren<Button>())
                {
                    if (!button.isActiveAndEnabled) continue; report.touchTargets++; var rect = (RectTransform)button.transform; var physical = ScreenRect(rect, canvas);
                    Require(physical.width >= 44 && physical.height >= 44, "Touch target below44px: " + button.name + " / " + physical); Within(rect, canvas, viewport);
                }
                var board = controller.GetComponentInChildren<Match3BoardView>(); Require(board != null && board.GetBallsAtlasTexture() != null, "Actual board atlas is missing.");
                foreach (Transform cell in board.cellContainer)
                {
                    if (!cell.name.StartsWith("Cell_", StringComparison.Ordinal)) continue; report.boardCells++; var physical = ScreenRect((RectTransform)cell, canvas);
                    Require(physical.width >= 44 && physical.height >= 44, "Board cell below44px."); Within((RectTransform)cell, canvas, viewport);
                }
                Require(report.boardCells == 36, "Expected36 real interactive board cells.");
                foreach (var image in controller.AbilityPanel.GetComponentsInChildren<Image>()) if (image.name == "AbilityIcon") { report.abilityIcons++; Require(image.sprite != null, "Actual ability sprite missing."); }
                Require(report.abilityIcons == 5, "Five real ability icons are required.");
                var buttons = new[] { controller.AbilityPanel.petardButton, controller.AbilityPanel.crossButton, controller.AbilityPanel.squareButton, controller.AbilityPanel.shieldButton, controller.AbilityPanel.furyButton };
                var expectedNames = new[] { "Петарда", "Крест", "Квадрат", "Щит", "Ярость" }; var expectedCosts = new[] { "30", "20", "20", "40", "30" };
                var previousX = float.NegativeInfinity;
                for (var i = 0; i < buttons.Length; i++)
                {
                    var button = buttons[i]; var buttonRect = ScreenRect((RectTransform)button.transform, canvas);
                    Require(buttonRect.xMin > previousX, "Tutorial ability order differs from the real battle."); previousX = buttonRect.xMin;
                    var cost = button.transform.Find("AbilityCost")?.GetComponent<TMP_Text>(); var name = button.transform.Find("ExpeditionAbilityName")?.GetComponent<TMP_Text>();
                    Require(cost != null && cost.text == expectedCosts[i] && (cost.fontStyle & FontStyles.Bold) != 0 && button.GetComponent<ExpeditionAbilityCostCorner>() != null, "Actual bottom-right numeric cost is missing.");
                    Require(name != null && name.text == expectedNames[i] && ScreenRect(name.rectTransform, canvas).center.y > buttonRect.center.y, "Ability name is not above its sprite.");
                }
                foreach (var panel in new[] { controller.PlayerPanel, controller.EnemyPanel })
                {
                    Require(panel != null && panel.hpFill != null && panel.manaFill != null && panel.damagePopup != null && panel.gainPopup != null, "Actual battle panel or popup wiring is missing.");
                    Require(ScreenRect(panel.hpFill.transform.parent as RectTransform, canvas).center.y > ScreenRect(panel.manaFill.transform.parent as RectTransform, canvas).center.y, "HP and mana are not grouped vertically as in battle.");
                    Require(panel.transform.Find("ExpeditionPortraitFrame") != null && panel.transform.Find("ExpeditionHeaderBody") != null, "Real portrait/header styling is missing.");
                }
                Require(BadgeClearsBoard(controller), "Active shield status badge overlaps the board.");
                var arrow = canvas.GetComponentInChildren<TutorialFocusArrow>(); Require(arrow != null && !arrow.raycastTarget, "Pointer intercepts controls.");
                if (arrow.isActiveAndEnabled)
                {
                    var mesh = arrow.canvasRenderer.GetMesh(); var count = mesh != null ? mesh.vertexCount : 0;
                    Require(count >= 14 && mesh.colors32.All(c => c.a == 255) && mesh.colors32.Any(c => c.g > 240 && c.r < 60), "Focus pointer is not opaque green with its contrast silhouette; wait a rendered frame.");
                }
                report.networkComponents = SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<MonoBehaviour>(true)).Count(c => c != null && (c.GetType().Namespace ?? "").StartsWith("Project.Nakama", StringComparison.Ordinal));
                Require(report.networkComponents == 0, "Network components are present in the training scene."); report.passed = true;
            }
            catch (Exception ex) { report.error = ex.Message; }
            Write("ui-" + _view, report); if (!report.passed) throw new InvalidOperationException(report.error); return JsonUtility.ToJson(report);
        }
        private static TutorialController Find()
        {
            Require(EditorApplication.isPlaying && !EditorApplication.isPaused, "Use the isolated Tutorial Play scene.");
            var scene = SceneManager.GetActiveScene(); Require(scene.name == TutorialLauncher.SceneName, "The active scene must be Tutorial."); TutorialSceneTools.AssertSafe(scene);
            foreach (var component in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Require(component == null || (component.GetType().Namespace ?? "") != "Project.Nakama", "QA requires a fresh isolated Tutorial Play session, without Nakama bootstrap.");
            var controller = UnityEngine.Object.FindFirstObjectByType<TutorialController>(); Require(controller != null && controller.Initialized, "Wait for Tutorial Start."); return controller;
        }
        private static void Enter(TutorialSession session, int step) => typeof(TutorialSession).GetMethod("Enter", PrivateInstance).Invoke(session, new object[] { step });
        private static GameObject FindObject(string name) => _controller.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name)?.gameObject;
        private static Button Button(string name) { var go = FindObject(name); Require(go != null, "Missing tutorial control: " + name); return go.GetComponent<Button>(); }
        private static void Click(string name, bool allowDisabled = false)
        {
            var button = Button(name); Require(button != null && button.gameObject.activeInHierarchy && (allowDisabled || button.interactable), "Tutorial control is unavailable: " + name); button.onClick.Invoke();
        }
        private static void CellClick(int x, int y)
        {
            var cell = FindObject("Cell_" + x + "_" + y); Require(cell != null, "Board cell is missing.");
            var data = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
            Require(ExecuteEvents.Execute(cell, data, ExecuteEvents.pointerDownHandler) && ExecuteEvents.Execute(cell, data, ExecuteEvents.pointerClickHandler), "Actual cell input handlers were not called.");
        }
        private static bool VisibleGain(Match3PlayerPanel panel, string expected) => panel.gainPopup.GetComponentsInChildren<TMP_Text>().Any(t => t.gameObject.activeInHierarchy && t.text == expected && t.GetComponentInParent<CanvasGroup>().alpha > .01f);
        private static bool BadgeClearsBoard(TutorialController controller)
        {
            var canvas = controller.GetComponentInChildren<Canvas>();
            var board = controller.GetComponentInChildren<Match3BoardView>();
            var boardBounds = ScreenRect((RectTransform)board.transform, canvas);
            foreach (var panel in new[] { controller.PlayerPanel, controller.EnemyPanel })
            {
                var badge = panel.transform.Find("ExpeditionShieldBadge") as RectTransform;
                if (badge != null && badge.gameObject.activeInHierarchy && ScreenRect(badge, canvas).yMin < boardBounds.yMax + 1) return false;
            }
            return true;
        }
        private static bool VisibleDamage(Match3PlayerPanel panel, string expected)
        {
            var value = panel.damagePopup.GetComponentInChildren<TMP_Text>(true);
            return value != null && value.text == expected && value.gameObject.activeInHierarchy && panel.damagePopup.GetComponent<CanvasGroup>().alpha > .01f;
        }
        private static void Shot(string name)
        {
            if (!_captureEffects) return;
            Canvas.ForceUpdateCanvases(); Directory.CreateDirectory(Output);
            var source = ExpeditionReleaseCaptureView.Capture("publish-tutorial0312-" + name);
            var target = Path.GetFullPath(Path.Combine(Output, name + ".png")); File.Copy(source, target, true);
            _result.screenshots = _result.screenshots.Concat(new[] { target }).ToArray();
        }
        private static Rect ScreenRect(RectTransform rect, Canvas canvas)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners); var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity); var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            foreach (var corner in corners) { var point = RectTransformUtility.WorldToScreenPoint(camera, corner); min = Vector2.Min(min, point); max = Vector2.Max(max, point); }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
        private static void Within(RectTransform rect, Canvas canvas, Rect viewport)
        {
            var bounds = ScreenRect(rect, canvas); Require(bounds.width > 0 && bounds.height > 0 && bounds.xMin >= viewport.xMin - 1 && bounds.xMax <= viewport.xMax + 1 && bounds.yMin >= viewport.yMin - 1 && bounds.yMax <= viewport.yMax + 1,
                "Tutorial control outside native viewport: " + rect.name + " / " + bounds + " / " + viewport);
        }
        private static void Check(bool condition, string error) { Require(condition, error); if (_result != null) _result.assertions++; }
        private static void Require(bool condition, string error) { if (!condition) throw new InvalidOperationException(error); }
        private static void Write(string name, Result result) { Directory.CreateDirectory(Output); File.WriteAllText(Output + "/" + name + ".json", JsonUtility.ToJson(result, true)); }
    }
}
