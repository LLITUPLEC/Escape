using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Character.UI
{
    public sealed class ItemSlotView : MonoBehaviour
    {
        [Tooltip("Индекс ячейки сундука 0..24. Если ≥0, порядок привязки к duel_character.inventory идёт по этому полю, а не по порядку детей.")]
        [SerializeField] private int inventorySlotIndex = -1;

        [Header("Wiring")]
        [SerializeField] private Image background;
        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text stackLabel;

        private void Awake()
        {
            EnsureStackLabel();
        }

        /// <summary>Создаёт подпись стека, если в префабе не назначена (иначе SetStackCount молчит).</summary>
        private void EnsureStackLabel()
        {
            if (stackLabel != null) return;
            var t = transform.Find("StackLabel");
            if (t != null) stackLabel = t.GetComponent<TMP_Text>();
            if (stackLabel != null) return;

            var tr = transform as RectTransform;
            var go = new GameObject("StackLabel", typeof(RectTransform), typeof(TextMeshProUGUI));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(tr, false);
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(1f, 0.4f);
            rt.offsetMin = new Vector2(2f, 2f);
            rt.offsetMax = new Vector2(-2f, -2f);
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.alignment = TextAlignmentOptions.BottomRight;
            tmp.fontSize = 18;
            tmp.color = Color.white;
            tmp.raycastTarget = false;
            stackLabel = tmp;
        }

        public void SetIcon(Sprite sprite)
        {
            if (icon == null) return;
            icon.sprite = sprite;
            icon.enabled = sprite != null;
        }

        /// <summary>Показывает число при стеке &gt; 1.</summary>
        public void SetStackCount(int amount)
        {
            EnsureStackLabel();
            if (stackLabel == null) return;
            if (amount <= 1)
            {
                stackLabel.gameObject.SetActive(false);
                return;
            }

            stackLabel.gameObject.SetActive(true);
            stackLabel.text = amount > 999 ? "999+" : amount.ToString();
        }

        /// <summary>Для drag/drop: чтобы события шли на фон ячейки, а не на иконку.</summary>
        public void SetIconRaycast(bool raycast)
        {
            if (icon != null) icon.raycastTarget = raycast;
        }

        public void SetBackground(Sprite sprite)
        {
            if (background == null) return;
            background.sprite = sprite;
            background.enabled = sprite != null;
        }

        public int InventorySlotIndex => inventorySlotIndex;
    }
}

