#nullable enable

namespace HTownSim.Runtime.Incidents.Models
{
    /// <summary>The values of an <see cref="IncidentPayload"/>, for building one in code.</summary>
    internal readonly struct IncidentPayloadParts
    {
        /// <summary>Creates the parts.</summary>
        internal IncidentPayloadParts(string incidentId, string agency, string callType, GeoPoint coordinates, int severity, string[] requiredUnits, string address, string source)
        {
            IncidentId = incidentId;

            Agency = agency;

            CallType = callType;

            Coordinates = coordinates;

            Severity = severity;

            RequiredUnits = requiredUnits;

            Address = address;

            Source = source;
        }

        /// <summary>The unique id.</summary>
        internal string IncidentId { get; }

        /// <summary>The agency text.</summary>
        internal string Agency { get; }

        /// <summary>The call type text.</summary>
        internal string CallType { get; }

        /// <summary>The coordinates.</summary>
        internal GeoPoint Coordinates { get; }

        /// <summary>The severity.</summary>
        internal int Severity { get; }

        /// <summary>The required unit names.</summary>
        internal string[] RequiredUnits { get; }

        /// <summary>The address text.</summary>
        internal string Address { get; }

        /// <summary>The source name.</summary>
        internal string Source { get; }
    }
}
