#nullable enable

namespace HTownSim.Runtime.Incidents.Models
{
    /// <summary>The outcome of validating one payload: an incident, or the reason it was refused.</summary>
    internal sealed class IncidentParseResult
    {
        private IncidentParseResult(Incident? incident, string error, string id)
        {
            Incident = incident;

            Error = error;

            Id = id;
        }

        /// <summary>The validated incident, or null when the payload was refused.</summary>
        internal Incident? Incident { get; }

        /// <summary>Why the payload was refused; empty when it was accepted.</summary>
        internal string Error { get; }

        /// <summary>The payload's id when it had one, for logging.</summary>
        internal string Id { get; }

        /// <summary>True when the payload became an incident.</summary>
        internal bool Ok => Incident is not null;

        /// <summary>An accepted payload.</summary>
        internal static IncidentParseResult Accept(Incident incident)
        {
            return new IncidentParseResult(incident, "", incident.Id);
        }

        /// <summary>A refused payload.</summary>
        internal static IncidentParseResult Refuse(string id, string error)
        {
            return new IncidentParseResult(null, error, id);
        }
    }
}
