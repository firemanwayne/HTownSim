#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace HTownSim.Runtime.Incidents.Models
{
    /// <summary>The places the fallback generator may put a call.</summary>
    internal readonly struct FallbackWorld
    {
        /// <summary>Creates the world description.</summary>
        internal FallbackWorld(IReadOnlyList<Vector3> sites, IReadOnlyList<string> streets)
        {
            Sites = sites;

            Streets = streets;
        }

        /// <summary>Candidate incident positions in the scene.</summary>
        internal IReadOnlyList<Vector3> Sites { get; }

        /// <summary>Street name per site.</summary>
        internal IReadOnlyList<string> Streets { get; }
    }
}
