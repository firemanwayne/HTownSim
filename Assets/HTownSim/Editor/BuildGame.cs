#nullable enable
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

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

        /// <summary>The live-incident programs shipped beside the game (<c>LiveServicesLauncher</c> starts them): project, output folder under Services.</summary>
        private static readonly (string Project, string Folder)[] Services =
        {
            ("Tools/IncidentService/src/Api/IncidentService.Api.csproj", "IncidentService"),
            ("Tools/HoustonIncidents/src/Harvester/HoustonIncidents.Harvester.csproj", "HoustonIncidents"),
        };

        private const int PublishTimeoutMilliseconds = 600000;

        [MenuItem("HTownSim/Build/Windows Player")]
        private static void BuildFromMenu()
        {
            Debug.Log(Build());
        }

        /// <summary>Builds the Linux city server used by the Docker deployment.</summary>
        [MenuItem("HTownSim/Build/Linux City Server")]
        public static void BuildCityServer()
        {
            if (EditorApplication.isPlaying)
            {
                throw new System.InvalidOperationException("Stop Play Mode before building the city server.");
            }

            var folder = Path.Combine(Directory.GetCurrentDirectory(), "Builds", "CityServer");
            Directory.CreateDirectory(folder);
            var options = new BuildPlayerOptions
            {
                scenes = new[] { CityScene },
                locationPathName = Path.Combine(folder, "HTownSimServer.x86_64"),
                target = BuildTarget.StandaloneLinux64,
                subtarget = (int)StandaloneBuildSubtarget.Server,
                options = BuildOptions.None,
            };

            // The server talks to the incident service over plain http on the private container network (not localhost), which Unity blocks by default; allow it for this build only.
            var insecureHttp = PlayerSettings.insecureHttpOption;
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;

            // HTOWNSIM_SERVER_TRACE=1 gives every server log line a script stack trace, to find where a message comes from (it makes the log much longer).
            var trace = System.Environment.GetEnvironmentVariable("HTOWNSIM_SERVER_TRACE") == "1";
            var traceTypes = new[] { LogType.Log, LogType.Warning, LogType.Error };
            var previousTraces = traceTypes.Select(PlayerSettings.GetStackTraceLogType).ToArray();

            if (trace)
            {
                foreach (var type in traceTypes)
                {
                    PlayerSettings.SetStackTraceLogType(type, StackTraceLogType.ScriptOnly);
                }
            }

            UnityEditor.Build.Reporting.BuildSummary summary;
            try
            {
                summary = BuildPipeline.BuildPlayer(options).summary;
            }
            finally
            {
                PlayerSettings.insecureHttpOption = insecureHttp;

                for (var i = 0; i < traceTypes.Length; i++)
                {
                    PlayerSettings.SetStackTraceLogType(traceTypes[i], previousTraces[i]);
                }
            }

            if (summary.result != BuildResult.Succeeded)
            {
                throw new System.InvalidOperationException("Linux city server build " + summary.result + ".");
            }

            Debug.Log("Built city server at " + options.locationPathName);
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
                subtarget = (int)StandaloneBuildSubtarget.Player,
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

            var published = PublishServices(folder);

            return "Built " + options.locationPathName + ": " + (summary.totalSize / (1024f * 1024f)).ToString("0") + " MB in " + summary.totalTime.TotalMinutes.ToString("0.0") + " min, " + summary.totalWarnings + " warnings. " + published;
        }

        /// <summary>Publishes the incident service and the harvester self-contained into the build's Services folder; returns what happened.</summary>
        private static string PublishServices(string folder)
        {
            var done = 0;

            foreach (var (project, name) in Services)
            {
                var output = Path.Combine(folder, "Services", name);

                var start = new ProcessStartInfo("dotnet", "publish \"" + project + "\" -c Release -r win-x64 --self-contained true -o \"" + output + "\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };

                try
                {
                    using var process = Process.Start(start);

                    if (process != null && process.WaitForExit(PublishTimeoutMilliseconds) && process.ExitCode == 0)
                    {
                        done++;
                    }
                    else
                    {
                        Debug.LogWarning("Publishing " + project + " failed.");
                    }
                }
                catch (System.ComponentModel.Win32Exception exception)
                {
                    Debug.LogWarning("Could not run dotnet to publish " + project + " (is the .NET SDK installed?): " + exception.Message);
                }
            }

            return done + " of " + Services.Length + " live services published.";
        }
    }
}
