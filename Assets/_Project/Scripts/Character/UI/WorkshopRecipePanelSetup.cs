using System;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Character.UI
{
    /// <summary>Создаёт/находит <see cref="WorkshopSceneController"/> панель рецептов (тот же иерархия, что в сцене).</summary>
    public static class WorkshopRecipePanelSetup
    {
        public readonly struct Refs
        {
            public readonly Text recipeHeader;
            public readonly Text itemStatsText;
            public readonly Text detailText;
            public readonly RectTransform recipeContent;
            public readonly Button createButton;
            public readonly Button claimButton;
            public readonly Button rushButton;

            public Refs(Text recipeHeader, Text itemStatsText, Text detailText, RectTransform recipeContent, Button create, Button claim, Button rush)
            {
                this.recipeHeader = recipeHeader;
                this.itemStatsText = itemStatsText;
                this.detailText = detailText;
                this.recipeContent = recipeContent;
                this.createButton = create;
                this.claimButton = claim;
                this.rushButton = rush;
            }
        }

        /// <summary>Привести уже встроенную панель к актуальной сетке якорей (без пересечения DetailText и кнопок), увеличить шрифты.</summary>
        public static void NormalizeRecipePanelLayout(RectTransform workshopBackground)
        {
            if (workshopBackground == null) return;
            var panel = workshopBackground.Find("WorkshopRecipePanel") as RectTransform;
            if (panel == null) return;

            var rh = panel.Find("RecipeHeader") as RectTransform;
            var stats = EnsureItemStatsTextTransform(panel);
            var scroll = panel.Find("RecipeScroll") as RectTransform;
            var dt = panel.Find("DetailText") as RectTransform;
            var br = panel.Find("Buttons") as RectTransform;

            if (rh != null)
            {
                rh.anchorMin = new Vector2(0f, 0.79f);
                rh.anchorMax = new Vector2(1f, 1f);
                rh.offsetMin = new Vector2(12f, 4f);
                rh.offsetMax = new Vector2(-12f, -8f);
                var t = rh.GetComponent<Text>();
                if (t != null) t.fontSize = Mathf.Max(t.fontSize, 26);
            }

            if (stats != null)
            {
                stats.anchorMin = new Vector2(0f, 0.66f);
                stats.anchorMax = new Vector2(1f, 0.78f);
                stats.offsetMin = new Vector2(12f, 2f);
                stats.offsetMax = new Vector2(-12f, -2f);
                var t = stats.GetComponent<Text>();
                if (t != null)
                {
                    t.fontSize = Mathf.Max(t.fontSize, 18);
                    t.lineSpacing = 1.05f;
                }
            }

            if (scroll != null)
            {
                scroll.anchorMin = new Vector2(0f, 0.36f);
                scroll.anchorMax = new Vector2(1f, 0.65f);
                scroll.offsetMin = new Vector2(8f, 4f);
                scroll.offsetMax = new Vector2(-8f, -4f);
            }

            if (dt != null)
            {
                dt.anchorMin = new Vector2(0f, 0.13f);
                dt.anchorMax = new Vector2(1f, 0.35f);
                dt.offsetMin = new Vector2(12f, 6f);
                dt.offsetMax = new Vector2(-12f, -4f);
                var t = dt.GetComponent<Text>();
                if (t != null)
                {
                    t.fontSize = Mathf.Max(t.fontSize, 20);
                    t.lineSpacing = 1.08f;
                    t.verticalOverflow = VerticalWrapMode.Truncate;
                }
            }

            if (br != null)
            {
                br.anchorMin = new Vector2(0f, 0f);
                br.anchorMax = new Vector2(1f, 0.12f);
                br.offsetMin = new Vector2(12f, 8f);
                br.offsetMax = new Vector2(-12f, 8f);
            }

            foreach (var btnName in new[] { "CreateButton", "ClaimButton", "RushButton" })
            {
                var b = br != null ? br.Find(btnName) : null;
                if (b == null) continue;
                var le = b.GetComponent<LayoutElement>();
                if (le != null) le.minHeight = Mathf.Max(le.minHeight, 44f);
                var lab = b.Find("Label")?.GetComponent<Text>();
                if (lab != null) lab.fontSize = Mathf.Max(lab.fontSize, 22);
            }
        }

        public static bool TryBindExisting(RectTransform workshopBackground, out Refs refs)
        {
            refs = default;
            if (workshopBackground == null) return false;
            var panel = workshopBackground.Find("WorkshopRecipePanel");
            if (panel == null) return false;
            var rh = panel.Find("RecipeHeader")?.GetComponent<Text>();
            var st = panel.Find("ItemStatsText")?.GetComponent<Text>();
            var dt = panel.Find("DetailText")?.GetComponent<Text>();
            var content = panel.Find("RecipeScroll/Viewport/Content") as RectTransform;
            var buttons = panel.Find("Buttons");
            if (content == null || buttons == null) return false;
            var create = buttons.Find("CreateButton")?.GetComponent<Button>();
            var claim = buttons.Find("ClaimButton")?.GetComponent<Button>();
            var rush = buttons.Find("RushButton")?.GetComponent<Button>();
            if (create == null || claim == null) return false;
            refs = new Refs(rh, st, dt, content, create, claim, rush);
            return true;
        }

        public static Button EnsureRushButton(RectTransform workshopBackground)
        {
            if (workshopBackground == null) return null;
            var panel = workshopBackground.Find("WorkshopRecipePanel");
            if (panel == null) return null;
            var buttons = panel.Find("Buttons") as RectTransform;
            if (buttons == null) return null;
            if (buttons.Find("RushButton") != null)
                return buttons.Find("RushButton").GetComponent<Button>();
            var go = new GameObject("RushButton", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(buttons, false);
            go.GetComponent<LayoutElement>().flexibleWidth = 1f;
            go.GetComponent<LayoutElement>().minHeight = 44f;
            go.GetComponent<Image>().color = new Color(0.28f, 0.22f, 0.2f, 1f);
            var tgo = new GameObject("Label", typeof(Text));
            tgo.transform.SetParent(go.transform, false);
            var t = tgo.GetComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.text = "Ускорить 20м (500з)";
            t.fontSize = 22;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = new Color(0.94f, 0.92f, 0.86f);
            t.raycastTarget = false;
            var tr = t.GetComponent<RectTransform>();
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.offsetMin = Vector2.zero;
            tr.offsetMax = Vector2.zero;
            if (buttons.GetComponent<HorizontalLayoutGroup>() == null)
            {
                var h = buttons.gameObject.AddComponent<HorizontalLayoutGroup>();
                h.spacing = 6f;
                h.childControlHeight = true;
                h.childControlWidth = true;
                h.childAlignment = TextAnchor.MiddleCenter;
                h.childForceExpandWidth = true;
            }
            return go.GetComponent<Button>();
        }

        public static Refs Build(RectTransform workshopBackground)
        {
            if (workshopBackground == null) throw new ArgumentNullException(nameof(workshopBackground));
            if (workshopBackground.Find("WorkshopRecipePanel") != null)
            {
                if (TryBindExisting(workshopBackground, out var r)) return r;
            }

            var panel = new GameObject("WorkshopRecipePanel", typeof(RectTransform), typeof(Image));
            var pr = panel.GetComponent<RectTransform>();
            pr.SetParent(workshopBackground, false);
            pr.anchorMin = new Vector2(0.5f, 0.22f);
            pr.anchorMax = new Vector2(0.98f, 0.88f);
            pr.offsetMin = Vector2.zero;
            pr.offsetMax = Vector2.zero;
            panel.GetComponent<Image>().color = new Color(0.14f, 0.11f, 0.1f, 0.95f);

            var recipeHeader = CreateUiText("RecipeHeader", pr, "Выберите слот слева", 26, TextAnchor.UpperLeft,
                new Vector2(0f, 0.79f), new Vector2(1f, 1f), new Vector2(12f, 4f), new Vector2(-12f, -8f));

            var itemStatsText = CreateUiText("ItemStatsText", pr,
                "Характеристики предмета появятся после выбора рецепта.", 18, TextAnchor.UpperLeft,
                new Vector2(0f, 0.66f), new Vector2(1f, 0.78f), new Vector2(12f, 2f), new Vector2(-12f, -2f));

            var scrollGo = new GameObject("RecipeScroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            var sr = scrollGo.GetComponent<RectTransform>();
            sr.SetParent(pr, false);
            sr.anchorMin = new Vector2(0f, 0.36f);
            sr.anchorMax = new Vector2(1f, 0.65f);
            sr.offsetMin = new Vector2(8f, 4f);
            sr.offsetMax = new Vector2(-8f, -4f);
            scrollGo.GetComponent<Image>().color = new Color(0.1f, 0.08f, 0.08f, 1f);

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            var vp = viewport.GetComponent<RectTransform>();
            vp.SetParent(sr, false);
            vp.anchorMin = Vector2.zero;
            vp.anchorMax = Vector2.one;
            vp.offsetMin = Vector2.zero;
            vp.offsetMax = Vector2.zero;
            viewport.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.02f);
            viewport.GetComponent<Mask>().showMaskGraphic = false;

            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            var recipeContent = content.GetComponent<RectTransform>();
            recipeContent.SetParent(vp, false);
            recipeContent.anchorMin = new Vector2(0f, 1f);
            recipeContent.anchorMax = new Vector2(1f, 1f);
            recipeContent.pivot = new Vector2(0.5f, 1f);
            var vlg = content.GetComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.spacing = 6f;
            vlg.padding = new RectOffset(4, 4, 6, 6);
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.viewport = vp;
            scroll.content = recipeContent;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            var detailText = CreateUiText("DetailText", pr, "", 20, TextAnchor.UpperLeft,
                new Vector2(0f, 0.13f), new Vector2(1f, 0.35f), new Vector2(12f, 6f), new Vector2(-12f, -4f));
            detailText.verticalOverflow = VerticalWrapMode.Truncate;

            var btnRow = new GameObject("Buttons", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            var br = btnRow.GetComponent<RectTransform>();
            br.SetParent(pr, false);
            br.anchorMin = new Vector2(0f, 0f);
            br.anchorMax = new Vector2(1f, 0.12f);
            br.offsetMin = new Vector2(12f, 8f);
            br.offsetMax = new Vector2(-12f, 8f);
            var rowH = br.GetComponent<HorizontalLayoutGroup>();
            rowH.padding = new RectOffset(0, 0, 0, 0);
            rowH.spacing = 6f;
            rowH.childAlignment = TextAnchor.MiddleCenter;
            rowH.childControlHeight = true;
            rowH.childControlWidth = true;
            rowH.childForceExpandWidth = true;

            var createButton = CreateUiButton(br, "CreateButton", "Создать");
            var claimButton = CreateUiButton(br, "ClaimButton", "Забрать в сундук");
            var rushButton = CreateUiButton(br, "RushButton", "Ускорить 20м (500з)");
            claimButton.gameObject.SetActive(false);
            return new Refs(recipeHeader, itemStatsText, detailText, recipeContent, createButton, claimButton, rushButton);
        }

        private static RectTransform EnsureItemStatsTextTransform(RectTransform panel)
        {
            var existing = panel.Find("ItemStatsText") as RectTransform;
            if (existing != null) return existing;
            var t = CreateUiText("ItemStatsText", panel,
                "Характеристики предмета появятся после выбора рецепта.", 18, TextAnchor.UpperLeft,
                new Vector2(0f, 0.66f), new Vector2(1f, 0.78f), new Vector2(12f, 2f), new Vector2(-12f, -2f));
            return t.rectTransform;
        }

        private static Text CreateUiText(string name, Transform parent, string msg, int size, TextAnchor align,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 offMin, Vector2 offMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offMin;
            rt.offsetMax = offMax;
            var tx = go.GetComponent<Text>();
            tx.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tx.text = msg;
            tx.fontSize = size;
            tx.color = new Color(0.94f, 0.92f, 0.86f);
            tx.alignment = align;
            tx.horizontalOverflow = HorizontalWrapMode.Wrap;
            tx.verticalOverflow = VerticalWrapMode.Overflow;
            tx.raycastTarget = false;
            return tx;
        }

        private static Button CreateUiButton(RectTransform parent, string name, string label)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.GetComponent<RectTransform>().SetParent(parent, false);
            go.GetComponent<LayoutElement>().flexibleWidth = 1f;
            go.GetComponent<LayoutElement>().minHeight = 44f;
            go.GetComponent<Image>().color = new Color(0.28f, 0.22f, 0.2f, 1f);
            CreateUiText("Label", go.transform, label, 22, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            return go.GetComponent<Button>();
        }
    }
}
