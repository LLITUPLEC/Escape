#if UNITY_EDITOR
using System.IO;
using Project.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Добавляет в PairRowTemplate префаба сетки две радиокнопки ставок (BetA / BetB) слева и справа от Vs.
/// Menu: Tools → Match3 → Добавить кнопки ставок в PairRowTemplate
/// Также автозапуск при наличии флага Temp/patch-arena-bet-toggles.flag (после рекомпиляции).
/// </summary>
[InitializeOnLoad]
public static class ArenaBracketBetTogglesPatcher
{
    private const string PrefabPath = "Assets/_Project/Resources/UI/ArenaBracketOverlay.prefab";
    private const string FlagPath = "Temp/patch-arena-bet-toggles.flag";

    static ArenaBracketBetTogglesPatcher()
    {
        EditorApplication.delayCall += TryAutoPatch;
    }

    private static void TryAutoPatch()
    {
        var abs = Path.Combine(Directory.GetCurrentDirectory(), FlagPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(abs))
            return;
        try
        {
            File.Delete(abs);
        }
        catch
        {
            // ignore
        }
        PatchPrefab();
    }

    [MenuItem("Tools/Match3/Добавить кнопки ставок в PairRowTemplate")]
    public static void PatchPrefab()
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var row = root.transform.Find("ArenaRows/PairRowTemplate");
            if (row == null)
            {
                Debug.LogError("[ArenaBracketBetToggles] PairRowTemplate не найден в " + PrefabPath);
                return;
            }

            var vs = row.Find("Vs");
            if (vs == null)
            {
                Debug.LogError("[ArenaBracketBetToggles] Vs не найден в PairRowTemplate");
                return;
            }

            // Remove previous embeds for idempotent re-run.
            DestroyChild(row, PairRowBetSelect.BetAName);
            DestroyChild(row, PairRowBetSelect.BetBName);

            var group = row.GetComponent<ToggleGroup>();
            if (group == null)
                group = row.gameObject.AddComponent<ToggleGroup>();
            group.allowSwitchOff = true;

            var white = WhiteSprite();
            var betA = MakeBetToggle(row, PairRowBetSelect.BetAName, new Vector2(-48f, 0f), white, group);
            var betB = MakeBetToggle(row, PairRowBetSelect.BetBName, new Vector2(48f, 0f), white, group);

            // Place near Vs in hierarchy (after Vs).
            betA.transform.SetSiblingIndex(vs.GetSiblingIndex() + 1);
            betB.transform.SetSiblingIndex(vs.GetSiblingIndex() + 2);

            var select = row.GetComponent<PairRowBetSelect>();
            if (select == null)
                select = row.gameObject.AddComponent<PairRowBetSelect>();

            var so = new SerializedObject(select);
            so.FindProperty("betA").objectReferenceValue = betA;
            so.FindProperty("betB").objectReferenceValue = betB;
            so.FindProperty("group").objectReferenceValue = group;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Default: hidden until BettingToggle is on (runtime).
            betA.gameObject.SetActive(false);
            betB.gameObject.SetActive(false);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log("[ArenaBracketBetToggles] BetA/BetB добавлены в PairRowTemplate (" + PrefabPath + ")");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static void DestroyChild(Transform parent, string name)
    {
        var t = parent.Find(name);
        if (t != null)
            Object.DestroyImmediate(t.gameObject);
    }

    private static Toggle MakeBetToggle(Transform parent, string name, Vector2 anchoredPos, Sprite white, ToggleGroup group)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(36f, 36f);
        rt.anchoredPosition = anchoredPos;

        var bg = go.AddComponent<Image>();
        bg.sprite = white;
        bg.color = new Color(0.22f, 0.24f, 0.28f, 1f);
        bg.raycastTarget = true;

        var checkGo = new GameObject("Checkmark", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        checkGo.layer = go.layer;
        checkGo.transform.SetParent(go.transform, false);
        var checkRt = checkGo.GetComponent<RectTransform>();
        checkRt.anchorMin = new Vector2(0.18f, 0.18f);
        checkRt.anchorMax = new Vector2(0.82f, 0.82f);
        checkRt.offsetMin = Vector2.zero;
        checkRt.offsetMax = Vector2.zero;
        var checkImg = checkGo.GetComponent<Image>();
        checkImg.sprite = white;
        checkImg.color = new Color(0.25f, 0.85f, 0.35f, 1f);
        checkImg.raycastTarget = false;

        var toggle = go.AddComponent<Toggle>();
        toggle.transition = Selectable.Transition.ColorTint;
        toggle.targetGraphic = bg;
        toggle.graphic = checkImg;
        toggle.group = group;
        toggle.isOn = false;
        toggle.toggleTransition = Toggle.ToggleTransition.Fade;

        return toggle;
    }

    private static Sprite WhiteSprite()
    {
        return ModalPanelCloseButton.WhiteSprite();
    }
}
#endif
