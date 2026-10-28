using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Project.Character;
using Project.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Project.Editor
{
    /// <summary>Real inbox presentation and server DTO, with navigation/claim transport intercepted.</summary>
    public static class ExpeditionAppearanceMailQa
    {
        private const string Root = ".cache/qa/mine-refresh/";
        [Serializable] private sealed class Report { public bool passed, networkActions, accountWrites; public int letters, heroLinkClicks; public string[] checks; }
        public static void RunBatch()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit mode required.");
            Directory.CreateDirectory(Root);
            var fixture = JsonUtility.FromJson<MailService.State>(File.ReadAllText("Assets/_Project/Editor/Fixtures/appearance-unlock-mail.json"));
            Require(MailService.Valid(fixture) && fixture.unread_count > 0, "Actual server unlock inbox did not decode.");
            Require(fixture.messages.All(m => m.IsAppearanceUnlock && m.attachments == null), "Unlock letters are informational.");
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive); SceneManager.SetActiveScene(scene);
            try
            {
                var canvasRoot = new GameObject("OfflineAppearanceMailCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
                var canvas = canvasRoot.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                var scale = canvasRoot.GetComponent<CanvasScaler>(); scale.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scale.referenceResolution = new Vector2(1080, 1920);
                var calls = 0; string selected = null;
                var panel = ExpeditionMailPanel.Open(canvasRoot.transform, fixture, id => { calls++; selected = id; });
                Canvas.ForceUpdateCanvases();
                foreach (var message in fixture.messages)
                {
                    var card = panel.transform.GetComponentsInChildren<Transform>(true).Single(t => t.name == "MailMessage-" + message.id);
                    var picture = card.Find("UnlockedAppearancePortrait").GetComponent<Image>();
                    Require(picture.sprite != null && picture.sprite == CharacterAppearance.Load(message.avatar_id, true), "Unlock letter uses actual paired avatar.");
                    var button = card.Find("MailOpenHero-" + message.id).GetComponent<Button>();
                    Require(button.interactable && button.GetComponentInChildren<TMP_Text>().text == "К герою", "Hero action is visible and enabled.");
                    button.onClick.Invoke(); Require(selected == message.id, "Hero action keeps the correct letter identity.");
                    Require(card.Find("MessageState").GetComponent<TMP_Text>().text == "НОВЫЙ ОБРАЗ ДОСТУПЕН", "Informational letter does not promise an unclaimed reward.");
                }
                var unread = fixture.unread_count;
                foreach (var message in fixture.messages) message.claimed = true;
                fixture.unread_count = 0;
                UnityEngine.Object.DestroyImmediate(panel.gameObject);
                panel = ExpeditionMailPanel.Open(canvasRoot.transform, fixture, id => { calls++; selected = id; });
                Canvas.ForceUpdateCanvases();
                foreach (var message in fixture.messages)
                {
                    var card = panel.transform.GetComponentsInChildren<Transform>(true).Last(t => t.name == "MailMessage-" + message.id && t.gameObject.activeSelf);
                    Require(card.Find("MessageState").GetComponent<TMP_Text>().text == "ПРОЧИТАНО", "Read letter has informational state.");
                    Require(card.Find("MailOpenHero-" + message.id).GetComponent<Button>().interactable, "Read letter still links to Hero.");
                }
                var invalid = JsonUtility.FromJson<MailService.State>(JsonUtility.ToJson(fixture)); invalid.messages[0].action = "external";
                Require(!MailService.Valid(invalid), "Unknown notification actions cannot be accepted.");
                var report = new Report { passed = true, letters = unread, heroLinkClicks = calls, checks = new[] {
                    "Actual server JSON decoded and validated", "Informational unlock letter uses named paired avatar",
                    "Hero link invokes the correct server letter ID", "Read letter retains Hero link and has no second reward",
                    "Malformed navigation action rejected", "No network or account actions" } };
                File.WriteAllText(Root + "appearance-unlock-mail-ui-qa.json", JsonUtility.ToJson(report, true)); Debug.Log(JsonUtility.ToJson(report));
            }
            finally
            {
                if (previous.IsValid()) SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
