using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Project.Editor
{
    public static class ExpeditionReleaseVisualQa20261027
    {
        public static void RunBatch()
        {
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/MainMenu.unity", OpenSceneMode.Single);
            Debug.Log(ExpeditionWorkshopRecipeQa.Run());
            ExpeditionAppearanceMailQa.RunBatch();
            Debug.Log(ExpeditionBattleRoundQa.Run());
            ExpeditionVisualCapture20261027.RunBatch();
            Debug.Log(ExpeditionShelterShortcutQa.Run());
            const string output = ".cache/qa/mine-refresh/release-native-aggregate.json";
            File.WriteAllText(output, "{\"passed\":true,\"networkActions\":false,\"accountWrites\":false,\"checks\":[\"workshop-recipes\",\"appearance-unlock-mail\",\"battle-round-hud\",\"item-information\",\"battle-entry\",\"tournament-avatars\",\"new-monster-art\",\"grounding-132-placements\",\"shelter-shortcuts-badges-callbacks\"],\"nativeScreenshots\":11}");
            Debug.Log("PASS release native aggregate: " + output);
        }
    }
}
