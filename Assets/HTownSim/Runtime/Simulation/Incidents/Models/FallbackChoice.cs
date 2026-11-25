#nullable enable

namespace HTownSim.Runtime.Incidents.Models
{
    /// <summary>The constraints on which site the fallback generator may pick.</summary>
    internal readonly struct FallbackChoice
    {
        /// <summary>Creates a choice.</summary>
        internal FallbackChoice(int preferredSite, int excludedSite, float minimumDistance)
        {
            PreferredSite = preferredSite;

            ExcludedSite = excludedSite;

            MinimumDistance = minimumDistance;
        }

        /// <summary>A site that must be used, or -1 to pick one.</summary>
        internal int PreferredSite { get; }

        /// <summary>A site that must not be used again, or -1.</summary>
        internal int ExcludedSite { get; }

        /// <summary>The nearest a picked site may be to the player, in metres.</summary>
        internal float MinimumDistance { get; }
    }
}
