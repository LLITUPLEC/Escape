using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Project.Nakama;
using Project.Match3;
using Project.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Project.Editor
{
    /// <summary>Production chrome writers/callbacks with temporary UI state only. Never starts Play or invokes RPC.</summary>
    public static class ExpeditionChromeQa
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static MainMenuController _menu;
        private static ServerAuraButtonView _view;
        private static ServerAuraGetRpcResponse _originalAura;
        private static GameObject _originalModal;
        private static bool _oldActive, _oldSpinning, _oldEnabled;
        private static long _oldEnds;
        private static float _oldAccum;
        private static Quaternion _oldRotation;
        private static string _oldTmpTimer, _oldLegacyTimer;
        private static GameObject _closeRoot;
        private static Button _closeButton;
        private static int _closeInstance, _closeCount, _closeFrame;
        private static Match3GameHUD _affixHud;
        private static Action _affixRestore;
        private static int _affixFrame;
        private static string _affixTitle, _affixBody;

        public static string RunHud()
        {
            RequirePlay();
            var header = UnityEngine.Object.FindFirstObjectByType<ExpeditionResourceHeader>();
            Check(header != null, "Actual expedition resource header is missing.");
            var fill = Read<Image>(header, "_xp");
            var name = Read<TMP_Text>(header, "_name"); var level = Read<TMP_Text>(header, "_level");
            Check(fill != null && name != null && level != null, "Wait for header construction.");
            var track = fill.transform.parent as RectTransform;
            var originalMin = fill.rectTransform.anchorMin; var originalMax = fill.rectTransform.anchorMax;
            var originalOffsetMin = fill.rectTransform.offsetMin; var originalOffsetMax = fill.rectTransform.offsetMax;
            var writer = typeof(ExpeditionResourceHeader).GetMethod("SetXpFraction", Private);
            Check(writer != null, "Root must route actual XP updates through SetXpFraction(float).");
            try
            {
                foreach (var fraction in new[] { 0f, .5f, 1f })
                {
                    writer.Invoke(header, new object[] { fraction });
                    fill.rectTransform.ForceUpdateRectTransforms(); fill.SetVerticesDirty();
                    Canvas.ForceUpdateCanvases(); fill.Rebuild(CanvasUpdate.PreRender);
                    var mesh = fill.canvasRenderer.GetMesh();
                    // RectMask2D may cull the zero-width image and retain its old mesh. A culled renderer draws nothing.
                    var drawn = fill.isActiveAndEnabled && !fill.canvasRenderer.cull && mesh != null ? mesh.bounds.size.x : 0;
                    Check(Mathf.Abs(drawn / Mathf.Max(1, track.rect.width) - fraction) < .035f,
                        "Actual XP drawing width wrong at " + fraction + "; drawn=" + drawn + ", rect=" + fill.rectTransform.rect.width
                        + ", track=" + track.rect.width + ", cull=" + fill.canvasRenderer.cull + ", mesh=" + (mesh != null ? mesh.bounds.size.x : 0));
                    if (drawn > .1f) CheckMeshInside(fill, track, 0);
                    Check(fill.rectTransform.anchorMin.x >= 0 && fill.rectTransform.anchorMax.x <= 1,
                        "XP writer leaves its track anchors.");
                }
                var root = name.transform.parent as RectTransform;
                CheckRectInside(name.rectTransform, root, 1); CheckRectInside(level.rectTransform, root, 1);
                var nameBox = BoundsIn(name.rectTransform, root); var levelBox = BoundsIn(level.rectTransform, root);
                var trackBox = BoundsIn(track, root);
                Check(nameBox.min.y > levelBox.max.y && levelBox.min.y > trackBox.max.y,
                    "Name/level/XP vertical bands overlap.");
                var avatar = root.Find("ExpeditionOpenHero") as RectTransform;
                Check(avatar == null || nameBox.min.x > BoundsIn(avatar, root).max.x,
                    "Name has no inner gap after the portrait.");
                name.ForceMeshUpdate(); level.ForceMeshUpdate();
                Check(name.textInfo.characterCount > 0 && level.textInfo.characterCount > 0, "Header text not drawn.");
                return "PASS: actual SetXpFraction writer draws0/50/100% inside track; name/level/XP bands and portrait gap are separate. Original fill restored; no profile/RPC/cache mutation.";
            }
            finally
            {
                writer.Invoke(header, new object[] { originalMax.x });
                fill.rectTransform.anchorMin = originalMin; fill.rectTransform.anchorMax = originalMax;
                fill.rectTransform.offsetMin = originalOffsetMin; fill.rectTransform.offsetMax = originalOffsetMax;
                fill.SetVerticesDirty(); Canvas.ForceUpdateCanvases(); fill.Rebuild(CanvasUpdate.PreRender);
            }
        }

        public static string ShowAura()
        {
            RequirePlay(); Check(_menu == null, "Restore the previous aura fixture first.");
            var menu = UnityEngine.Object.FindFirstObjectByType<MainMenuController>();
            Check(menu != null, "Open MainMenu first.");
            var view = Read<ServerAuraButtonView>(menu, "_serverAuraButtonView");
            var button = Read<Button>(menu, "_serverAuraButton");
            var cached = Read<ServerAuraGetRpcResponse>(menu, "_serverAuraLast");
            Check(view != null && button != null && cached != null && cached.ok, "Wait for the real cached aura response and source button.");
            Check(view.name == "aura_btn" && view.transform.parent != null && view.transform.parent.name == "ExpeditionScenePageHost",
                "Original aura_btn is not attached to the scene page host.");
            Check(button.transform.IsChildOf(view.transform) && button.name == "border", "Original aura border Button was replaced.");
            var originalModal = Read<GameObject>(menu, "_serverAuraModalRoot");
            Check(originalModal == null || !originalModal.activeSelf, "Close the real aura modal before running a fixture.");
            _menu = menu; _view = view; _originalAura = cached; _originalModal = originalModal;
            _oldActive = view.gameObject.activeSelf; _oldEnabled = view.enabled;
            _oldSpinning = Read<bool>(view, "_spinning"); _oldEnds = Read<long>(view, "_endsAtUnix");
            _oldAccum = Read<float>(view, "_timerAccum");
            var border = Read<RectTransform>(view, "border"); _oldRotation = border != null ? border.localRotation : Quaternion.identity;
            var tmp = Read<TMP_Text>(view, "timerTmp"); var legacy = Read<Text>(view, "timerText");
            _oldTmpTimer = tmp != null ? tmp.text : null; _oldLegacyTimer = legacy != null ? legacy.text : null;
            try
            {
                view.SetAnomalyActive(false); Check(!view.gameObject.activeSelf, "Inactive aura still visible.");
                var fixture = JsonUtility.FromJson<ServerAuraGetRpcResponse>(JsonUtility.ToJson(cached));
                fixture.active = true; fixture.endsAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 7200;
                // Preserve all cached effect values; only local view activity/deadline is simulated.
                Set(menu, "_serverAuraLast", fixture);
                view.SetAnomalyActive(true, fixture.endsAtUnix);
                Check(view.gameObject.activeInHierarchy && button.IsInteractable(), "Active original aura button is hidden/unreachable.");
                button.onClick.Invoke(); Canvas.ForceUpdateCanvases();
                Check(Read<GameObject>(menu, "_serverAuraModalRoot")?.activeInHierarchy == true,
                    "Original aura click callback did not open the actual cached-data modal.");
                return "SHOWN: original aura_btn/border in scene host, SetAnomalyActive false/true, real cached-data modal via original click callback; effects unchanged, only local activity/deadline simulated. VerifyAuraAndClose(), then RestoreAura(). No RPC/spend.";
            }
            catch { RestoreAura(); throw; }
        }

        public static string VerifyAuraAndClose()
        {
            RequirePlay(); Check(_menu != null && _view != null, "Call ShowAura() first.");
            var modal = Read<GameObject>(_menu, "_serverAuraModalRoot");
            Check(modal != null && modal.activeInHierarchy, "Actual aura modal is not open.");
            var cached = Read<ServerAuraGetRpcResponse>(_menu, "_serverAuraLast");
            Check(cached != null && cached.active && cached.endsAtUnix > DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                "Local aura view state was replaced before verification; restore and retry promptly.");
            var heading = Read<Text>(_menu, "_serverAuraModalHeaderText");
            Check(heading != null && !string.IsNullOrWhiteSpace(heading.text), "Cached aura modal has no real header.");
            if (!string.IsNullOrEmpty(cached.title)) Check(heading.text.Contains(cached.title), "Modal header does not reflect cached title.");
            var close = modal.transform.Find("Panel/CloseButton")?.GetComponent<Button>();
            Check(close != null, "Actual aura CloseButton missing.");
            Canvas.ForceUpdateCanvases(); Click(close);
            Check(!modal.activeSelf, "Original close callback did not hide aura modal.");
            return "PASS: original aura activity gate and cached-data modal/open/close callbacks work; actual close raycast reached original button. Fixture still needs RestoreAura(); no RPC or anomaly server write.";
        }

        public static string RestoreAura()
        {
            if (_menu != null)
            {
                var current = Read<GameObject>(_menu, "_serverAuraModalRoot");
                if (current != null) current.SetActive(false);
                Set(_menu, "_serverAuraLast", _originalAura);
                if (_originalModal == null && current != null)
                {
                    UnityEngine.Object.DestroyImmediate(current);
                    foreach (var name in new[] { "_serverAuraModalRoot", "_serverAuraModalHeaderText", "_serverAuraModalStatsRoot", "_serverAuraModalFooterText" }) Set(_menu, name, null);
                }
                else if (_originalModal != null)
                    typeof(MainMenuController).GetMethod("PopulateServerAuraModal", Private).Invoke(_menu, new object[] { _originalAura });
            }
            if (_view != null)
            {
                _view.SetAnomalyActive(_oldSpinning, _oldEnds); Set(_view, "_timerAccum", _oldAccum);
                _view.enabled = _oldEnabled; _view.gameObject.SetActive(_oldActive);
                var border = Read<RectTransform>(_view, "border"); if (border != null) border.localRotation = _oldRotation;
                var tmp = Read<TMP_Text>(_view, "timerTmp"); var legacy = Read<Text>(_view, "timerText");
                if (tmp != null) tmp.text = _oldTmpTimer; if (legacy != null) legacy.text = _oldLegacyTimer;
            }
            _menu = null; _view = null; _originalAura = null; _originalModal = null;
            return "RESTORED: original cached aura reference, button activity/rotation/timer and prior closed-modal state; no RPC/server mutation.";
        }

        public static string RunCloseIcon() => _closeRoot == null ? ShowCloseIcon() : VerifyCloseIcon();

        public static string ShowCloseIcon()
        {
            RequirePlay(); Check(_closeRoot == null, "Restore the previous close fixture first.");
            _closeRoot = new GameObject("ExpeditionCloseQaFixture", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            try
            {
                var canvas = _closeRoot.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.overrideSorting = true; canvas.sortingOrder = 32765;
                _closeButton = ExpeditionUi.Button(_closeRoot.transform, "OriginalCloseButton", "×", .44f, .44f, .56f, .56f, null);
                _closeInstance = _closeButton.GetInstanceID(); _closeCount = 0; _closeFrame = Time.frameCount;
                _closeButton.onClick.AddListener(() => _closeCount++);
                ExpeditionCloseButton.Apply(_closeButton); ExpeditionCloseButton.Apply(_closeButton); ExpeditionCloseButton.StyleWithin(_closeRoot.transform);
                Canvas.ForceUpdateCanvases();
                return "SHOWN: close fixture created atframe" + _closeFrame + ". VerifyCloseIcon() after an actual render frame, then RestoreCloseIcon(). No external callbacks.";
            }
            catch { RestoreCloseIcon(); throw; }
        }

        public static string VerifyCloseIcon()
        {
            RequirePlay(); Check(_closeRoot != null && _closeButton != null, "Call ShowCloseIcon first.");
            Check(Time.frameCount > _closeFrame, "Wait one actual rendered frame before close raycast verification.");
            Canvas.ForceUpdateCanvases();
            Check(ExpeditionCloseButton.IsIconClose(_closeButton) && _closeButton.GetInstanceID() == _closeInstance, "Close API replaced the original Button.");
            Check(_closeButton.GetComponentsInChildren<ExpeditionCloseGlyph>(true).Length == 1, "Repeated close styling duplicates the glyph.");
            CheckCloseAppearance(_closeButton); var before = _closeCount; Click(_closeButton); Click(_closeButton);
            Check(_closeCount == before + 2, "Close styling lost or duplicated existing callbacks."); CheckCloseAppearance(_closeButton);
            return "PASS: actual rendered close fixture, one glyph/CanvasRenderer, transparent hit and original Button; two real raycasts/pointer callbacks exactly twice. Call RestoreCloseIcon().";
        }

        public static string RestoreCloseIcon()
        {
            if (_closeRoot != null) UnityEngine.Object.DestroyImmediate(_closeRoot);
            _closeRoot = null; _closeButton = null; _closeCount = 0;
            return "RESTORED: local close fixture removed; no external callback or account action.";
        }

        public static string DiagnoseActiveClose()
        {
            RequirePlay(); Button button = null;
            foreach (var settings in UnityEngine.Object.FindObjectsByType<MainMenuSettingsModalController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var candidate = Read<Button>(settings, "closeButton");
                if (candidate != null && candidate.IsActive() && candidate.IsInteractable()) { button = candidate; break; }
            }
            Check(button != null, "Open the actual Settings modal; no active settings closeButton found.");
            var before = DescribeClose(button);
            try
            {
                ExpeditionCloseButton.Apply(button); Canvas.ForceUpdateCanvases();
                var glyph = button.GetComponentInChildren<ExpeditionCloseGlyph>(true);
                if (glyph != null) { glyph.SetVerticesDirty(); glyph.Rebuild(CanvasUpdate.PreRender); }
                CheckCloseAppearance(button); CheckRaycast(button);
                return "ACTUAL SETTINGS CLOSE BEFORE: " + before + " AFTER: " + DescribeClose(button)
                    + "; real point raycast passes. Modal left open; no close/account callback invoked.";
            }
            catch (Exception error) { throw new InvalidOperationException("ACTUAL SETTINGS CLOSE BEFORE: " + before + " AFTER: " + DescribeClose(button) + "; " + error.Message, error); }
        }

        public static string ShowAffix()
        {
            RequirePlay(); Check(_affixRestore == null, "Restore the previous affix fixture first.");
            var manager = UnityEngine.Object.FindFirstObjectByType<DuelMatch3Manager>();
            var hud = UnityEngine.Object.FindFirstObjectByType<Match3GameHUD>();
            Check(manager != null && hud != null && Read<object>(manager, "_match") == null
                && !Read<bool>(manager, "_hasInitialBoardSync"), "Only a pre-match local board; never fixture a live match.");
            Check(hud.affixButton != null && hud.affixButtonIconImage != null && hud.affixTooltipRoot != null,
                "Wait for actual affix UI construction.");
            Check(!hud.affixTooltipRoot.gameObject.activeSelf, "Close the real tooltip before this fixture.");
            var title = Read<TMP_Text>(hud, "_affixTooltipTitle");
            var oldTitle = title != null ? title.text : null; var oldBody = hud.affixTooltipText.text;
            var oldLabel = hud.affixButtonLabel.text; var labelActive = hud.affixButtonLabel.gameObject.activeSelf;
            var active = hud.affixButton.gameObject.activeSelf; var target = hud.affixButton.targetGraphic;
            var oldIcon = hud.affixButtonIconImage.sprite; var oldColor = hud.affixButtonIconImage.color;
            var iconEnabled = hud.affixButtonIconImage.enabled; var managerEnabled = manager.enabled;
            var tip = hud.affixTooltipRoot; var parent = tip.parent; var sibling = tip.GetSiblingIndex();
            var aMin = tip.anchorMin; var aMax = tip.anchorMax; var offsetMin = tip.offsetMin; var offsetMax = tip.offsetMax;
            var pivot = tip.pivot;
            _affixHud = hud;
            _affixRestore = () =>
            {
                if (hud != null)
                {
                    hud.affixTooltipRoot.gameObject.SetActive(false);
                    hud.affixTooltipText.text = oldBody;
                    if (title != null) title.text = oldTitle;
                    hud.affixButtonLabel.text = oldLabel; hud.affixButtonLabel.gameObject.SetActive(labelActive);
                    hud.affixButtonIconImage.sprite = oldIcon; hud.affixButtonIconImage.color = oldColor;
                    hud.affixButtonIconImage.enabled = iconEnabled; hud.affixButton.targetGraphic = target;
                    hud.affixButton.gameObject.SetActive(active);
                    tip.SetParent(parent, false); tip.pivot = pivot; tip.anchorMin = aMin; tip.anchorMax = aMax;
                    tip.offsetMin = offsetMin; tip.offsetMax = offsetMax; tip.SetSiblingIndex(sibling);
                }
                if (manager != null) manager.enabled = managerEnabled;
            };
            try
            {
                manager.enabled = false;
                var catalog = Read<AffixCatalog>(manager, "affixCatalog"); Sprite icon = null;
                _affixTitle = "UI-аффикс"; _affixBody = "Локальная проверка подсказки без применения эффекта.";
                if (catalog != null) catalog.TryGet("acid", out _affixTitle, out _affixBody, out icon);
                if (string.IsNullOrWhiteSpace(_affixTitle)) _affixTitle = "UI-аффикс";
                if (string.IsNullOrWhiteSpace(_affixBody)) _affixBody = "Локальная проверка подсказки без применения эффекта.";
                if (icon == null) icon = ExpeditionTheme.Icon("shield");
                Check(icon != null, "Real catalog/Resources icon missing; no fabricated texture supplied.");
                hud.SetAffixInfo("?", _affixTitle + ": " + _affixBody, icon);
                _affixFrame = Time.frameCount; Canvas.ForceUpdateCanvases();
                return "SHOWN: actual SetAffixInfo on pre-match HUD, real catalog/Resources icon, local text only. After a rendered frame call OpenAffix(), then after another frame VerifyAffixAndClose(), then RestoreAffix(). No match/ability/RPC.";
            }
            catch { RestoreAffix(); throw; }
        }

        public static string OpenAffix()
        {
            RequirePlay(); Check(_affixHud != null && Time.frameCount > _affixFrame, "ShowAffix then wait for a rendered frame.");
            var button = _affixHud.affixButton; var hit = button.GetComponent<Image>();
            Check(hit != null && hit.raycastTarget && hit.color.a <= .01f,
                "Affix root hit still draws an opaque frame: " + DescribeClose(button));
            Check(button.targetGraphic == _affixHud.affixButtonIconImage && _affixHud.affixButtonIconImage.enabled
                && _affixHud.affixButtonIconImage.sprite != null && !_affixHud.affixButtonIconImage.raycastTarget,
                "Affix tint/hit does not target its original real icon.");
            Click(button);
            Check(_affixHud.affixTooltipRoot.gameObject.activeSelf, "Actual affix click failed to open real tooltip.");
            _affixFrame = Time.frameCount;
            return "OPENED: icon-only affix hit/target + actual raycast callback passed. Wait one rendered frame, then VerifyAffixAndClose().";
        }

        public static string VerifyAffixAndClose()
        {
            RequirePlay(); Check(_affixHud != null && Time.frameCount > _affixFrame, "OpenAffix then wait for a rendered frame.");
            Check(_affixHud.affixTooltipRoot.gameObject.activeInHierarchy
                && _affixHud.affixTooltipText.text.Contains(_affixBody), "Real tooltip is hidden or ignores SetAffixInfo body.");
            var title = Read<TMP_Text>(_affixHud, "_affixTooltipTitle");
            Check(title != null && title.text == _affixTitle, "Real tooltip title does not match source parameters.");
            var close = _affixHud.affixTooltipCloseButton;
            Check(close != null && ExpeditionCloseButton.IsIconClose(close), "Actual tooltip X is not common icon-close.");
            CheckCloseAppearance(close); Click(close);
            Check(!_affixHud.affixTooltipRoot.gameObject.activeSelf, "Actual tooltip X callback did not close it.");
            return "PASS: real pre-match SetAffixInfo keeps root hit transparent and targets the original icon; actual pointer opens cached/local tooltip and its actual icon-X raycast closes it. RestoreAffix() required; no gameplay/RPC.";
        }

        public static string RestoreAffix()
        {
            try { _affixRestore?.Invoke(); }
            finally { _affixRestore = null; _affixHud = null; _affixTitle = null; _affixBody = null; Canvas.ForceUpdateCanvases(); }
            return "RESTORED: original affix text/icon/activity/tooltip rectangle and manager enable state; no gameplay or account mutation.";
        }

        private static string DescribeClose(Button button)
        {
            var result = new StringBuilder();
            result.Append(Path(button.transform)).Append(" marker=").Append(button.GetComponent<ExpeditionCloseButton>() != null)
                .Append(" IsIconClose=").Append(ExpeditionCloseButton.IsIconClose(button))
                .Append(" target=").Append(button.targetGraphic != null ? Path(button.targetGraphic.transform) + ":" + button.targetGraphic.GetType().Name : "null");
            foreach (var graphic in button.GetComponentsInChildren<Graphic>(true))
            {
                result.Append(" | ").Append(Path(graphic.transform)).Append(":").Append(graphic.GetType().Name)
                    .Append(" active=").Append(graphic.gameObject.activeInHierarchy).Append(" enabled=").Append(graphic.enabled)
                    .Append(" raycast=").Append(graphic.raycastTarget).Append(" rgba=").Append(graphic.color)
                    .Append(" CR=").Append(graphic.GetComponent<CanvasRenderer>() != null)
                    .Append(" depth=").Append(graphic.canvasRenderer != null ? graphic.canvasRenderer.absoluteDepth : -1)
                    .Append(" enclosingButton=").Append(graphic.GetComponentInParent<Button>() == button ? "this" : "other");
                if (graphic is Image image) result.Append(" sprite=").Append(image.sprite != null ? image.sprite.name : "null").Append(" override=").Append(image.overrideSprite != null ? image.overrideSprite.name : "null");
                var text = graphic is Text legacy ? legacy.text : graphic is TMP_Text tmp ? tmp.text : null;
                if (text != null)
                {
                    result.Append(" text=[").Append(text).Append("] unicode=");
                    foreach (var c in text) result.Append("U+").Append(((int)c).ToString("X4")).Append(' ');
                }
            }
            return result.ToString();
        }

        private static void CheckCloseAppearance(Button button)
        {
            var hit = button.GetComponent<Image>(); var glyph = button.GetComponentInChildren<ExpeditionCloseGlyph>(true);
            Check(hit != null && hit.enabled && hit.raycastTarget && hit.color.a <= .01f,
                "Close root hit is missing, opaque or not raycastable.");
            Check(glyph != null && glyph.enabled && !glyph.raycastTarget && button.targetGraphic == glyph,
                "Close tint/hit targets are not separated.");
            var mesh = glyph.canvasRenderer.GetMesh(); Check(mesh != null && mesh.vertexCount > 0, "Close glyph has no actual mesh.");
        }

        private static void Click(Button button)
        {
            var data = CheckRaycast(button);
            ExecuteEvents.Execute(button.gameObject, data, ExecuteEvents.pointerClickHandler);
        }

        private static PointerEventData CheckRaycast(Button button)
        {
            Check(EventSystem.current != null && button.IsActive() && button.IsInteractable(), "Actual active button/EventSystem required.");
            var canvas = button.GetComponentInParent<Canvas>();
            var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            var point = RectTransformUtility.WorldToScreenPoint(camera, (button.transform as RectTransform).TransformPoint((button.transform as RectTransform).rect.center));
            var data = new PointerEventData(EventSystem.current) { position = point, button = PointerEventData.InputButton.Left };
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(data, hits);
            var top = hits.Count > 0 ? hits[0] : default;
            Check(hits.Count > 0 && top.gameObject.GetComponentInParent<Button>() == button,
                "Close point " + point + " intercepted: top=" + (hits.Count > 0 ? Path(top.gameObject.transform) : "none")
                + ", layer=" + top.sortingLayer + ", order=" + top.sortingOrder + ", depth=" + top.depth
                + "; expected=" + Path(button.transform) + ", expectedDepth=" + (button.targetGraphic != null ? button.targetGraphic.canvasRenderer.absoluteDepth : -1));
            return data;
        }
        private static string Path(Transform value)
        {
            var path = value.name; while (value.parent != null) { value = value.parent; path = value.name + "/" + path; } return path;
        }

        private static Bounds BoundsIn(RectTransform child, RectTransform parent) => RectTransformUtility.CalculateRelativeRectTransformBounds(parent, child);
        private static void CheckRectInside(RectTransform child, RectTransform parent, float inset)
        {
            var b = BoundsIn(child, parent); var r = parent.rect;
            Check(b.min.x >= r.xMin + inset && b.max.x <= r.xMax - inset && b.min.y >= r.yMin + inset && b.max.y <= r.yMax - inset,
                "Header label lacks an inner margin: " + child.name);
        }
        private static void CheckMeshInside(Graphic graphic, RectTransform parent, float inset)
        {
            var mesh = graphic.canvasRenderer.GetMesh(); var bounds = mesh.bounds;
            foreach (var x in new[] { bounds.min.x, bounds.max.x }) foreach (var y in new[] { bounds.min.y, bounds.max.y })
            {
                var p = parent.InverseTransformPoint(graphic.transform.TransformPoint(new Vector3(x, y, 0))); var r = parent.rect;
                Check(p.x >= r.xMin + inset - .5f && p.x <= r.xMax - inset + .5f && p.y >= r.yMin + inset - .5f && p.y <= r.yMax - inset + .5f,
                    "XP rendered mesh escapes track bounds.");
            }
        }
        private static T Read<T>(object value, string field) => (T)value.GetType().GetField(field, Private).GetValue(value);
        private static void Set(object value, string field, object data) => value.GetType().GetField(field, Private).SetValue(value, data);
        private static void RequirePlay() => Check(Application.isPlaying, "Play Mode required; helper never starts it.");
        private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
