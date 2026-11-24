#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace HTownSim.Runtime.Incidents.Models
{
    /// <summary>The values of an <see cref="Incident"/>, so building one stays a single argument.</summary>
    internal readonly struct IncidentParts
    {
        /// <summary>Creates the parts.</summary>
        internal IncidentParts(IncidentIdentity identity, GeoPoint geo, Vector3 worldPosition, IncidentDetails details)
        {
            Id = identity.Id;

            Agency = identity.Agency;

            CallType = identity.CallType;

            CallText = identity.CallText;

            Geo = geo;

            WorldPosition = worldPosition;

            Severity = details.Severity;

            RequiredUnits = details.RequiredUnits;

            Address = details.Address;

            Source = details.Source;
        }

        /// <summary>The unique incident id.</summary>
        internal string Id { get; }

        /// <summary>The owning agency.</summary>
        internal IncidentAgency Agency { get; }

        /// <summary>The call type.</summary>
        internal IncidentCallType CallType { get; }

        /// <summary>The call type text as sent.</summary>
        internal string CallText { get; }

        /// <summary>The position in degrees.</summary>
        internal GeoPoint Geo { get; }

        /// <summary>The position in the scene.</summary>
        internal Vector3 WorldPosition { get; }

        /// <summary>The severity.</summary>
        internal int Severity { get; }

        /// <summary>The required units.</summary>
        internal IReadOnlyList<IncidentUnit> RequiredUnits { get; }

        /// <summary>The address text.</summary>
        internal string Address { get; }

        /// <summary>The source name.</summary>
        internal string Source { get; }
    }
}
