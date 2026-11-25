#nullable enable
using System.Diagnostics;
using System.IO;
using UnityEditor;
using Debug = UnityEngine.Debug;

/// <summary>
/// Starts the incident service (Tools/IncidentService, which owns the incident lifecycle: created, assigned, handled, cleared) when
/// Play Mode starts and stops it when Play Mode ends. It runs hidden with <c>dotnet run</c>; its output goes to
/// Logs/IncidentService.log. The incident ledger it keeps survives between runs (%LOCALAPPDATA%\HTownSim\IncidentService). The process id
/// is kept in SessionState so it survives the domain reload on entering Play Mode. Menu: HTownSim > Live Incidents.
/// </summary>
[InitializeOnLoad]
internal static class IncidentServiceLauncher
{
    /// <summary>The address the service listens on (the game and the harvester use the same one).</summary>
    internal const string Address = "http://127.0.0.1:5870";

    /// <summary>The service project inside this repository, relative to the Unity project folder.</summary>
    internal const string DefaultProject = "Tools/IncidentService/src/Api";

    private const string EnabledKey = "HTownSim.IncidentService.Enabled";

    private const string ProcessKey = "HTownSim.IncidentService.Pid";

    private const string LogPath = "Logs/IncidentService.log";

    private const string ToggleMenu = "HTownSim/Live Incidents/Start Incident Service On Play";

    static IncidentServiceLauncher()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;

        EditorApplication.quitting += Stop;
    }

    /// <summary>True when the service project is in the repository, so the harvester should post to it.</summary>
    internal static bool Available => Enabled && Directory.Exists(Path.GetFullPath(DefaultProject));

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

    /// <summary>Starts the service unless it is already running.</summary>
    private static void Start()
    {
        if (Running() != null)
        {
            return;
        }

        var project = Path.GetFullPath(DefaultProject);

        if (!Directory.Exists(project))
        {
            Debug.LogWarning("Incident service not found at " + project + "; the game runs on feed files only.");

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

        try
        {
            var process = Process.Start(start);

            if (process == null)
            {
                return;
            }

            SessionState.SetInt(ProcessKey, process.Id);

            Debug.Log("Incident service started at " + Address + ". Log: " + log);
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            Debug.LogWarning("Could not start the incident service (is the .NET SDK installed?): " + exception.Message);
        }
    }

    /// <summary>Stops the service and everything it started (dotnet run starts the app as a child process).</summary>
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

            Debug.Log("Incident service stopped.");
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            Debug.LogWarning("Could not stop the incident service: " + exception.Message);
        }
    }

    /// <summary>The service process this editor started, if it is still running.</summary>
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

    [MenuItem("HTownSim/Live Incidents/Open Incident Service Log")]
    private static void OpenLog()
    {
        var log = Path.GetFullPath(LogPath);

        if (File.Exists(log))
        {
            EditorUtility.OpenWithDefaultApp(log);
        }
    }
}
