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

        /// <summary>Shortest wait, in seconds, between two incidents released from the waiting queue onto the active-incidents board. Raise it so calls arrive further apart (fewer runs); lower it for a busier board. This and the longest gap set how often new runs appear.</summary>
        [Tooltip("Shortest wait, in seconds, between two incidents released onto the board.")]
        [SerializeField] private float _minimumGapSeconds = 8f;

        /// <summary>Longest wait, in seconds, between two released incidents. Raise it for long quiet stretches between calls; lower it to keep calls coming steadily. Keep it at or above the shortest gap.</summary>
        [Tooltip("Longest wait, in seconds, between two incidents released onto the board.")]
        [SerializeField] private float _maximumGapSeconds = 35f;

        /// <summary>How many released incidents the board keeps (the oldest drop off). Raise it to keep more calls active at once; lower it so older calls disappear sooner.</summary>
        [Tooltip("An incident opened within this many minutes is recent: recent ones are released first.")]
        [SerializeField] private int _recentMinutes = LiveQueueSettings.DefaultRecentMinutes;

        [Tooltip("An incident opened more than this many minutes ago is not taken in at all (0 keeps everything).")]
        [SerializeField] private int _maximumAgeMinutes = LiveQueueSettings.DefaultMaximumAgeMinutes;

        [Tooltip("How many released incidents the board keeps.")]
        [SerializeField] private int _boardSize = 40;

        /// <summary>
        /// When on, a listed engine, ladder or medic unit the game does not have (E035, say) is covered by the closest free game unit of the same
        /// type, so most live calls get a response. Turn it off to send only the exact units the City lists (most calls then get nobody).
        /// </summary>
        [Tooltip("Cover a listed engine, ladder or medic unit the game lacks with the closest free game unit of that type.")]
        [SerializeField] private bool _substituteUnits = true;

        /// <summary>How many lines the active-incidents HUD shows. Raise it for a longer list on screen (a taller panel); lower it for a shorter one.</summary>
        [Tooltip("How many lines the active-incidents HUD shows.")]
        [SerializeField] private int _hudLines = 10;

        /// <summary>Seconds between checks of the inbox folder for new feed files. Raise it to check less often (a slower reaction to new files); lower it to pick files up quickly.</summary>
        [Tooltip("Seconds between checks of the inbox folder for new feed files.")]
        [SerializeField] private float _scanSeconds = 1f;

        /// <summary>While a feed file arrived within this many seconds the random fallback calls stay off (the feed counts as live). Raise it to keep the fallback off for longer after the feed goes quiet; lower it so the game makes up calls sooner when the feed stops.</summary>
        [Tooltip("While a feed file arrived within this many seconds, the random fallback calls stay off.")]
        [SerializeField] private float _liveWindowSeconds = 600f;

        /// <summary>Shortest gap between releases.</summary>
        internal float MinimumGapSeconds => _minimumGapSeconds;

        /// <summary>Longest gap between releases.</summary>
        internal float MaximumGapSeconds => _maximumGapSeconds;

        /// <summary>
        /// Incidents opened within this many minutes are released first. Raise it to let older calls on the board earlier; lower it to keep the
        /// board close to the current time.
        /// </summary>
        internal int RecentMinutes => _recentMinutes;

        /// <summary>
        /// Incidents opened more than this many minutes ago are not taken in. Raise it to keep older calls (the City page and the saved
        /// ledger can hold calls from hours ago); lower it for only fresh ones; 0 keeps everything.
        /// </summary>
        internal int MaximumAgeMinutes => _maximumAgeMinutes;

        /// <summary>How many released incidents the board keeps.</summary>
        internal int BoardSize => _boardSize;

        /// <summary>How many lines the HUD shows.</summary>
        internal int HudLines => _hudLines;

        /// <summary>True when a missing listed engine, ladder or medic unit is covered by the closest free unit of that type.</summary>
        internal bool SubstituteUnits => _substituteUnits;

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
