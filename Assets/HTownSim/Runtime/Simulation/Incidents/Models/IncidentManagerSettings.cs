#nullable enable
using UnityEngine;

namespace HTownSim.Runtime.Incidents.Models
{
    /// <summary>How the <c>IncidentManager</c> is set up for a scene.</summary>
    internal readonly struct IncidentManagerSettings
    {
        /// <summary>Creates the settings.</summary>
        internal IncidentManagerSettings(Rect? playableArea, string inboxDirectory)
        {
            PlayableArea = playableArea;

            InboxDirectory = inboxDirectory;
        }

        /// <summary>The playable area in scene x, z metres; incidents outside it are refused. Null accepts any position.</summary>
        internal Rect? PlayableArea { get; }

        /// <summary>The folder watched for incident JSON files.</summary>
        internal string InboxDirectory { get; }
    }
}
