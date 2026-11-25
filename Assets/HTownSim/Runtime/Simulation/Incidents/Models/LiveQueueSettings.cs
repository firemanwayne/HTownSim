#nullable enable

namespace HTownSim.Runtime.Incidents.Models
{
    /// <summary>How the live queue trickles incidents out: the random gap between releases, and how many it keeps.</summary>
    internal readonly struct LiveQueueSettings
    {
        /// <summary>Creates the settings.</summary>
        internal LiveQueueSettings(float minimumGapSeconds, float maximumGapSeconds, int boardSize)
        {
            MinimumGapSeconds = minimumGapSeconds;

            MaximumGapSeconds = maximumGapSeconds;

            BoardSize = boardSize;
        }

        /// <summary>Shortest wait between two releases.</summary>
        internal float MinimumGapSeconds { get; }

        /// <summary>Longest wait between two releases.</summary>
        internal float MaximumGapSeconds { get; }

        /// <summary>How many released incidents the board keeps (the oldest drop off).</summary>
        internal int BoardSize { get; }
    }
}
