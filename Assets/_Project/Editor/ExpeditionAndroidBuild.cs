using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Project.Editor
{
    /// <summary>Reproducible local development APK, with a machine-readable result for editor QA.</summary>
    public static class ExpeditionAndroidBuild
    {
        [Serializable]
        private sealed class Status
        {
            public string state, output, result, message, startedUtc, finishedUtc;
            public ulong bytes;
            public double seconds;
            public int errors, warnings;
            public bool development;
        }

        private static readonly string ProjectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        public static string StatusPath => Path.Combine(ProjectRoot, ".utmp/expedition/android-build-status.json");
        private static bool _pending;

        [MenuItem("Tools/Escape/Expedition/Build Android development APK")]
        public static void Begin() => BeginBuild(true);

        [MenuItem("Tools/Escape/Expedition/Build Android release APK")]
        public static void BeginRelease() => BeginBuild(false);

        private static void BeginBuild(bool development)
        {
            if (_pending || BuildPipeline.isBuildingPlayer) throw new InvalidOperationException("A build is already running");
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Stop Play mode and let scripts compile before building");
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                throw new InvalidOperationException("Select Android as the active build target first");
            if (!development)
            {
                PlayerSettings.bundleVersion = "0.3.5";
                PlayerSettings.Android.bundleVersionCode = Math.Max(7, PlayerSettings.Android.bundleVersionCode);
            }
            var status = new Status
            {
                state = "queued",
                development = development,
                startedUtc = DateTime.UtcNow.ToString("O"),
                output = Path.Combine(ProjectRoot, "Builds/Expedition/Escape-Expedition-" + PlayerSettings.bundleVersion + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".apk")
            };
            Write(status);
            _pending = true;
            // Unlike delayCall, update also runs while an inspector has no repaint.
            // Dispatch on the next tick so the QA mailbox can acknowledge the request first.
            EditorApplication.CallbackFunction start = null;
            start = () =>
            {
                if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                EditorApplication.update -= start;
                Run(status);
            };
            EditorApplication.update += start;
        }

        private static void Run(Status status)
        {
            var bundle = EditorUserBuildSettings.buildAppBundle;
            var export = EditorUserBuildSettings.exportAsGoogleAndroidProject;
            try
            {
                status.state = "building";
                Write(status);
                Directory.CreateDirectory(Path.GetDirectoryName(status.output));
                EditorUserBuildSettings.buildAppBundle = false;
                EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                    locationPathName = status.output,
                    target = BuildTarget.Android,
                    targetGroup = BuildTargetGroup.Android,
                    options = BuildOptions.DetailedBuildReport | BuildOptions.CompressWithLz4HC |
                        (status.development ? BuildOptions.Development | BuildOptions.AllowDebugging : BuildOptions.None)
                });
                status.result = report.summary.result.ToString();
                status.state = report.summary.result == BuildResult.Succeeded ? "succeeded" : "failed";
                status.bytes = report.summary.totalSize;
                status.seconds = report.summary.totalTime.TotalSeconds;
                status.errors = report.summary.totalErrors;
                status.warnings = report.summary.totalWarnings;
                status.message = string.Join("\n", report.steps.SelectMany(s => s.messages)
                    .Where(m => m.type == LogType.Error || m.type == LogType.Exception)
                    .Select(m => m.content).Take(12));
            }
            catch (Exception ex)
            {
                status.state = "failed";
                status.result = "Exception";
                status.message = ex.Message;
            }
            finally
            {
                EditorUserBuildSettings.buildAppBundle = bundle;
                EditorUserBuildSettings.exportAsGoogleAndroidProject = export;
                _pending = false;
                status.finishedUtc = DateTime.UtcNow.ToString("O");
                Write(status);
            }
        }

        private static void Write(Status status)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatusPath));
            File.WriteAllText(StatusPath, JsonUtility.ToJson(status, true));
        }
    }
}
