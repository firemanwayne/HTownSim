#nullable enable
using System;
using System.IO;
using HTownSim.Runtime.Incidents.Interfaces;
using HTownSim.Runtime.Incidents.Managers;
using HTownSim.Runtime.Incidents.Models;
using UnityEngine;

namespace HTownSim.Runtime.Incidents.Sources
{
    /// <summary>
    /// Watches an inbox folder for incident files (.json: one incident or a batch; .jsonl: one incident per line). This is how the
    /// COH MCP data, a scanner-transcript tool or any other program hands calls to the game without being part of it. A file is
    /// read once it has stopped changing, then moved to <c>processed</c> (or <c>rejected</c>, with a .error.txt beside it).
    /// </summary>
    internal sealed class FolderIncidentSource : IIncidentSource
    {
        private const float PollSeconds = 1f;

        /// <summary>A file written this recently may still be mid-write, so it waits for the next poll.</summary>
        private const double SettleSeconds = 0.5d;

        private const string ProcessedFolder = "processed";

        private const string RejectedFolder = "rejected";

        private readonly string _inbox;

        private float _nextPoll;

        /// <summary>Creates the folder (and its processed and rejected subfolders) when needed.</summary>
        internal FolderIncidentSource(string inbox)
        {
            _inbox = inbox;

            try
            {
                Directory.CreateDirectory(Path.Combine(inbox, ProcessedFolder));

                Directory.CreateDirectory(Path.Combine(inbox, RejectedFolder));
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Incident inbox " + inbox + " could not be created: " + exception.Message);
            }
        }

        /// <inheritdoc />
        public string Name => "inbox";

        /// <inheritdoc />
        public void Poll(IncidentManager manager)
        {
            if (Time.unscaledTime < _nextPoll)
            {
                return;
            }

            _nextPoll = Time.unscaledTime + PollSeconds;

            if (!Directory.Exists(_inbox))
            {
                return;
            }

            try
            {
                foreach (var path in Directory.GetFiles(_inbox))
                {
                    var extension = Path.GetExtension(path).ToLowerInvariant();

                    if (extension is ".json" or ".jsonl")
                    {
                        ReadFile(manager, path);
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Incident inbox " + _inbox + " could not be read: " + exception.Message);
            }
        }

        /// <summary>Submits one file's incidents and files it away.</summary>
        private static void ReadFile(IncidentManager manager, string path)
        {
            if ((DateTime.UtcNow - File.GetLastWriteTimeUtc(path)).TotalSeconds < SettleSeconds)
            {
                return;
            }

            string text;

            try
            {
                text = File.ReadAllText(path);
            }
            catch (IOException)
            {
                // Still open in the writer; try again next poll.
                return;
            }

            var result = manager.Submit(text, Path.GetFileName(path));

            var folder = result.Accepted > 0 ? ProcessedFolder : RejectedFolder;

            var destination = UniquePath(Path.Combine(Path.GetDirectoryName(path)!, folder), Path.GetFileName(path));

            File.Move(path, destination);

            if (result.Rejected > 0)
            {
                File.WriteAllLines(destination + ".error.txt", result.Errors);
            }
        }

        /// <summary>A path in the folder that does not exist yet: the name itself, or the name with a time stamp in front.</summary>
        private static string UniquePath(string folder, string fileName)
        {
            var path = Path.Combine(folder, fileName);

            return File.Exists(path) ? Path.Combine(folder, DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + fileName) : path;
        }
    }
}
