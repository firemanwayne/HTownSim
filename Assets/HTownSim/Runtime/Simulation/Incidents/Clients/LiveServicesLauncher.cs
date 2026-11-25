#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace HTownSim.Runtime.Incidents.Clients
{
    /// <summary>
    /// Starts the live incident services next to a standalone build: the incident service (which owns the incident lifecycle) and the
    /// HoustonIncidents harvester (the City of Houston incident feed), both from the Services folder beside the game's executable, hidden,
    /// and stops them when the game quits. The editor has its own launchers (IncidentServiceLauncher and HarvesterLauncher), so this
    /// does nothing there. A service that is already answering on its port is left alone, and a missing Services folder is not an error:
    /// the game then runs on its own calls.
    /// </summary>
    internal static class LiveServicesLauncher
    {
#if !UNITY_EDITOR
        /// <summary>Where the incident service listens (the same address the game's client and the harvester use).</summary>
        private const string ServiceHost = "127.0.0.1";

        /// <summary>The port the incident service listens on. It must match the service's own setting and the game's client address, or the game will not find the service.</summary>
        private const int ServicePort = 5870;

        private const string ServiceUrl = "http://127.0.0.1:5870";

        /// <summary>The published programs, under Services next to the game.</summary>
        private const string ServicesFolder = "Services";

        private const string ServiceProgram = "IncidentService/IncidentService.Api.exe";

        private const string HarvesterProgram = "HoustonIncidents/HoustonIncidents.Harvester.exe";

        /// <summary>How long a connection to the service port may take when checking whether it is already running (milliseconds).</summary>
        private const int ServicePollMilliseconds = 250;

        private static Process? _service;

        private static Process? _harvester;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Start()
        {
            Application.quitting += Stop;

            var root = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? string.Empty, ServicesFolder);

            if (!Directory.Exists(root))
            {
                return;
            }

            var serviceUp = Listening();

            if (!serviceUp)
            {
                _service = Launch(Path.Combine(root, ServiceProgram), "IncidentService", false);

                // The harvester retries its posts, so it need not wait for the service to finish starting.
                serviceUp = _service != null;
            }

            // Without the service the harvester would write feed files, which the game also reads; with it, it posts to the service.
            _harvester = Launch(Path.Combine(root, HarvesterProgram), "HoustonIncidents", serviceUp);
        }

        /// <summary>Starts a program hidden with its output in a log file; null when it is missing or will not start.</summary>
        private static Process? Launch(string program, string name, bool postToService)
        {
            if (!File.Exists(program))
            {
                Debug.LogWarning("Live services: " + program + " not found.");

                return null;
            }

            var logFolder = Path.Combine(Application.persistentDataPath, "Logs");

            Directory.CreateDirectory(logFolder);

            var log = Path.Combine(logFolder, name + ".log");

            var start = new ProcessStartInfo("cmd.exe", "/c \"\"" + program + "\" > \"" + log + "\" 2>&1\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(program) ?? string.Empty,
            };

            if (postToService)
            {
                start.Environment["Incidents__ServiceUrl"] = ServiceUrl;
            }

            try
            {
                var process = Process.Start(start);

                Debug.Log("Live services: started " + name + ". Log: " + log);

                return process;
            }
            catch (Exception exception) when (exception is System.ComponentModel.Win32Exception || exception is InvalidOperationException)
            {
                Debug.LogWarning("Live services: could not start " + name + ": " + exception.Message);

                return null;
            }
        }

        /// <summary>True when something is accepting connections on the service's port.</summary>
        private static bool Listening()
        {
            try
            {
                using var client = new TcpClient();

                return client.ConnectAsync(ServiceHost, ServicePort).Wait(ServicePollMilliseconds);
            }
            catch (Exception exception) when (exception is SocketException || exception is AggregateException)
            {
                return false;
            }
        }

        /// <summary>Stops both programs and anything they started.</summary>
        private static void Stop()
        {
            Kill(_harvester);

            Kill(_service);

            _harvester = null;

            _service = null;
        }

        private static void Kill(Process? process)
        {
            if (process == null)
            {
                return;
            }

            try
            {
                if (!process.HasExited)
                {
                    using var kill = Process.Start(new ProcessStartInfo("taskkill", "/PID " + process.Id + " /T /F") { UseShellExecute = false, CreateNoWindow = true });

                    kill?.WaitForExit(5000);
                }
            }
            catch (Exception exception) when (exception is System.ComponentModel.Win32Exception || exception is InvalidOperationException)
            {
                Debug.LogWarning("Live services: could not stop a service: " + exception.Message);
            }
        }
#endif
    }
}
