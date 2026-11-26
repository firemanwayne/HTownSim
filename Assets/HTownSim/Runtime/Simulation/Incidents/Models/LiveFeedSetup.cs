#nullable enable
using HTownSim.Runtime.Incidents.Managers;
using UnityEngine;

namespace HTownSim.Runtime.Incidents.Models
{
    /// <summary>What the live feed needs from the scene: the incident manager, the driven apparatus, the map area and the inbox.</summary>
    internal readonly struct LiveFeedSetup
    {
        /// <summary>Creates a setup.</summary>
        internal LiveFeedSetup(IncidentManager manager, Transform player, Rect? playableArea, string inboxDirectory)
        {
            Manager = manager;

            Player = player;

            PlayableArea = playableArea;

            InboxDirectory = inboxDirectory;
        }

        /// <summary>The incident manager live dispatches are submitted to.</summary>
        internal IncidentManager Manager { get; }

        /// <summary>The driven apparatus.</summary>
        internal Transform Player { get; }

        /// <summary>The drivable map in scene x, z, or null for no limit.</summary>
        internal Rect? PlayableArea { get; }

        /// <summary>The incident inbox folder (the feed reads its Live subfolder).</summary>
        internal string InboxDirectory { get; }
    }
}
