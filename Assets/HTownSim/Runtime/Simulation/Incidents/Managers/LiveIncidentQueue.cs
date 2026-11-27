#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using HTownSim.Runtime.Incidents.Models;
using HTownSim.Runtime.Incidents.Projections;

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

        /// <summary>The time now in Houston, which the incidents' opened times are measured against.</summary>
        private readonly Func<DateTime> _clock;

        /// <summary>Harvested incidents not released yet.</summary>
        private readonly List<LiveIncidentRecord> _waiting = new List<LiveIncidentRecord>();

        /// <summary>Released incidents, newest first.</summary>
        private readonly List<LiveBoardEntry> _board = new List<LiveBoardEntry>();

        private double _nextRelease;

        /// <summary>Creates a queue.</summary>
        internal LiveIncidentQueue(LiveQueueSettings settings, Random random, Func<DateTime>? clock = null)
        {
            _settings = settings;

            _random = random;

            _clock = clock ?? (() => HoustonClock.Now);
        }

        /// <summary>
        /// Incidents for which this returns true (they involve the player) skip the random trickle and are released on the next tick.
        /// </summary>
        internal Func<LiveIncidentRecord, bool>? Priority { get; set; }

        /// <summary>An incident moved onto the board.</summary>
        internal event Action<LiveBoardEntry>? Released;

        /// <summary>A released incident's units changed.</summary>
        internal event Action<LiveBoardEntry>? Updated;

        /// <summary>A released incident closed (it left the City's page; a unit may still be on it).</summary>
        internal event Action<string>? Closed;

        /// <summary>An incident is finished for good: the incident service cleared it (or, without the service, it closed with no unit assigned).</summary>
        internal event Action<string>? Cleared;

        /// <summary>Released incidents, newest first.</summary>
        internal IReadOnlyList<LiveBoardEntry> Board => _board;

        /// <summary>How many harvested incidents are still waiting.</summary>
        internal int Waiting => _waiting.Count;

        /// <summary>The harvested incidents not released yet, for a scene reload to carry over.</summary>
        internal IReadOnlyList<LiveIncidentRecord> WaitingRecords => _waiting;

        /// <summary>Puts back a board and waiting list saved before a scene reload. Nothing is released or updated, so nothing is dispatched again.</summary>
        internal void Restore(IEnumerable<LiveBoardEntry> board, IEnumerable<LiveIncidentRecord> waiting)
        {
            _board.Clear();

            _board.AddRange(board);

            _waiting.Clear();

            _waiting.AddRange(waiting);
        }

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
                else if (!TooOld(record))
                {
                    _waiting.Add(record);
                }
            }

            foreach (var id in feed.Closed)
            {
                _waiting.RemoveAll(item => item.IncidentId == id);

                var onBoard = _board.Find(entry => entry.Record.IncidentId == id);

                if (onBoard == null)
                {
                    continue;
                }

                _board.Remove(onBoard);

                Closed?.Invoke(id);

                // Without the incident service nothing decides when a scene may go, so a closed call nobody was assigned to clears here.
                if (!feed.FromService && onBoard.Record.Units.Length == 0)
                {
                    Cleared?.Invoke(id);
                }
            }

            foreach (var id in feed.Cleared)
            {
                Cleared?.Invoke(id);
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

            Release(NextToRelease(), now);

            var gap = _settings.MinimumGapSeconds + (_random.NextDouble() * (_settings.MaximumGapSeconds - _settings.MinimumGapSeconds));

            _nextRelease = now + gap;
        }

        /// <summary>True when the incident was opened longer ago than the settings keep (an incident with no readable time is kept).</summary>
        private bool TooOld(LiveIncidentRecord record)
        {
            return _settings.MaximumAgeMinutes > 0 && AgeMinutes(record) > _settings.MaximumAgeMinutes;
        }

        /// <summary>How many minutes ago the incident was opened; 0 when its time cannot be read (it is treated as current).</summary>
        private double AgeMinutes(LiveIncidentRecord record)
        {
            var opened = record.OpenedAt;

            return opened == null ? 0d : Math.Max(0d, (_clock() - opened.Value).TotalMinutes);
        }

        /// <summary>
        /// The waiting incident to release next: one picked at random from those opened within the recent window, or, when none is that
        /// recent, the newest, so the board stays as close to the current time as the feed allows.
        /// </summary>
        private int NextToRelease()
        {
            var recent = new List<int>();

            var newest = 0;

            var newestAge = double.MaxValue;

            for (var i = 0; i < _waiting.Count; i++)
            {
                var age = AgeMinutes(_waiting[i]);

                if (age <= _settings.RecentMinutes)
                {
                    recent.Add(i);
                }

                if (age < newestAge)
                {
                    newestAge = age;

                    newest = i;
                }
            }

            return recent.Count > 0 ? recent[_random.Next(recent.Count)] : newest;
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
