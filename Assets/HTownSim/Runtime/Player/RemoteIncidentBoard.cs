#nullable enable
using System;
using System.Collections.Generic;
using HTownSim.Runtime.Incidents.Models;

namespace HTownSim.Runtime
{
    /// <summary>
    /// The incident board a city server streams to a player: a snapshot at a time (<see cref="Begin"/>, one <see cref="Add"/> per incident,
    /// <see cref="End"/>), which replaces the last one. The HUD reads it like the board of a local feed (see <see cref="IIncidentBoard"/>).
    /// </summary>
    internal sealed class RemoteIncidentBoard : IIncidentBoard
    {
        private List<LiveBoardEntry> _board = new List<LiveBoardEntry>();

        private readonly List<LiveBoardEntry> _building = new List<LiveBoardEntry>();

        private int _buildingWaiting;

        private bool _buildingLive;

        public IReadOnlyList<LiveBoardEntry> Board => _board;

        public int Waiting { get; private set; }

        public bool IsLive { get; private set; }

        /// <summary>The server decides how many lines are worth sending; the HUD shows them all.</summary>
        public int HudLines { get; private set; } = DefaultLines;

        public event Action? Changed;

        /// <summary>Lines the panel has until the server has sent its first snapshot.</summary>
        private const int DefaultLines = 8;

        /// <summary>A snapshot starts: how many incidents wait, and whether the feed is live.</summary>
        internal void Begin(int waiting, bool live)
        {
            _building.Clear();

            _buildingWaiting = waiting;

            _buildingLive = live;
        }

        /// <summary>One incident of the snapshot; <paramref name="reason"/> is why it was sent to this player (empty when it was not).</summary>
        internal void Add(LiveIncidentRecord record, bool inMap, string reason)
        {
            _building.Add(new LiveBoardEntry(record, 0d) { InMap = inMap, DispatchReason = reason });
        }

        /// <summary>The snapshot is complete: it becomes the board.</summary>
        internal void End()
        {
            _board = new List<LiveBoardEntry>(_building);

            Waiting = _buildingWaiting;

            IsLive = _buildingLive;

            HudLines = Math.Max(DefaultLines, _board.Count);

            Changed?.Invoke();
        }
    }
}
