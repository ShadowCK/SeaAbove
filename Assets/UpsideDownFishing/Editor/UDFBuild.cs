using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace UpsideDownFishing.EditorTools
{
    /// <summary>Android APK build entry points (menu + -executeMethod).</summary>
    public static class UDFBuild
    {
        public const string ApkPath = "Builds/Android/SeaAbove.apk";

        [MenuItem("UpsideDownFishing/Build Android APK (Development)", priority = 20)]
        public static void BuildDevelopmentMenu() => Build(true);

        [MenuItem("UpsideDownFishing/Build Android APK (Release)", priority = 21)]
        public static void BuildReleaseMenu() => Build(false);

        public static void BuildBatch()
        {
            var r = Build(true);
            EditorApplication.Exit(r == BuildResult.Succeeded ? 0 : 1);
        }

        public static BuildResult Build(bool development)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ApkPath));
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { UDFProjectSetup.MainScene },
                locationPathName = ApkPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = development ? BuildOptions.Development : BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            var s = report.summary;
            string msg = $"{s.result} errors={s.totalErrors} warnings={s.totalWarnings} time={s.totalTime} size={s.totalSize / (1024 * 1024)}MB path={Path.GetFullPath(ApkPath)}";
            Debug.Log("[UDFBuild] " + msg);
            File.WriteAllText("Logs/udf_build.txt", msg);
            return s.result;
        }
    }
}
