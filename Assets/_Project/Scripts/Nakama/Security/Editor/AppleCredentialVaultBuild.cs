#if UNITY_IOS
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

namespace Project.Nakama.Security.Editor
{
    internal static class AppleCredentialVaultBuild
    {
        [PostProcessBuild(100)]
        private static void AddSecurityFramework(BuildTarget target, string buildPath)
        {
            if (target != BuildTarget.iOS) return;
            var path = PBXProject.GetPBXProjectPath(buildPath);
            var project = new PBXProject();
            project.ReadFromFile(path);
            project.AddFrameworkToProject(project.GetUnityFrameworkTargetGuid(), "Security.framework", false);
            project.WriteToFile(path);
        }
    }
}
#endif
