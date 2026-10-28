using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Android;
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
        private static string OutputDirectory => Path.Combine(ProjectRoot, "Builds", PlayerSettings.bundleVersion);
        public static string StatusPath => Path.Combine(OutputDirectory, "build-status.json");
        private static string NewApkPath() => Path.Combine(OutputDirectory,
            "Escape-Expedition-" + PlayerSettings.bundleVersion + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".apk");
        private static bool _pending;

        [MenuItem("Tools/Escape/Expedition/Build Android development APK")]
        public static void Begin() => BeginBuild(true);

        [MenuItem("Tools/Escape/Expedition/Build Android release APK")]
        public static void BeginRelease() => BeginBuild(false);

        /// <summary>Noninteractive recovery/build entry: SDK failures become errors instead of hidden Editor dialogs.</summary>
        public static void BuildReleaseBatch()
        {
            if (!Application.isBatchMode || EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                throw new InvalidOperationException("Run in Android batch mode.");
            ValidateReleaseSettings();
            var args = Environment.GetCommandLineArgs();
            var restoreIndex = Array.IndexOf(args, "-escapeRestoreAndroidSdk");
            var restoreSdk = restoreIndex >= 0 && restoreIndex + 1 < args.Length ? args[restoreIndex + 1] : null;
            var status = new Status
            {
                state = "queued", development = false, startedUtc = DateTime.UtcNow.ToString("O"),
                output = NewApkPath()
            };
            Run(status, restoreSdk);
            if (status.state != "succeeded") throw new InvalidOperationException("Release APK build failed: " + status.message);
        }

        private static void BeginBuild(bool development)
        {
            if (_pending || BuildPipeline.isBuildingPlayer) throw new InvalidOperationException("A build is already running");
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Stop Play mode and let scripts compile before building");
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                throw new InvalidOperationException("Select Android as the active build target first");
            if (!development)
            {
                ValidateReleaseSettings();
            }
            var status = new Status
            {
                state = "queued",
                development = development,
                startedUtc = DateTime.UtcNow.ToString("O"),
                output = NewApkPath()
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

        private static void ValidateReleaseSettings()
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(PlayerSettings.bundleVersion ?? "", @"^\d+\.\d+\.\d+$") ||
                PlayerSettings.Android.bundleVersionCode < 1)
                throw new InvalidOperationException("Set a numeric release version and a positive Android version code in PlayerSettings before building.");
        }

        private static void Run(Status status, string restoreSdkRoot = null)
        {
            var bundle = EditorUserBuildSettings.buildAppBundle;
            var export = EditorUserBuildSettings.exportAsGoogleAndroidProject;
            var previousSdkRoot = restoreSdkRoot ?? AndroidExternalToolsSettings.sdkRootPath;
            var previousTargetSdk = PlayerSettings.Android.targetSdkVersion;
            var sdkOverridden = false;
            try
            {
                status.state = "building";
                Write(status);
                // External Tools preferences are shared between Editor versions.
                // An SDK from another installation can contain preview platforms
                // this Editor's bundled Gradle/lint cannot parse (e.g. android-37.0).
                // Prefer this Editor's SDK. Incomplete installations can instead
                // use the project-scoped copy prepared by Tools/Release/prepare-android-sdk.ps1.
                // Restore the user's selection without changing any installed SDK.
                var bundledSdk = Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines/AndroidPlayer/SDK");
                var sdkManager = Path.Combine("cmdline-tools", "latest", "bin", "sdkmanager" + (Application.platform == RuntimePlatform.WindowsEditor ? ".bat" : ""));
                if (!File.Exists(Path.Combine(bundledSdk, sdkManager)))
                    bundledSdk = Path.Combine(ProjectRoot, ".cache/android-sdk");
                if (!Directory.Exists(Path.Combine(bundledSdk, "platforms")) || !File.Exists(Path.Combine(bundledSdk, sdkManager)))
                    throw new DirectoryNotFoundException("Release SDK is incomplete. Run Tools/Release/prepare-android-sdk.ps1 before building.");
                AndroidExternalToolsSettings.sdkRootPath = bundledSdk;
                sdkOverridden = true;
                PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel36;
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
                PlayerSettings.Android.targetSdkVersion = previousTargetSdk;
                if (sdkOverridden) AndroidExternalToolsSettings.sdkRootPath = previousSdkRoot;
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
