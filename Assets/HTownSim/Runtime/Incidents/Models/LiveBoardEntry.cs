#nullable enable

namespace HTownSim.Runtime.Incidents.Models
{
    /// <summary>An incident on the active-incidents board: the City record, when it was released, and whether the player was sent.</summary>
    internal sealed class LiveBoardEntry
    {
        /// <summary>Creates an entry for a released incident.</summary>
        internal LiveBoardEntry(LiveIncidentRecord record, double releasedAt)
        {
            Record = record;

            ReleasedAt = releasedAt;
        }

        /// <summary>The City record (its units are kept up to date).</summary>
        internal LiveIncidentRecord Record { get; }

        /// <summary>Game time (seconds) when the incident was released onto the board.</summary>
        internal double ReleasedAt { get; }

        /// <summary>Why the player was dispatched (your unit is listed, or first-due), or empty when the player was not sent.</summary>
        internal string DispatchReason { get; set; } = "";

        /// <summary>True when the player was dispatched to this incident.</summary>
        internal bool DispatchedToPlayer => DispatchReason.Length > 0;

        /// <summary>True when the incident lies inside the drivable map.</summary>
        internal bool InMap { get; set; }
    }
}
