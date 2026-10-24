using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using Project.Character;
using Project.UI;
using Project.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Project.Character.UI
{
    /// <summary>
    /// NickName/name + edit_name: показ Username и модалка смены (1-я бесплатно / далее золото).
    /// </summary>
    public sealed class CharacterNicknameEditor : MonoBehaviour
    {
        [SerializeField] private Transform searchRoot;
        [SerializeField] private TMP_Text nameLabel;
        [SerializeField] private Button editButton;

        private NicknameStatusRpcResponse _status;
        private GameObject _modalRoot;
        private TMP_InputField _input;
        private TMP_Text _costText;
        private TMP_Text _statusText;
        private bool _opening;
        private bool _saving;
        private int _editorGeneration;
        private CancellationTokenSource _cts;
        private Coroutine _focusRoutine;

        public bool IsEditing => _modalRoot != null && _modalRoot.activeSelf;
        internal TMP_Text NameLabel => nameLabel;
        internal Button EditButton => editButton;

        private void Awake()
        {
            _cts = new CancellationTokenSource();
            EnsureWired();
        }

        private void OnDestroy()
        {
            CloseEditor();
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            if (_modalRoot != null) Destroy(_modalRoot);
        }

        public void EnsureWired()
        {
            if (searchRoot == null) searchRoot = transform;
            if (nameLabel == null)
            {
                var nameTr = FindDeep(searchRoot, "name");
                if (nameTr != null)
                    nameLabel = nameTr.GetComponent<TMP_Text>() ?? nameTr.GetComponentInChildren<TMP_Text>(true);
            }

            if (editButton == null)
            {
                var editTr = FindDeep(searchRoot, "edit_name");
                if (editTr != null)
                {
                    editButton = editTr.GetComponent<Button>();
                    if (editButton == null)
                    {
                        var img = editTr.GetComponent<Image>() ?? editTr.GetComponentInChildren<Image>(true);
                        if (img != null) img.raycastTarget = true;
                        editButton = editTr.gameObject.AddComponent<Button>();
                        if (img != null) editButton.targetGraphic = img;
                    }
                }
            }

            // The settings controller owns the rename entry point. Resolving the old
            // button here is only needed by the character layout that hides it.
        }

        public async Task RefreshAsync(CancellationToken ct)
        {
            await MainThreadDispatcher.RunAsync(EnsureWired).ConfigureAwait(false);
            try
            {
                var st = await NicknameService.GetStatusAsync(ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested) return;
                await MainThreadDispatcher.RunAsync(() =>
                {
                    _status = st;
                    if (st != null && st.ok && !string.IsNullOrWhiteSpace(st.username))
                        SetNameLabel(st.username);
                }).ConfigureAwait(false);
            }
            catch
            {
                // ignored
            }
        }

        public void SetNameLabel(string username)
        {
            if (nameLabel == null) return;
            nameLabel.text = string.IsNullOrWhiteSpace(username) ? "—" : username;
            // После смены текста с async-потока mesh TMP мог не пересобраться.
            nameLabel.ForceMeshUpdate(ignoreActiveState: true);
        }

        public void CloseEditor()
        {
            _editorGeneration++;
            if (_focusRoutine != null)
            {
                StopCoroutine(_focusRoutine);
                _focusRoutine = null;
            }
            if (_input != null) _input.DeactivateInputField();
            var events = EventSystem.current;
            if (events != null && events.currentSelectedGameObject != null && _modalRoot != null &&
                events.currentSelectedGameObject.transform.IsChildOf(_modalRoot.transform))
                events.SetSelectedGameObject(null);
            if (_modalRoot != null)
                _modalRoot.SetActive(false);
            _opening = false;
        }

        public async void OpenEditor(Transform modalParent = null)
        {
            if (IsEditing) return;
            try { await OpenEditorAsync(modalParent); }
            catch (OperationCanceledException) { }
            catch (Exception error) { Debug.LogException(error, this); }
        }

        private async Task OpenEditorAsync(Transform modalParent)
        {
            if (_opening || _saving) return;
            _opening = true;
            var generation = ++_editorGeneration;
            var ct = _cts != null ? _cts.Token : CancellationToken.None;
            NicknameStatusRpcResponse st = null;
            try
            {
                await MainThreadDispatcher.RunAsync(() =>
                {
                    if (this == null || ct.IsCancellationRequested || generation != _editorGeneration) return;
                    EnsureWired();
                    EnsureModal(modalParent);
                }).ConfigureAwait(false);
                if (ct.IsCancellationRequested || generation != _editorGeneration) return;
                st = await NicknameService.GetStatusAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }
            finally
            {
                if (generation == _editorGeneration) _opening = false;
            }

            if (ct.IsCancellationRequested || generation != _editorGeneration) return;

            await MainThreadDispatcher.RunAsync(() =>
            {
                if (this == null || ct.IsCancellationRequested || generation != _editorGeneration) return;
                _status = st;
                var current = _status != null && _status.ok && !string.IsNullOrWhiteSpace(_status.username)
                    ? _status.username
                    : (nameLabel != null ? nameLabel.text : "");
                if (_input != null) _input.text = current ?? "";
                UpdateCostHint();
                if (_statusText != null) _statusText.text = "";
                if (_modalRoot != null)
                {
                    _modalRoot.SetActive(true);
                    _modalRoot.transform.SetAsLastSibling();
                    if (_focusRoutine != null) StopCoroutine(_focusRoutine);
                    _focusRoutine = StartCoroutine(FocusInputAfterOpening(generation));
                }
            }).ConfigureAwait(false);
        }

        private IEnumerator FocusInputAfterOpening(int generation)
        {
            // Wait until the opening button has finished its pointer event and the
            // inactive TMP field has enabled its text renderer and selection caret.
            yield return null;
            _focusRoutine = null;
            if (generation != _editorGeneration || !IsEditing || _input == null) yield break;
            Canvas.ForceUpdateCanvases();
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(_input.gameObject);
            _input.Select();
            _input.ActivateInputField();
        }

        private void UpdateCostHint()
        {
            if (_costText == null) return;
            if (_status == null || !_status.ok)
            {
                _costText.text = "Загрузка…";
                return;
            }

            if (_status.free_change_available || _status.next_change_gold_cost <= 0)
                _costText.text = "Первая смена — бесплатно";
            else
                _costText.text = $"Стоимость: {_status.next_change_gold_cost} золота";
        }

        private async void OnSaveClicked()
        {
            if (_opening || _saving || _input == null) return;
            var next = (_input.text ?? "").Trim();
            var minLen = _status != null && _status.min_len > 0 ? _status.min_len : 3;
            var maxLen = _status != null && _status.max_len > 0 ? _status.max_len : 17;
            if (next.Length < minLen || next.Length > maxLen)
            {
                if (_statusText != null)
                    _statusText.text = $"Длина {minLen}–{maxLen} символов.";
                return;
            }

            var cost = _status != null && _status.ok ? Math.Max(0L, _status.next_change_gold_cost) : 0L;
            if (cost > 0)
            {
                // Простое подтверждение через status text + повторный Save не делаем —
                // сразу отправляем; сервер спишет золото.
            }

            _saving = true;
            var generation = _editorGeneration;
            if (_statusText != null) _statusText.text = "Сохранение…";
            var ct = _cts != null ? _cts.Token : CancellationToken.None;
            NicknameChangeRpcResponse resp = null;
            try
            {
                resp = await NicknameService.ChangeAsync(next, ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested) return;
                await MainThreadDispatcher.RunAsync(() =>
                {
                    if (this == null || ct.IsCancellationRequested) return;
                    if (resp == null || !resp.ok)
                    {
                        if (_statusText != null && generation == _editorGeneration)
                            _statusText.text = NicknameService.DescribeError(resp);
                        return;
                    }

                    SetNameLabel(resp.username);
                    _status = new NicknameStatusRpcResponse
                    {
                        ok = true,
                        username = resp.username,
                        nickname_changes = resp.nickname_changes,
                        next_change_gold_cost = resp.next_change_gold_cost,
                        free_change_available = false,
                        min_len = minLen,
                        max_len = maxLen,
                    };
                    if (generation == _editorGeneration) CloseEditor();
                }).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            finally
            {
                _saving = false;
            }
        }

        private void EnsureModal(Transform modalParent)
        {
            var ownerCanvas = GetComponentInParent<Canvas>();
            var parent = modalParent != null ? modalParent
                : ownerCanvas != null ? ownerCanvas.rootCanvas.transform : transform;
            if (_modalRoot != null)
            {
                if (_modalRoot.transform.parent != parent)
                {
                    _modalRoot.transform.SetParent(parent, false);
                    Stretch(_modalRoot.GetComponent<RectTransform>());
                }
                return;
            }

            _modalRoot = new GameObject("NicknameEditModal", typeof(RectTransform), typeof(CanvasGroup));
            // Configure TMP references before OnEnable creates the caret and caches
            // its viewport mask. The editor must not inherit a hidden hero page.
            _modalRoot.SetActive(false);
            var group = _modalRoot.GetComponent<CanvasGroup>();
            group.ignoreParentGroups = true;
            group.alpha = 1;
            group.interactable = group.blocksRaycasts = true;
            var rootRt = _modalRoot.GetComponent<RectTransform>();
            rootRt.SetParent(parent, false);
            Stretch(rootRt);

            var dimGo = new GameObject("Dim", typeof(RectTransform), typeof(Image), typeof(Button));
            var dimRt = dimGo.GetComponent<RectTransform>();
            dimRt.SetParent(rootRt, false);
            Stretch(dimRt);
            var dimImg = dimGo.GetComponent<Image>();
            dimImg.color = new Color(0f, 0f, 0f, 0.55f);
            dimImg.raycastTarget = true;
            dimGo.GetComponent<Button>().onClick.AddListener(CloseEditor);

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            var pr = panel.GetComponent<RectTransform>();
            pr.SetParent(rootRt, false);
            pr.anchorMin = new Vector2(0.5f, 0.5f);
            pr.anchorMax = new Vector2(0.5f, 0.5f);
            pr.sizeDelta = new Vector2(520f, 340f);
            panel.GetComponent<Image>().color = new Color(0.12f, 0.14f, 0.2f, 1f);
            panel.GetComponent<Image>().raycastTarget = true;
            var v = panel.GetComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(22, 22, 22, 22);
            v.spacing = 12f;
            v.childControlHeight = true;
            v.childControlWidth = true;
            v.childForceExpandWidth = true;

            NewTmp(panel.transform, "Title", "Смена никнейма", 28, FontStyles.Bold);
            _costText = NewTmp(panel.transform, "Cost", "", 18, FontStyles.Normal);
            _input = CreateTmpInput(panel.transform, "NickInput", "Новый ник (латиница, 3–17)");
            _statusText = NewTmp(panel.transform, "Status", "", 16, FontStyles.Normal);
            _statusText.color = new Color(1f, 0.85f, 0.7f);

            var row = new GameObject("Buttons", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(panel.transform, false);
            row.GetComponent<LayoutElement>().minHeight = 48f;
            var h = row.GetComponent<HorizontalLayoutGroup>();
            h.spacing = 16f;
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = true;

            var save = CreateBtn(row.transform, "Save", "Сохранить", new Color(0.25f, 0.55f, 0.35f, 1f));
            save.onClick.AddListener(OnSaveClicked);
            var cancel = CreateBtn(row.transform, "Cancel", "Отмена", new Color(0.32f, 0.28f, 0.3f, 1f));
            cancel.onClick.AddListener(CloseEditor);

            var canvas = _modalRoot.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 32766;
            _modalRoot.AddComponent<GraphicRaycaster>();
            ExpeditionMetaPanels.StylePopup(_modalRoot.transform);
            var inputSlot = _input.transform.parent as RectTransform;
            if (inputSlot != null)
            {
                var inputHeight = ExpeditionUi.Ensure<LayoutElement>(inputSlot.gameObject);
                inputHeight.minHeight = inputHeight.preferredHeight = 100;
                ExpeditionUi.StylePanel(inputSlot.GetComponent<Image>(), ExpeditionTheme.Teal);
            }
            var statusHeight = ExpeditionUi.Ensure<LayoutElement>(_statusText.gameObject);
            statusHeight.minHeight = statusHeight.preferredHeight = 90;
            ExpeditionUi.StyleButton(save, true, 34);
            ExpeditionUi.StyleButton(cancel, false, 34);
            foreach (var button in new[] { save, cancel })
                foreach (var label in button.GetComponentsInChildren<TMP_Text>(true))
                    ExpeditionUi.SetRect(label.rectTransform, .025f, .04f, .975f, .96f);
            _modalRoot.SetActive(false);
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static TMP_Text NewTmp(Transform parent, string name, string text, float size, FontStyles style)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().minHeight = size + 10f;
            var t = go.GetComponent<TextMeshProUGUI>();
            t.font = ExpeditionTheme.Font;
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
            t.alignment = TextAlignmentOptions.Center;
            t.color = new Color(0.94f, 0.92f, 0.86f);
            t.raycastTarget = false;
            return t;
        }

        private static TMP_InputField CreateTmpInput(Transform parent, string name, string placeholder)
        {
            var slot = new GameObject(name + "Slot", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            slot.transform.SetParent(parent, false);
            slot.GetComponent<LayoutElement>().minHeight = 48f;
            slot.GetComponent<Image>().color = new Color(0.08f, 0.09f, 0.12f, 1f);
            slot.GetComponent<Image>().raycastTarget = false;

            var inputGo = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
            var irt = inputGo.GetComponent<RectTransform>();
            irt.SetParent(slot.transform, false);
            Stretch(irt);

            var viewportGo = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
            var viewport = viewportGo.GetComponent<RectTransform>();
            viewport.SetParent(irt, false);
            Stretch(viewport);

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            var tr = textGo.GetComponent<RectTransform>();
            tr.SetParent(viewport, false);
            Stretch(tr);
            tr.offsetMin = new Vector2(12f, 6f);
            tr.offsetMax = new Vector2(-12f, -6f);
            var text = textGo.GetComponent<TextMeshProUGUI>();
            text.font = ExpeditionTheme.Font;
            text.fontSize = 34;
            text.color = ExpeditionTheme.Ivory;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.raycastTarget = false;

            var phGo = new GameObject("Placeholder", typeof(RectTransform), typeof(TextMeshProUGUI));
            var phr = phGo.GetComponent<RectTransform>();
            phr.SetParent(viewport, false);
            Stretch(phr);
            phr.offsetMin = new Vector2(12f, 6f);
            phr.offsetMax = new Vector2(-12f, -6f);
            var ph = phGo.GetComponent<TextMeshProUGUI>();
            ph.text = placeholder;
            ph.font = ExpeditionTheme.Font;
            ph.fontSize = 30;
            ph.fontStyle = FontStyles.Normal;
            ph.color = ExpeditionTheme.Muted;
            ph.alignment = TextAlignmentOptions.MidlineLeft;
            ph.raycastTarget = false;

            var input = inputGo.GetComponent<TMP_InputField>();
            var inputBackground = ExpeditionUi.Ensure<Image>(inputGo);
            ExpeditionUi.StylePanel(inputBackground, ExpeditionTheme.Teal);
            inputBackground.raycastTarget = true;
            input.targetGraphic = inputBackground;
            input.textViewport = viewport;
            input.textComponent = text;
            input.placeholder = ph;
            // TMP applies fontAsset immediately to textComponent and placeholder.
            input.fontAsset = ExpeditionTheme.Font;
            input.characterLimit = 17;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.contentType = TMP_InputField.ContentType.Standard;
            input.keyboardType = TouchScreenKeyboardType.ASCIICapable;
            input.readOnly = false;
            input.shouldHideSoftKeyboard = false;
            input.shouldActivateOnSelect = true;
            input.interactable = true;
            input.richText = false;
            return input;
        }

        private static Button CreateBtn(Transform parent, string name, string label, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().minHeight = 46f;
            go.GetComponent<Image>().color = color;
            var t = NewTmp(go.transform, "L", label, 20, FontStyles.Bold);
            t.raycastTarget = false;
            return go.GetComponent<Button>();
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t != null && string.Equals(t.name, name, StringComparison.OrdinalIgnoreCase))
                    return t;
            }
            return null;
        }
    }
}
