#nullable enable
using System;
using UnityEngine;

namespace HTownSim.Runtime.Incidents.Models
{
    /// <summary>
    /// The standard incident JSON, exactly as every source sends it (COH data, scanner transcripts, the fallback generator).
    /// Field names are the JSON keys. See Documentation/Incidents.md.
    /// </summary>
    [Serializable]
    internal sealed class IncidentPayload
    {
        /// <summary>A unique id; an id already seen this session is ignored.</summary>
        [SerializeField] private string incidentId = "";

        /// <summary>HFD, EMS or HPD.</summary>
        [SerializeField] private string agency = "";

        /// <summary>The call type, e.g. STRUCTURE_FIRE or MEDICAL.</summary>
        [SerializeField] private string callType = "";

        /// <summary>Latitude and longitude of the incident.</summary>
        [SerializeField] private GeoPoint coordinates = new GeoPoint();

        /// <summary>1 (minor) to 5 (major).</summary>
        [SerializeField] private int severity;

        /// <summary>The apparatus the call asks for, e.g. ENGINE, LADDER, AMBULANCE.</summary>
        [SerializeField] private string[] requiredUnits = new string[0];

        /// <summary>Optional street address or location text for the HUD.</summary>
        [SerializeField] private string address = "";

        /// <summary>Optional name of whatever produced the call (e.g. coh-mcp, scanner).</summary>
        [SerializeField] private string source = "";

        /// <summary>Creates an empty payload for JSON deserialization.</summary>
        internal IncidentPayload()
        {
        }

        /// <summary>Creates a payload from its parts.</summary>
        internal IncidentPayload(IncidentPayloadParts parts)
        {
            incidentId = parts.IncidentId;

            agency = parts.Agency;

            callType = parts.CallType;

            coordinates = parts.Coordinates;

            severity = parts.Severity;

            requiredUnits = parts.RequiredUnits;

            address = parts.Address;

            source = parts.Source;
        }

        /// <summary>The unique id.</summary>
        internal string IncidentId => incidentId ?? "";

        /// <summary>The agency text.</summary>
        internal string Agency => agency ?? "";

        /// <summary>The call type text.</summary>
        internal string CallType => callType ?? "";

        /// <summary>The coordinates.</summary>
        internal GeoPoint Coordinates => coordinates ?? new GeoPoint();

        /// <summary>The severity.</summary>
        internal int Severity => severity;

        /// <summary>The required unit names.</summary>
        internal string[] RequiredUnits => requiredUnits ?? new string[0];

        /// <summary>The address text.</summary>
        internal string Address => address ?? "";

        /// <summary>The source name.</summary>
        internal string Source => source ?? "";
    }
}
