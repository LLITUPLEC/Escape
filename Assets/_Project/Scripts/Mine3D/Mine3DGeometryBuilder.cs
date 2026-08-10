using UnityEngine;
using UnityEngine.UI;

namespace Project.Mine3D
{
    /// <summary>
    /// Собирает демо-шахту из примитивов: 3 грани × 12 этажей, лампы, декор, барьеры, капсулы.
    /// </summary>
    public static class Mine3DGeometryBuilder
    {
        public const int FloorCount = 12;
        public const float RoomWidth = 7.2f;
        public const float RoomDepth = 5.4f;
        public const float BarrierLocalZ = -2.2f;
        /// <summary>Смещение центра комнаты от оси к камере (локальный -Z грани).</summary>
        public const float RoomCenterOffsetZ = -(RoomDepth * 0.5f + 1.1f);
        /// <summary>Дистанция камеры от центра комнаты вдоль -Z.</summary>
        public const float CameraDistanceFromRoom = 9.5f;

        /// <summary>Фиксированные высоты этажей (3.85…5.3). Боссы 4/8/12 = 5.3.</summary>
        private static readonly float[] FloorOpenHeights =
        {
            4.10f, 3.85f, 4.40f, 5.30f,
            4.00f, 4.60f, 3.95f, 5.30f,
            4.25f, 3.90f, 4.75f, 5.30f
        };

        public struct BuiltShaft
        {
            public Transform Root;
            public float TopFloorCenterY;
            public float BottomFloorCenterY;
            public float CameraZ;
        }

        public static float GetFloorOpenHeight(int floor)
        {
            if (floor < 1 || floor > FloorCount)
                return 4.2f;
            if (floor == 4 || floor == 8 || floor == 12)
                return 5.3f;
            return FloorOpenHeights[floor - 1];
        }

        /// <summary>Центр этажа по Y. Зазора между этажами нет: стык потолок-пол.</summary>
        public static float FloorCenterY(int floor)
        {
            var y = 0f;
            for (var i = 1; i < floor; i++)
                y -= GetFloorOpenHeight(i);
            y -= GetFloorOpenHeight(floor) * 0.5f;
            return y;
        }

        public static float TopOfShaftY() => GetFloorOpenHeight(1) * 0.5f;

        public static float BottomOfShaftY()
        {
            var y = 0f;
            for (var i = 1; i <= FloorCount; i++)
                y -= GetFloorOpenHeight(i);
            return y;
        }

        public static BuiltShaft Build(Transform parent, Material rock, Material metal, Material rust,
            Material lampGlass, Material barrierMat, Material accentEasy, Material accentMedium, Material accentHard,
            Material wood = null, Material darkRock = null)
        {
            wood ??= CreateLitMaterial("MineWood", new Color(0.28f, 0.18f, 0.10f), 0.02f, 0.2f);
            darkRock ??= CreateLitMaterial("MineDarkRock", new Color(0.10f, 0.09f, 0.08f), 0.04f, 0.12f);

            var rootGo = new GameObject("MineShaftRoot");
            var root = rootGo.transform;
            if (parent != null)
                root.SetParent(parent, false);
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;

            BuildCentralColumn(root, rock, metal);

            CreateFace(root, Mine3DShaftController.Easy, accentEasy, rock, metal, rust, lampGlass, barrierMat, wood, darkRock);
            CreateFace(root, Mine3DShaftController.Medium, accentMedium, rock, metal, rust, lampGlass, barrierMat, wood, darkRock);
            CreateFace(root, Mine3DShaftController.Hard, accentHard, rock, metal, rust, lampGlass, barrierMat, wood, darkRock);

            var topY = FloorCenterY(1);
            var bottomY = FloorCenterY(FloorCount);
            var cameraZ = RoomCenterOffsetZ - CameraDistanceFromRoom;

            BuildCaveEnclosure(parent, darkRock, rock, topY, bottomY, cameraZ);

            return new BuiltShaft
            {
                Root = root,
                TopFloorCenterY = topY,
                BottomFloorCenterY = bottomY,
                CameraZ = cameraZ
            };
        }

        public static void BuildCaveEnclosure(Transform parent, Material darkRock, Material rock,
            float topY, float bottomY, float cameraZ)
        {
            var midY = (topY + bottomY) * 0.5f;
            var height = Mathf.Abs(topY - bottomY) + 8f;

            var cave = new GameObject("MineCaveEnclosure").transform;
            if (parent != null)
                cave.SetParent(parent, false);
            cave.localPosition = Vector3.zero;

            var wingDepth = Mathf.Abs(cameraZ) + RoomDepth + 4f;
            var wingZ = cameraZ * 0.35f;
            CreateBox(cave, "CaveWall_L",
                new Vector3(4.5f, height, wingDepth),
                new Vector3(-RoomWidth * 0.5f - 3.2f, midY, wingZ),
                darkRock);
            CreateBox(cave, "CaveWall_R",
                new Vector3(4.5f, height, wingDepth),
                new Vector3(RoomWidth * 0.5f + 3.2f, midY, wingZ),
                darkRock);

            CreateBox(cave, "CaveFloor",
                new Vector3(RoomWidth + 14f, 2.2f, wingDepth + 2f),
                new Vector3(0f, bottomY - 3f, wingZ),
                rock);
            CreateBox(cave, "CaveCeiling",
                new Vector3(RoomWidth + 14f, 2.2f, wingDepth + 2f),
                new Vector3(0f, topY + 3f, wingZ),
                darkRock);

            CreateBox(cave, "CaveBack",
                new Vector3(RoomWidth + 10f, height, 3f),
                new Vector3(0f, midY, 3.2f),
                darkRock);

            for (var i = 0; i < 8; i++)
            {
                var side = i % 2 == 0 ? -1f : 1f;
                var y = topY - i * (height / 9f);
                CreateBox(cave, "RockChunk_" + i,
                    new Vector3(1.2f + (i % 3) * 0.35f, 1.1f + (i % 2) * 0.5f, 1.4f),
                    new Vector3(side * (RoomWidth * 0.5f + 1.6f), y, RoomCenterOffsetZ - 1.2f - (i % 3) * 0.4f),
                    rock);
            }
        }

        private static void BuildCentralColumn(Transform root, Material rock, Material metal)
        {
            var midY = (FloorCenterY(1) + FloorCenterY(FloorCount)) * 0.5f;
            var height = Mathf.Abs(TopOfShaftY() - BottomOfShaftY()) + 2f;
            CreateBox(root, "RearSpine",
                new Vector3(0.45f, height, 0.45f),
                new Vector3(0f, midY, 0.4f),
                rock);
            _ = metal;
        }

        private static void CreateFace(Transform shaftRoot, string difficulty, Material accent,
            Material rock, Material metal, Material rust, Material lampGlass, Material barrierMat,
            Material wood, Material darkRock)
        {
            var faceGo = new GameObject("Face_" + difficulty);
            var face = faceGo.transform;
            face.SetParent(shaftRoot, false);
            face.localRotation = Quaternion.Euler(0f, Mine3DShaftController.FaceLocalYaw(difficulty), 0f);
            face.localPosition = Vector3.zero;

            for (var floor = 1; floor <= FloorCount; floor++)
                CreateFloor(face, floor, difficulty, accent, rock, metal, rust, lampGlass, barrierMat, wood, darkRock);
        }

        private static void CreateFloor(Transform face, int floor, string difficulty, Material accent,
            Material rock, Material metal, Material rust, Material lampGlass, Material barrierMat,
            Material wood, Material darkRock)
        {
            var h = GetFloorOpenHeight(floor);
            var y = FloorCenterY(floor);
            var floorGo = new GameObject("Floor_" + floor);
            var floorT = floorGo.transform;
            floorT.SetParent(face, false);
            floorT.localPosition = new Vector3(0f, y, RoomCenterOffsetZ);

            var view = floorGo.AddComponent<Mine3DFloorView>();
            view.Floor = floor;
            view.Difficulty = difficulty;

            CreateBox(floorT, "FloorSlab",
                new Vector3(RoomWidth + 1.2f, 0.45f, RoomDepth + 0.6f),
                new Vector3(0f, -h * 0.5f, 0f),
                darkRock);
            CreateBox(floorT, "CeilingSlab",
                new Vector3(RoomWidth + 1.2f, 0.4f, RoomDepth + 0.6f),
                new Vector3(0f, h * 0.5f, 0f),
                darkRock);
            CreateBox(floorT, "BackWall",
                new Vector3(RoomWidth + 0.8f, h + 0.2f, 0.85f),
                new Vector3(0f, 0f, RoomDepth * 0.5f - 0.1f),
                rock);

            CreateBox(floorT, "LeftWall",
                new Vector3(0.85f, h + 0.2f, RoomDepth),
                new Vector3(-RoomWidth * 0.5f - 0.15f, 0f, 0f),
                rock);
            CreateBox(floorT, "RightWall",
                new Vector3(0.85f, h + 0.2f, RoomDepth),
                new Vector3(RoomWidth * 0.5f + 0.15f, 0f, 0f),
                rock);
            CreateBox(floorT, "LeftWing",
                new Vector3(2.2f, h + 0.6f, RoomDepth * 0.95f),
                new Vector3(-RoomWidth * 0.5f - 1.5f, 0f, -RoomDepth * 0.25f),
                darkRock);
            CreateBox(floorT, "RightWing",
                new Vector3(2.2f, h + 0.6f, RoomDepth * 0.95f),
                new Vector3(RoomWidth * 0.5f + 1.5f, 0f, -RoomDepth * 0.25f),
                darkRock);

            CreateBox(floorT, "TimberLeft",
                new Vector3(0.28f, h * 0.92f, 0.28f),
                new Vector3(-RoomWidth * 0.42f, 0f, -RoomDepth * 0.28f),
                wood);
            CreateBox(floorT, "TimberRight",
                new Vector3(0.28f, h * 0.92f, 0.28f),
                new Vector3(RoomWidth * 0.42f, 0f, -RoomDepth * 0.28f),
                wood);
            CreateBox(floorT, "TimberBeam",
                new Vector3(RoomWidth * 0.88f, 0.22f, 0.28f),
                new Vector3(0f, h * 0.38f, -RoomDepth * 0.28f),
                wood);

            CreateBox(floorT, "Grate",
                new Vector3(RoomWidth * 0.88f, 0.07f, RoomDepth * 0.82f),
                new Vector3(0f, -h * 0.5f + 0.32f, -0.1f),
                metal);

            CreateCylinder(floorT, "PipeLeft",
                new Vector3(0.14f, h * 0.48f, 0.14f),
                new Vector3(-RoomWidth * 0.36f, 0f, RoomDepth * 0.12f),
                rust);
            CreateCylinder(floorT, "PipeRight",
                new Vector3(0.14f, h * 0.48f, 0.14f),
                new Vector3(RoomWidth * 0.36f, 0f, RoomDepth * 0.12f),
                rust);

            var rungCount = Mathf.Clamp(Mathf.RoundToInt(h / 0.75f), 5, 9);
            var rungSpan = h * 0.72f;
            for (var i = 0; i < rungCount; i++)
            {
                var t = rungCount == 1 ? 0f : i / (float)(rungCount - 1);
                CreateBox(floorT, "LadderRung_" + i,
                    new Vector3(0.55f, 0.06f, 0.08f),
                    new Vector3(-RoomWidth * 0.38f, -rungSpan * 0.5f + t * rungSpan, -RoomDepth * 0.35f),
                    metal);
            }

            var lampAnchor = new GameObject("Lamp").transform;
            lampAnchor.SetParent(floorT, false);
            lampAnchor.localPosition = new Vector3(0f, h * 0.42f, -0.2f);
            CreateBox(lampAnchor, "LampHousing", new Vector3(2.4f, 0.12f, 0.28f), Vector3.zero, metal);
            CreateBox(lampAnchor, "LampTube", new Vector3(2.1f, 0.08f, 0.14f), new Vector3(0f, -0.08f, 0f), lampGlass);

            var lightGo = new GameObject("FloorLight");
            lightGo.transform.SetParent(lampAnchor, false);
            lightGo.transform.localPosition = new Vector3(0f, -0.25f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 9f;
            light.intensity = 6.5f;
            light.color = new Color(1f, 0.93f, 0.78f);
            light.shadows = LightShadows.None;

            CreateBox(floorT, "Crate",
                new Vector3(0.85f, 0.7f, 0.7f),
                new Vector3(RoomWidth * 0.28f, -h * 0.5f + 0.55f, -RoomDepth * 0.2f),
                rust);
            CreateCylinder(floorT, "Barrel",
                new Vector3(0.45f, 0.55f, 0.45f),
                new Vector3(-RoomWidth * 0.22f, -h * 0.5f + 0.55f, RoomDepth * 0.05f),
                rust);

            var monsterRoot = new GameObject("MonsterRoot").transform;
            monsterRoot.SetParent(floorT, false);
            CreateMonsters(monsterRoot, floor, difficulty, accent, h);
            view.MonsterRoot = monsterRoot;

            var barrierRoot = new GameObject("BarrierRoot").transform;
            barrierRoot.SetParent(floorT, false);
            CreateBarrier(barrierRoot, floor, barrierMat, metal, h);
            view.BarrierRoot = barrierRoot;

            // До ApplyRows не показываем барьер поверх монстра (иначе «все этажи — барьеры»).
            barrierRoot.gameObject.SetActive(false);
            monsterRoot.gameObject.SetActive(false);

            CreateFloorUi(floorT, floor, h, view);
            view.BindInteractables();
        }

        private static void CreateFloorUi(Transform floorT, int floor, float roomH, Mine3DFloorView view)
        {
            // Без GraphicRaycaster: иначе IsPointerOverGameObject блокирует скролл/клики по шахте.
            var uiGo = new GameObject("FloorUi", typeof(RectTransform), typeof(Canvas));
            var canvas = uiGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 20;
            var rt = uiGo.GetComponent<RectTransform>();
            rt.SetParent(floorT, false);
            rt.sizeDelta = new Vector2(780f, 420f);
            ApplyReadableFloorUiTransform(rt, roomH);

            CreateUiText(rt, "Label", floor + " этаж", 34, TextAnchor.UpperRight,
                new Vector2(0.38f, 0.72f), new Vector2(0.96f, 0.95f), new Color(0.92f, 0.9f, 0.82f));

            CreateUiText(rt, "StateText", "", 28, TextAnchor.MiddleCenter,
                new Vector2(0.08f, 0.05f), new Vector2(0.92f, 0.28f), new Color(0.96f, 0.78f, 0.45f));

            // Невидимые кнопки-заглушки для совместимости с MineSceneController (без спрайтов/рейкаста).
            var monsterBtn = CreateUiButton(rt, "MonsterButton", "",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Color(1f, 1f, 1f, 0f));
            var lockBtn = CreateUiButton(rt, "LockButton", "",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Color(1f, 1f, 1f, 0f));
            DisableUiRaycast(monsterBtn);
            DisableUiRaycast(lockBtn);

            view.FloorUi = rt;
            view.MonsterButton = monsterBtn;
            view.LockButton = lockBtn;
        }

        /// <summary>
        /// FloorUi: Y=180 (лицом к камере) + scale.x &lt; 0 (иначе Legacy Text зеркалит → «жатэ N»).
        /// </summary>
        public static void ApplyReadableFloorUiTransform(RectTransform rt, float roomH)
        {
            if (rt == null) return;
            rt.localPosition = new Vector3(0f, roomH * 0.05f, BarrierLocalZ + 0.2f);
            rt.localRotation = Quaternion.Euler(0f, 180f, 0f);
            rt.sizeDelta = new Vector2(780f, 420f);
            rt.localScale = new Vector3(-0.0065f, 0.0065f, 0.0065f);
        }

        private static void DisableUiRaycast(Button btn)
        {
            if (btn == null) return;
            btn.interactable = false;
            var imgs = btn.GetComponentsInChildren<Image>(true);
            for (var i = 0; i < imgs.Length; i++)
            {
                if (imgs[i] == null) continue;
                imgs[i].raycastTarget = false;
                imgs[i].color = new Color(1f, 1f, 1f, 0f);
            }
        }

        private static void CreateMonsters(Transform root, int floor, string difficulty, Material accent, float roomH)
        {
            var count = (floor == 4 || floor == 8 || floor == 12) ? 1
                : difficulty == Mine3DShaftController.Easy ? 1
                : difficulty == Mine3DShaftController.Medium ? 1 + (floor % 2)
                : 1 + (floor % 3);

            var bossScale = (floor == 4 || floor == 8 || floor == 12) ? 1.35f : 1f;
            for (var i = 0; i < count; i++)
            {
                var x = (i - (count - 1) * 0.5f) * 1.35f;
                var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                capsule.name = "MonsterCapsule_" + (i + 1);
                capsule.transform.SetParent(root, false);
                // Ближе к камере, чем балки/лестница, чтобы капсула была видна и кликабельна.
                capsule.transform.localPosition = new Vector3(x, -roomH * 0.5f + 1.15f * bossScale, -1.65f);
                capsule.transform.localScale = new Vector3(0.9f, 1.05f, 0.9f) * bossScale;
                ApplyMaterial(capsule, accent);

                var eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                eye.name = "Eye";
                eye.transform.SetParent(capsule.transform, false);
                eye.transform.localPosition = new Vector3(0f, 0.35f, -0.42f);
                eye.transform.localScale = new Vector3(0.28f, 0.18f, 0.18f);
                var eyeCol = eye.GetComponent<Collider>();
                if (eyeCol != null)
                    Object.Destroy(eyeCol);
                var eyeMat = CreateUnlitMaterial("Eye", new Color(1f, 0.15f, 0.12f));
                ApplyMaterial(eye, eyeMat);
            }
        }

        private static void CreateBarrier(Transform root, int floor, Material barrierMat, Material metal, float roomH)
        {
            var z = BarrierLocalZ;
            CreateBox(root, "BarrierPlate",
                new Vector3(RoomWidth * 0.92f, roomH * 0.88f, 0.18f),
                new Vector3(0f, 0f, z),
                barrierMat);

            CreateBox(root, "BarrierBoltTL", new Vector3(0.22f, 0.22f, 0.08f),
                new Vector3(-RoomWidth * 0.38f, roomH * 0.32f, z - 0.12f), metal);
            CreateBox(root, "BarrierBoltTR", new Vector3(0.22f, 0.22f, 0.08f),
                new Vector3(RoomWidth * 0.38f, roomH * 0.32f, z - 0.12f), metal);
            CreateBox(root, "BarrierBoltBL", new Vector3(0.22f, 0.22f, 0.08f),
                new Vector3(-RoomWidth * 0.38f, -roomH * 0.32f, z - 0.12f), metal);
            CreateBox(root, "BarrierBoltBR", new Vector3(0.22f, 0.22f, 0.08f),
                new Vector3(RoomWidth * 0.38f, -roomH * 0.32f, z - 0.12f), metal);

            var label = new GameObject("BarrierLabel");
            label.transform.SetParent(root, false);
            label.transform.localPosition = new Vector3(0f, 0.35f, z - 0.14f);
            var tm = label.AddComponent<TextMesh>();
            tm.text = "БАРЬЕР\nЭтаж " + floor + "\nруда + золото";
            tm.fontSize = 48;
            tm.characterSize = 0.045f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = new Color(0.95f, 0.82f, 0.55f);
            tm.fontStyle = FontStyle.Bold;
            ApplyTextMeshDepthMaterial(tm, tm.color);

            var cost = new GameObject("BarrierCost");
            cost.transform.SetParent(root, false);
            cost.transform.localPosition = new Vector3(0f, -0.55f, z - 0.14f);
            var costTm = cost.AddComponent<TextMesh>();
            costTm.text = "руда " + (200 + floor * 45) + "   золото " + (800 + floor * 220);
            costTm.fontSize = 42;
            costTm.characterSize = 0.04f;
            costTm.anchor = TextAnchor.MiddleCenter;
            costTm.alignment = TextAlignment.Center;
            costTm.color = new Color(0.85f, 0.88f, 0.92f);
            ApplyTextMeshDepthMaterial(costTm, costTm.color);
        }

        private static Button CreateUiButton(Transform parent, string name, string label, Vector2 aMin, Vector2 aMax, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            go.GetComponent<Image>().color = color;
            CreateUiText(go.transform, "Label", label, 22, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, Color.white);
            return go.GetComponent<Button>();
        }

        private static Text CreateUiText(Transform parent, string name, string value, int size, TextAnchor anchor,
            Vector2 aMin, Vector2 aMax, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                        ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.text = value;
            text.fontSize = size;
            text.color = color;
            text.alignment = anchor;
            text.raycastTarget = false;
            return text;
        }

        private static GameObject CreateBox(Transform parent, string name, Vector3 scale, Vector3 localPos, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            ApplyMaterial(go, mat);
            return go;
        }

        private static GameObject CreateCylinder(Transform parent, string name, Vector3 scale, Vector3 localPos, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            ApplyMaterial(go, mat);
            return go;
        }

        private static void ApplyMaterial(GameObject go, Material mat)
        {
            if (go == null || mat == null) return;
            var r = go.GetComponent<MeshRenderer>();
            if (r != null)
                r.sharedMaterial = mat;
        }

        private static Material _textDepthMat;

        private static void ApplyTextMeshDepthMaterial(TextMesh tm, Color color)
        {
            if (tm == null) return;
            var r = tm.GetComponent<MeshRenderer>();
            if (r == null) return;

            if (_textDepthMat == null)
            {
                var shader = Shader.Find("Escape/Mine3DText")
                             ?? Shader.Find("GUI/Text Shader");
                _textDepthMat = new Material(shader) { name = "Mine3DTextDepth" };
            }

            var mat = new Material(_textDepthMat);
            if (tm.font != null && tm.font.material != null && tm.font.material.mainTexture != null)
                mat.mainTexture = tm.font.material.mainTexture;
            mat.color = color;
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        public static Material CreateLitMaterial(string name, Color color, float metallic = 0.05f, float smoothness = 0.35f)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit")
                         ?? Shader.Find("Lit")
                         ?? Shader.Find("Standard");
            var mat = new Material(shader) { name = name };
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", color);
            if (mat.HasProperty("_Metallic"))
                mat.SetFloat("_Metallic", metallic);
            if (mat.HasProperty("_Smoothness"))
                mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Glossiness"))
                mat.SetFloat("_Glossiness", smoothness);
            return mat;
        }

        public static Material CreateUnlitMaterial(string name, Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Unlit/Color")
                         ?? Shader.Find("Sprites/Default");
            var mat = new Material(shader) { name = name };
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", color);
            return mat;
        }
    }
}
