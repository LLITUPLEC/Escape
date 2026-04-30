#if UNITY_EDITOR
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Menu: Tools → Match3 → Создать префаб турнирной сетки
/// Creates: Assets/_Project/Resources/UI/ArenaBracketOverlay.prefab
/// Runtime uses it (ArenaMatch8Bridge) if present.
/// </summary>
public static class ArenaBracketOverlayPrefabCreator
{
    private const string PrefabPath = "Assets/_Project/Resources/UI/ArenaBracketOverlay.prefab";

    [MenuItem("Tools/Match3/Создать префаб турнирной сетки")]
    public static void CreatePrefab()
    {
        EnsureFolders();

        var root = new GameObject("ArenaBracketOverlay", typeof(RectTransform));
        var rect = root.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var cg = root.AddComponent<CanvasGroup>();
        cg.interactable = true;
        cg.blocksRaycasts = true;

        var bg = root.AddComponent<Image>();
        bg.color = new Color(0.03f, 0.06f, 0.12f, 0.88f);
        // Must block clicks behind the overlay.
        bg.raycastTarget = true;

        var vl = root.AddComponent<VerticalLayoutGroup>();
        vl.padding = new RectOffset(24, 24, 90, 24);
        vl.spacing = 12f;
        vl.childAlignment = TextAnchor.UpperCenter;
        vl.childControlHeight = true;
        vl.childControlWidth = true;
        vl.childForceExpandWidth = true;
        vl.childForceExpandHeight = false;

        var title = MakeTmp(root.transform, "Title", "Турнир Кузнеца", 28, FontStyles.Bold);
        title.gameObject.AddComponent<LayoutElement>().preferredHeight = 44f;

        var cd = MakeTmp(root.transform, "ArenaCd", "-", 36, FontStyles.Bold);
        cd.gameObject.AddComponent<LayoutElement>().preferredHeight = 52f;

        var rows = new GameObject("ArenaRows", typeof(RectTransform));
        rows.transform.SetParent(root.transform, false);
        var rowsLe = rows.AddComponent<LayoutElement>();
        rowsLe.flexibleHeight = 1f;
        rowsLe.minHeight = 260f;
        rowsLe.flexibleWidth = 1f;

        var rowsVl = rows.AddComponent<VerticalLayoutGroup>();
        rowsVl.spacing = 10f;
        rowsVl.padding = new RectOffset(4, 4, 4, 4);
        rowsVl.childAlignment = TextAnchor.UpperCenter;
        rowsVl.childControlHeight = true;
        rowsVl.childControlWidth = true;
        rowsVl.childForceExpandWidth = true;
        rowsVl.childForceExpandHeight = false;

        // Templates (inactive): runtime clones these
        var headerT = MakeTmp(rows.transform, "RoundHeaderTemplate", "1/4", 22, FontStyles.Bold);
        headerT.gameObject.SetActive(false);
        headerT.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;

        var rowT = new GameObject("PairRowTemplate", typeof(RectTransform));
        rowT.transform.SetParent(rows.transform, false);
        rowT.SetActive(false);

        var rowRt = rowT.GetComponent<RectTransform>();
        rowRt.anchorMin = new Vector2(0f, 1f);
        rowRt.anchorMax = new Vector2(1f, 1f);
        rowRt.pivot = new Vector2(0.5f, 1f);
        rowRt.sizeDelta = new Vector2(0f, 72f);
        var rowLe = rowT.AddComponent<LayoutElement>();
        rowLe.minHeight = 64f;
        rowLe.preferredHeight = 72f;
        rowLe.flexibleWidth = 1f;

        var leftName = MakeTmp(rowT.transform, "LeftName", "Player_A", 20, FontStyles.Normal);
        SetRect(leftName.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(200f, 36f), new Vector2(120f, 0f));

        MakeHpBar(rowT.transform, "HpA");

        var vs = MakeTmp(rowT.transform, "Vs", "—", 22, FontStyles.Bold);
        SetRect(vs.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(28f, 32f), Vector2.zero);

        MakeHpBar(rowT.transform, "HpB");

        var rightName = MakeTmp(rowT.transform, "RightName", "Player_B", 20, FontStyles.Normal);
        rightName.alignment = TextAlignmentOptions.Right;
        SetRect(rightName.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(200f, 36f), new Vector2(-120f, 0f));

        var status = MakeTmp(rowT.transform, "Status", "", 18, FontStyles.Italic);
        SetRect(status.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(160f, 28f), new Vector2(-120f, 10f));

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab != null)
            Selection.activeObject = prefab;
    }

    private static void EnsureFolders()
    {
        var dir = Path.GetDirectoryName(PrefabPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }

    private static TextMeshProUGUI MakeTmp(Transform parent, string name, string text, float size, FontStyles fs)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = fs;
        tmp.alignment = TextAlignmentOptions.Left;
        tmp.color = Color.white;
        return tmp;
    }

    private static void MakeHpBar(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var ws = BuiltinWhiteSprite();

        var bg = go.AddComponent<Image>();
        bg.color = new Color(0.15f, 0.15f, 0.18f, 1f);
        bg.sprite = ws;

        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(140f, 22f);

        var fill = new GameObject("Fill", typeof(RectTransform));
        fill.transform.SetParent(go.transform, false);
        var fillImg = fill.AddComponent<Image>();
        fillImg.color = new Color(0.28f, 0.82f, 0.38f, 1f);
        fillImg.sprite = ws;
        fillImg.type = Image.Type.Filled;
        fillImg.fillMethod = Image.FillMethod.Horizontal;
        fillImg.fillOrigin = (int)Image.OriginHorizontal.Left;
        fillImg.fillAmount = 1f;

        var frt = fill.GetComponent<RectTransform>();
        frt.anchorMin = Vector2.zero;
        frt.anchorMax = Vector2.one;
        frt.offsetMin = Vector2.zero;
        frt.offsetMax = Vector2.zero;

        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = 140f;
        le.preferredHeight = 26f;
    }

    private static Sprite BuiltinWhiteSprite()
    {
        var t = Texture2D.whiteTexture;
        return Sprite.Create(
            t,
            new Rect(0, 0, t.width, t.height),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect);
    }

    private static void SetRect(RectTransform rt, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 size, Vector2 anchoredPos)
    {
        if (rt == null) return;
        rt.anchorMin = aMin;
        rt.anchorMax = aMax;
        rt.pivot = pivot;
        rt.sizeDelta = size;
        rt.anchoredPosition = anchoredPos;
    }
}
#endif

