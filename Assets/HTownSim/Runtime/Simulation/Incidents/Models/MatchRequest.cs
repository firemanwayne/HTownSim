#nullable enable
using UnityEngine;

namespace HTownSim.Runtime.Incidents.Models
{
    /// <summary>What the dispatcher needs to know about an incident besides its listed units: where it is, and whether a missing listed engine, ladder or medic unit may be covered by another of the same type.</summary>
    internal readonly struct MatchRequest
    {
        /// <summary>Creates a request.</summary>
        internal MatchRequest(Vector2 target, bool substitute)
        {
            Target = target;

            Substitute = substitute;
        }

        /// <summary>Where the incident is (ground x, z), used to pick the closest cover.</summary>
        internal Vector2 Target { get; }

        /// <summary>True when same-type cover is allowed.</summary>
        internal bool Substitute { get; }
    }
}
