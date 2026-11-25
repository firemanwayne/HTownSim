#nullable enable
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Starts the HoustonIncidents harvester (Tools/HoustonIncidents; the live City of Houston incident feed, see Documentation/LiveIncidents.md) when Play Mode
/// starts and stops it when Play Mode ends. It runs hidden with <c>dotnet run</c> (which rebuilds it if its code changed); its output
/// goes to Logs/HoustonIncidents.log. The process id is kept in SessionState so it survives the domain reload on entering Play Mode.
/// Menu: HTownSim > Live Incidents.
/// </summary>
[InitializeOnLoad]
internal static class HarvesterLauncher
{
    /// <summary>Editor preference: whether to start the harvester with Play Mode.</summary>
    private const string EnabledKey = "HTownSim.Harvester.Enabled";

    /// <summary>Editor preference: the harvester project folder.</summary>
    private const string ProjectKey = "HTownSim.Harvester.Project";

    /// <summary>Session key holding the running harvester's process id.</summary>
    private const string ProcessKey = "HTownSim.Harvester.Pid";

    /// <summary>The harvester project inside this repository (Tools/HoustonIncidents), relative to the Unity project folder.</summary>
    private const string DefaultProject = "Tools/HoustonIncidents/src/Harvester";

    private const string LogPath = "Logs/HoustonIncidents.log";

    private const string ToggleMenu = "HTownSim/Live Incidents/Start Harvester On Play";

    static HarvesterLauncher()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;

        EditorApplication.quitting += Stop;
    }

    private static bool Enabled => EditorPrefs.GetBool(EnabledKey, true);

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode && Enabled)
        {
            Start();
        }
        else if (change == PlayModeStateChange.ExitingPlayMode)
        {
            Stop();
        }
    }

    /// <summary>Starts the harvester unless it is already running.</summary>
    private static void Start()
    {
        if (Running() != null)
        {
            return;
        }

        var project = Path.GetFullPath(EditorPrefs.GetString(ProjectKey, DefaultProject));

        if (!Directory.Exists(project))
        {
            Debug.LogWarning("HoustonIncidents harvester not found at " + project + ". Set it with HTownSim > Live Incidents > Set Harvester Folder.");

            return;
        }

        var log = Path.GetFullPath(LogPath);

        Directory.CreateDirectory(Path.GetDirectoryName(log)!);

        var start = new ProcessStartInfo("cmd.exe", "/c dotnet run --project \"" + project + "\" > \"" + log + "\" 2>&1")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = project,
        };

        // With the incident service available the harvester posts each poll to it instead of writing feed files.
        if (IncidentServiceLauncher.Available)
        {
            start.Environment["Incidents__ServiceUrl"] = IncidentServiceLauncher.Address;
        }

        try
        {
            var process = Process.Start(start);

            if (process == null)
            {
                return;
            }

            SessionState.SetInt(ProcessKey, process.Id);

            Debug.Log("HoustonIncidents harvester started (live City incidents). Log: " + log);
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            Debug.LogWarning("Could not start the HoustonIncidents harvester (is the .NET SDK installed?): " + exception.Message);
        }
    }

    /// <summary>Stops the harvester and everything it started (dotnet run starts the app as a child process).</summary>
    private static void Stop()
    {
        var process = Running();

        SessionState.EraseInt(ProcessKey);

        if (process == null)
        {
            return;
        }

        try
        {
            using var kill = Process.Start(new ProcessStartInfo("taskkill", "/PID " + process.Id + " /T /F") { UseShellExecute = false, CreateNoWindow = true });

            kill?.WaitForExit(5000);

            Debug.Log("HoustonIncidents harvester stopped.");
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            Debug.LogWarning("Could not stop the HoustonIncidents harvester: " + exception.Message);
        }
    }

    /// <summary>The harvester process this editor started, if it is still running.</summary>
    private static Process? Running()
    {
        var id = SessionState.GetInt(ProcessKey, 0);

        if (id == 0)
        {
            return null;
        }

        try
        {
            var process = Process.GetProcessById(id);

            return process.HasExited ? null : process;
        }
        catch (System.ArgumentException)
        {
            return null;
        }
    }

    [MenuItem(ToggleMenu)]
    private static void Toggle()
    {
        EditorPrefs.SetBool(EnabledKey, !Enabled);
    }

    [MenuItem(ToggleMenu, true)]
    private static bool ToggleCheck()
    {
        Menu.SetChecked(ToggleMenu, Enabled);

        return true;
    }

    [MenuItem("HTownSim/Live Incidents/Set Harvester Folder...")]
    private static void SetFolder()
    {
        var folder = EditorUtility.OpenFolderPanel("HoustonIncidents harvester project (src/Harvester)", Path.GetFullPath(EditorPrefs.GetString(ProjectKey, DefaultProject)), "");

        if (!string.IsNullOrEmpty(folder))
        {
            EditorPrefs.SetString(ProjectKey, folder);
        }
    }

    [MenuItem("HTownSim/Live Incidents/Open Harvester Log")]
    private static void OpenLog()
    {
        var log = Path.GetFullPath(LogPath);

        if (File.Exists(log))
        {
            EditorUtility.OpenWithDefaultApp(log);
        }
    }
}
