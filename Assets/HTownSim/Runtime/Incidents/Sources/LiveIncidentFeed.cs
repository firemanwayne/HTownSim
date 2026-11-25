#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using HTownSim.Runtime.Incidents.Managers;
using HTownSim.Runtime.Incidents.Models;
using HTownSim.Runtime.Incidents.Projections;
using UnityEngine;

namespace HTownSim.Runtime.Incidents.Sources
{
    /// <summary>
    /// The live City of Houston incident feed in the game. The HoustonIncidents harvester writes feed files into the Live folder of
    /// the incident inbox; this reads them into a <see cref="LiveIncidentQueue"/>, which trickles incidents onto the active-incidents
    /// board at random gaps. When a released (or updated) incident lists the player's unit, or the player is first-due, it is sent to
    /// the <see cref="IncidentManager"/> as a standard call and played like any other. Logic lives in the pure C# queue and rules;
    /// this component only does the folder, the clock and the scene lookups.
    /// </summary>
    internal sealed class LiveIncidentFeed : MonoBehaviour
    {
        /// <summary>The subfolder of the incident inbox the harvester writes to.</summary>
        internal const string FolderName = "Live";

        /// <summary>Scene names of the game's stations, in station-index order (Station 7 is the scene origin).</summary>
        private static readonly string[] StationObjects = { "Station07", "FireStation8", "FireStation16" };

        private LiveFeedSettings _settings = null!;

        private LiveIncidentQueue _queue = null!;

        private IncidentManager _manager = null!;

        private Transform _player = null!;

        private Rect? _playableArea;

        private string _folder = "";

        private readonly List<Vector2> _stations = new List<Vector2>();

        private float _nextScan;

        private float _lastFeedTime = float.NegativeInfinity;

        /// <summary>The queue and board (read by the HUD).</summary>
        internal LiveIncidentQueue Queue => _queue;

        /// <summary>The settings in use.</summary>
        internal LiveFeedSettings Settings => _settings;

        /// <summary>True while feed files are arriving (the random fallback calls stay off).</summary>
        internal bool IsLive => Time.time - _lastFeedTime < _settings.LiveWindowSeconds;

        /// <summary>The folder the harvester writes to.</summary>
        internal string Folder => _folder;

        /// <summary>Sets the feed up; called once by FireResponse after the incident manager.</summary>
        internal void Initialize(LiveFeedSetup setup)
        {
            _settings = LiveFeedSettings.Load();

            _manager = setup.Manager;

            _player = setup.Player;

            _playableArea = setup.PlayableArea;

            _folder = Path.Combine(setup.InboxDirectory, FolderName);

            Directory.CreateDirectory(_folder);

            // Station positions are looked up once; an absent station (not built in this scene) is left out of first-due.
            foreach (var name in StationObjects)
            {
                var station = GameObject.Find(name);

                _stations.Add(station != null ? new Vector2(station.transform.position.x, station.transform.position.z) : new Vector2(float.NaN, float.NaN));
            }

            _queue = new LiveIncidentQueue(new LiveQueueSettings(_settings.MinimumGapSeconds, _settings.MaximumGapSeconds, _settings.BoardSize), new System.Random());

            _queue.Priority = InvolvesPlayer;

            _queue.Released += OnReleased;

            _queue.Updated += OnUpdated;

            Debug.Log("Live incident feed: run the HoustonIncidents harvester; it writes to " + _folder);
        }

        private void Update()
        {
            if (_queue == null)
            {
                return;
            }

            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + _settings.ScanSeconds;

                ReadInbox();
            }

            _queue.Tick(Time.time);
        }

        /// <summary>Reads every finished feed file, oldest first, and deletes it.</summary>
        private void ReadInbox()
        {
            string[] files;

            try
            {
                files = Directory.GetFiles(_folder, "live-*.json");
            }
            catch (IOException)
            {
                return;
            }

            Array.Sort(files, StringComparer.Ordinal);

            foreach (var file in files)
            {
                try
                {
                    var feed = JsonUtility.FromJson<LiveFeedFile>(File.ReadAllText(file));

                    File.Delete(file);

                    if (feed != null)
                    {
                        _queue.Ingest(feed);

                        _lastFeedTime = Time.time;
                    }
                }
                catch (IOException)
                {
                    // Still being written or locked: try again on the next scan.
                }
                catch (ArgumentException exception)
                {
                    Debug.LogWarning("Live incident feed: could not read " + file + ": " + exception.Message);

                    File.Delete(file);
                }
            }
        }

        private void OnReleased(LiveBoardEntry entry)
        {
            Evaluate(entry);
        }

        private void OnUpdated(LiveBoardEntry entry)
        {
            if (!entry.DispatchedToPlayer)
            {
                Evaluate(entry);
            }
        }

        /// <summary>True when the incident would be dispatched to the player (so it skips the random trickle).</summary>
        private bool InvolvesPlayer(LiveIncidentRecord record)
        {
            var entry = new LiveBoardEntry(record, 0d);

            return DispatchReason(entry).Length > 0 && entry.InMap;
        }

        /// <summary>Places the incident on the map and works out whether the player is sent (the reason, or empty).</summary>
        private string DispatchReason(LiveBoardEntry entry)
        {
            var record = entry.Record;

            var nearest = -1;

            if (record.HasPosition)
            {
                var world = GeoProjection.ToWorld(record.Geo);

                entry.InMap = _playableArea == null || _playableArea.Value.Contains(new Vector2(world.x, world.z));

                if (entry.InMap)
                {
                    nearest = NearestStation(new Vector2(world.x, world.z));
                }
            }

            var unitName = ApparatusProfile.For(_player.gameObject).UnitName;

            var context = new LiveDispatchContext(LiveDispatchRule.UnitCodes(unitName), LiveDispatchRule.KindOf(unitName), PlayerStationIndex());

            return LiveDispatchRule.Reason(record, context, nearest);
        }

        /// <summary>Places the incident, and sends it to the player when the dispatch rule says so and it is inside the map.</summary>
        private void Evaluate(LiveBoardEntry entry)
        {
            var reason = DispatchReason(entry);

            if (reason.Length == 0 || !entry.InMap)
            {
                return;
            }

            var unitName = ApparatusProfile.For(_player.gameObject).UnitName;

            var payload = LivePayloadBuilder.Build(entry.Record, RequiredUnit(unitName, LiveDispatchRule.KindOf(unitName)));

            var result = _manager.Submit(JsonUtility.ToJson(payload), LivePayloadBuilder.SourceName);

            if (result.Accepted > 0)
            {
                entry.DispatchReason = reason;
            }
        }

        private int NearestStation(Vector2 position)
        {
            var best = -1;

            var bestDistance = float.MaxValue;

            for (var i = 0; i < _stations.Count; i++)
            {
                if (float.IsNaN(_stations[i].x))
                {
                    continue;
                }

                var distance = Vector2.Distance(_stations[i], position);

                if (distance < bestDistance)
                {
                    bestDistance = distance;

                    best = i;
                }
            }

            return best;
        }

        /// <summary>The player's station as an index into the station list (7, 8, 16).</summary>
        private int PlayerStationIndex()
        {
            return StartingTruckChoice.StationOf(ApparatusLoadout.Detect(_player)) switch
            {
                StartingStation.Station8 => 1,
                StartingStation.Station16 => 2,
                _ => 0,
            };
        }

        /// <summary>The player's rig in payload terms, so the call shows them assigned.</summary>
        private static string RequiredUnit(string unitName, LiveUnitKind kind)
        {
            return kind switch
            {
                LiveUnitKind.Ems => "AMBULANCE",
                LiveUnitKind.Chief => "BATTALION",
                _ => unitName.StartsWith("Ladder", StringComparison.OrdinalIgnoreCase) ? "LADDER" : "ENGINE",
            };
        }

        private void OnDestroy()
        {
            if (_queue != null)
            {
                _queue.Released -= OnReleased;

                _queue.Updated -= OnUpdated;
            }
        }
    }
}
