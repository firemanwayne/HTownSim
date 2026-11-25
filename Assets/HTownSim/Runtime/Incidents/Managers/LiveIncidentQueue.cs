#nullable enable
using System;
using System.Collections.Generic;
using HTownSim.Runtime.Incidents.Models;

namespace HTownSim.Runtime.Incidents.Managers
{
    /// <summary>
    /// Pure C# queue between the harvester's feed and the game (no UnityEngine dependency). Harvested incidents wait here, then are
    /// released one at a time, in random order, at random gaps, onto the active-incidents board. Unit changes update a waiting or
    /// released incident; a closure removes it from both.
    /// </summary>
    internal sealed class LiveIncidentQueue
    {
        private readonly LiveQueueSettings _settings;

        private readonly Random _random;

        /// <summary>Harvested incidents not released yet.</summary>
        private readonly List<LiveIncidentRecord> _waiting = new List<LiveIncidentRecord>();

        /// <summary>Released incidents, newest first.</summary>
        private readonly List<LiveBoardEntry> _board = new List<LiveBoardEntry>();

        private double _nextRelease;

        /// <summary>Creates a queue.</summary>
        internal LiveIncidentQueue(LiveQueueSettings settings, Random random)
        {
            _settings = settings;

            _random = random;
        }

        /// <summary>
        /// Incidents for which this returns true (they involve the player) skip the random trickle and are released on the next tick.
        /// </summary>
        internal Func<LiveIncidentRecord, bool>? Priority { get; set; }

        /// <summary>An incident moved onto the board.</summary>
        internal event Action<LiveBoardEntry>? Released;

        /// <summary>A released incident's units changed.</summary>
        internal event Action<LiveBoardEntry>? Updated;

        /// <summary>A released incident closed.</summary>
        internal event Action<string>? Closed;

        /// <summary>Released incidents, newest first.</summary>
        internal IReadOnlyList<LiveBoardEntry> Board => _board;

        /// <summary>How many harvested incidents are still waiting.</summary>
        internal int Waiting => _waiting.Count;

        /// <summary>Takes in one feed file.</summary>
        internal void Ingest(LiveFeedFile feed)
        {
            foreach (var record in feed.Incidents)
            {
                if (record == null || record.IncidentId.Length == 0)
                {
                    continue;
                }

                var released = _board.Find(entry => entry.Record.IncidentId == record.IncidentId);

                if (released != null)
                {
                    if (!SameUnits(released.Record.Units, record.Units))
                    {
                        released.Record.SetUnits(record.Units);

                        Updated?.Invoke(released);
                    }

                    continue;
                }

                var waiting = _waiting.FindIndex(item => item.IncidentId == record.IncidentId);

                if (waiting >= 0)
                {
                    _waiting[waiting] = record;
                }
                else
                {
                    _waiting.Add(record);
                }
            }

            foreach (var id in feed.Closed)
            {
                _waiting.RemoveAll(item => item.IncidentId == id);

                if (_board.RemoveAll(entry => entry.Record.IncidentId == id) > 0)
                {
                    Closed?.Invoke(id);
                }
            }
        }

        /// <summary>Releases one waiting incident (picked at random) when its time has come.</summary>
        internal void Tick(double now)
        {
            if (_waiting.Count == 0)
            {
                return;
            }

            var urgent = Priority == null ? -1 : _waiting.FindIndex(record => Priority(record));

            if (urgent >= 0)
            {
                Release(urgent, now);

                return;
            }

            if (now < _nextRelease)
            {
                return;
            }

            Release(_random.Next(_waiting.Count), now);

            var gap = _settings.MinimumGapSeconds + (_random.NextDouble() * (_settings.MaximumGapSeconds - _settings.MinimumGapSeconds));

            _nextRelease = now + gap;
        }

        /// <summary>Moves one waiting incident onto the board.</summary>
        private void Release(int index, double now)
        {
            var record = _waiting[index];

            _waiting.RemoveAt(index);

            var entry = new LiveBoardEntry(record, now);

            _board.Insert(0, entry);

            if (_board.Count > _settings.BoardSize)
            {
                _board.RemoveAt(_board.Count - 1);
            }

            Released?.Invoke(entry);
        }

        private static bool SameUnits(string[] a, string[] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            for (var i = 0; i < a.Length; i++)
            {
                if (!string.Equals(a[i], b[i], StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
