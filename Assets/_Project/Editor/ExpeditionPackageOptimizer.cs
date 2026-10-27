using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Project.Editor
{
    /// <summary>Explicit, local UI font migration. Never runs at import, build or startup.</summary>
    public static class ExpeditionPackageOptimizer
    {
        public const string Revision = "2026-10-23-atlas-only-v3";
        private const string OldFontPath = "Assets/_Project/Fonts/TF2CSecondary SDF.asset";
        private const string OldSourcePath = "Assets/_Project/Fonts/TF2CSecondary.otf";
        private const string NewFontPath = "Assets/_Project/Resources/Expedition/ExpeditionSerif.asset";
        private static readonly string[] PrefabOwners =
        {
            "Assets/_Project/Prefabs/UI/Leaderboard/LeaderboardPanel.prefab",
            "Assets/_Project/Prefabs/UI/Achievements/AchievementsPanel.prefab",
            "Assets/_Project/Prefabs/UI/Achievements/AchievementChainRowPrefabTemplate.prefab",
            "Assets/_Project/Prefabs/CharacterUI/AchievementChainRowPrefabTemplate(Clone).prefab",
            "Assets/_Project/Prefabs/MainMenu/ArenaMenuWorld.prefab"
        };
        private static readonly string[] SceneOwners =
        {
            "Assets/_Project/Scenes/MainMenu.unity",
            "Assets/_Project/Scenes/ArenaMenu.unity"
        };

        [Serializable]
        private sealed class OwnerResult
        {
            public string path;
            public int fontReferencesReplaced, materialReferencesReplaced, textComponentsUpdated;
            public int prefabFontOverridesReplaced, prefabMaterialOverridesReplaced;
            public bool saved;
        }

        [Serializable]
        private sealed class Report
        {
            public string optimizerRevision = Revision;
            public string state = "preparing", startedUtc, finishedUtc, message;
            public string oldFontPath, oldSourcePath, newFontPath, oldFontGuid, newFontGuid;
            public string oldMaterialIdentifier, newMaterialIdentifier;
            public List<OwnerResult> owners = new List<OwnerResult>();
            public int fontReferencesReplaced, materialReferencesReplaced, textComponentsUpdated;
            public string[] enabledScenes, resourceRoots, buildRootsReachingOldFont, targetOwnersStillReachingOldFont;
            public int buildDependencyCount;
            public bool oldFontInBuildClosure, oldSourceInBuildClosure, sceneSetupRestored;
            public string[] retainedFallbackResources;
        }

        public static string ReportPath => Path.GetFullPath(Path.Combine(Application.dataPath,
            "../.cache/qa/bridge/package-optimizer-report.json"));

        [MenuItem("Tools/Escape/Expedition/Optimize UI font references")]
        public static void Prepare()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || BuildPipeline.isBuildingPlayer)
                throw new InvalidOperationException("Stop Play mode and wait for compilation/building before optimizing UI font references.");
            // Opening scene assets must never silently save or discard the user's work.
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isDirty) throw new InvalidOperationException("Save or discard the dirty scene before optimization: " + scene.name);
            }
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null)
                throw new InvalidOperationException("Close Prefab Mode before optimization; its contents will not be saved automatically.");

            var report = new Report
            {
                startedUtc = DateTime.UtcNow.ToString("O"), oldFontPath = OldFontPath, oldSourcePath = OldSourcePath, newFontPath = NewFontPath,
                oldFontGuid = AssetDatabase.AssetPathToGUID(OldFontPath), newFontGuid = AssetDatabase.AssetPathToGUID(NewFontPath),
                retainedFallbackResources = new[]
                {
                    "Assets/_Project/Resources/Expedition/ExpeditionSans.asset",
                    "Assets/_Project/Resources/Expedition/Art/ui-plate.png",
                    "Assets/_Project/Resources/Expedition/UI/primary-button.png"
                }.Where(path => File.Exists(Path.Combine(Application.dataPath, "..", path))).ToArray()
            };
            var setup = EditorSceneManager.GetSceneManagerSetup();
            Exception failure = null;
            try
            {
                var oldFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(OldFontPath);
                var newFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(NewFontPath);
                if (oldFont == null) throw new InvalidOperationException("The source TMP font asset is missing: " + OldFontPath);
                if (newFont == null || newFont.material == null)
                    throw new InvalidOperationException("Prepare and preview ExpeditionSerif with its material before optimizing UI references.");
                report.oldMaterialIdentifier = Identifier(oldFont.material);
                report.newMaterialIdentifier = Identifier(newFont.material);
                foreach (var path in PrefabOwners)
                {
                    if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                        throw new InvalidOperationException("UI prefab is missing: " + path);
                }
                foreach (var path in SceneOwners)
                    if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
                        throw new InvalidOperationException("UI scene is missing: " + path);

                // Prefab sources first, so subsequently opened scenes inherit the new references.
                foreach (var path in PrefabOwners)
                {
                    var result = new OwnerResult { path = path };
                    report.owners.Add(result);
                    var root = PrefabUtility.LoadPrefabContents(path);
                    try
                    {
                        ReplaceInHierarchy(root, oldFont, newFont, result);
                        if (Changed(result))
                        {
                            if (PrefabUtility.SaveAsPrefabAsset(root, path) == null)
                                throw new InvalidOperationException("Could not save migrated UI prefab: " + path);
                            result.saved = true;
                        }
                    }
                    finally { PrefabUtility.UnloadPrefabContents(root); }
                }
                foreach (var path in SceneOwners)
                {
                    var result = new OwnerResult { path = path };
                    report.owners.Add(result);
                    var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    foreach (var root in scene.GetRootGameObjects()) ReplaceInHierarchy(root, oldFont, newFont, result);
                    if (Changed(result))
                    {
                        EditorSceneManager.MarkSceneDirty(scene);
                        if (!EditorSceneManager.SaveScene(scene))
                            throw new InvalidOperationException("Could not save migrated UI scene: " + path);
                        result.saved = true;
                    }
                }

                report.fontReferencesReplaced = report.owners.Sum(o => o.fontReferencesReplaced);
                report.materialReferencesReplaced = report.owners.Sum(o => o.materialReferencesReplaced);
                report.textComponentsUpdated = report.owners.Sum(o => o.textComponentsUpdated);
                ValidateBuildClosure(report);
                report.state = "succeeded";
                report.message = "The large TMP atlas is absent from the build dependency closure. Font files were not deleted. " +
                    (report.oldSourceInBuildClosure
                        ? "The legacy source font remains referenced by existing UI and was retained (informational)."
                        : "The source font was retained.");
            }
            catch (Exception ex)
            {
                failure = ex;
                report.state = "failed";
                report.message = ex.ToString();
            }
            finally
            {
                report.fontReferencesReplaced = report.owners.Sum(o => o.fontReferencesReplaced);
                report.materialReferencesReplaced = report.owners.Sum(o => o.materialReferencesReplaced);
                report.textComponentsUpdated = report.owners.Sum(o => o.textComponentsUpdated);
                try
                {
                    EditorSceneManager.RestoreSceneManagerSetup(setup);
                    report.sceneSetupRestored = true;
                }
                catch (Exception ex)
                {
                    if (failure == null) failure = ex;
                    report.state = "failed";
                    report.message += "\nScene setup restore failed: " + ex;
                }
                report.finishedUtc = DateTime.UtcNow.ToString("O");
                Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
                File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true));
            }
            if (failure != null) throw new InvalidOperationException("UI font optimization failed; see " + ReportPath, failure);
            Debug.Log("[Expedition] UI font optimization complete: " + ReportPath);
        }

        private static bool Changed(OwnerResult result) => result.fontReferencesReplaced + result.materialReferencesReplaced > 0;

        private static void ReplaceInHierarchy(GameObject root, TMP_FontAsset oldFont, TMP_FontAsset newFont, OwnerResult result)
        {
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null) continue;
                var text = component as TMP_Text;
                var textUsedOldFont = text != null && text.font == oldFont;
                var textUsedOldMaterial = text != null && IsOldMaterial(text.fontSharedMaterial, oldFont);
                var serialized = new SerializedObject(component);
                var property = serialized.GetIterator();
                var changed = false;
                while (property.Next(true))
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                    var value = property.objectReferenceValue;
                    if (value == oldFont)
                    {
                        property.objectReferenceValue = newFont;
                        result.fontReferencesReplaced++;
                        changed = true;
                    }
                    else if (IsOldMaterial(value as Material, oldFont))
                    {
                        property.objectReferenceValue = newFont.material;
                        result.materialReferencesReplaced++;
                        changed = true;
                    }
                }
                if (!changed) continue;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                if (textUsedOldFont || textUsedOldMaterial)
                {
                    // Keep the TMP public properties and its rendering cache consistent
                    // with both serialized font and material references just migrated.
                    text.font = newFont;
                    text.fontSharedMaterial = newFont.material;
                    text.SetAllDirty();
                    result.textComponentsUpdated++;
                }
                if (PrefabUtility.IsPartOfPrefabInstance(component))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                EditorUtility.SetDirty(component);
            }
            ReplacePrefabOverrides(root, oldFont, newFont, result);
        }

        private static void ReplacePrefabOverrides(GameObject root, TMP_FontAsset oldFont, TMP_FontAsset newFont, OwnerResult result)
        {
            var visited = new HashSet<int>();
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                var instance = PrefabUtility.GetNearestPrefabInstanceRoot(transform.gameObject);
                if (instance == null || !visited.Add(instance.GetInstanceID())) continue;
                var modifications = PrefabUtility.GetPropertyModifications(instance);
                if (modifications == null) continue;
                var changed = false;
                foreach (var modification in modifications)
                {
                    if (modification == null) continue;
                    // Stale overrides can refer to source component fileIDs that no
                    // longer exist. They still retain dependencies even though no
                    // live component exposes the font property. Remap their object
                    // references too, keeping every other override field intact.
                    if (modification.objectReference == oldFont)
                    {
                        modification.objectReference = newFont;
                        result.fontReferencesReplaced++;
                        result.prefabFontOverridesReplaced++;
                        changed = true;
                    }
                    else if (IsOldMaterial(modification.objectReference as Material, oldFont))
                    {
                        modification.objectReference = newFont.material;
                        result.materialReferencesReplaced++;
                        result.prefabMaterialOverridesReplaced++;
                        changed = true;
                    }
                }
                if (!changed) continue;
                PrefabUtility.SetPropertyModifications(instance, modifications);
                PrefabUtility.RecordPrefabInstancePropertyModifications(instance);
                EditorUtility.SetDirty(instance);
            }
        }

        private static bool IsOldMaterial(Material material, TMP_FontAsset oldFont)
        {
            if (material == null) return false;
            if (material == oldFont.material || AssetDatabase.GetAssetPath(material) == OldFontPath) return true;
            // A material preset can live outside the font asset but still retain its atlas.
            return material.HasProperty("_MainTex") &&
                AssetDatabase.GetAssetPath(material.GetTexture("_MainTex")) == OldFontPath;
        }

        private static string Identifier(Object value)
        {
            if (value == null) return "";
            return AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out string guid, out long id) ? guid + ":" + id : value.name;
        }

        private static void ValidateBuildClosure(Report report)
        {
            report.enabledScenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            report.resourceRoots = AssetDatabase.GetAllAssetPaths().Where(path =>
                path.Contains("/Resources/") && !path.Contains("/Editor/") && !AssetDatabase.IsValidFolder(path)).ToArray();
            var roots = report.enabledScenes.Concat(report.resourceRoots).Distinct().ToArray();
            var dependencies = new HashSet<string>(AssetDatabase.GetDependencies(roots, true), StringComparer.Ordinal);
            report.buildDependencyCount = dependencies.Count;
            report.oldFontInBuildClosure = dependencies.Contains(OldFontPath);
            report.oldSourceInBuildClosure = dependencies.Contains(OldSourcePath);
            report.targetOwnersStillReachingOldFont = PrefabOwners.Concat(SceneOwners)
                .Where(path => AssetDatabase.GetDependencies(path, true).Contains(OldFontPath)).ToArray();
            report.buildRootsReachingOldFont = report.oldFontInBuildClosure
                ? roots.Where(path => AssetDatabase.GetDependencies(path, true).Contains(OldFontPath)).ToArray()
                : Array.Empty<string>();
            // The small source font is still used by legacy UI. Its inclusion is
            // informative; this migration removes the separate 16 MiB TMP atlas.
            if (report.oldFontInBuildClosure || report.targetOwnersStillReachingOldFont.Length > 0)
                throw new InvalidOperationException("The old TMP atlas is still reachable. Inspect dependency roots in the optimizer report; no font assets were deleted.");
        }
    }
}
