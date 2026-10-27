using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Project.Match3;
using Project.Nakama;
using Project.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Project.Editor
{
    /// <summary>Native inbox/cards/badge and claim callback; no real inbox request or reward mutation.</summary>
    public static class ExpeditionMailQa0313
    {
        private const string Output = ".cache/qa/legacy-0.3.13/mail";
        private const BindingFlags Hidden = BindingFlags.Static | BindingFlags.NonPublic;
        private static readonly string[] Fields = { "_scope", "_state", "_busy", "_error", "_status", "_nextRefresh" };
        private static readonly Dictionary<FieldInfo, object> Saved = new();
        private static GameObject _host;
        private static ExpeditionMailPanel _panel;
        private static ExpeditionMailBadge _badge;
        private static MailService.State _state;
        private static string _stage;
        private static int _claims;
        [Serializable] private sealed class Report { public bool passed, networkOrAccountActions, preferenceWrites; public int assertions, labels, icons, claimIntents, unread; public string stage, error; }
        public static string Run(string target)
        {
            if (target == "logic") return Logic();
            if (target == "close") { Close(); return "Offline mail closed and original scoped cache restored."; }
            Require(EditorApplication.isPlaying && SceneManager.GetActiveScene().name.StartsWith("NotificationStakeQaFixture", StringComparison.Ordinal), "Use only the isolated notification/stake Play fixture.");
            Require(UnityEngine.Object.FindFirstObjectByType<NakamaBootstrap>(FindObjectsInactive.Include) == null && !UnityEngine.Object.FindFirstObjectByType<DuelMatch3Manager>().enabled, "Mail QA requires no account or active battle host.");
            if (target == "verify") return Verify();
            if (target.StartsWith("open-", StringComparison.Ordinal)) { Open(target.Substring(5)); return "Offline mail " + _stage + "; wait one rendered frame, verify and capture."; }
            Require(_panel != null, "Open mail first.");
            if (target == "bottom" || target == "top") { var scroll = _panel.GetComponentInChildren<ScrollRect>(); scroll.StopMovement(); scroll.verticalNormalizedPosition = target == "top" ? 1 : 0; _stage += "-" + target; return "Mail scroll updated."; }
            if (target == "claim")
            {
                var before = _claims; var button = _panel.GetComponentsInChildren<Button>().First(b => b.name.StartsWith("MailClaim-", StringComparison.Ordinal) && b.interactable);
                button.OnPointerClick(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left });
                Require(_claims == before + 1 && _state.unread_count == 2, "Offline claim handler changed the entitlement or did not emit exactly one intent."); _stage = "claim-intent"; return "One offline message claim intention; no RPC or reward mutation.";
            }
            throw new ArgumentException("Use logic, open-inbox/empty/claimed, top, bottom, claim, verify or close.");
        }
        private static MailService.State Fixture(string view)
        {
            if (view == "empty") return new MailService.State { ok = true, unread_count = 0 };
            var message = new MailService.Message { id = "global_boss:week-1792972800", title = "Победа над Сердцем Разлома", body = "Ваш вклад помог победить глобального босса. Заберите заслуженные награды.", created_at = 1793100000,
                attachments = new[] {
                    new MailService.Attachment { resource = "gold", amount = 1500 }, new MailService.Attachment { resource = "ore", amount = 2000 },
                    new MailService.Attachment { def_id = "recipe_drop_t1_gold_Helmet", amount = 1 }, new MailService.Attachment { def_id = "tesseract", amount = 1 },
                    new MailService.Attachment { def_id = "elixir_assault", amount = 1 }, new MailService.Attachment { def_id = "scroll_pve_bounty", amount = 1 },
                    new MailService.Attachment { resource = "matter", amount = 5 } } };
            if (view == "claimed") { message.claimed = true; return new MailService.State { ok = true, unread_count = 0, messages = new[] { message } }; }
            return new MailService.State { ok = true, unread_count = 2, messages = new[] { message,
                new MailService.Message { id = "server:gift-0313", title = "Припасы для экспедиции", body = "Дополнительный телепорт и свиток уже ждут вас во вложениях.", created_at = 1793090000,
                    attachments = new[] { new MailService.Attachment { def_id = "global_boss_teleport", amount = 1 }, new MailService.Attachment { def_id = "scroll_pve_bounty", amount = 1 } } } } };
        }
        private static string Logic()
        {
            var report = new Report { stage = "logic" };
            try
            {
                Check(report, MailService.Valid(Fixture("empty")), "Empty omitted message array is invalid.");
                var state = Fixture("inbox"); Check(report, MailService.Valid(state) && state.messages[0].attachments.Length == 7, "Actual uniform attachment schema is invalid.");
                state.unread_count = 1; Check(report, !MailService.Valid(state), "Mismatched unread counter accepted.");
                state = Fixture("inbox"); state.messages[0].attachments[0].resource = "xp"; Check(report, !MailService.Valid(state), "Unknown wallet reward accepted.");
                state = Fixture("inbox"); state.messages[0].attachments[0].def_id = "tesseract"; Check(report, !MailService.Valid(state), "Mixed item/resource attachment accepted.");
                Save();
                typeof(MailService).GetField("_scope", Hidden).SetValue(null, "foreign-qa-account"); typeof(MailService).GetField("_state", Hidden).SetValue(null, Fixture("inbox"));
                Check(report, MailService.Current == null && MailService.UnreadCount == 0, "Foreign account inbox leaked across scope change.");
                Restore(); report.passed = true;
            }
            catch (Exception error) { report.error = error.ToString(); Restore(); }
            Write(report); if (!report.passed) throw new InvalidOperationException(report.error); return JsonUtility.ToJson(report);
        }
        private static void Open(string view)
        {
            Require(view == "inbox" || view == "empty" || view == "claimed", "Unknown mail fixture."); Close(); Save();
            _state = Fixture(view); _stage = view; _claims = 0;
            typeof(MailService).GetField("_scope", Hidden).SetValue(null, LocalAccountCacheScope.Current); typeof(MailService).GetField("_state", Hidden).SetValue(null, _state);
            _host = new GameObject("OfflineMailQaCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = _host.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 11000;
            var scaler = _host.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1080, 1920); scaler.matchWidthOrHeight = .5f;
            _panel = ExpeditionMailPanel.Open(_host.transform, _state, id => { Require(_state.messages.Any(m => m.id == id && !m.claimed), "Claimed/missing letter emitted a claim."); _claims++; });
            var owner = ExpeditionUi.Button(_host.transform, "MailBadgeOwner", "Почта", .02f, .005f, .23f, .058f, null, false, 26); _badge = ExpeditionMailBadge.Bind(owner);
        }
        private static string Verify()
        {
            var report = new Report { stage = _stage, claimIntents = _claims, unread = _state?.unread_count ?? 0 };
            try
            {
                Require(_panel != null, "Open mail first."); Canvas.ForceUpdateCanvases(); var canvas = _host.GetComponent<Canvas>();
                Check(report, canvas.pixelRect.width == 1080 && canvas.pixelRect.height == 1920, "Use native1080x1920 Game view.");
                foreach (var text in _host.GetComponentsInChildren<TMP_Text>()) { if (string.IsNullOrEmpty(text.text)) continue; text.ForceMeshUpdate(); report.labels++; Check(report, !text.isTextOverflowing, "Mail text overflow: " + text.name + " / " + text.text + " / " + text.rectTransform.rect); }
                foreach (var image in _panel.GetComponentsInChildren<Image>()) if (image.name == "AttachmentIcon") { report.icons++; Check(report, image.sprite != null, "Mail attachment image missing: " + image.transform.parent.name); }
                foreach (var button in _host.GetComponentsInChildren<Button>()) { var rect = (RectTransform)button.transform; Check(report, rect.rect.width * canvas.scaleFactor >= 44 && rect.rect.height * canvas.scaleFactor >= 44, "Mail touch target below44px: " + button.name); }
                var group = _badge.transform.Find("ExpeditionMailBadge").GetComponent<CanvasGroup>(); Check(report, (group.alpha > 0) == (_state.unread_count > 0), "Unread badge visibility is wrong.");
                if (_stage.StartsWith("claimed", StringComparison.Ordinal)) Check(report, _panel.GetComponentsInChildren<Button>().Where(b => b.name.StartsWith("MailClaim-", StringComparison.Ordinal)).All(b => !b.interactable), "A claimed letter still permits a claim.");
                Check(report, UnityEngine.Object.FindFirstObjectByType<NakamaBootstrap>(FindObjectsInactive.Include) == null, "Inbox preview created a network bootstrap."); report.passed = true;
            }
            catch (Exception error) { report.error = error.ToString(); }
            Write(report); if (!report.passed) throw new InvalidOperationException(report.error); return JsonUtility.ToJson(report);
        }
        private static void Save() { if (Saved.Count > 0) return; foreach (var name in Fields) { var field = typeof(MailService).GetField(name, Hidden); Saved[field] = field.GetValue(null); } }
        private static void Restore() { foreach (var pair in Saved) pair.Key.SetValue(null, pair.Value); Saved.Clear(); }
        private static void Close() { if (_host != null) { _host.SetActive(false); UnityEngine.Object.Destroy(_host); } _host = null; _panel = null; _badge = null; Restore(); }
        private static void Check(Report report, bool condition, string message) { report.assertions++; Require(condition, message); }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static void Write(Report report) { Directory.CreateDirectory(Output); File.WriteAllText(Path.Combine(Output, "ui-" + report.stage + ".json"), JsonUtility.ToJson(report, true)); }
    }
}
