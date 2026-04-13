using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Project.UI.Editor
{
    /// <summary>
    /// Генерирует префаб модалки монстра с фоном modal_back и секциями под шаблон modal_png.
    /// </summary>
    public static class MonsterModalPrefabBuilder
    {
        private const string PrefabPath = "Assets/_Project/Resources/UI/MonsterModal.prefab";
        private const string BackgroundSpritePath = "Assets/_Project/img/modal_back.png";

        [MenuItem("Tools/UI/Создать префаб Monster Modal")]
        public static void CreatePrefab()
        {
            EnsureDir("Assets/_Project/Resources/UI");

            var rootGo = new GameObject("MonsterModal", typeof(RectTransform), typeof(MonsterModalView));
            var root = rootGo.GetComponent<RectTransform>();
            root.SetParent(null, false);
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = new Vector2(742f, 640f);
            root.anchoredPosition = Vector2.zero;

            var bgSprite = AssetDatabase.LoadAssetAtPath<Sprite>(BackgroundSpritePath);
            var bgGo = new GameObject("Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.SetParent(root, false);
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;
            var bgImg = bgGo.GetComponent<Image>();
            bgImg.sprite = bgSprite;
            bgImg.type = bgSprite != null ? Image.Type.Sliced : Image.Type.Simple;
            bgImg.color = Color.white;
            bgImg.raycastTarget = true;

            var mainCol = new GameObject("MainColumn", typeof(RectTransform), typeof(VerticalLayoutGroup));
            var mainRt = mainCol.GetComponent<RectTransform>();
            mainRt.SetParent(root, false);
            mainRt.anchorMin = new Vector2(0.03f, 0.06f);
            mainRt.anchorMax = new Vector2(0.97f, 0.94f);
            mainRt.offsetMin = Vector2.zero;
            mainRt.offsetMax = Vector2.zero;
            var mainV = mainCol.GetComponent<VerticalLayoutGroup>();
            mainV.padding = new RectOffset(12, 12, 8, 8);
            mainV.spacing = 10f;
            mainV.childAlignment = TextAnchor.UpperCenter;
            mainV.childControlHeight = true;
            mainV.childControlWidth = true;
            mainV.childForceExpandHeight = false;
            mainV.childForceExpandWidth = true;

            var header = new GameObject("Header", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            var headerRt = header.GetComponent<RectTransform>();
            headerRt.SetParent(mainCol.transform, false);
            var headerH = header.GetComponent<HorizontalLayoutGroup>();
            headerH.padding = new RectOffset(4, 4, 0, 4);
            headerH.spacing = 12f;
            headerH.childAlignment = TextAnchor.MiddleCenter;
            headerH.childControlHeight = true;
            headerH.childControlWidth = true;
            headerH.childForceExpandHeight = false;
            headerH.childForceExpandWidth = false;
            var headerLe = header.AddComponent<LayoutElement>();
            headerLe.preferredHeight = 56f;
            headerLe.flexibleWidth = 1f;

            var titleText = CreateText(header.transform, "Title", "Монстр", 30, new Color(0.95f, 0.95f, 0.98f),
                TextAnchor.MiddleLeft, Vector2.zero, Vector2.one);
            var titleLe = titleText.gameObject.AddComponent<LayoutElement>();
            titleLe.flexibleWidth = 1f;
            titleLe.minHeight = 44f;

            var closeBtn = CreateButton(header.transform, "CloseButton", "✕", new Color(0.2f, 0.16f, 0.16f, 0.92f),
                new Vector2(44f, 44f));

            var body = new GameObject("Body", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            var bodyRt = body.GetComponent<RectTransform>();
            bodyRt.SetParent(mainCol.transform, false);
            var bodyV = body.GetComponent<VerticalLayoutGroup>();
            bodyV.padding = new RectOffset(4, 4, 4, 4);
            bodyV.spacing = 12f;
            bodyV.childAlignment = TextAnchor.UpperCenter;
            bodyV.childControlHeight = true;
            bodyV.childControlWidth = true;
            bodyV.childForceExpandHeight = false;
            bodyV.childForceExpandWidth = true;
            var bodyLe = body.GetComponent<LayoutElement>();
            bodyLe.flexibleHeight = 1f;
            bodyLe.flexibleWidth = 1f;

            var monsterBlock = new GameObject("MonsterContent", typeof(RectTransform), typeof(VerticalLayoutGroup));
            var monsterRt = monsterBlock.GetComponent<RectTransform>();
            monsterRt.SetParent(body.transform, false);
            var monsterV = monsterBlock.GetComponent<VerticalLayoutGroup>();
            monsterV.spacing = 10f;
            monsterV.childAlignment = TextAnchor.UpperCenter;
            monsterV.childControlHeight = true;
            monsterV.childControlWidth = true;
            monsterV.childForceExpandHeight = false;
            monsterV.childForceExpandWidth = true;
            monsterBlock.AddComponent<LayoutElement>().flexibleHeight = 1f;

            var charTitle = CreateText(monsterBlock.transform, "CharacteristicsTitle", "Характеристики", 20,
                new Color(0.85f, 0.9f, 1f), TextAnchor.MiddleLeft, Vector2.zero, Vector2.one);
            var charTitleLe = charTitle.gameObject.AddComponent<LayoutElement>();
            charTitleLe.preferredHeight = 28f;

            var statsRow1 = CreateHorizontalRow(monsterBlock.transform, "StatsRow1", 40f);
            var statsRow2 = CreateHorizontalRow(monsterBlock.transform, "StatsRow2", 40f);
            var statTexts = new Text[6];
            statTexts[0] = CreateStatCell(statsRow1.transform, "HP", "HP: —");
            statTexts[1] = CreateStatCell(statsRow1.transform, "Dmg", "Урон: —");
            statTexts[2] = CreateStatCell(statsRow1.transform, "Armor", "Броня: —");
            statTexts[3] = CreateStatCell(statsRow2.transform, "Crit", "Крит: —");
            statTexts[4] = CreateStatCell(statsRow2.transform, "Mana", "Мана: —");
            statTexts[5] = CreateStatCell(statsRow2.transform, "Timer", "—");

            var rewTitle = CreateText(monsterBlock.transform, "RewardsTitle", "Награды", 20, new Color(0.85f, 0.9f, 1f),
                TextAnchor.MiddleLeft, Vector2.zero, Vector2.one);
            rewTitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;

            var rewardsDyn = new GameObject("RewardsDynamic", typeof(RectTransform), typeof(VerticalLayoutGroup));
            var rewardsDynRt = rewardsDyn.GetComponent<RectTransform>();
            rewardsDynRt.SetParent(monsterBlock.transform, false);
            var rewardsDynV = rewardsDyn.GetComponent<VerticalLayoutGroup>();
            rewardsDynV.spacing = 6f;
            rewardsDynV.childAlignment = TextAnchor.UpperCenter;
            rewardsDynV.childControlHeight = true;
            rewardsDynV.childControlWidth = true;
            rewardsDynV.childForceExpandHeight = false;
            rewardsDynV.childForceExpandWidth = true;
            rewardsDyn.AddComponent<LayoutElement>().minHeight = 48f;

            var anoTitle = CreateText(monsterBlock.transform, "AnomalyTitle", "Аномалия", 20, new Color(0.85f, 0.9f, 1f),
                TextAnchor.MiddleLeft, Vector2.zero, Vector2.one);
            anoTitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;

            var anomalyRow = new GameObject("AnomalyRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            var anoRowRt = anomalyRow.GetComponent<RectTransform>();
            anoRowRt.SetParent(monsterBlock.transform, false);
            var anoH = anomalyRow.GetComponent<HorizontalLayoutGroup>();
            anoH.spacing = 12f;
            anoH.padding = new RectOffset(0, 0, 0, 0);
            anoH.childAlignment = TextAnchor.MiddleLeft;
            anoH.childControlHeight = true;
            anoH.childControlWidth = true;
            anoH.childForceExpandHeight = false;
            anoH.childForceExpandWidth = false;
            anomalyRow.AddComponent<LayoutElement>().minHeight = 72f;

            var affixIconGo = new GameObject("AffixIcon", typeof(RectTransform), typeof(Image));
            var affixIconRt = affixIconGo.GetComponent<RectTransform>();
            affixIconRt.SetParent(anomalyRow.transform, false);
            var affixIcon = affixIconGo.GetComponent<Image>();
            affixIcon.color = new Color(0.25f, 0.25f, 0.35f, 0.96f);
            var affixIconLe = affixIconGo.AddComponent<LayoutElement>();
            affixIconLe.preferredWidth = 64f;
            affixIconLe.preferredHeight = 64f;

            CreateText(affixIconGo.transform, "Glyph", "?", 22, Color.white, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one);

            var affixTextsCol = new GameObject("AffixTexts", typeof(RectTransform), typeof(VerticalLayoutGroup));
            var affixTextsRt = affixTextsCol.GetComponent<RectTransform>();
            affixTextsRt.SetParent(anomalyRow.transform, false);
            var affixTextsV = affixTextsCol.GetComponent<VerticalLayoutGroup>();
            affixTextsV.spacing = 4f;
            affixTextsV.childAlignment = TextAnchor.UpperLeft;
            affixTextsV.childControlHeight = true;
            affixTextsV.childControlWidth = true;
            affixTextsV.childForceExpandHeight = false;
            affixTextsV.childForceExpandWidth = true;
            var affixTextsLe = affixTextsCol.AddComponent<LayoutElement>();
            affixTextsLe.flexibleWidth = 1f;
            affixTextsLe.minHeight = 64f;

            var affixTitle = CreateText(affixTextsCol.transform, "AffixTitle", "Аффикс: —", 18, Color.white,
                TextAnchor.UpperLeft, Vector2.zero, Vector2.one);
            affixTitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;
            var affixDesc = CreateText(affixTextsCol.transform, "AffixDescription", "", 16, new Color(0.88f, 0.88f, 0.92f),
                TextAnchor.UpperLeft, Vector2.zero, Vector2.one);
            affixDesc.gameObject.AddComponent<LayoutElement>().minHeight = 36f;

            var supplemental = CreateText(monsterBlock.transform, "SupplementalInfo", "", 16, new Color(1f, 0.75f, 0.65f),
                TextAnchor.UpperLeft, Vector2.zero, Vector2.one);
            supplemental.gameObject.AddComponent<LayoutElement>().minHeight = 24f;

            var barrierBlock = new GameObject("BarrierContent", typeof(RectTransform), typeof(VerticalLayoutGroup));
            barrierBlock.transform.SetParent(body.transform, false);
            var barrierV = barrierBlock.GetComponent<VerticalLayoutGroup>();
            barrierV.spacing = 10f;
            barrierV.childAlignment = TextAnchor.UpperCenter;
            barrierV.childControlHeight = true;
            barrierV.childControlWidth = true;
            barrierV.childForceExpandHeight = false;
            barrierV.childForceExpandWidth = true;
            barrierBlock.AddComponent<LayoutElement>().flexibleHeight = 1f;
            barrierBlock.SetActive(false);

            var barrierSecTitle = CreateText(barrierBlock.transform, "BarrierSectionTitle", "Барьер", 20,
                new Color(0.85f, 0.9f, 1f), TextAnchor.MiddleLeft, Vector2.zero, Vector2.one);
            barrierSecTitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;

            var barrierInfo = CreateText(barrierBlock.transform, "BarrierInfo", "", 18, Color.white,
                TextAnchor.UpperLeft, Vector2.zero, Vector2.one);
            barrierInfo.gameObject.AddComponent<LayoutElement>().minHeight = 48f;

            var barrierReqTitle = CreateText(barrierBlock.transform, "BarrierRequirementsTitle", "Требуется для разбития:", 18,
                new Color(0.85f, 0.9f, 1f), TextAnchor.MiddleLeft, Vector2.zero, Vector2.one);
            barrierReqTitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;

            var barrierDyn = new GameObject("BarrierRequirementsDynamic", typeof(RectTransform), typeof(VerticalLayoutGroup));
            var barrierDynRt = barrierDyn.GetComponent<RectTransform>();
            barrierDynRt.SetParent(barrierBlock.transform, false);
            var barrierDynV = barrierDyn.GetComponent<VerticalLayoutGroup>();
            barrierDynV.spacing = 6f;
            barrierDynV.childAlignment = TextAnchor.UpperCenter;
            barrierDynV.childControlHeight = true;
            barrierDynV.childControlWidth = true;
            barrierDynV.childForceExpandHeight = false;
            barrierDynV.childForceExpandWidth = true;
            barrierDyn.AddComponent<LayoutElement>().minHeight = 40f;

            var footer = new GameObject("Footer", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            var footerRt = footer.GetComponent<RectTransform>();
            footerRt.SetParent(mainCol.transform, false);
            var footerH = footer.GetComponent<HorizontalLayoutGroup>();
            footerH.padding = new RectOffset(4, 4, 8, 4);
            footerH.spacing = 16f;
            footerH.childAlignment = TextAnchor.MiddleCenter;
            footerH.childControlHeight = true;
            footerH.childControlWidth = true;
            footerH.childForceExpandHeight = false;
            footerH.childForceExpandWidth = true;
            var footerLe = footer.AddComponent<LayoutElement>();
            footerLe.preferredHeight = 64f;

            var fightBtn = CreateFooterButton(footer.transform, "FightButton", "В бой");
            var dismissBtn = CreateFooterButton(footer.transform, "DismissButton", "Прогнать");

            var view = rootGo.GetComponent<MonsterModalView>();
            var so = new SerializedObject(view);
            so.FindProperty("backgroundImage").objectReferenceValue = bgImg;
            so.FindProperty("titleText").objectReferenceValue = titleText;
            so.FindProperty("closeButton").objectReferenceValue = closeBtn;
            so.FindProperty("supplementalInfoText").objectReferenceValue = supplemental;
            so.FindProperty("monsterContentRoot").objectReferenceValue = monsterBlock;
            so.FindProperty("barrierContentRoot").objectReferenceValue = barrierBlock;
            so.FindProperty("characteristicsSectionTitle").objectReferenceValue = charTitle;
            var statArray = so.FindProperty("statTexts");
            statArray.arraySize = 6;
            for (var i = 0; i < 6; i++)
                statArray.GetArrayElementAtIndex(i).objectReferenceValue = statTexts[i];
            so.FindProperty("rewardsSectionTitle").objectReferenceValue = rewTitle;
            so.FindProperty("rewardsDynamicRoot").objectReferenceValue = rewardsDynRt;
            so.FindProperty("anomalySectionTitle").objectReferenceValue = anoTitle;
            so.FindProperty("affixIcon").objectReferenceValue = affixIcon;
            so.FindProperty("affixIconGlyph").objectReferenceValue = affixIconGo.transform.Find("Glyph").GetComponent<Text>();
            so.FindProperty("affixTitleText").objectReferenceValue = affixTitle;
            so.FindProperty("affixDescriptionText").objectReferenceValue = affixDesc;
            so.FindProperty("barrierSectionTitle").objectReferenceValue = barrierSecTitle;
            so.FindProperty("barrierInfoText").objectReferenceValue = barrierInfo;
            so.FindProperty("barrierRequirementsSectionTitle").objectReferenceValue = barrierReqTitle;
            so.FindProperty("barrierRequirementsRoot").objectReferenceValue = barrierDynRt;
            so.FindProperty("fightButton").objectReferenceValue = fightBtn;
            so.FindProperty("dismissButton").objectReferenceValue = dismissBtn;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(rootGo, PrefabPath);
            Object.DestroyImmediate(rootGo);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[MonsterModalPrefabBuilder] Сохранено: " + PrefabPath);
        }

        private static RectTransform CreateHorizontalRow(Transform parent, string name, float height)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            var h = go.GetComponent<HorizontalLayoutGroup>();
            h.spacing = 8f;
            h.padding = new RectOffset(0, 0, 0, 0);
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlHeight = true;
            h.childControlWidth = true;
            h.childForceExpandHeight = false;
            h.childForceExpandWidth = true;
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.flexibleWidth = 1f;
            return rt;
        }

        private static Text CreateStatCell(Transform row, string name, string placeholder)
        {
            var t = CreateText(row, name, placeholder, 17, new Color(0.93f, 0.93f, 0.96f), TextAnchor.MiddleCenter,
                Vector2.zero, Vector2.one);
            var le = t.gameObject.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            le.minHeight = 36f;
            return t;
        }

        private static Button CreateFooterButton(Transform parent, string name, string label)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = new Color(0.24f, 0.18f, 0.18f, 0.95f);
            var le = go.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            le.preferredHeight = 52f;
            CreateText(go.transform, "Label", label, 22, Color.white, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one);
            return go.GetComponent<Button>();
        }

        private static Button CreateButton(Transform parent, string name, string label, Color bg, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            go.GetComponent<Image>().color = bg;
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = size.x;
            le.preferredHeight = size.y;
            CreateText(go.transform, "Label", label, 20, Color.white, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one);
            return go.GetComponent<Button>();
        }

        private static Text CreateText(Transform parent, string name, string value, int size, Color color, TextAnchor anchor,
            Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var text = go.GetComponent<Text>();
            text.font = GetBuiltinFont();
            text.text = value;
            text.fontSize = size;
            text.color = color;
            text.alignment = anchor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        private static Font GetBuiltinFont()
        {
            try
            {
                var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (font != null) return font;
            }
            catch { }

            try
            {
                return Resources.GetBuiltinResource<Font>("Arial.ttf");
            }
            catch
            {
                return null;
            }
        }

        private static void EnsureDir(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return;
            if (!System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);
        }
    }
}
