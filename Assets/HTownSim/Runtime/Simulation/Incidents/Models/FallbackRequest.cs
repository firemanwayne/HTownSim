#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace HTownSim.Runtime.Incidents.Models
{
    /// <summary>What the fallback generator needs to invent a call: the player's unit and position and the candidate sites.</summary>
    internal readonly struct FallbackRequest
    {
        /// <summary>Creates a request.</summary>
        internal FallbackRequest(FallbackWorld world, IncidentUnit playerUnit, Vector3 playerPosition, FallbackChoice choice)
        {
            Sites = world.Sites;

            Streets = world.Streets;

            PlayerUnit = playerUnit;

            PlayerPosition = playerPosition;

            PreferredSite = choice.PreferredSite;

            ExcludedSite = choice.ExcludedSite;

            MinimumDistance = choice.MinimumDistance;
        }

        /// <summary>Candidate incident positions in the scene.</summary>
        internal IReadOnlyList<Vector3> Sites { get; }

        /// <summary>Street name per site.</summary>
        internal IReadOnlyList<string> Streets { get; }

        /// <summary>The apparatus the player drives; the call always asks for it.</summary>
        internal IncidentUnit PlayerUnit { get; }

        /// <summary>Where the player is, to keep the call far enough away.</summary>
        internal Vector3 PlayerPosition { get; }

        /// <summary>A site that must be used, or -1 to pick one.</summary>
        internal int PreferredSite { get; }

        /// <summary>A site that must not be used again, or -1.</summary>
        internal int ExcludedSite { get; }

        /// <summary>The nearest a picked site may be to the player, in metres.</summary>
        internal float MinimumDistance { get; }
    }
}
