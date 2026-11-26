#nullable enable
using System.Collections.Generic;

namespace HTownSim.Runtime.Incidents.Models
{
    /// <summary>How big an incident is, what it asks for, and where the text for it came from.</summary>
    internal readonly struct IncidentDetails
    {
        /// <summary>Creates the details.</summary>
        internal IncidentDetails(int severity, IReadOnlyList<IncidentUnit> requiredUnits, string address, string source)
        {
            Severity = severity;

            RequiredUnits = requiredUnits;

            Address = address;

            Source = source;
        }

        /// <summary>1 (minor) to 5 (major).</summary>
        internal int Severity { get; }

        /// <summary>The apparatus the call asks for.</summary>
        internal IReadOnlyList<IncidentUnit> RequiredUnits { get; }

        /// <summary>The address text.</summary>
        internal string Address { get; }

        /// <summary>The source name.</summary>
        internal string Source { get; }
    }
}
