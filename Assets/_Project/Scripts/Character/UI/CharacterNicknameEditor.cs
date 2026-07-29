using System;
using System.Threading;
using System.Threading.Tasks;
using Project.Character;
using Project.Utils;
using TMPro;
using UnityEngine;
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
        private bool _busy;
        private CancellationTokenSource _cts;

        public bool IsEditing => _modalRoot != null && _modalRoot.activeSelf;

        private void Awake()
        {
            _cts = new CancellationTokenSource();
            EnsureWired();
        }

        private void OnDestroy()
        {
            if (editButton != null) editButton.onClick.RemoveListener(OpenEditor);
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
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

            if (editButton != null)
            {
                editButton.onClick.RemoveListener(OpenEditor);
                editButton.onClick.AddListener(OpenEditor);
            }
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
            if (_modalRoot != null)
                _modalRoot.SetActive(false);
            _busy = false;
        }

        private void OpenEditor()
        {
            _ = OpenEditorAsync();
        }

        private async Task OpenEditorAsync()
        {
            if (_busy) return;
            await MainThreadDispatcher.RunAsync(() =>
            {
                EnsureWired();
                EnsureModal();
            }).ConfigureAwait(false);

            var ct = _cts != null ? _cts.Token : CancellationToken.None;

            _busy = true;
            NicknameStatusRpcResponse st = null;
            try
            {
                st = await NicknameService.GetStatusAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                _busy = false;
            }

            if (ct.IsCancellationRequested) return;

            await MainThreadDispatcher.RunAsync(() =>
            {
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
                }
            }).ConfigureAwait(false);
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
            if (_busy || _input == null) return;
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

            _busy = true;
            if (_statusText != null) _statusText.text = "Сохранение…";
            var ct = _cts != null ? _cts.Token : CancellationToken.None;
            NicknameChangeRpcResponse resp = null;
            try
            {
                resp = await NicknameService.ChangeAsync(next, ct).ConfigureAwait(false);
            }
            finally
            {
                _busy = false;
            }

            if (ct.IsCancellationRequested) return;

            await MainThreadDispatcher.RunAsync(() =>
            {
                if (resp == null || !resp.ok)
                {
                    if (_statusText != null)
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
                CloseEditor();
            }).ConfigureAwait(false);
        }

        private void EnsureModal()
        {
            if (_modalRoot != null) return;
            var parent = GetComponentInParent<Canvas>()?.transform ?? transform;

            _modalRoot = new GameObject("NicknameEditModal", typeof(RectTransform));
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

            var inputGo = new GameObject(name, typeof(RectTransform), typeof(TMP_InputField));
            var irt = inputGo.GetComponent<RectTransform>();
            irt.SetParent(slot.transform, false);
            Stretch(irt);

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            var tr = textGo.GetComponent<RectTransform>();
            tr.SetParent(irt, false);
            Stretch(tr);
            tr.offsetMin = new Vector2(12f, 6f);
            tr.offsetMax = new Vector2(-12f, -6f);
            var text = textGo.GetComponent<TextMeshProUGUI>();
            text.fontSize = 22;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.MidlineLeft;

            var phGo = new GameObject("Placeholder", typeof(RectTransform), typeof(TextMeshProUGUI));
            var phr = phGo.GetComponent<RectTransform>();
            phr.SetParent(irt, false);
            Stretch(phr);
            phr.offsetMin = new Vector2(12f, 6f);
            phr.offsetMax = new Vector2(-12f, -6f);
            var ph = phGo.GetComponent<TextMeshProUGUI>();
            ph.text = placeholder;
            ph.fontSize = 20;
            ph.fontStyle = FontStyles.Italic;
            ph.color = new Color(1f, 1f, 1f, 0.35f);
            ph.alignment = TextAlignmentOptions.MidlineLeft;

            var input = inputGo.GetComponent<TMP_InputField>();
            input.textViewport = irt;
            input.textComponent = text;
            input.placeholder = ph;
            input.characterLimit = 17;
            input.lineType = TMP_InputField.LineType.SingleLine;
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
