#nullable enable

namespace HTownSim.Runtime.Incidents.Models
{
    /// <summary>How the live queue trickles incidents out: the random gap between releases, and how many it keeps.</summary>
    internal readonly struct LiveQueueSettings
    {
        /// <summary>Creates the settings.</summary>
        internal LiveQueueSettings(float minimumGapSeconds, float maximumGapSeconds, int boardSize, int recentMinutes = DefaultRecentMinutes, int maximumAgeMinutes = DefaultMaximumAgeMinutes)
        {
            RecentMinutes = recentMinutes;

            MaximumAgeMinutes = maximumAgeMinutes;

            MinimumGapSeconds = minimumGapSeconds;

            MaximumGapSeconds = maximumGapSeconds;

            BoardSize = boardSize;
        }

        /// <summary>The default for <see cref="RecentMinutes"/>.</summary>
        internal const int DefaultRecentMinutes = 60;

        /// <summary>The default for <see cref="MaximumAgeMinutes"/>.</summary>
        internal const int DefaultMaximumAgeMinutes = 480;

        /// <summary>
        /// An incident opened within this many minutes counts as recent: recent incidents are released first, in random order, and an older
        /// one only when no recent one is waiting (then the newest goes first). Raise it to let older calls on the board earlier; lower it to
        /// keep the board close to the current time.
        /// </summary>
        internal int RecentMinutes { get; }

        /// <summary>
        /// An incident opened more than this many minutes ago is not taken in at all (the City page and the incident service's saved
        /// ledger can still hold calls from hours ago). Raise it to keep older calls; lower it for a board of only fresh ones; 0 keeps everything.
        /// </summary>
        internal int MaximumAgeMinutes { get; }

        /// <summary>Shortest wait between two releases.</summary>
        internal float MinimumGapSeconds { get; }

        /// <summary>Longest wait between two releases.</summary>
        internal float MaximumGapSeconds { get; }

        /// <summary>How many released incidents the board keeps (the oldest drop off).</summary>
        internal int BoardSize { get; }
    }
}
