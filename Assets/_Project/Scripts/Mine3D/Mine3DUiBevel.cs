using UnityEngine;
using UnityEngine.UI;

namespace Project.Mine3D
{
    /// <summary>
    /// Выпуклые (bevel) кнопки для панели сложности — чистый uGUI, без отдельных мешей.
    /// </summary>
    public static class Mine3DUiBevel
    {
        public static Button CreateBevelTab(Transform parent, string id, string label, Color faceColor)
        {
            var root = new GameObject("Diff_" + id, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            root.transform.SetParent(parent, false);
            var rootImg = root.GetComponent<Image>();
            rootImg.color = new Color(0.08f, 0.07f, 0.06f, 1f);
            rootImg.raycastTarget = true;

            var le = root.GetComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            le.minHeight = 84f;
            le.preferredHeight = 92f;

            // Нижняя «толщина» кнопки (тень корпуса).
            CreateLayer(root.transform, "Depth", new Color(0.04f, 0.035f, 0.03f, 1f),
                new Vector2(0.04f, 0.02f), new Vector2(0.96f, 0.78f));

            // Боковой скос.
            CreateLayer(root.transform, "BevelLeft", new Color(1f, 1f, 1f, 0.14f),
                new Vector2(0.05f, 0.18f), new Vector2(0.12f, 0.88f));
            CreateLayer(root.transform, "BevelRight", new Color(0f, 0f, 0f, 0.35f),
                new Vector2(0.88f, 0.18f), new Vector2(0.95f, 0.88f));

            // Верхний блик / нижняя тень — ощущение выпуклости.
            CreateLayer(root.transform, "BevelTop", Lighten(faceColor, 0.35f),
                new Vector2(0.08f, 0.72f), new Vector2(0.92f, 0.90f));
            CreateLayer(root.transform, "BevelBottom", Darken(faceColor, 0.45f),
                new Vector2(0.08f, 0.14f), new Vector2(0.92f, 0.32f));

            // Лицевая площадка.
            var face = CreateLayer(root.transform, "Face", faceColor,
                new Vector2(0.10f, 0.28f), new Vector2(0.90f, 0.78f));

            // Вдавленная кромка вокруг текста.
            CreateLayer(root.transform, "Inset", new Color(0f, 0f, 0f, 0.28f),
                new Vector2(0.14f, 0.34f), new Vector2(0.86f, 0.72f));

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            var labelRt = labelGo.GetComponent<RectTransform>();
            labelRt.SetParent(root.transform, false);
            labelRt.anchorMin = new Vector2(0.12f, 0.30f);
            labelRt.anchorMax = new Vector2(0.88f, 0.76f);
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;
            var text = labelGo.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                        ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.text = label;
            text.fontSize = 26;
            text.fontStyle = FontStyle.Bold;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;

            var btn = root.GetComponent<Button>();
            btn.targetGraphic = rootImg;
            var colors = btn.colors;
            colors.highlightedColor = new Color(1.1f, 1.1f, 1.1f, 1f);
            colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            colors.selectedColor = Color.white;
            btn.colors = colors;

            // Сохраняем ссылку на face для перекраски.
            _ = face;
            return btn;
        }

        public static void ApplyDifficultySelection(string selectedId)
        {
            ApplyTab("easy", selectedId,
                new Color(0.20f, 0.62f, 0.30f, 1f), new Color(0.20f, 0.22f, 0.20f, 1f));
            ApplyTab("medium", selectedId,
                new Color(0.62f, 0.55f, 0.22f, 1f), new Color(0.22f, 0.22f, 0.24f, 1f));
            ApplyTab("hard", selectedId,
                new Color(0.70f, 0.22f, 0.18f, 1f), new Color(0.24f, 0.18f, 0.18f, 1f));
        }

        private static void ApplyTab(string id, string selectedId, Color on, Color off)
        {
            var go = GameObject.Find("Diff_" + id);
            if (go == null) return;
            var btn = go.GetComponent<Button>();
            if (btn == null) return;
            var selected = string.Equals(id, selectedId, System.StringComparison.OrdinalIgnoreCase);
            SetFaceColor(btn, selected ? on : off, selected);
            var label = go.transform.Find("Label")?.GetComponent<Text>();
            if (label != null)
                label.color = selected ? Color.white : new Color(0.85f, 0.85f, 0.88f, 1f);
        }

        public static void SetFaceColor(Button btn, Color faceColor, bool active)
        {
            if (btn == null) return;
            var face = btn.transform.Find("Face");
            var top = btn.transform.Find("BevelTop");
            var bottom = btn.transform.Find("BevelBottom");
            var inset = btn.transform.Find("Inset");

            var c = faceColor;
            if (face != null)
            {
                var img = face.GetComponent<Image>();
                if (img != null) img.color = c;
            }

            if (top != null)
            {
                var img = top.GetComponent<Image>();
                if (img != null) img.color = Lighten(c, active ? 0.42f : 0.22f);
            }

            if (bottom != null)
            {
                var img = bottom.GetComponent<Image>();
                if (img != null) img.color = Darken(c, active ? 0.35f : 0.5f);
            }

            if (inset != null)
            {
                var img = inset.GetComponent<Image>();
                if (img != null) img.color = active ? new Color(1f, 1f, 1f, 0.10f) : new Color(0f, 0f, 0f, 0.35f);
            }

            // Лёгкий «выступ» активной кнопки.
            var rt = btn.transform as RectTransform;
            if (rt != null)
                rt.localScale = active ? new Vector3(1.02f, 1.06f, 1f) : Vector3.one;
        }

        public static void StyleTabsHousing(RectTransform tabsRoot)
        {
            if (tabsRoot == null) return;
            var img = tabsRoot.GetComponent<Image>();
            if (img != null)
                img.color = new Color(0.12f, 0.10f, 0.09f, 0.96f);

            // Рамка-корпус (ignoreLayout — иначе HorizontalLayoutGroup съест слои).
            var top = CreateLayer(tabsRoot, "HousingTop", new Color(0.32f, 0.28f, 0.22f, 0.9f),
                new Vector2(0.02f, 0.88f), new Vector2(0.98f, 0.98f));
            var bottom = CreateLayer(tabsRoot, "HousingBottom", new Color(0.05f, 0.04f, 0.03f, 0.95f),
                new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.14f));
            IgnoreLayout(top);
            IgnoreLayout(bottom);
            top.SetAsFirstSibling();
            bottom.SetAsFirstSibling();

            // Корпус не должен перехватывать клики вкладок.
            var housingImg = tabsRoot.GetComponent<Image>();
            if (housingImg != null)
                housingImg.raycastTarget = false;
        }

        private static void IgnoreLayout(Component c)
        {
            if (c == null) return;
            var le = c.gameObject.GetComponent<LayoutElement>() ?? c.gameObject.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
        }

        private static RectTransform CreateLayer(Transform parent, string name, Color color, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return rt;
        }

        private static Color Lighten(Color c, float amount)
        {
            return new Color(
                Mathf.Clamp01(c.r + amount),
                Mathf.Clamp01(c.g + amount),
                Mathf.Clamp01(c.b + amount),
                c.a);
        }

        private static Color Darken(Color c, float amount)
        {
            return new Color(
                Mathf.Clamp01(c.r * (1f - amount)),
                Mathf.Clamp01(c.g * (1f - amount)),
                Mathf.Clamp01(c.b * (1f - amount)),
                c.a);
        }
    }
}
