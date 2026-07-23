using System.IO;
using Project.Character.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Character.Editor
{
    public static class CharacterUiPrefabCreator
    {
        private const string PrefabsDir = "Assets/_Project/Prefabs/CharacterUI";
        private const string ItemSlotPrefabPath = PrefabsDir + "/ItemSlotView.prefab";
        private const string EquipmentSlotPrefabPath = PrefabsDir + "/EquipmentSlotView.prefab";
        private const string ItemActionModalPrefabPath = PrefabsDir + "/CharacterItemActionModal.prefab";
        private const string ItemInfoModalPrefabPath = PrefabsDir + "/CharacterItemInfoModal.prefab";
        private const string CharacterHudPrefabPath = PrefabsDir + "/CharacterHudOverlay.prefab";

        [MenuItem("Tools/Character/Создать префабы персонажа (экипировка + мешок)")]
        public static void CreateAll()
        {
            EnsureDir(PrefabsDir);

            var itemSlotPrefab = CreateOrReplaceItemSlotPrefab();
            var equipmentSlotPrefab = CreateOrReplaceEquipmentSlotPrefab(itemSlotPrefab);
            var actionModalPrefab = CreateOrReplaceItemActionModalPrefab();
            var infoModalPrefab = CreateOrReplaceItemInfoModalPrefab();
            CreateOrReplaceCharacterHudPrefab(equipmentSlotPrefab, itemSlotPrefab, actionModalPrefab, infoModalPrefab);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var hud = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterHudPrefabPath);
            if (hud != null) Selection.activeObject = hud;
        }

        private static GameObject CreateOrReplaceItemSlotPrefab()
        {
            var root = new GameObject("ItemSlotView", typeof(RectTransform), typeof(Image), typeof(ItemSlotView));
            var rt = root.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(110, 110);

            var bg = root.GetComponent<Image>();
            bg.color = new Color(0.1f, 0.1f, 0.12f, 0.95f);

            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            var iconRt = iconGo.GetComponent<RectTransform>();
            iconRt.SetParent(rt, false);
            iconRt.anchorMin = new Vector2(0.1f, 0.1f);
            iconRt.anchorMax = new Vector2(0.9f, 0.9f);
            iconRt.offsetMin = Vector2.zero;
            iconRt.offsetMax = Vector2.zero;

            var iconImg = iconGo.GetComponent<Image>();
            iconImg.color = new Color(1f, 1f, 1f, 0.85f);
            iconImg.enabled = false;

            var view = root.GetComponent<ItemSlotView>();
            SetPrivateField(view, "background", bg);
            SetPrivateField(view, "icon", iconImg);

            ReplacePrefab(root, ItemSlotPrefabPath);
            Object.DestroyImmediate(root);
            return AssetDatabase.LoadAssetAtPath<GameObject>(ItemSlotPrefabPath);
        }

        private static GameObject CreateOrReplaceEquipmentSlotPrefab(GameObject itemSlotPrefab)
        {
            var root = new GameObject("EquipmentSlotView", typeof(RectTransform), typeof(Image), typeof(Button), typeof(EquipmentSlotView));
            var rt = root.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(100, 100);

            var bg = root.GetComponent<Image>();
            bg.color = new Color(0.16f, 0.14f, 0.20f, 0.95f);

            // Important: LayoutGroups ignore RectTransform.sizeDelta without ILayoutElement.
            // Add LayoutElement so VerticalLayoutGroup won't collapse the slot height.
            var le = root.AddComponent<LayoutElement>();
            le.minHeight = 110;
            le.minWidth = 110;
            le.preferredHeight = 110;
            le.preferredWidth = 110;

            var outline = root.AddComponent<Outline>();
            outline.effectColor = new Color(1f, 1f, 1f, 0.25f);
            outline.effectDistance = new Vector2(1f, -1f);

            ItemSlotView itemView;
            if (itemSlotPrefab != null)
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(itemSlotPrefab);
                inst.name = "Item";
                var instRt = inst.GetComponent<RectTransform>();
                instRt.SetParent(rt, false);
                instRt.anchorMin = Vector2.zero;
                instRt.anchorMax = Vector2.one;
                instRt.offsetMin = new Vector2(4, 4);
                instRt.offsetMax = new Vector2(-4, -4);
                itemView = inst.GetComponent<ItemSlotView>();
            }
            else
            {
                var itemGo = new GameObject("Item", typeof(RectTransform), typeof(Image), typeof(ItemSlotView));
                var itemRt = itemGo.GetComponent<RectTransform>();
                itemRt.SetParent(rt, false);
                itemRt.anchorMin = Vector2.zero;
                itemRt.anchorMax = Vector2.one;
                itemRt.offsetMin = new Vector2(4, 4);
                itemRt.offsetMax = new Vector2(-4, -4);
                itemView = itemGo.GetComponent<ItemSlotView>();
            }

            var view = root.GetComponent<EquipmentSlotView>();
            SetPrivateField(view, "button", root.GetComponent<Button>());
            SetPrivateField(view, "itemView", itemView);
            SetPrivateField(view, "slotImage", bg);

            ReplacePrefab(root, EquipmentSlotPrefabPath);
            Object.DestroyImmediate(root);
            return AssetDatabase.LoadAssetAtPath<GameObject>(EquipmentSlotPrefabPath);
        }

        private static GameObject CreateOrReplaceItemActionModalPrefab()
        {
            var root = new GameObject("CharacterItemActionModal", typeof(RectTransform), typeof(Image), typeof(CanvasGroup), typeof(VerticalLayoutGroup), typeof(CharacterItemActionModalView));
            var rt = root.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(380f, 220f);

            var bg = root.GetComponent<Image>();
            bg.color = new Color(0.11f, 0.11f, 0.17f, 0.98f);

            var layout = root.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 24, 24);
            layout.spacing = 14;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            MakeLayoutButton(rt, "InfoButton", "Информация");
            MakeLayoutButton(rt, "SellButton", "Продать");

            ReplacePrefab(root, ItemActionModalPrefabPath);
            Object.DestroyImmediate(root);
            return AssetDatabase.LoadAssetAtPath<GameObject>(ItemActionModalPrefabPath);
        }

        private static GameObject CreateOrReplaceItemInfoModalPrefab()
        {
            var root = new GameObject("CharacterItemInfoModal", typeof(RectTransform), typeof(Image), typeof(CanvasGroup), typeof(CharacterItemInfoModalView));
            var rt = root.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(680f, 560f);

            var bg = root.GetComponent<Image>();
            bg.color = new Color(0.10f, 0.10f, 0.15f, 0.99f);

            MakeTmp(rt, "Title", "Предмет", 34, Color.white,
                new Vector2(0.06f, 0.84f), new Vector2(0.84f, 0.96f), TextAlignmentOptions.Left);
            MakeTmp(rt, "Slot", "Слот", 24, new Color(0.85f, 0.88f, 0.95f, 1f),
                new Vector2(0.06f, 0.76f), new Vector2(0.94f, 0.84f), TextAlignmentOptions.Left);
            MakeTmp(rt, "Desc", "", 22, new Color(0.78f, 0.80f, 0.88f, 1f),
                new Vector2(0.06f, 0.58f), new Vector2(0.94f, 0.74f), TextAlignmentOptions.TopLeft);
            MakeTmp(rt, "Stats", "-", 26, Color.white,
                new Vector2(0.06f, 0.22f), new Vector2(0.94f, 0.56f), TextAlignmentOptions.TopLeft);

            MakeRectButton(rt, "CloseButton", "X", new Vector2(0.88f, 0.88f), new Vector2(0.97f, 0.97f), 24);
            MakeRectButton(rt, "EquipButton", "Надеть", new Vector2(0.06f, 0.05f), new Vector2(0.34f, 0.16f), 24);
            MakeRectButton(rt, "LearnRecipeButton", "Изучить", new Vector2(0.36f, 0.05f), new Vector2(0.64f, 0.16f), 24);
            MakeRectButton(rt, "SalvageButton", "Разобрать", new Vector2(0.66f, 0.05f), new Vector2(0.94f, 0.16f), 24);

            ReplacePrefab(root, ItemInfoModalPrefabPath);
            Object.DestroyImmediate(root);
            return AssetDatabase.LoadAssetAtPath<GameObject>(ItemInfoModalPrefabPath);
        }

        private static void CreateOrReplaceCharacterHudPrefab(GameObject equipmentSlotPrefab, GameObject itemSlotPrefab, GameObject actionModalPrefab, GameObject infoModalPrefab)
        {
            var root = new GameObject("CharacterHudOverlay", typeof(RectTransform));
            var rootRt = root.GetComponent<RectTransform>();
            rootRt.sizeDelta = new Vector2(0, 0);

            // Canvas (if used as prefab dropped into scene, it's ready).
            var canvasGo = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvasRt = canvasGo.GetComponent<RectTransform>();
            canvasRt.SetParent(rootRt, false);
            canvasRt.anchorMin = Vector2.zero;
            canvasRt.anchorMax = Vector2.one;
            canvasRt.offsetMin = Vector2.zero;
            canvasRt.offsetMax = Vector2.zero;

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1f;

            // Open button (avatar).
            var openBtnGo = MakeButton(canvasRt, "OpenCharacterButton", "Персонаж",
                new Color(0.20f, 0.20f, 0.28f, 0.95f),
                new Vector2(0.03f, 0.86f), new Vector2(0.15f, 0.97f));
            var openBtn = openBtnGo.GetComponent<Button>();

            // Screen root.
            var screenGo = new GameObject("CharacterScreen", typeof(RectTransform), typeof(CanvasGroup), typeof(Image), typeof(CharacterScreenView));
            var screenRt = screenGo.GetComponent<RectTransform>();
            screenRt.SetParent(canvasRt, false);
            screenRt.anchorMin = new Vector2(0f, 0f);
            screenRt.anchorMax = new Vector2(1f, 1f);
            screenRt.offsetMin = Vector2.zero;
            screenRt.offsetMax = Vector2.zero;

            var dim = screenGo.GetComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.65f);
            dim.raycastTarget = true;

            var cg = screenGo.GetComponent<CanvasGroup>();
            cg.alpha = 0f;
            cg.interactable = false;
            cg.blocksRaycasts = false;

            // Panel.
            var panelGo = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(Outline));
            var panelRt = panelGo.GetComponent<RectTransform>();
            panelRt.SetParent(screenRt, false);
            panelRt.anchorMin = new Vector2(0.08f, 0.12f);
            panelRt.anchorMax = new Vector2(0.92f, 0.92f);
            panelRt.offsetMin = Vector2.zero;
            panelRt.offsetMax = Vector2.zero;

            var panelImg = panelGo.GetComponent<Image>();
            panelImg.color = new Color(0.10f, 0.10f, 0.14f, 0.98f);
            var panelOutline = panelGo.GetComponent<Outline>();
            panelOutline.effectColor = new Color(1f, 1f, 1f, 0.2f);
            panelOutline.effectDistance = new Vector2(1f, -1f);

            // Close button.
            var closeBtnGo = MakeButton(panelRt, "CloseButton", "X",
                new Color(0.35f, 0.15f, 0.15f, 0.95f),
                new Vector2(0.94f, 0.92f), new Vector2(0.99f, 0.99f));
            var closeBtn = closeBtnGo.GetComponent<Button>();

            // Title + Level
            MakeTmp(panelRt, "Title", "Персонаж", 40, new Color(1f, 0.75f, 0.25f, 1f),
                new Vector2(0.02f, 0.92f), new Vector2(0.50f, 0.99f), TextAlignmentOptions.Left);
            var levelTmp = MakeTmp(panelRt, "LevelValue", "1", 34, Color.white,
                new Vector2(0.50f, 0.92f), new Vector2(0.62f, 0.99f), TextAlignmentOptions.Left);
            MakeTmp(panelRt, "LevelLabel", "Ур.", 26, new Color(0.8f, 0.8f, 0.9f, 1f),
                new Vector2(0.44f, 0.925f), new Vector2(0.50f, 0.99f), TextAlignmentOptions.Right);

            // Stats block (bottom-left-ish).
            var statsGo = new GameObject("Stats", typeof(RectTransform));
            var statsRt = statsGo.GetComponent<RectTransform>();
            statsRt.SetParent(panelRt, false);
            statsRt.anchorMin = new Vector2(0.02f, 0.02f);
            statsRt.anchorMax = new Vector2(0.40f, 0.28f);
            statsRt.offsetMin = Vector2.zero;
            statsRt.offsetMax = Vector2.zero;

            MakeTmp(statsRt, "HpLabel", "Здоровье:", 20, new Color(0.85f, 0.85f, 0.95f, 1f),
                new Vector2(0f, 0.78f), new Vector2(0.60f, 1f), TextAlignmentOptions.Left);
            var hpVal = MakeTmp(statsRt, "HpValue", "0", 20, new Color(1f, 0.75f, 0.25f, 1f),
                new Vector2(0.60f, 0.78f), new Vector2(1f, 1f), TextAlignmentOptions.Right);

            MakeTmp(statsRt, "DmgLabel", "Урон:", 20, new Color(0.85f, 0.85f, 0.95f, 1f),
                new Vector2(0f, 0.58f), new Vector2(0.60f, 0.78f), TextAlignmentOptions.Left);
            var dmgVal = MakeTmp(statsRt, "DmgValue", "0", 20, new Color(1f, 0.75f, 0.25f, 1f),
                new Vector2(0.60f, 0.58f), new Vector2(1f, 0.78f), TextAlignmentOptions.Right);

            MakeTmp(statsRt, "ArmorLabel", "Броня:", 20, new Color(0.85f, 0.85f, 0.95f, 1f),
                new Vector2(0f, 0.38f), new Vector2(0.60f, 0.58f), TextAlignmentOptions.Left);
            var armorVal = MakeTmp(statsRt, "ArmorValue", "0", 20, new Color(1f, 0.75f, 0.25f, 1f),
                new Vector2(0.60f, 0.38f), new Vector2(1f, 0.58f), TextAlignmentOptions.Right);

            MakeTmp(statsRt, "HealLabel", "Лечение:", 20, new Color(0.85f, 0.85f, 0.95f, 1f),
                new Vector2(0f, 0.18f), new Vector2(0.60f, 0.38f), TextAlignmentOptions.Left);
            var healVal = MakeTmp(statsRt, "HealValue", "0", 20, new Color(1f, 0.75f, 0.25f, 1f),
                new Vector2(0.60f, 0.18f), new Vector2(1f, 0.38f), TextAlignmentOptions.Right);

            MakeTmp(statsRt, "CritLabel", "Шанс крита:", 20, new Color(0.85f, 0.85f, 0.95f, 1f),
                new Vector2(0f, -0.02f), new Vector2(0.60f, 0.18f), TextAlignmentOptions.Left);
            var critVal = MakeTmp(statsRt, "CritValue", "0%", 20, new Color(1f, 0.75f, 0.25f, 1f),
                new Vector2(0.60f, -0.02f), new Vector2(1f, 0.18f), TextAlignmentOptions.Right);

            // Equipment: левая колонна | портрет по центру | правая колонна; снизу — два оружия (110×110).
            var equipGo = new GameObject("EquipmentRoot", typeof(RectTransform));
            var equipRt = equipGo.GetComponent<RectTransform>();
            equipRt.SetParent(panelRt, false);
            equipRt.anchorMin = new Vector2(0.02f, 0.30f);
            equipRt.anchorMax = new Vector2(0.72f, 0.92f);
            equipRt.offsetMin = Vector2.zero;
            equipRt.offsetMax = Vector2.zero;

            var equipBg = equipGo.AddComponent<Image>();
            equipBg.color = new Color(0.07f, 0.07f, 0.10f, 0.6f);

            MakeTmp(equipRt, "EquipTitle", "Снаряжение", 26, new Color(1f, 0.75f, 0.25f, 1f),
                new Vector2(0.02f, 0.92f), new Vector2(0.98f, 1f), TextAlignmentOptions.Center);

            const float slotSize = 110f;

            var leftCol = new GameObject("LeftColumn", typeof(RectTransform));
            var leftRt = leftCol.GetComponent<RectTransform>();
            leftRt.SetParent(equipRt, false);
            leftRt.anchorMin = new Vector2(0.02f, 0.34f);
            leftRt.anchorMax = new Vector2(0.22f, 0.98f);
            leftRt.offsetMin = Vector2.zero;
            leftRt.offsetMax = Vector2.zero;
            var leftVlg = leftCol.AddComponent<VerticalLayoutGroup>();
            leftVlg.padding = new RectOffset(4, 4, 8, 8);
            leftVlg.spacing = 10;
            leftVlg.childAlignment = TextAnchor.UpperCenter;
            leftVlg.childControlHeight = true;
            leftVlg.childControlWidth = true;
            leftVlg.childForceExpandHeight = false;
            leftVlg.childForceExpandWidth = false;

            var portraitGo = new GameObject("CharacterPortrait", typeof(RectTransform), typeof(Image), typeof(Outline));
            var portraitRt = portraitGo.GetComponent<RectTransform>();
            portraitRt.SetParent(equipRt, false);
            portraitRt.anchorMin = new Vector2(0.24f, 0.34f);
            portraitRt.anchorMax = new Vector2(0.76f, 0.98f);
            portraitRt.offsetMin = Vector2.zero;
            portraitRt.offsetMax = Vector2.zero;
            var portraitImg = portraitGo.GetComponent<Image>();
            portraitImg.color = new Color(0.12f, 0.12f, 0.18f, 0.95f);
            var portraitOutline = portraitGo.GetComponent<Outline>();
            portraitOutline.effectColor = new Color(1f, 0.75f, 0.25f, 0.35f);
            portraitOutline.effectDistance = new Vector2(2f, -2f);
            MakeTmp(portraitRt, "PortraitPlaceholder", "?", 72, new Color(0.5f, 0.5f, 0.65f, 1f),
                Vector2.zero, Vector2.one, TextAlignmentOptions.Center);

            var rightCol = new GameObject("RightColumn", typeof(RectTransform));
            var rightRt = rightCol.GetComponent<RectTransform>();
            rightRt.SetParent(equipRt, false);
            rightRt.anchorMin = new Vector2(0.78f, 0.34f);
            rightRt.anchorMax = new Vector2(0.98f, 0.98f);
            rightRt.offsetMin = Vector2.zero;
            rightRt.offsetMax = Vector2.zero;
            var rightVlg = rightCol.AddComponent<VerticalLayoutGroup>();
            rightVlg.padding = new RectOffset(4, 4, 8, 8);
            rightVlg.spacing = 10;
            rightVlg.childAlignment = TextAnchor.UpperCenter;
            rightVlg.childControlHeight = true;
            rightVlg.childControlWidth = true;
            rightVlg.childForceExpandHeight = false;
            rightVlg.childForceExpandWidth = false;

            var bottomRow = new GameObject("WeaponsRow", typeof(RectTransform));
            var bottomRt = bottomRow.GetComponent<RectTransform>();
            bottomRt.SetParent(equipRt, false);
            bottomRt.anchorMin = new Vector2(0.08f, 0.02f);
            bottomRt.anchorMax = new Vector2(0.92f, 0.30f);
            bottomRt.offsetMin = Vector2.zero;
            bottomRt.offsetMax = Vector2.zero;
            var hlg = bottomRow.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(8, 8, 4, 8);
            hlg.spacing = 16;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childControlHeight = true;
            hlg.childControlWidth = true;
            hlg.childForceExpandHeight = false;
            hlg.childForceExpandWidth = false;

            EquipmentSlotView AddEquipSlot(Transform parent, EquipmentSlotId id, string label)
            {
                GameObject slotGo;
                if (equipmentSlotPrefab != null)
                    slotGo = (GameObject)PrefabUtility.InstantiatePrefab(equipmentSlotPrefab);
                else
                    slotGo = new GameObject("EquipmentSlotView", typeof(RectTransform), typeof(Image), typeof(Button), typeof(EquipmentSlotView));

                slotGo.name = label;
                var slotRt = slotGo.GetComponent<RectTransform>();
                slotRt.SetParent(parent, false);
                slotRt.sizeDelta = new Vector2(slotSize, slotSize);

                var le = slotGo.GetComponent<LayoutElement>();
                if (le == null) le = slotGo.AddComponent<LayoutElement>();
                le.minWidth = le.preferredWidth = slotSize;
                le.minHeight = le.preferredHeight = slotSize;

                var slotView = slotGo.GetComponent<EquipmentSlotView>();
                slotView.Init(id);

                MakeTmp(slotRt, "Label", label, 16, new Color(0.9f, 0.9f, 1f, 0.9f),
                    new Vector2(0f, 0f), new Vector2(1f, 0.22f), TextAlignmentOptions.Center);
                return slotView;
            }

            AddEquipSlot(leftRt, EquipmentSlotId.Helmet, "Шлем");
            AddEquipSlot(leftRt, EquipmentSlotId.Shoulders, "Плечи");
            AddEquipSlot(leftRt, EquipmentSlotId.Chest, "Тело");

            AddEquipSlot(rightRt, EquipmentSlotId.Gloves, "Перчатки");
            AddEquipSlot(rightRt, EquipmentSlotId.Legs, "Ноги");
            AddEquipSlot(rightRt, EquipmentSlotId.Feet, "Ступни");

            AddEquipSlot(bottomRt, EquipmentSlotId.WeaponLeft, "Оружие (Л)");
            AddEquipSlot(bottomRt, EquipmentSlotId.WeaponRight, "Оружие (П)");

            // Inventory (25 cells) on the right side.
            var invGo = new GameObject("InventoryRoot", typeof(RectTransform), typeof(Image), typeof(GridLayoutGroup));
            var invRt = invGo.GetComponent<RectTransform>();
            invRt.SetParent(panelRt, false);
            invRt.anchorMin = new Vector2(0.45f, 0.10f);
            invRt.anchorMax = new Vector2(0.98f, 0.90f);
            invRt.offsetMin = Vector2.zero;
            invRt.offsetMax = Vector2.zero;
            invGo.GetComponent<Image>().color = new Color(0.07f, 0.07f, 0.10f, 0.6f);

            MakeTmp(invRt, "InvTitle", "Сундук", 26, new Color(1f, 0.75f, 0.25f, 1f),
                new Vector2(0.02f, 0.94f), new Vector2(0.98f, 1f), TextAlignmentOptions.Center);
            var invTitleGo = invRt.Find("InvTitle");
            if (invTitleGo != null)
            {
                var titleLe = invTitleGo.gameObject.GetComponent<LayoutElement>();
                if (titleLe == null) titleLe = invTitleGo.gameObject.AddComponent<LayoutElement>();
                titleLe.ignoreLayout = true;
            }

            var grid = invGo.GetComponent<GridLayoutGroup>();
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 5;
            grid.cellSize = new Vector2(110, 110);
            grid.spacing = new Vector2(10, 10);
            grid.padding = new RectOffset(16, 16, 60, 16);

            // 25 placeholder cells.
            for (int i = 0; i < 25; i++)
            {
                GameObject cellGo;
                if (itemSlotPrefab != null)
                    cellGo = (GameObject)PrefabUtility.InstantiatePrefab(itemSlotPrefab);
                else
                    cellGo = new GameObject("ItemSlotView", typeof(RectTransform), typeof(Image), typeof(ItemSlotView));
                cellGo.name = "Cell_" + i;
                var cellRt = cellGo.GetComponent<RectTransform>();
                cellRt.SetParent(invRt, false);
            }

            // Wire view + controller
            var view = screenGo.GetComponent<CharacterScreenView>();
            SetPrivateField(view, "root", cg);
            SetPrivateField(view, "panelRoot", panelRt);
            SetPrivateField(view, "hpText", hpVal);
            SetPrivateField(view, "damageText", dmgVal);
            SetPrivateField(view, "armorText", armorVal);
            SetPrivateField(view, "healText", healVal);
            SetPrivateField(view, "critText", critVal);
            SetPrivateField(view, "equipmentRoot", equipRt);
            SetPrivateField(view, "equipmentSlotPrefab", equipmentSlotPrefab != null ? equipmentSlotPrefab.GetComponent<EquipmentSlotView>() : null);
            SetPrivateField(view, "inventoryRoot", invRt);
            SetPrivateField(view, "inventorySlotPrefab", itemSlotPrefab != null ? itemSlotPrefab.GetComponent<ItemSlotView>() : null);
            SetPrivateField(view, "inventorySize", 25);

            var controller = screenGo.AddComponent<CharacterScreenController>();
            SetPrivateField(controller, "view", view);
            SetPrivateField(controller, "openButton", openBtn);
            SetPrivateField(controller, "closeButton", closeBtn);
            SetPrivateField(controller, "levelText", levelTmp);
            SetPrivateField(controller, "startHidden", true);
            SetPrivateField(controller, "actionModalPrefab", actionModalPrefab != null ? actionModalPrefab.GetComponent<CharacterItemActionModalView>() : null);
            SetPrivateField(controller, "infoModalPrefab", infoModalPrefab != null ? infoModalPrefab.GetComponent<CharacterItemInfoModalView>() : null);

            ReplacePrefab(root, CharacterHudPrefabPath);
            Object.DestroyImmediate(root);
        }

        private static GameObject MakeButton(Transform parent, string name, string text, Color bgColor, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            go.GetComponent<Image>().color = bgColor;
            var outline = go.GetComponent<Outline>();
            outline.effectColor = new Color(1f, 1f, 1f, 0.25f);
            outline.effectDistance = new Vector2(1f, -1f);

            MakeTmp(rt, "Text", text, 26, Color.white, Vector2.zero, Vector2.one, TextAlignmentOptions.Center);
            return go;
        }

        private static Button MakeRectButton(Transform parent, string name, string text, Vector2 aMin, Vector2 aMax, int fontSize)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            go.GetComponent<Image>().color = new Color(0.25f, 0.25f, 0.34f, 1f);
            var outline = go.GetComponent<Outline>();
            outline.effectColor = new Color(1f, 1f, 1f, 0.22f);
            outline.effectDistance = new Vector2(1f, -1f);

            MakeTmp(rt, "Text", text, fontSize, Color.white, Vector2.zero, Vector2.one, TextAlignmentOptions.Center);
            return go.GetComponent<Button>();
        }

        private static Button MakeLayoutButton(Transform parent, string name, string text)
        {
            var button = MakeRectButton(parent, name, text, new Vector2(0f, 1f), new Vector2(1f, 1f), 24);
            var rt = button.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, 56f);

            var le = button.gameObject.AddComponent<LayoutElement>();
            le.minHeight = 56f;
            le.preferredHeight = 56f;
            return button;
        }

        private static TMP_Text MakeTmp(Transform parent, string name, string text, int size, Color color,
            Vector2 aMin, Vector2 aMax, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = align;
            tmp.raycastTarget = false;
            return tmp;
        }

        private static void ReplacePrefab(GameObject root, string prefabPath)
        {
            EnsureDir(Path.GetDirectoryName(prefabPath));
            // Не удалять asset перед сохранением: иначе ломаются GUID вложенных префабов (Missing Nested Prefab).
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }

        private static void EnsureDir(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }

        private static void SetPrivateField(Object target, string fieldName, object value)
        {
            if (target == null || string.IsNullOrEmpty(fieldName)) return;
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var f = target.GetType().GetField(fieldName, flags);
            if (f == null) return;
            f.SetValue(target, value);
            EditorUtility.SetDirty(target);
        }
    }
}

