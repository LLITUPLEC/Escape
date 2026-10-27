using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Project.Character;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>System rewards remain in their server letter until explicitly claimed.</summary>
    public sealed class ExpeditionMailPanel : MonoBehaviour
    {
        private CancellationTokenSource _cts;
        private MailService.State _fixture;
        private Action<string> _fixtureClaim;
        private ItemCatalog _catalog;
        private RectTransform _content;
        private TMP_Text _subtitle, _status;
        private string _fingerprint;
        private readonly List<Button> _claims = new();
        public static ExpeditionMailPanel Open(Transform host) => Open(host, null, null);
        public static ExpeditionMailPanel Open(Transform host, MailService.State fixture, Action<string> fixtureClaim)
        {
            var root = ExpeditionUi.Rect(host, "ExpeditionMailModal", 0, 0, 1, 1).gameObject;
            var view = root.AddComponent<ExpeditionMailPanel>(); view._fixture = fixture; view._fixtureClaim = fixtureClaim;
            view._catalog = Resources.FindObjectsOfTypeAll<ItemCatalog>().FirstOrDefault(c => c.Get("ingot_green") != null);
            view.Build(); view.Refresh(); if (fixture == null) _ = MailService.RefreshAsync(view._cts.Token); return view;
        }
        private void Build()
        {
            _cts = new CancellationTokenSource(); if (_fixture == null) MailService.Changed += Refresh;
            ExpeditionUi.Fill(transform, "MailShade", 0, 0, 1, 1, new Color(0, 0, 0, .86f)).raycastTarget = true;
            var safe = ExpeditionUi.Rect(transform, "MailSafeArea", 0, 0, 1, 1); safe.gameObject.AddComponent<ExpeditionSafeArea>();
            var panel = ExpeditionUi.Panel(safe, "MailPanel", .03f, .065f, .97f, .935f).rectTransform;
            Label(panel, "MailTitle", "ПОЧТА УБЕЖИЩА", 37, .055f, .922f, .87f, .98f).fontStyle = FontStyles.Bold;
            ExpeditionUi.Button(panel, "MailClose", "×", .895f, .925f, .974f, .98f, () => Destroy(gameObject), false, 42);
            _subtitle = Label(panel, "MailSubtitle", "Загружаем письма…", 22, .06f, .867f, .94f, .921f); _subtitle.color = ExpeditionTheme.Muted;
            var viewport = ExpeditionUi.Rect(panel, "MailViewport", .04f, .14f, .96f, .85f);
            viewport.gameObject.AddComponent<RectMask2D>(); var hit = viewport.gameObject.AddComponent<Image>(); hit.color = Color.clear; hit.raycastTarget = true;
            _content = ExpeditionUi.Rect(viewport, "MailMessages", 0, 0, 1, 1); _content.anchorMin = new Vector2(0, 1); _content.anchorMax = Vector2.one; _content.pivot = new Vector2(.5f, 1); _content.anchoredPosition = Vector2.zero;
            var scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport; scroll.content = _content; scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 36;
            _status = Label(panel, "MailStatus", "", 21, .055f, .025f, .72f, .12f); _status.color = ExpeditionTheme.Muted;
            ExpeditionUi.Button(panel, "MailRefresh", "Обновить", .755f, .04f, .95f, .105f, () => { if (_fixture == null) _ = MailService.RefreshAsync(_cts.Token); }, false, 22);
        }
        private void Refresh()
        {
            if (_content == null) return;
            var state = _fixture ?? MailService.Current; var busy = _fixture == null && MailService.Busy;
            if (!MailService.Valid(state))
            {
                _subtitle.text = "Загружаем письма…";
                foreach (Transform child in _content) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
                _claims.Clear(); _fingerprint = null;
                _status.text = _fixture == null ? MailService.Error ?? "Загружаем письма…" : "Письма недоступны"; return;
            }
            _subtitle.text = state.unread_count > 0 ? "Непрочитанных писем: " + state.unread_count : "Новых писем нет";
            var fingerprint = JsonUtility.ToJson(state);
            if (_fingerprint != fingerprint) { _fingerprint = fingerprint; BuildMessages(state.messages ?? Array.Empty<MailService.Message>()); }
            foreach (var button in _claims) button.interactable = !busy;
            _status.text = _fixture == null ? MailService.Error ?? MailService.Status ?? (busy ? "Подтверждаем получение…" : "Сообщения и награды сохраняются в письмах.") : "Просмотр системных писем";
        }
        private void BuildMessages(MailService.Message[] messages)
        {
            foreach (Transform child in _content) { child.gameObject.SetActive(false); Destroy(child.gameObject); } _claims.Clear();
            if (messages.Length == 0)
            {
                _content.sizeDelta = new Vector2(0, 500);
                var empty = ExpeditionUi.Panel(_content, "MailEmpty", .01f, 0, .99f, 1).rectTransform; empty.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Top, 25, 420);
                Label(empty, "EmptyTitle", "ПОКА НЕТ ПИСЕМ", 32, .06f, .56f, .94f, .82f, TextAlignmentOptions.Center).color = ExpeditionTheme.Amber;
                Label(empty, "EmptyHint", "Здесь появятся награды за события\nи важные сообщения убежища.", 25, .07f, .19f, .93f, .55f, TextAlignmentOptions.Center); return;
            }
            var top = 8f;
            foreach (var message in messages)
            {
                var attachments = message.attachments ?? Array.Empty<MailService.Attachment>(); var rows = Mathf.CeilToInt(attachments.Length / 4f);
                var appearance = message.IsAppearanceUnlock;
                var height = appearance ? 410 : 270 + rows * 140;
                var card = ExpeditionUi.Panel(_content, "MailMessage-" + message.id, .012f, 0, .988f, 1).rectTransform; card.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Top, top, height); top += height + 18;
                ExpeditionUi.Fill(card, "MailAccent", .035f, .975f, .965f, .989f, message.claimed ? ExpeditionTheme.Muted : ExpeditionTheme.Amber);
                Label(card, "MessageDate", Date(message.created_at), 18, .055f, .91f, .94f, .97f).color = ExpeditionTheme.Muted;
                Label(card, "MessageTitle", message.title ?? "Системная награда", 29, .05f, .79f, .95f, .91f).fontStyle = FontStyles.Bold;
                Label(card, "MessageBody", message.body ?? "", 22, appearance ? .33f : .055f, appearance ? .27f : .64f, .945f, appearance ? .76f : .79f).color = ExpeditionTheme.Muted;
                if (appearance)
                {
                    var portrait = ExpeditionUi.Fill(card, "UnlockedAppearancePortrait", .055f, .29f, .30f, .73f, Color.white);
                    portrait.sprite = CharacterAppearance.Load(message.avatar_id, true); portrait.preserveAspect = true; portrait.raycastTarget = false;
                }
                if (attachments.Length > 0)
                {
                    var area = ExpeditionUi.Rect(card, "MailAttachments", .045f, .235f, .955f, .625f);
                    for (var i = 0; i < attachments.Length; i++)
                    {
                        var a = attachments[i]; var column = i % 4; var row = i / 4; var h = 1f / rows;
                        var tile = ExpeditionUi.Rect(area, "MailAttachment-" + (a.def_id ?? a.resource), column * .25f + .008f, 1 - (row + 1) * h, (column + 1) * .25f - .008f, 1 - row * h);
                        var icon = ExpeditionUi.Fill(tile, "AttachmentIcon", .20f, .35f, .80f, .97f, Color.white); icon.sprite = Icon(a); icon.preserveAspect = true;
                        Label(tile, "AttachmentAmount", "×" + a.amount.ToString("N0", CultureInfo.GetCultureInfo("ru-RU")), 23, .02f, .055f, .98f, .35f, TextAlignmentOptions.Center).color = ExpeditionTheme.Amber;
                    }
                }
                var received = Label(card, "MessageState", attachments.Length == 0 ? (message.claimed ? "ПРОЧИТАНО" : appearance ? "НОВЫЙ ОБРАЗ ДОСТУПЕН" : "НОВОЕ СООБЩЕНИЕ") : message.claimed ? "НАГРАДА ПОЛУЧЕНА" : "НАГРАДА ОЖИДАЕТ ВАС", 19, .055f, .06f, .63f, .205f); received.color = message.claimed ? ExpeditionTheme.Muted : ExpeditionTheme.Amber;
                var button = ExpeditionUi.Button(card, (appearance ? "MailOpenHero-" : "MailClaim-") + message.id, appearance ? "К герою" : message.claimed ? "Получено" : attachments.Length == 0 ? "Прочитать" : "Получить", .665f, .055f, .955f, .205f, () =>
                { if (appearance) OpenHero(message); else if (_fixture != null) _fixtureClaim?.Invoke(message.id); else _ = MailService.ClaimAsync(message.id, _cts.Token); }, !message.claimed, 24);
                button.interactable = appearance || !message.claimed; if (button.interactable) _claims.Add(button);
            }
            _content.sizeDelta = new Vector2(0, top + 8); _content.anchoredPosition = Vector2.zero;
        }
        private async void OpenHero(MailService.Message message)
        {
            if (_fixture != null) { _fixtureClaim?.Invoke(message.id); return; }
            var owner = Project.Nakama.LocalAccountCacheScope.Current;
            if (!message.claimed) await MailService.ClaimAsync(message.id, _cts.Token);
            if (this == null || !Project.Nakama.LocalAccountCacheScope.IsCurrent(owner)) return;
            if (!message.claimed && MailService.Current?.messages?.Any(m => m.id == message.id && m.claimed) != true) return;
            gameObject.SetActive(false); Destroy(gameObject); ExpeditionPresentation.OpenCharacter();
        }
        private Sprite Icon(MailService.Attachment attachment)
        {
            if (!string.IsNullOrEmpty(attachment.resource)) return ExpeditionResourceIcons.Get(attachment.resource);
            if (attachment.def_id == "global_boss_teleport") return ExpeditionShopPanel.TokenIcon;
            if (ConsumablePresentation.IsKnown(attachment.def_id)) return ConsumablePresentation.Icon(attachment.def_id);
            var def = _catalog?.Get(attachment.def_id); return def != null ? _catalog.GetDisplayIcon(def) : ExpeditionTheme.Icon("inventory");
        }
        private static string Date(long timestamp)
        { try { return DateTimeOffset.FromUnixTimeSeconds(timestamp).ToOffset(TimeSpan.FromHours(3)).ToString("dd.MM · HH:mm", CultureInfo.GetCultureInfo("ru-RU")); } catch (ArgumentOutOfRangeException) { return "Сообщение убежища"; } }
        private static TMP_Text Label(Transform parent, string name, string text, int size, float x0, float y0, float x1, float y1, TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
        {
            var label = ExpeditionUi.Text(parent, name, text, size, x0, y0, x1, y1, alignment); label.richText = false; label.margin = Vector4.zero; label.enableAutoSizing = true; label.fontSizeMin = Math.Max(15, size - 4); label.fontSizeMax = size; label.overflowMode = TextOverflowModes.Overflow; return label;
        }
        private void OnDestroy() { if (_fixture == null) MailService.Changed -= Refresh; _cts?.Cancel(); _cts?.Dispose(); }
    }
}
