#nullable enable
using UnityEngine;

namespace HTownSim.Runtime.Incidents.Models
{
    /// <summary>
    /// Tunable numbers for the live City of Houston incident feed. Create one with Assets > Create > HTownSim > Live Feed Settings
    /// and put it at Resources/LiveFeedSettings to override the defaults.
    /// </summary>
    [CreateAssetMenu(fileName = "LiveFeedSettings", menuName = "HTownSim/Live Feed Settings")]
    internal sealed class LiveFeedSettings : ScriptableObject
    {
        /// <summary>The resource path the feed loads the settings from.</summary>
        internal const string ResourcePath = "LiveFeedSettings";

        [Tooltip("Shortest wait, in seconds, between two incidents released onto the board.")]
        [SerializeField] private float _minimumGapSeconds = 8f;

        [Tooltip("Longest wait, in seconds, between two incidents released onto the board.")]
        [SerializeField] private float _maximumGapSeconds = 35f;

        [Tooltip("How many released incidents the board keeps.")]
        [SerializeField] private int _boardSize = 40;

        [Tooltip("How many lines the active-incidents HUD shows.")]
        [SerializeField] private int _hudLines = 10;

        [Tooltip("Seconds between checks of the inbox folder for new feed files.")]
        [SerializeField] private float _scanSeconds = 1f;

        [Tooltip("While a feed file arrived within this many seconds, the random fallback calls stay off.")]
        [SerializeField] private float _liveWindowSeconds = 600f;

        /// <summary>Shortest gap between releases.</summary>
        internal float MinimumGapSeconds => _minimumGapSeconds;

        /// <summary>Longest gap between releases.</summary>
        internal float MaximumGapSeconds => _maximumGapSeconds;

        /// <summary>How many released incidents the board keeps.</summary>
        internal int BoardSize => _boardSize;

        /// <summary>How many lines the HUD shows.</summary>
        internal int HudLines => _hudLines;

        /// <summary>Seconds between inbox scans.</summary>
        internal float ScanSeconds => _scanSeconds;

        /// <summary>How long after the last feed file the feed counts as live.</summary>
        internal float LiveWindowSeconds => _liveWindowSeconds;

        /// <summary>The settings asset under Resources, or a default instance when there is none.</summary>
        internal static LiveFeedSettings Load()
        {
            var asset = Resources.Load<LiveFeedSettings>(ResourcePath);

            return asset != null ? asset : CreateInstance<LiveFeedSettings>();
        }
    }
}
