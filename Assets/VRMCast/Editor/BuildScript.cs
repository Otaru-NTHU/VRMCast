using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace VRMCast.Editor
{
    /// <summary>
    /// Batch-mode macOS build entry point used by <c>Scripts/build-macos.sh</c>:
    /// <c>Unity -batchmode -quit -projectPath . -executeMethod VRMCast.Editor.BuildScript.BuildMacOS -buildPath Builds/macOS/VRMCast.app</c>
    /// Targets Apple Silicon (arm64) only, per the PRD.
    /// </summary>
    public static class BuildScript
    {
        public const string DefaultBuildPath = "Builds/macOS/VRMCast.app";

        [MenuItem("VRMCast/Build/macOS (Apple Silicon)")]
        public static void BuildMacOSFromMenu()
        {
            var report = BuildMacOSInternal(DefaultBuildPath);
            EditorUtility.DisplayDialog("VRMCast", $"Build {report.summary.result}: {report.summary.outputPath}", "OK");
        }

        public static void BuildMacOS()
        {
            var report = BuildMacOSInternal(GetArgument("-buildPath") ?? DefaultBuildPath);
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
            }
        }

        private static BuildReport BuildMacOSInternal(string outputPath)
        {
            MainSceneBuilder.Build();
            TrySetAppleSiliconArchitecture();

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)));
            var options = new BuildPlayerOptions
            {
                scenes = new[] { MainSceneBuilder.ScenePath },
                locationPathName = outputPath,
                target = BuildTarget.StandaloneOSX,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(options);
            var s = report.summary;
            Debug.Log($"VRMCast build {s.result}: {s.outputPath} ({s.totalSize / (1024 * 1024)} MB, {s.totalErrors} errors, {s.totalWarnings} warnings)");
            return report;
        }

        /// <summary>
        /// Sets the macOS architecture to arm64 through reflection so this file compiles even when the Mac build
        /// support module is not installed on the machine running the editor.
        /// </summary>
        private static void TrySetAppleSiliconArchitecture()
        {
            try
            {
                var type = Type.GetType("UnityEditor.OSXStandalone.UserBuildSettings, UnityEditor.OSXStandalone.Extensions");
                var property = type?.GetProperty("architecture", BindingFlags.Public | BindingFlags.Static);
                if (property == null)
                {
                    Debug.LogWarning("VRMCast build: Mac build support module not found; architecture left at the project default.");
                    return;
                }
                var enumType = property.PropertyType;
                var arm64 = Enum.GetNames(enumType).FirstOrDefault(n => n.Equals("ARM64", StringComparison.OrdinalIgnoreCase));
                if (arm64 == null)
                {
                    Debug.LogWarning($"VRMCast build: {enumType.Name} has no ARM64 value; architecture left at the project default.");
                    return;
                }
                property.SetValue(null, Enum.Parse(enumType, arm64));
                Debug.Log("VRMCast build: macOS architecture set to ARM64.");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"VRMCast build: could not set macOS architecture ({e.Message}).");
            }
        }

        private static string GetArgument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name) return args[i + 1];
            }
            return null;
        }
    }
}
