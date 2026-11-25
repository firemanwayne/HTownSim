#nullable enable
using System;
using UnityEngine;

namespace HTownSim.Runtime.Incidents.Models
{
    /// <summary>A file or message carrying several incidents: an object with an <c>incidents</c> array.</summary>
    [Serializable]
    internal sealed class IncidentBatch
    {
        /// <summary>The incidents. The field name is the JSON key.</summary>
        [SerializeField] private IncidentPayload[] incidents = new IncidentPayload[0];

        /// <summary>The incidents in the batch.</summary>
        internal IncidentPayload[] Incidents => incidents ?? new IncidentPayload[0];
    }
}
