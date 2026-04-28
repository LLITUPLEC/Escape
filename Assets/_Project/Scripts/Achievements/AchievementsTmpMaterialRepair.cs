using TMPro;
using UnityEngine;

namespace Project.Achievements
{
    /// <summary>
    /// TMP_SubMeshUI.UpdateMaterial падает с NRE, если <see cref="TMP_SubMeshUI.textComponent"/> == null (осиротевший SubMesh).
    /// Также выставляет один надёжный шрифт и материал, чтобы не плодить битые fallback SubMesh.
    /// </summary>
    internal static class AchievementsTmpMaterialRepair
    {
        internal static void RepairHierarchy(Transform root, TMP_FontAsset preferredFont)
        {
            if (root == null) return;

            // Сначала убираем осиротевшие SubMesh — иначе TMP_SubMeshUI.OnValidate падает при null textComponent.
            RemoveBrokenSubMeshes(root);

            var fa = AchievementUiFontLoader.Resolve(preferredFont);
            if (fa == null)
                return;

            foreach (var tmp in root.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                tmp.font = fa;
                if (fa.material != null)
                    tmp.fontSharedMaterial = fa.material;
                tmp.richText = false;
                tmp.havePropertiesChanged = true;
            }

            RemoveBrokenSubMeshes(root);

            foreach (var tmp in root.GetComponentsInChildren<TextMeshProUGUI>(true))
                tmp.ForceMeshUpdate(true);

            RemoveBrokenSubMeshes(root);
        }

        private static void RemoveBrokenSubMeshes(Transform root)
        {
            var subs = root.GetComponentsInChildren<TMP_SubMeshUI>(true);
            for (var i = subs.Length - 1; i >= 0; i--)
            {
                var sm = subs[i];
                if (sm == null)
                    continue;

                TMP_Text tc = null;
                try
                {
                    tc = sm.textComponent;
                }
                catch
                {
                    tc = null;
                }

                if (tc != null)
                    continue;

                var go = sm.gameObject;
                if (go == null)
                    continue;

                if (Application.isPlaying)
                    Object.Destroy(go);
                else
                    Object.DestroyImmediate(go);
            }
        }
    }
}
