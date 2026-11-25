#nullable enable
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace HTownSim.EditorTools
{
    /// <summary>
    /// Builds the game as a Windows standalone player: the start scene first, then the city. The output goes to
    /// Builds/HTownSim_{version}/HTownSim.exe in the project folder (Builds is not tracked by git). Menu: HTownSim > Build > Windows Player.
    /// </summary>
    public static class BuildGame
    {
        private const string StartScene = "Assets/Scenes/StartMenu.unity";

        private const string CityScene = "Assets/Scenes/HoustonResponse.unity";

        private const string ExecutableName = "HTownSim.exe";

        [MenuItem("HTownSim/Build/Windows Player")]
        private static void BuildFromMenu()
        {
            Debug.Log(Build());
        }

        /// <summary>Builds the Windows player and returns a summary; leaves the editor's open scene alone.</summary>
        public static string Build()
        {
            if (EditorApplication.isPlaying)
            {
                return "Stop Play Mode first.";
            }

            var folder = Path.Combine(Directory.GetCurrentDirectory(), "Builds", "HTownSim_" + PlayerSettings.bundleVersion);

            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }

            Directory.CreateDirectory(folder);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { StartScene, CityScene },
                locationPathName = Path.Combine(folder, ExecutableName),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(options);

            var summary = report.summary;

            if (summary.result != BuildResult.Succeeded)
            {
                var errors = report.steps
                    .SelectMany(step => step.messages)
                    .Where(message => message.type == LogType.Error || message.type == LogType.Exception)
                    .Select(message => message.content)
                    .Take(10);

                return "Build " + summary.result + " after " + summary.totalTime.TotalMinutes.ToString("0.0") + " min: " + string.Join(" | ", errors);
            }

            return "Built " + options.locationPathName + ": " + (summary.totalSize / (1024f * 1024f)).ToString("0") + " MB in " + summary.totalTime.TotalMinutes.ToString("0.0") + " min, " + summary.totalWarnings + " warnings.";
        }
    }
}
