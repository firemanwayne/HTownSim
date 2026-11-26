#nullable enable
using System;
using UnityEngine;

namespace HTownSim.Runtime.Incidents.Models
{
    /// <summary>One live feed file from the harvester: new or updated incidents and the ids that closed.</summary>
    [Serializable]
    internal sealed class LiveFeedFile
    {
        [SerializeField] private string generated = "";

        [SerializeField] private LiveIncidentRecord[] incidents = new LiveIncidentRecord[0];

        [SerializeField] private string[] closed = new string[0];

        [SerializeField] private string[] cleared = new string[0];

        [SerializeField] private long revision;

        /// <summary>The incident service's revision this answer is current to (0 for a feed file from the harvester).</summary>
        internal long Revision => revision;

        /// <summary>True when the incident service sent this (it decides when an incident is cleared), false for a harvester feed file.</summary>
        internal bool FromService => revision > 0;

        /// <summary>The ids the incident service cleared: their scene (such as a patient) can go.</summary>
        internal string[] Cleared => cleared ?? new string[0];

        /// <summary>When the harvester polled.</summary>
        internal string Generated => generated ?? "";

        /// <summary>The new and updated incidents.</summary>
        internal LiveIncidentRecord[] Incidents => incidents ?? new LiveIncidentRecord[0];

        /// <summary>The ids that left the City's page.</summary>
        internal string[] Closed => closed ?? new string[0];
    }
}
