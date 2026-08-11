using UnityEngine;
using UnityEngine.UI;

namespace Project.Mine3D
{
    /// <summary>
    /// Собирает демо-шахту из примитивов: 3 грани × 12 этажей с процедурным износом
    /// (неровный пол, дыры, обвалы, обугленные стены), лампы, декор, барьеры, капсулы.
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

        private enum FloorWear
        {
            Intact = 0,
            Uneven = 1,
            Holed = 2,
            Burned = 3,
            Collapsed = 4
        }

        private struct FloorStyle
        {
            public FloorWear Wear;
            public int Seed;
            public bool FloorHole;
            public bool CeilingGap;
            public bool LeftWallShort;
            public bool RightWallShort;
            public bool BackWallBroken;
            public bool CharredTimber;
            public bool SkipCrate;
            public bool SkipBarrel;
            public bool ExtraDebris;
            public int HoleQuad; // 0=FL, 1=FR, 2=BL, 3=BR
        }

        public static BuiltShaft Build(Transform parent, Material rock, Material metal, Material rust,
            Material lampGlass, Material barrierMat, Material accentEasy, Material accentMedium, Material accentHard,
            Material wood = null, Material darkRock = null)
        {
            wood ??= CreateLitMaterial("MineWood", new Color(0.28f, 0.18f, 0.10f), 0.02f, 0.2f);
            darkRock ??= CreateLitMaterial("MineDarkRock", new Color(0.10f, 0.09f, 0.08f), 0.04f, 0.12f);
            var charred = CreateLitMaterial("MineCharred", new Color(0.07f, 0.06f, 0.05f), 0.08f, 0.08f);
            var ash = CreateLitMaterial("MineAsh", new Color(0.16f, 0.14f, 0.12f), 0.04f, 0.12f);

            var rootGo = new GameObject("MineShaftRoot");
            var root = rootGo.transform;
            if (parent != null)
                root.SetParent(parent, false);
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;

            BuildCentralColumn(root, rock, metal);

            CreateFace(root, Mine3DShaftController.Easy, accentEasy, rock, metal, rust, lampGlass, barrierMat, wood, darkRock, charred, ash);
            CreateFace(root, Mine3DShaftController.Medium, accentMedium, rock, metal, rust, lampGlass, barrierMat, wood, darkRock, charred, ash);
            CreateFace(root, Mine3DShaftController.Hard, accentHard, rock, metal, rust, lampGlass, barrierMat, wood, darkRock, charred, ash);

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
            Material wood, Material darkRock, Material charred, Material ash)
        {
            var faceGo = new GameObject("Face_" + difficulty);
            var face = faceGo.transform;
            face.SetParent(shaftRoot, false);
            face.localRotation = Quaternion.Euler(0f, Mine3DShaftController.FaceLocalYaw(difficulty), 0f);
            face.localPosition = Vector3.zero;

            for (var floor = 1; floor <= FloorCount; floor++)
                CreateFloor(face, floor, difficulty, accent, rock, metal, rust, lampGlass, barrierMat, wood, darkRock, charred, ash);
        }

        private static FloorStyle ResolveFloorStyle(int floor, string difficulty)
        {
            var diffBias = difficulty == Mine3DShaftController.Hard ? 211
                : difficulty == Mine3DShaftController.Medium ? 97
                : 17;
            var seed = floor * 37 + diffBias * 3 + floor * floor;
            var rng = new System.Random(seed);

            FloorWear wear;
            if (floor == 4 || floor == 8 || floor == 12)
                wear = FloorWear.Collapsed;
            else if (floor == 1)
                wear = FloorWear.Uneven;
            else
            {
                var roll = rng.Next(0, 100);
                if (roll < 18) wear = FloorWear.Intact;
                else if (roll < 40) wear = FloorWear.Uneven;
                else if (roll < 62) wear = FloorWear.Holed;
                else if (roll < 82) wear = FloorWear.Burned;
                else wear = FloorWear.Collapsed;
            }

            var style = new FloorStyle
            {
                Wear = wear,
                Seed = seed,
                HoleQuad = rng.Next(0, 4),
                FloorHole = wear == FloorWear.Holed || wear == FloorWear.Collapsed || (wear == FloorWear.Burned && rng.Next(0, 100) < 35),
                CeilingGap = wear == FloorWear.Collapsed || wear == FloorWear.Holed || (wear != FloorWear.Intact && rng.Next(0, 100) < 55),
                LeftWallShort = wear == FloorWear.Burned || wear == FloorWear.Collapsed || rng.Next(0, 100) < 28,
                RightWallShort = wear == FloorWear.Burned || wear == FloorWear.Collapsed || rng.Next(0, 100) < 28,
                BackWallBroken = wear == FloorWear.Burned || wear == FloorWear.Collapsed || rng.Next(0, 100) < 40,
                CharredTimber = wear == FloorWear.Burned || (wear == FloorWear.Collapsed && rng.Next(0, 100) < 50),
                SkipCrate = rng.Next(0, 100) < 32,
                SkipBarrel = rng.Next(0, 100) < 38,
                ExtraDebris = wear == FloorWear.Collapsed || wear == FloorWear.Holed || wear == FloorWear.Burned
            };

            // Не оставляем обе стены целыми на burned/collapsed.
            if (wear == FloorWear.Burned && !style.LeftWallShort && !style.RightWallShort)
                style.LeftWallShort = true;
            if (wear == FloorWear.Collapsed)
                style.CeilingGap = true;

            return style;
        }

        private static float StyleNoise(int seed, int channel)
        {
            var n = Mathf.Sin(seed * 12.9898f + channel * 78.233f) * 43758.5453f;
            return n - Mathf.Floor(n);
        }

        private static void CreateFloor(Transform face, int floor, string difficulty, Material accent,
            Material rock, Material metal, Material rust, Material lampGlass, Material barrierMat,
            Material wood, Material darkRock, Material charred, Material ash)
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

            var style = ResolveFloorStyle(floor, difficulty);
            var timberMat = style.CharredTimber ? charred : wood;
            var floorBaseY = -h * 0.5f;

            BuildVariedFloor(floorT, style, floorBaseY, darkRock, rock, ash);
            BuildVariedCeiling(floorT, style, h, darkRock, rock, ash);
            BuildVariedWalls(floorT, style, h, rock, darkRock, ash);
            BuildTimber(floorT, style, h, timberMat, charred);
            BuildFloorDecor(floorT, style, h, floorBaseY, metal, rust, rock, ash);

            var rungCount = Mathf.Clamp(Mathf.RoundToInt(h / 0.75f), 5, 9);
            var rungSpan = h * 0.72f;
            for (var i = 0; i < rungCount; i++)
            {
                var t = rungCount == 1 ? 0f : i / (float)(rungCount - 1);
                var rung = CreateBox(floorT, "LadderRung_" + i,
                    new Vector3(0.55f, 0.06f, 0.08f),
                    new Vector3(-RoomWidth * 0.38f, -rungSpan * 0.5f + t * rungSpan, -RoomDepth * 0.35f),
                    metal);
                // Лёгкий износ лестницы
                if (style.Wear != FloorWear.Intact && StyleNoise(style.Seed, 40 + i) > 0.82f)
                    rung.transform.localRotation = Quaternion.Euler(0f, 0f, (StyleNoise(style.Seed, 50 + i) - 0.5f) * 14f);
            }

            var lampAnchor = new GameObject("Lamp").transform;
            lampAnchor.SetParent(floorT, false);
            var lampY = style.CeilingGap ? h * 0.36f : h * 0.42f;
            lampAnchor.localPosition = new Vector3(
                (StyleNoise(style.Seed, 8) - 0.5f) * 0.35f,
                lampY,
                -0.2f);
            CreateBox(lampAnchor, "LampHousing", new Vector3(2.4f, 0.12f, 0.28f), Vector3.zero, metal);
            CreateBox(lampAnchor, "LampTube", new Vector3(2.1f, 0.08f, 0.14f), new Vector3(0f, -0.08f, 0f), lampGlass);

            var lightGo = new GameObject("FloorLight");
            lightGo.transform.SetParent(lampAnchor, false);
            lightGo.transform.localPosition = new Vector3(0f, -0.25f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 9f;
            light.intensity = style.CharredTimber ? 5.2f : 6.5f;
            light.color = style.CharredTimber
                ? new Color(1f, 0.72f, 0.45f)
                : new Color(1f, 0.93f, 0.78f);
            light.shadows = LightShadows.None;

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

        private static void BuildVariedFloor(Transform floorT, FloorStyle style, float floorBaseY,
            Material darkRock, Material rock, Material ash)
        {
            const int cols = 3;
            const int rows = 2;
            var totalW = RoomWidth + 1.2f;
            var totalD = RoomDepth + 0.6f;
            var tileW = totalW / cols;
            var tileD = totalD / rows;
            var originX = -totalW * 0.5f + tileW * 0.5f;
            var originZ = -totalD * 0.5f + tileD * 0.5f;

            var holeCx = style.HoleQuad % 2;
            var holeCz = style.HoleQuad / 2;
            // Дыра не в переднем ряду по центру — оставляем опору под монстров (передний центр = col1,row0).
            if (style.FloorHole && holeCz == 0 && holeCx == 1)
                holeCx = StyleNoise(style.Seed, 3) > 0.5f ? 0 : 2;

            for (var cz = 0; cz < rows; cz++)
            {
                for (var cx = 0; cx < cols; cx++)
                {
                    var isHole = style.FloorHole && cx == holeCx && cz == holeCz;
                    if (isHole)
                    {
                        BuildFloorHoleRim(floorT, style, floorBaseY,
                            originX + cx * tileW, originZ + cz * tileD, tileW, tileD, darkRock, ash);
                        continue;
                    }

                    var heightJitter = style.Wear == FloorWear.Intact
                        ? (StyleNoise(style.Seed, cx * 5 + cz * 11) - 0.5f) * 0.06f
                        : (StyleNoise(style.Seed, cx * 5 + cz * 11) - 0.5f) * 0.22f;
                    var thick = 0.38f + StyleNoise(style.Seed, 20 + cx + cz * 3) * 0.16f;
                    var y = floorBaseY + heightJitter;
                    var mat = StyleNoise(style.Seed, 30 + cx + cz) > 0.72f ? rock : darkRock;
                    if (style.CharredTimber && StyleNoise(style.Seed, 33 + cx) > 0.65f)
                        mat = ash;

                    var slab = CreateBox(floorT, "FloorTile_" + cx + "_" + cz,
                        new Vector3(tileW * (0.92f + StyleNoise(style.Seed, 40 + cx) * 0.08f), thick,
                            tileD * (0.90f + StyleNoise(style.Seed, 45 + cz) * 0.1f)),
                        new Vector3(originX + cx * tileW + (StyleNoise(style.Seed, 50 + cx) - 0.5f) * 0.08f,
                            y,
                            originZ + cz * tileD + (StyleNoise(style.Seed, 55 + cz) - 0.5f) * 0.08f),
                        mat);
                    if (style.Wear != FloorWear.Intact)
                        slab.transform.localRotation = Quaternion.Euler(
                            (StyleNoise(style.Seed, 60 + cx) - 0.5f) * 4f,
                            (StyleNoise(style.Seed, 61 + cz) - 0.5f) * 6f,
                            (StyleNoise(style.Seed, 62 + cx + cz) - 0.5f) * 3f);
                }
            }

            // Мелкие камни на полу
            var pebbleCount = style.Wear == FloorWear.Intact ? 2 : 5 + (style.ExtraDebris ? 4 : 0);
            for (var i = 0; i < pebbleCount; i++)
            {
                var px = (StyleNoise(style.Seed, 70 + i) - 0.5f) * RoomWidth * 0.85f;
                var pz = (StyleNoise(style.Seed, 80 + i) - 0.5f) * RoomDepth * 0.75f;
                // Не ставим камень в дыру
                if (style.FloorHole)
                {
                    var hx = originX + holeCx * tileW;
                    var hz = originZ + holeCz * tileD;
                    if (Mathf.Abs(px - hx) < tileW * 0.55f && Mathf.Abs(pz - hz) < tileD * 0.55f)
                        continue;
                }

                CreateBoxDecor(floorT, "Pebble_" + i,
                    new Vector3(0.18f + StyleNoise(style.Seed, 90 + i) * 0.35f,
                        0.08f + StyleNoise(style.Seed, 95 + i) * 0.14f,
                        0.16f + StyleNoise(style.Seed, 100 + i) * 0.28f),
                    new Vector3(px, floorBaseY + 0.28f, pz),
                    new Vector3(
                        StyleNoise(style.Seed, 105 + i) * 25f,
                        StyleNoise(style.Seed, 106 + i) * 40f,
                        StyleNoise(style.Seed, 107 + i) * 20f),
                    StyleNoise(style.Seed, 108 + i) > 0.5f ? rock : darkRock);
            }
        }

        private static void BuildFloorHoleRim(Transform floorT, FloorStyle style, float floorBaseY,
            float cx, float cz, float tileW, float tileD, Material darkRock, Material ash)
        {
            // Неровный «ободок» вокруг пустоты вместо круглого выреза.
            var rimMat = style.CharredTimber ? ash : darkRock;
            var halfW = tileW * 0.48f;
            var halfD = tileD * 0.48f;
            var rimH = 0.32f;
            var y = floorBaseY - 0.02f;

            CreateBox(floorT, "HoleRim_N",
                new Vector3(tileW * 0.95f, rimH, tileD * 0.22f),
                new Vector3(cx + (StyleNoise(style.Seed, 110) - 0.5f) * 0.15f, y, cz + halfD * 0.72f),
                rimMat);
            CreateBox(floorT, "HoleRim_S",
                new Vector3(tileW * 0.78f, rimH * 1.1f, tileD * 0.2f),
                new Vector3(cx - (StyleNoise(style.Seed, 111) - 0.5f) * 0.2f, y - 0.04f, cz - halfD * 0.7f),
                rimMat);
            CreateBox(floorT, "HoleRim_W",
                new Vector3(tileW * 0.2f, rimH * 0.95f, tileD * 0.7f),
                new Vector3(cx - halfW * 0.7f, y + 0.03f, cz + (StyleNoise(style.Seed, 112) - 0.5f) * 0.12f),
                rimMat);
            CreateBox(floorT, "HoleRim_E",
                new Vector3(tileW * 0.24f, rimH, tileD * 0.55f),
                new Vector3(cx + halfW * 0.65f, y - 0.05f, cz - (StyleNoise(style.Seed, 113) - 0.5f) * 0.1f),
                rimMat);

            // Тёмный «провал» глубже плиты — читается как дыра вниз.
            CreateBoxDecor(floorT, "HoleVoid",
                new Vector3(tileW * 0.55f, 0.9f, tileD * 0.5f),
                new Vector3(cx, floorBaseY - 0.55f, cz),
                new Vector3(0f, StyleNoise(style.Seed, 114) * 12f, 0f),
                ash);

            // Обломки на краю
            for (var i = 0; i < 3; i++)
            {
                CreateBoxDecor(floorT, "HoleDebris_" + i,
                    new Vector3(0.25f + StyleNoise(style.Seed, 120 + i) * 0.3f,
                        0.12f + StyleNoise(style.Seed, 121 + i) * 0.15f,
                        0.22f + StyleNoise(style.Seed, 122 + i) * 0.25f),
                    new Vector3(
                        cx + (StyleNoise(style.Seed, 123 + i) - 0.5f) * tileW * 0.9f,
                        floorBaseY + 0.3f,
                        cz + (StyleNoise(style.Seed, 124 + i) - 0.5f) * tileD * 0.9f),
                    new Vector3(StyleNoise(style.Seed, 125 + i) * 30f, StyleNoise(style.Seed, 126 + i) * 50f, StyleNoise(style.Seed, 127 + i) * 25f),
                    darkRock);
            }
        }

        private static void BuildVariedCeiling(Transform floorT, FloorStyle style, float h,
            Material darkRock, Material rock, Material ash)
        {
            var ceilingY = h * 0.5f;
            var totalW = RoomWidth + 1.2f;
            var totalD = RoomDepth + 0.6f;

            if (!style.CeilingGap)
            {
                CreateBox(floorT, "CeilingSlab",
                    new Vector3(totalW, 0.4f, totalD),
                    new Vector3(0f, ceilingY, 0f),
                    darkRock);
                return;
            }

            // Периметр + щель: читаемый обвал между этажами.
            var gapSide = StyleNoise(style.Seed, 130) > 0.5f ? -1f : 1f;
            var gapW = totalW * (0.30f + StyleNoise(style.Seed, 131) * 0.2f);
            var gapD = totalD * (0.32f + StyleNoise(style.Seed, 132) * 0.18f);
            var gapX = gapSide * totalW * (0.08f + StyleNoise(style.Seed, 133) * 0.16f);
            var gapZ = (StyleNoise(style.Seed, 134) - 0.5f) * totalD * 0.2f;

            var xMin = -totalW * 0.5f;
            var xMax = totalW * 0.5f;
            var gapLeft = gapX - gapW * 0.5f;
            var gapRight = gapX + gapW * 0.5f;

            var leftW = gapLeft - xMin;
            if (leftW > 0.35f)
            {
                CreateBox(floorT, "Ceil_L",
                    new Vector3(leftW, 0.38f, totalD),
                    new Vector3(xMin + leftW * 0.5f, ceilingY, 0f),
                    darkRock);
            }

            var rightW = xMax - gapRight;
            if (rightW > 0.35f)
            {
                CreateBox(floorT, "Ceil_R",
                    new Vector3(rightW, 0.38f, totalD),
                    new Vector3(xMax - rightW * 0.5f, ceilingY, 0f),
                    darkRock);
            }

            var zMin = -totalD * 0.5f;
            var zMax = totalD * 0.5f;
            var gapFront = gapZ - gapD * 0.5f;
            var gapBack = gapZ + gapD * 0.5f;
            var frontD = gapFront - zMin;
            if (frontD > 0.3f)
            {
                CreateBox(floorT, "Ceil_F",
                    new Vector3(gapW, 0.36f, frontD),
                    new Vector3(gapX, ceilingY + 0.02f, zMin + frontD * 0.5f),
                    rock);
            }

            var backD = zMax - gapBack;
            if (backD > 0.3f)
            {
                CreateBox(floorT, "Ceil_B",
                    new Vector3(gapW * 0.95f, 0.34f, backD),
                    new Vector3(gapX, ceilingY - 0.02f, zMax - backD * 0.5f),
                    darkRock);
            }

            var debrisCount = style.Wear == FloorWear.Collapsed ? 6 : 3;
            for (var i = 0; i < debrisCount; i++)
            {
                var hang = StyleNoise(style.Seed, 140 + i) > 0.4f;
                var dy = hang
                    ? ceilingY - 0.35f - StyleNoise(style.Seed, 150 + i) * 0.9f
                    : -h * 0.5f + 0.35f + StyleNoise(style.Seed, 151 + i) * 0.25f;
                CreateBoxDecor(floorT, "CeilDebris_" + i,
                    new Vector3(0.35f + StyleNoise(style.Seed, 152 + i) * 0.7f,
                        0.18f + StyleNoise(style.Seed, 153 + i) * 0.35f,
                        0.3f + StyleNoise(style.Seed, 154 + i) * 0.55f),
                    new Vector3(
                        gapX + (StyleNoise(style.Seed, 155 + i) - 0.5f) * gapW,
                        dy,
                        gapZ + (StyleNoise(style.Seed, 156 + i) - 0.5f) * gapD),
                    new Vector3(
                        StyleNoise(style.Seed, 157 + i) * 40f,
                        StyleNoise(style.Seed, 158 + i) * 55f,
                        StyleNoise(style.Seed, 159 + i) * 35f),
                    StyleNoise(style.Seed, 160 + i) > 0.55f ? rock : ash);
            }
        }

        private static void BuildVariedWalls(Transform floorT, FloorStyle style, float h,
            Material rock, Material darkRock, Material ash)
        {
            // Back wall
            if (style.BackWallBroken)
            {
                var leftW = RoomWidth * (0.28f + StyleNoise(style.Seed, 170) * 0.2f);
                var rightW = RoomWidth * (0.25f + StyleNoise(style.Seed, 171) * 0.2f);
                var midGap = RoomWidth + 0.8f - leftW - rightW;
                var wallH = h * (0.45f + StyleNoise(style.Seed, 172) * 0.35f);
                CreateBox(floorT, "BackWall_L",
                    new Vector3(leftW, wallH + 0.15f, 0.85f),
                    new Vector3(-RoomWidth * 0.4f, -h * 0.5f + wallH * 0.5f, RoomDepth * 0.5f - 0.1f),
                    rock);
                CreateBox(floorT, "BackWall_R",
                    new Vector3(rightW, wallH * 0.85f + 0.1f, 0.8f),
                    new Vector3(RoomWidth * 0.38f, -h * 0.5f + wallH * 0.42f, RoomDepth * 0.5f - 0.1f),
                    style.CharredTimber ? ash : rock);
                if (midGap > 0.6f)
                {
                    // Нижний обломок стены
                    CreateBox(floorT, "BackWall_Stub",
                        new Vector3(midGap * 0.7f, h * 0.18f, 0.7f),
                        new Vector3((StyleNoise(style.Seed, 173) - 0.5f) * 0.4f, -h * 0.5f + h * 0.1f, RoomDepth * 0.5f - 0.15f),
                        ash);
                }
            }
            else
            {
                CreateBox(floorT, "BackWall",
                    new Vector3(RoomWidth + 0.8f, h + 0.2f, 0.85f),
                    new Vector3(0f, 0f, RoomDepth * 0.5f - 0.1f),
                    rock);
            }

            BuildSideWall(floorT, "LeftWall", style.LeftWallShort, style, h,
                -RoomWidth * 0.5f - 0.15f, rock, ash);
            BuildSideWall(floorT, "RightWall", style.RightWallShort, style, h,
                RoomWidth * 0.5f + 0.15f, rock, ash);

            // Крылья — тоже чуть разной высоты
            var leftWingH = style.LeftWallShort ? h * 0.55f : h + 0.6f;
            var rightWingH = style.RightWallShort ? h * 0.6f : h + 0.6f;
            CreateBox(floorT, "LeftWing",
                new Vector3(2.2f, leftWingH, RoomDepth * 0.95f),
                new Vector3(-RoomWidth * 0.5f - 1.5f, -h * 0.5f + leftWingH * 0.5f - 0.3f, -RoomDepth * 0.25f),
                darkRock);
            CreateBox(floorT, "RightWing",
                new Vector3(2.2f, rightWingH, RoomDepth * 0.95f),
                new Vector3(RoomWidth * 0.5f + 1.5f, -h * 0.5f + rightWingH * 0.5f - 0.3f, -RoomDepth * 0.25f),
                darkRock);

            // Выпуклости на стенах
            var bumpCount = style.Wear == FloorWear.Intact ? 2 : 4;
            for (var i = 0; i < bumpCount; i++)
            {
                var side = StyleNoise(style.Seed, 180 + i) > 0.5f ? -1f : 1f;
                CreateBoxDecor(floorT, "WallBump_" + i,
                    new Vector3(0.35f + StyleNoise(style.Seed, 181 + i) * 0.55f,
                        0.4f + StyleNoise(style.Seed, 182 + i) * 0.7f,
                        0.45f + StyleNoise(style.Seed, 183 + i) * 0.5f),
                    new Vector3(
                        side * (RoomWidth * 0.5f - 0.1f),
                        (StyleNoise(style.Seed, 184 + i) - 0.5f) * h * 0.55f,
                        (StyleNoise(style.Seed, 185 + i) - 0.5f) * RoomDepth * 0.55f),
                    new Vector3(
                        StyleNoise(style.Seed, 186 + i) * 20f,
                        StyleNoise(style.Seed, 187 + i) * 30f,
                        StyleNoise(style.Seed, 188 + i) * 15f),
                    StyleNoise(style.Seed, 189 + i) > 0.5f ? rock : darkRock);
            }
        }

        private static void BuildSideWall(Transform floorT, string name, bool shortened, FloorStyle style, float h,
            float x, Material rock, Material ash)
        {
            if (!shortened)
            {
                CreateBox(floorT, name,
                    new Vector3(0.85f, h + 0.2f, RoomDepth),
                    new Vector3(x, 0f, 0f),
                    rock);
                return;
            }

            var wallH = h * (0.38f + StyleNoise(style.Seed, name.Length * 7) * 0.35f);
            var depth = RoomDepth * (0.45f + StyleNoise(style.Seed, name.Length * 9) * 0.4f);
            var zOff = (StyleNoise(style.Seed, name.Length * 11) - 0.5f) * RoomDepth * 0.25f;
            CreateBox(floorT, name + "_Stub",
                new Vector3(0.85f, wallH, depth),
                new Vector3(x, -h * 0.5f + wallH * 0.5f, zOff),
                style.CharredTimber ? ash : rock);

            // Верхний обломок / «сгоревший» край
            if (StyleNoise(style.Seed, name.Length * 13) > 0.35f)
            {
                CreateBoxDecor(floorT, name + "_Cap",
                    new Vector3(0.7f, 0.25f, depth * 0.55f),
                    new Vector3(x, -h * 0.5f + wallH + 0.1f, zOff - depth * 0.1f),
                    new Vector3(0f, 0f, (StyleNoise(style.Seed, name.Length * 15) - 0.5f) * 18f),
                    ash);
            }
        }

        private static void BuildTimber(Transform floorT, FloorStyle style, float h, Material timberMat, Material charred)
        {
            var leftH = style.LeftWallShort ? h * 0.55f : h * 0.92f;
            var rightH = style.RightWallShort ? h * 0.5f : h * 0.92f;

            var left = CreateBox(floorT, "TimberLeft",
                new Vector3(0.28f, leftH, 0.28f),
                new Vector3(-RoomWidth * 0.42f, -h * 0.5f + leftH * 0.5f, -RoomDepth * 0.28f),
                timberMat);
            if (style.CharredTimber)
                left.transform.localRotation = Quaternion.Euler(0f, 0f, (StyleNoise(style.Seed, 200) - 0.5f) * 8f);

            var right = CreateBox(floorT, "TimberRight",
                new Vector3(0.28f, rightH, 0.28f),
                new Vector3(RoomWidth * 0.42f, -h * 0.5f + rightH * 0.5f, -RoomDepth * 0.28f),
                timberMat);
            if (style.Wear == FloorWear.Burned || style.Wear == FloorWear.Collapsed)
                right.transform.localRotation = Quaternion.Euler(0f, 0f, (StyleNoise(style.Seed, 201) - 0.5f) * 12f);

            if (style.Wear == FloorWear.Burned || style.Wear == FloorWear.Collapsed)
            {
                // Сломанная балка под углом
                CreateBoxDecor(floorT, "TimberBeamBroken",
                    new Vector3(RoomWidth * (0.45f + StyleNoise(style.Seed, 202) * 0.3f), 0.2f, 0.26f),
                    new Vector3(
                        (StyleNoise(style.Seed, 203) - 0.5f) * RoomWidth * 0.2f,
                        h * (0.15f + StyleNoise(style.Seed, 204) * 0.25f),
                        -RoomDepth * 0.28f),
                    new Vector3(0f, 0f, 18f + StyleNoise(style.Seed, 205) * 28f),
                    charred);

                if (StyleNoise(style.Seed, 206) > 0.4f)
                {
                    CreateBoxDecor(floorT, "TimberSplinter",
                        new Vector3(0.18f, h * 0.25f, 0.18f),
                        new Vector3(RoomWidth * 0.2f, -h * 0.15f, -RoomDepth * 0.22f),
                        new Vector3(12f, 20f, -25f),
                        timberMat);
                }
            }
            else
            {
                CreateBox(floorT, "TimberBeam",
                    new Vector3(RoomWidth * 0.88f, 0.22f, 0.28f),
                    new Vector3(0f, h * 0.38f, -RoomDepth * 0.28f),
                    timberMat);
            }
        }

        private static void BuildFloorDecor(Transform floorT, FloorStyle style, float h, float floorBaseY,
            Material metal, Material rust, Material rock, Material ash)
        {
            // Решётка — иногда дырявая / смещённая
            if (style.Wear != FloorWear.Collapsed || StyleNoise(style.Seed, 210) > 0.35f)
            {
                var grateW = RoomWidth * (style.FloorHole ? 0.55f : 0.88f);
                var grate = CreateBox(floorT, "Grate",
                    new Vector3(grateW, 0.07f, RoomDepth * (style.FloorHole ? 0.5f : 0.82f)),
                    new Vector3(
                        (StyleNoise(style.Seed, 211) - 0.5f) * 0.4f,
                        floorBaseY + 0.32f,
                        -0.1f + (StyleNoise(style.Seed, 212) - 0.5f) * 0.25f),
                    metal);
                if (style.Wear != FloorWear.Intact)
                    grate.transform.localRotation = Quaternion.Euler(0f, (StyleNoise(style.Seed, 213) - 0.5f) * 8f, 0f);
            }

            if (StyleNoise(style.Seed, 214) > 0.25f)
            {
                CreateCylinder(floorT, "PipeLeft",
                    new Vector3(0.14f, h * (0.35f + StyleNoise(style.Seed, 215) * 0.2f), 0.14f),
                    new Vector3(-RoomWidth * 0.36f, -h * 0.1f + (StyleNoise(style.Seed, 216) - 0.5f) * 0.3f, RoomDepth * 0.12f),
                    rust);
            }

            if (StyleNoise(style.Seed, 217) > 0.3f)
            {
                var pipe = CreateCylinder(floorT, "PipeRight",
                    new Vector3(0.14f, h * 0.42f, 0.14f),
                    new Vector3(RoomWidth * 0.36f, 0f, RoomDepth * 0.12f),
                    rust);
                if (style.Wear == FloorWear.Burned)
                    pipe.transform.localRotation = Quaternion.Euler(0f, 0f, (StyleNoise(style.Seed, 218) - 0.5f) * 16f);
            }

            if (!style.SkipCrate)
            {
                var crate = CreateBox(floorT, "Crate",
                    new Vector3(0.85f, 0.7f, 0.7f),
                    new Vector3(
                        RoomWidth * (0.22f + StyleNoise(style.Seed, 220) * 0.12f),
                        floorBaseY + 0.55f,
                        -RoomDepth * (0.12f + StyleNoise(style.Seed, 221) * 0.18f)),
                    rust);
                crate.transform.localRotation = Quaternion.Euler(
                    0f,
                    (StyleNoise(style.Seed, 222) - 0.5f) * 50f,
                    (StyleNoise(style.Seed, 223) - 0.5f) * 6f);
            }

            if (!style.SkipBarrel)
            {
                var barrel = CreateCylinder(floorT, "Barrel",
                    new Vector3(0.45f, 0.55f, 0.45f),
                    new Vector3(
                        -RoomWidth * (0.16f + StyleNoise(style.Seed, 224) * 0.14f),
                        floorBaseY + 0.55f,
                        RoomDepth * (StyleNoise(style.Seed, 225) * 0.12f - 0.02f)),
                    rust);
                if (StyleNoise(style.Seed, 226) > 0.7f)
                    barrel.transform.localRotation = Quaternion.Euler(0f, 0f, 88f + StyleNoise(style.Seed, 227) * 8f);
                else
                    barrel.transform.localRotation = Quaternion.Euler(0f, StyleNoise(style.Seed, 228) * 40f, 0f);
            }

            // Доп. обломки на collapsed/burned
            if (style.ExtraDebris)
            {
                for (var i = 0; i < 3; i++)
                {
                    CreateBoxDecor(floorT, "RoomDebris_" + i,
                        new Vector3(0.3f + StyleNoise(style.Seed, 230 + i) * 0.5f,
                            0.15f + StyleNoise(style.Seed, 231 + i) * 0.25f,
                            0.25f + StyleNoise(style.Seed, 232 + i) * 0.4f),
                        new Vector3(
                            (StyleNoise(style.Seed, 233 + i) - 0.5f) * RoomWidth * 0.7f,
                            floorBaseY + 0.28f,
                            (StyleNoise(style.Seed, 234 + i) - 0.5f) * RoomDepth * 0.55f),
                        new Vector3(
                            StyleNoise(style.Seed, 235 + i) * 35f,
                            StyleNoise(style.Seed, 236 + i) * 60f,
                            StyleNoise(style.Seed, 237 + i) * 25f),
                        StyleNoise(style.Seed, 238 + i) > 0.5f ? rock : ash);
                }
            }
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

        /// <summary>Декор: куб с поворотом, без коллайдера (не перехватывает клики по монстрам).</summary>
        private static GameObject CreateBoxDecor(Transform parent, string name, Vector3 scale, Vector3 localPos,
            Vector3 localEuler, Material mat)
        {
            var go = CreateBox(parent, name, scale, localPos, mat);
            go.transform.localRotation = Quaternion.Euler(localEuler);
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying)
                    Object.Destroy(col);
                else
                    Object.DestroyImmediate(col);
            }
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
