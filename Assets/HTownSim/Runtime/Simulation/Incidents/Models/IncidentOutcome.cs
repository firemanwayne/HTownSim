#nullable enable

namespace HTownSim.Runtime.Incidents.Models
{
    /// <summary>How an incident ended.</summary>
    internal enum IncidentOutcome
    {
        /// <summary>The player's unit handled it.</summary>
        Cleared,

        /// <summary>The response time ran out.</summary>
        Failed,
    }
}
