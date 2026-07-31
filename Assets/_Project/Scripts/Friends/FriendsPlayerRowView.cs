using System;
using Project.Achievements;
using Project.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Friends
{
    /// <summary>Строка списка друзей / онлайн.</summary>
    public sealed class FriendsPlayerRowView : MonoBehaviour
    {
        private Image _background;
        private TMP_Text _nameText;
        private TMP_Text _statusText;
        private Button _removeButton;
        private Button _acceptButton;
        private Button _actionButton;
        private Text _removeLabel;
        private Text _acceptLabel;
        private TMP_FontAsset _font;

        private string _userId;
        private string _username;
        private FriendRelationState _state;
        private Action<FriendsPlayerRowView> _onRemove;
        private Action<FriendsPlayerRowView> _onAccept;
        private Action<FriendsPlayerRowView> _onAction;

        public string UserId => _userId;
        public string Username => _username;
        public FriendRelationState State => _state;

        public static FriendsPlayerRowView Create(Transform parent, TMP_FontAsset font, bool showFriendControls)
        {
            var go = new GameObject("FriendsPlayerRow", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var row = go.AddComponent<FriendsPlayerRowView>();
            row._font = font;
            row.Build(showFriendControls);
            return row;
        }

        private void Build(bool showFriendControls)
        {
            _background = GetComponent<Image>();
            _background.sprite = ModalPanelCloseButton.WhiteSprite();
            _background.color = new Color(0.10f, 0.11f, 0.14f, 0.92f);
            _background.raycastTarget = false;

            var le = GetComponent<LayoutElement>();
            le.preferredHeight = 64f;
            le.minHeight = 64f;
            le.flexibleWidth = 1f;

            var hl = gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.padding = new RectOffset(14, 10, 8, 8);
            hl.spacing = 8f;
            hl.childAlignment = TextAnchor.MiddleLeft;
            hl.childControlHeight = true;
            hl.childControlWidth = true;
            hl.childForceExpandHeight = true;
            hl.childForceExpandWidth = false;

            var nameGo = new GameObject("Name", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            nameGo.transform.SetParent(transform, false);
            _nameText = nameGo.GetComponent<TextMeshProUGUI>();
            ApplyFont(_nameText, 26f, FontStyles.Bold);
            EnableAutoSize(_nameText, 18f, 72f);
            _nameText.alignment = TextAlignmentOptions.MidlineLeft;
            _nameText.color = Color.white;
            _nameText.raycastTarget = false;
            var nameLe = nameGo.GetComponent<LayoutElement>();
            nameLe.flexibleWidth = 1f;
            nameLe.minWidth = 120f;

            var statusGo = new GameObject("Status", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            statusGo.transform.SetParent(transform, false);
            _statusText = statusGo.GetComponent<TextMeshProUGUI>();
            ApplyFont(_statusText, 20f, FontStyles.Normal);
            EnableAutoSize(_statusText, 18f, 72f);
            _statusText.alignment = TextAlignmentOptions.MidlineRight;
            _statusText.raycastTarget = false;
            var statusLe = statusGo.GetComponent<LayoutElement>();
            statusLe.preferredWidth = 150f;
            statusLe.minWidth = 110f;

            if (showFriendControls)
            {
                // ✗ / ✓ — тот же LegacyRuntime, что StatusIcon в Workshop CreateButton.
                _removeButton = MakeSymbolButton("RemoveButton", "✗", new Color(0.55f, 0.22f, 0.22f, 1f), out _removeLabel);
                _removeButton.onClick.AddListener(() => _onRemove?.Invoke(this));

                _acceptButton = MakeSymbolButton("AcceptButton", "✓", new Color(0.22f, 0.52f, 0.28f, 1f), out _acceptLabel);
                _acceptButton.onClick.AddListener(() => _onAccept?.Invoke(this));

                _actionButton = MakeIconButton("ActionButton", "...", new Color(0.24f, 0.40f, 0.62f, 1f));
                _actionButton.onClick.AddListener(() => _onAction?.Invoke(this));
            }

            AchievementsTmpMaterialRepair.RepairHierarchy(transform, _font);
        }

        public void BindFriend(
            FriendListEntry entry,
            Action<FriendsPlayerRowView> onRemove,
            Action<FriendsPlayerRowView> onAccept,
            Action<FriendsPlayerRowView> onAction)
        {
            if (entry == null)
            {
                gameObject.SetActive(false);
                return;
            }

            gameObject.SetActive(true);
            _userId = entry.UserId;
            _username = entry.Username;
            _state = entry.State;
            _onRemove = onRemove;
            _onAccept = onAccept;
            _onAction = onAction;

            if (_nameText != null)
                _nameText.text = string.IsNullOrWhiteSpace(entry.Username) ? "-" : entry.Username;

            if (_statusText != null)
            {
                _statusText.text = FormatFriendStatus(entry);
                _statusText.color = entry.State == FriendRelationState.Mutual
                    ? (entry.Online
                        ? new Color(0.45f, 0.90f, 0.50f, 1f)
                        : new Color(0.62f, 0.64f, 0.68f, 1f))
                    : new Color(0.95f, 0.78f, 0.40f, 1f);
            }

            var incoming = entry.State == FriendRelationState.InviteReceived;
            if (_removeLabel != null)
                _removeLabel.text = incoming ? "✗" : "-";
            if (_acceptLabel != null)
                _acceptLabel.text = "✓";

            if (_removeButton != null)
                _removeButton.gameObject.SetActive(true);
            if (_acceptButton != null)
                _acceptButton.gameObject.SetActive(incoming);
            if (_actionButton != null)
                _actionButton.gameObject.SetActive(entry.State == FriendRelationState.Mutual);
        }

        public void BindOnline(OnlinePlayerEntry entry)
        {
            if (entry == null)
            {
                gameObject.SetActive(false);
                return;
            }

            gameObject.SetActive(true);
            _userId = entry.UserId;
            _username = entry.Username;
            _onRemove = null;
            _onAction = null;

            if (_nameText != null)
                _nameText.text = string.IsNullOrWhiteSpace(entry.Username) ? "-" : entry.Username;

            if (_statusText != null)
            {
                var level = entry.Level < 1 ? 1 : (entry.Level > 12 ? 12 : entry.Level);
                _statusText.text = level.ToString();
                _statusText.color = new Color(0.85f, 0.88f, 0.95f, 1f);
            }
        }

        private static string FormatFriendStatus(FriendListEntry entry)
        {
            return entry.State switch
            {
                FriendRelationState.InviteSent => "заявка...",
                FriendRelationState.InviteReceived => "входящая",
                _ => entry.Online ? "онлайн" : "оффлайн",
            };
        }

        private Button MakeIconButton(string name, string label, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(transform, false);
            var img = go.GetComponent<Image>();
            img.sprite = ModalPanelCloseButton.WhiteSprite();
            img.color = color;
            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            var le = go.GetComponent<LayoutElement>();
            le.preferredWidth = 48f;
            le.preferredHeight = 48f;
            le.minWidth = 48f;
            le.minHeight = 48f;

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(go.transform, false);
            var tmp = labelGo.GetComponent<TextMeshProUGUI>();
            ApplyFont(tmp, 30f, FontStyles.Bold);
            tmp.text = label;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.raycastTarget = false;
            var lrt = labelGo.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero;
            lrt.offsetMax = Vector2.zero;
            return btn;
        }

        /// <summary>Кнопка с символами ✓/✗ через LegacyRuntime.ttf — как StatusIcon в Workshop.</summary>
        private Button MakeSymbolButton(string name, string label, Color color, out Text labelText)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(transform, false);
            var img = go.GetComponent<Image>();
            img.sprite = ModalPanelCloseButton.WhiteSprite();
            img.color = color;
            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            var le = go.GetComponent<LayoutElement>();
            le.preferredWidth = 48f;
            le.preferredHeight = 48f;
            le.minWidth = 48f;
            le.minHeight = 48f;

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelGo.transform.SetParent(go.transform, false);
            labelText = labelGo.GetComponent<Text>();
            labelText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            labelText.fontSize = 28;
            labelText.fontStyle = FontStyle.Bold;
            labelText.text = label;
            labelText.alignment = TextAnchor.MiddleCenter;
            labelText.color = Color.white;
            labelText.raycastTarget = false;
            labelText.horizontalOverflow = HorizontalWrapMode.Overflow;
            labelText.verticalOverflow = VerticalWrapMode.Overflow;
            var lrt = labelGo.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero;
            lrt.offsetMax = Vector2.zero;
            return btn;
        }

        private void ApplyFont(TMP_Text tmp, float size, FontStyles style)
        {
            if (tmp == null) return;
            var fa = AchievementUiFontLoader.Resolve(_font);
            if (fa != null)
            {
                tmp.font = fa;
                if (fa.material != null)
                    tmp.fontSharedMaterial = fa.material;
            }

            tmp.fontSize = size;
            tmp.fontStyle = style;
            tmp.richText = false;
        }

        private static void EnableAutoSize(TMP_Text tmp, float min, float max)
        {
            if (tmp == null) return;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = min;
            tmp.fontSizeMax = max;
        }
    }
}
