#nullable enable

namespace HTownSim.Runtime.Incidents.Models
{
    /// <summary>Who owns an incident and what kind of call it is.</summary>
    internal readonly struct IncidentIdentity
    {
        /// <summary>Creates an identity.</summary>
        internal IncidentIdentity(string id, IncidentAgency agency, IncidentCallType callType, string callText)
        {
            Id = id;

            Agency = agency;

            CallType = callType;

            CallText = callText;
        }

        /// <summary>The unique incident id.</summary>
        internal string Id { get; }

        /// <summary>The owning agency.</summary>
        internal IncidentAgency Agency { get; }

        /// <summary>The call type.</summary>
        internal IncidentCallType CallType { get; }

        /// <summary>The call type text as sent.</summary>
        internal string CallText { get; }
    }
}
