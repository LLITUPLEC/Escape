using System;
using System.Threading;
using System.Threading.Tasks;
using Project.UI;
using Project.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Match3
{
    public sealed partial class DuelMatch3Manager
    {
        private bool _isGlobalBoss;
        private bool _isGlobalBossPractice;
        private M3GameOverMsg _bossGameOver;
        private TMP_Text _bossBattleStatus;
        private int _lastScreamTurn = -1;
        private long _bossDamagePresented;
        private long _lastAdaptationSequence=-1;
        private bool _bossTurnPaused;
        private float _bossDecisionDuration=30;
        private async Task JoinGlobalBossAsync(Match3LaunchContext.GlobalBossJoin join, CancellationToken ct)
        {
            var boot = Project.Nakama.NakamaBootstrap.Instance;
            var session = boot?.Session; var socket = boot?.Socket;
            if (session == null || socket == null || string.IsNullOrEmpty(join.MatchId) || string.IsNullOrEmpty(join.AttemptId) || string.IsNullOrEmpty(join.EventId) || !Project.Nakama.LocalAccountCacheScope.IsCurrent(join.OwnerScope)) throw new OperationCanceledException("Boss entry belongs to another account.", ct);
            _isGlobalBoss = true; _isGlobalBossPractice = join.Practice; _pveProgress = new PveProgressInfo();
            _activePveBotFloor = 0; _opponentIsServerBot = true;
            _opUserId = string.IsNullOrWhiteSpace(join.BotUserId) ? "zz-bot-global_boss" : join.BotUserId;
            _opDisplayName = string.IsNullOrWhiteSpace(join.Name) ? "Сердце Разлома" : join.Name;
            var match = await socket.JoinMatchAsync(join.MatchId);
            ct.ThrowIfCancellationRequested();
            await MainThreadDispatcher.RunAsync(() =>
            {
                if (this == null || ct.IsCancellationRequested || !Project.Nakama.LocalAccountCacheScope.IsCurrent(join.OwnerScope) || !ReferenceEquals(session, boot.Session) || !ReferenceEquals(socket, boot.Socket)) return;
                _match = match;
                Match3LaunchContext.ClearGlobalBoss();
                OnBotMatchFound(_opDisplayName); ApplyExpeditionPortraits();
                InstallGlobalBossHud();
            }).ConfigureAwait(false);
        }
        private void InstallGlobalBossHud()
        {
            var canvas = _myPanel != null ? _myPanel.GetComponentInParent<Canvas>() : null;
            var safe = canvas != null ? ExpeditionUi.Find(canvas.transform, "ExpeditionBattleSafeArea") : null;
            if (safe == null || _bossBattleStatus != null) return;
            ExpeditionBattleLayout.UseGlobalBossArena(canvas.transform);
            var caption = ExpeditionUi.Find(safe, "ExpeditionBattleCaption"); if (caption != null) caption.gameObject.SetActive(false);
            _bossBattleStatus = ExpeditionUi.Text(safe, "GlobalBossContribution", _isGlobalBossPractice ? "ПРОБНЫЙ БОЙ · БЕЗ НАГРАД И ЗАТРАТ" : "МИРОВОЙ БОСС · ВКЛАД: 0", 20, .035f, .006f, .79f, .042f, TextAlignmentOptions.Center);
            _bossBattleStatus.enableAutoSizing = true; _bossBattleStatus.fontSizeMin = 15; _bossBattleStatus.fontSizeMax = 20;
            ExpeditionUi.Button(safe, "GlobalBossRules", "БОСС", .035f, .665f, .155f, .712f, () => ShowBossRules(safe), false, 19);
        }
        private bool HasBossAbilityAction(M3BoardSyncMsg msg)
        {
            return msg != null && msg.globalBoss && msg.bossActionSequence > 0 &&
                   string.Equals(msg.actionActorUserId, _opUserId, StringComparison.Ordinal) &&
                   (msg.bossScreamTriggered || msg.bossAdaptationTriggered);
        }
        private bool KeepsBossTurnWithoutTimerReset(M3BoardSyncMsg msg) =>
            HasBossAbilityAction(msg) && msg.actionType == 0 && !msg.turnPaused &&
            msg.activeUserId == _opUserId && !_isMyTurn;
        private static float BossAbilityPresentationDuration(M3BoardSyncMsg msg) =>
            msg.bossAdaptationTriggered ? ExpeditionRiftAdaptation.Lifetime : ExpeditionRiftScream.Lifetime;
        private void UpdateGlobalBossPresentation(M3BoardSyncMsg msg, bool animate)
        {
            _isGlobalBoss = true;
            _isGlobalBossPractice = msg.globalBossPractice;
            if (_bossBattleStatus != null) _bossBattleStatus.text = $"{(_isGlobalBossPractice ? "ПРОБНЫЙ БОЙ · УРОН" : "ВКЛАД")}: {ExpeditionGlobalBossPanel.Number(msg.globalBossMyDamage)}  ·  ОСТАЛОСЬ ХОДОВ: {Math.Max(0, msg.bossPlayerTurnsLeft)}";
            if(animate&&msg.bossAdaptationTriggered&&msg.bossActionSequence>_lastAdaptationSequence)
            {
                _lastAdaptationSequence=msg.bossActionSequence;
                ExpeditionRiftAdaptation.Play(_opPanel);
                _boardView?.ShowCenterAnnouncement("АДАПТАЦИЯ · ХОД "+msg.bossOwnTurn,new Color32(255,112,91,255),1.5f);
            }
            if (animate && msg.bossScreamTriggered && msg.bossOwnTurn > _lastScreamTurn)
            {
                _lastScreamTurn = msg.bossOwnTurn;
                ExpeditionRiftScream.Play(_opPanel, _myPanel);
                _boardView?.ShowCenterAnnouncement("УЖАСАЮЩИЙ КРИК\n20% здоровья сквозь броню", new Color32(215, 164, 255, 255), 1.8f);
                if (msg.actionType <= 0 && msg.bossScreamDamage > 0) _myPanel?.ShowDamagePopup(msg.bossScreamDamage, false);
            }
            // The shared health can also change because other players are fighting.
            // Its authoritative bar is always refreshed, without treating that change as our damage.
        }
        private static void ShowBossRules(Transform safe)
        {
            if (ExpeditionUi.Find(safe, "GlobalBossRulesModal") != null) return;
            var modal = ExpeditionUi.Rect(safe, "GlobalBossRulesModal", 0, 0, 1, 1);
            ExpeditionUi.Fill(modal, "Shade", 0, 0, 1, 1, new Color(0, 0, 0, .84f)).raycastTarget = true;
            var panel = ExpeditionUi.Panel(modal, "RulesCard", .07f, .32f, .93f, .72f);
            ExpeditionUi.Text(panel.transform, "Title", "СПОСОБНОСТИ БОССА", 34, .06f, .84f, .94f, .96f, TextAlignmentOptions.Center).fontStyle = FontStyles.Bold;
            ExpeditionUi.Text(panel.transform, "Rules", "УЖАСАЮЩИЙ КРИК\n20% вашего начального здоровья. Броня не помогает. Без маны, не завершает ход. Перезарядка: 2 хода босса.\n\nАДАПТАЦИЯ\nНа каждом своём ходе n босс умножает текущий урон на (1+n%) и добавляет n п.п. к текущему шансу крита (до 100%). Усиление накапливается.\n\nБосс не лечится. Попытка длится до 30 ваших ходов. Все пять обычных способностей также доступны боссу.", 25, .065f, .19f, .935f, .84f);
            ExpeditionUi.Button(panel.transform, "CloseRules", "ПОНЯТНО", .17f, .04f, .83f, .16f, () => Destroy(modal.gameObject), true, 29);
        }
        private void ShowGlobalBossOutcome()
        {
            _isMyTurn = false; _inputBlocked = true; _boardView?.SetDimmed(false);
            if (_hud != null && _hud.affixTooltipRoot != null) _hud.affixTooltipRoot.gameObject.SetActive(false);
            var canvas = _myPanel != null ? _myPanel.GetComponentInParent<Canvas>() : null;
            if (canvas == null) return;
            var overlay = ExpeditionUi.Rect(canvas.transform, "GlobalBossOutcomeModal", 0, 0, 1, 1);
            var sorting = overlay.gameObject.AddComponent<Canvas>(); sorting.overrideSorting = true; sorting.sortingOrder = 2700; overlay.gameObject.AddComponent<GraphicRaycaster>();
            var safe = ExpeditionUi.Rect(overlay, "SafeArea", 0, 0, 1, 1); safe.gameObject.AddComponent<ExpeditionSafeArea>();
            ExpeditionUi.Fill(safe, "Shade", 0, 0, 1, 1, new Color(0, 0, 0, .93f)).raycastTarget = true;
            var result = _bossGameOver; var defeated = result != null && result.bossGlobalHp <= 0; var refund = result != null && result.bossAttemptRefunded;
            var practice = _isGlobalBossPractice || result?.globalBossPractice == true;
            var title = practice ? "ПРОБНЫЙ БОЙ ЗАВЕРШЁН" : refund ? "ПОПЫТКА ВОЗВРАЩЕНА" : defeated ? "ОБЩАЯ ПОБЕДА!" : "ВКЛАД СОХРАНЁН";
            ExpeditionUi.Text(safe, "Title", title, 48, .06f, .81f, .94f, .90f, TextAlignmentOptions.Center).fontStyle = FontStyles.Bold;
            var art = ExpeditionUi.Rect(safe, "BossArt", .16f, .46f, .84f, .80f).gameObject.AddComponent<Image>(); art.sprite = Resources.Load<Sprite>(GlobalBossService.Art); art.preserveAspect = true; art.raycastTarget = false;
            var card = ExpeditionUi.Panel(safe, "ContributionCard", .08f, .255f, .92f, .46f);
            ExpeditionUi.Text(card.transform, "Damage", "ВАШ УРОН: " + ExpeditionGlobalBossPanel.Number(result?.bossMyDamage ?? 0), 37, .045f, .66f, .955f, .94f, TextAlignmentOptions.Center).fontStyle = FontStyles.Bold;
            var percent = result != null && result.bossGlobalMaxHp > 0 ? 100.0 * result.bossMyDamage / result.bossGlobalMaxHp : 0;
            ExpeditionUi.Text(card.transform, "Percent", (practice ? "ЭТО БЫЛО БЫ " : "") + percent.ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("ru-RU")) + "% " + (practice ? "полного HP босса" : "общего здоровья босса"), 27, .06f, .48f, .94f, .66f, TextAlignmentOptions.Center);
            var notice = refund ? "Жетон и попытка возвращены. Можно попробовать снова." : defeated ? result?.bossQualified == true ? "Босс повержен всеми участниками! Награды доставляются в игровую почту." : "Общая победа! Личная награда доступна тем, кто успел нанести боссу урон." : result?.bossQualified == true ? "Вы участвуете в награде. Письмо придёт после общей победы над боссом." : "Для участия в награде нужно нанести боссу урон. Новое событие — новые попытки.";
            if (practice) notice = "Тренировка без ограничений. Урон не засчитан в событие, наград и затрат нет. Следующий бой — с полным здоровьем и исходными характеристиками.";
            ExpeditionUi.Text(card.transform, "RewardNotice", notice, 25, .065f, .06f, .935f, .46f, TextAlignmentOptions.Center);
            if (practice) ExpeditionUi.Button(safe, "RetryPractice", "ЕЩЁ ПРОБНЫЙ БОЙ", .18f, .185f, .82f, .245f, () => { _ = GlobalBossService.PracticeAsync(_cts.Token); }, false, 27);
            ExpeditionUi.Button(safe, "ReturnShelter", "В УБЕЖИЩЕ", .18f, practice ? .105f : .14f, .82f, practice ? .170f : .215f, ReturnFromGameOver, true, 36);
        }
    }
}
