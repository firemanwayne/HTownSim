#nullable enable
using System;
using UnityEngine;

namespace HTownSim.Runtime.Incidents.Models
{
    /// <summary>Tells the incident service how an incident ended. The field name is the JSON key.</summary>
    [Serializable]
    internal sealed class OutcomePayload
    {
        [SerializeField] private string outcome = "";

        /// <summary>Creates a payload.</summary>
        internal OutcomePayload(string text)
        {
            outcome = text;
        }
    }
}
