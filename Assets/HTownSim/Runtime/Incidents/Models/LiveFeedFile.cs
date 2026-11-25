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

        /// <summary>When the harvester polled.</summary>
        internal string Generated => generated ?? "";

        /// <summary>The new and updated incidents.</summary>
        internal LiveIncidentRecord[] Incidents => incidents ?? new LiveIncidentRecord[0];

        /// <summary>The ids that left the City's page.</summary>
        internal string[] Closed => closed ?? new string[0];
    }
}
