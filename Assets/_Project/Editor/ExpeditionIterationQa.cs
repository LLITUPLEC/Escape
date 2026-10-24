using System;
using System.Linq;
using Project.Character.UI;
using Project.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Project.Editor
{
    /// <summary>Presentation checks only: no rename submissions, tournament entries or inventory RPCs.</summary>
    public static class ExpeditionIterationQa
    {
        public static string Run(string target)
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Play required");
            switch (target)
            {
                case "navigation":
                    var labels = new[] { "Убежище", "Достижения", "Герой", "Арена", "Мастерская" };
                    for (var i = 0; i < labels.Length; i++)
                    {
                        var button = GameObject.Find("ExpeditionNav" + i)?.GetComponent<Button>();
                        if (button == null || button.GetComponentInChildren<TMP_Text>().text != labels[i]) throw new InvalidOperationException("Wrong navigation order");
                    }
                    if (GameObject.Find("ExpeditionOpenAchievements") != null || GameObject.Find("ExpeditionWorldMine") != null || GameObject.Find("ExpeditionOpenForge") != null || GameObject.Find("ExpeditionOpenArena") != null)
                        throw new InvalidOperationException("Duplicate shelter shortcut remains");
                    return "Navigation order and removal of duplicate shelter signs verified";
                case "header":
                    foreach (var name in new[] { "ExpeditionOpenSupplies", "ExpeditionOpenSettings" })
                    {
                        var button = GameObject.Find(name)?.GetComponent<Button>();
                        if (button == null || button.transform.parent.name != "ExpeditionResourceHeader") throw new InvalidOperationException("Header action misplaced");
                        var rect = button.transform as RectTransform;
                        if (rect.anchorMin.y <= 0 || rect.anchorMax.y >= 1) throw new InvalidOperationException("Header button outside frame");
                    }
                    foreach (var key in new[] { "energy", "gold", "ore", "matter" })
                    {
                        var source = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/img/resources_hud/" + key + ".png");
                        if (ExpeditionResourceIcons.Get(key) != source) throw new InvalidOperationException("Resource source mismatch: " + key);
                    }
                    return "Header actions inset; all four resource icons reference original assets";
                case "nickname-input":
                    var field = UnityEngine.Object.FindObjectsByType<TMP_InputField>(FindObjectsSortMode.None).Single(f => f.name == "NickInput");
                    // TMP reports the soft keyboard hidden on desktop platforms by design.
                    if (field.readOnly || !field.interactable || (Application.isMobilePlatform && field.shouldHideSoftKeyboard) || EventSystem.current == null) throw new InvalidOperationException("Nickname field cannot accept keyboard input");
                    Canvas.ForceUpdateCanvases();
                    var canvas = field.GetComponentInParent<Canvas>();
                    var point = RectTransformUtility.WorldToScreenPoint(canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, field.textViewport.TransformPoint(field.textViewport.rect.center));
                    var pointer = new PointerEventData(EventSystem.current) { position = point, button = PointerEventData.InputButton.Left };
                    var hits = new System.Collections.Generic.List<RaycastResult>();
                    EventSystem.current.RaycastAll(pointer, hits);
                    var handler = hits.Count == 0 ? null : ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject);
                    if (handler != field.gameObject) throw new InvalidOperationException("Nickname input raycast intercepted: " + (hits.Count > 0 ? hits[0].gameObject.name : "none"));
                    ExecuteEvents.Execute(handler, pointer, ExecuteEvents.pointerDownHandler);
                    ExecuteEvents.Execute(handler, pointer, ExecuteEvents.pointerUpHandler);
                    ExecuteEvents.Execute(handler, pointer, ExecuteEvents.pointerClickHandler);
                    field.Select(); field.ActivateInputField();
                    var previous = field.text;
                    field.text = "";
                    foreach (var letter in "QaProbe12") field.ProcessEvent(new Event { type = EventType.KeyDown, character = letter, keyCode = KeyCode.None });
                    if (field.text != "QaProbe12") throw new InvalidOperationException("Nickname input did not process text events");
                    field.ProcessEvent(new Event { type = EventType.KeyDown, keyCode = KeyCode.Backspace });
                    if (field.text != "QaProbe1") throw new InvalidOperationException("Nickname input cannot delete text");
                    field.text = previous;
                    return "Real input raycast, character insertion and Backspace passed; text restored, no save submitted";
                case "arena-modal":
                    var modal = GameObject.Find("ArenaBetModal");
                    if (modal == null || !modal.activeInHierarchy || modal.scene.name != "ArenaMenu") throw new InvalidOperationException("Stake modal is not visible in ArenaMenu");
                    var parentCanvas = modal.GetComponentInParent<Canvas>();
                    if (parentCanvas == null || parentCanvas.name != "ArenaSceneOverlays" || parentCanvas.sortingOrder < 250) throw new InvalidOperationException("Stake modal on wrong layer");
                    if (modal.GetComponentsInParent<CanvasGroup>().Any(g => g.alpha < .99f || !g.blocksRaycasts)) throw new InvalidOperationException("Hidden ancestor above stake modal");
                    return "Stake chooser is visible on its scene-owned overlay; no entry fee selected";
                case "arena-no-leak":
                    var stale = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Any(t => t.name == "ArenaBetModal" || t.name == "ArenaSceneOverlays");
                    if (stale) throw new InvalidOperationException("Arena overlay survived leaving the scene");
                    return "Arena stake UI destroyed with its scene; no stale overlay after navigation";
            }
            throw new ArgumentException("Unknown iteration check");
        }
    }
}
