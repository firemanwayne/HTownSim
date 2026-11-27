#nullable enable
using System;
using System.Collections.Generic;
using HTownSim.Runtime.Incidents.Models;
using HTownSim.Runtime.Incidents.Sources;

namespace HTownSim.Runtime
{
    /// <summary>The incident board of the city's own live feed, for the HUD (see <see cref="IIncidentBoard"/>).</summary>
    internal sealed class FeedIncidentBoard : IIncidentBoard, IDisposable
    {
        private readonly LiveIncidentFeed _feed;

        internal FeedIncidentBoard(LiveIncidentFeed feed)
        {
            _feed = feed;

            feed.Queue.Released += OnEntry;

            feed.Queue.Updated += OnEntry;

            feed.Queue.Closed += OnClosed;
        }

        public IReadOnlyList<LiveBoardEntry> Board => _feed.Queue.Board;

        public int Waiting => _feed.Queue.Waiting;

        public bool IsLive => _feed.IsLive;

        public int HudLines => _feed.Settings.HudLines;

        public event Action? Changed;

        private void OnEntry(LiveBoardEntry entry)
        {
            Changed?.Invoke();
        }

        private void OnClosed(string id)
        {
            Changed?.Invoke();
        }

        public void Dispose()
        {
            if (_feed != null && _feed.Queue != null)
            {
                _feed.Queue.Released -= OnEntry;

                _feed.Queue.Updated -= OnEntry;

                _feed.Queue.Closed -= OnClosed;
            }
        }
    }
}
