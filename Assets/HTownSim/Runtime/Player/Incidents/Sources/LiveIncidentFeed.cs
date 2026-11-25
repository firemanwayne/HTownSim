#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HTownSim.Runtime.Incidents.Clients;
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
    /// Every fire and EMS incident is also matched to the units the game has (<see cref="FleetDispatcher"/>): each unit the City lists
    /// goes to the game unit of that name, or to the closest free unit of the same kind. The player's unit, when it is one of them,
    /// is sent as above; the others are driven by the computer (<see cref="AiFleet"/>). The incident is cleared when every unit on it is done.
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

        private IncidentServiceClient _service = null!;

        private AiFleet _fleet = null!;

        /// <summary>The units assigned to each matched incident, by incident id (the service records them).</summary>
        private readonly Dictionary<string, string[]> _assigned = new Dictionary<string, string[]>();

        /// <summary>How many units are still on each matched incident; the incident is cleared when this reaches zero.</summary>
        private readonly Dictionary<string, int> _pending = new Dictionary<string, int>();

        /// <summary>Incidents already matched to units.</summary>
        private readonly HashSet<string> _matched = new HashSet<string>();

        /// <summary>EMS incidents that have no ambulance or medic unit yet (none was free), with where they are; one is sent when a unit frees up.</summary>
        private readonly Dictionary<string, (LiveIncidentRecord Record, Vector2 Target)> _awaitingTransport = new Dictionary<string, (LiveIncidentRecord Record, Vector2 Target)>();

        private float _nextTransportCheck;

        /// <summary>How often the incidents waiting for a transport unit are tried again, in seconds.</summary>
        private const float TransportRetrySeconds = 5f;

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

            // The incident service owns the lifecycle (created, assigned, handled, cleared); the feed folder stays as the fallback.
            _service = gameObject.AddComponent<IncidentServiceClient>();

            _service.Received += OnServiceFeed;

            _service.Begin();

            // The computer-driven units: every apparatus parked at the stations except the one the player is in.
            _fleet = gameObject.AddComponent<AiFleet>();

            _fleet.Initialize(ApparatusProfile.For(_player.gameObject).UnitName);

            _fleet.UnitFinished += OnAiUnitFinished;

            _fleet.PatientLoaded += OnPatientLoaded;

            _queue.Cleared += OnIncidentCleared;

            _manager.Accepted += OnAccepted;

            _manager.Completed += OnCompleted;

            Debug.Log("Live incident feed: run the HoustonIncidents harvester; it writes to " + _folder);
        }

        /// <summary>The incident service answered with what changed: handled like a feed file, so the board and scenes follow the ledger.</summary>
        private void OnServiceFeed(LiveFeedFile feed)
        {
            _queue.Ingest(feed);

            if (feed.Incidents.Length > 0 || feed.Closed.Length > 0)
            {
                _lastFeedTime = Time.time;
            }
        }

        /// <summary>A call the game accepted (the player's unit is on it): the service records the assignment.</summary>
        private void OnAccepted(Incident incident)
        {
            var unitName = ApparatusProfile.For(_player.gameObject).UnitName;

            // On a matched incident the service is told every unit on it, not only the player's.
            var units = _assigned.TryGetValue(incident.Id, out var all) ? all : new[] { unitName };

            _service.Assign(incident.Id, new AssignPayload(units, incident.CallLabel, incident.Address, incident.Geo));
        }

        /// <summary>The player finished a call: the service clears it, which removes its scene.</summary>
        private void OnCompleted(Incident incident, IncidentOutcome outcome)
        {
            FinishUnit(incident.Id, outcome.ToString().ToLowerInvariant());
        }

        /// <summary>A computer-driven ambulance has the patient: the patient leaves the scene.</summary>
        private void OnPatientLoaded(string incidentId)
        {
            var patients = GetComponent<LiveIncidentPatients>();

            if (patients != null)
            {
                patients.Load(incidentId);
            }
        }

        /// <summary>An incident is finished for good: nothing is kept for it any more.</summary>
        private void OnIncidentCleared(string incidentId)
        {
            _matched.Remove(incidentId);

            _awaitingTransport.Remove(incidentId);
        }

        /// <summary>A computer-driven unit finished its part of an incident.</summary>
        private void OnAiUnitFinished(string incidentId, bool transported)
        {
            FinishUnit(incidentId, transported ? "transported" : "cleared");
        }

        /// <summary>One unit is done with an incident; the incident is cleared when the last unit on it is done.</summary>
        private void FinishUnit(string incidentId, string outcome)
        {
            if (_pending.TryGetValue(incidentId, out var left))
            {
                left--;

                if (left > 0)
                {
                    _pending[incidentId] = left;

                    return;
                }

                _pending.Remove(incidentId);
            }

            _assigned.Remove(incidentId);

            _service.Clear(incidentId, outcome);
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

            if (Time.time >= _nextTransportCheck)
            {
                _nextTransportCheck = Time.time + TransportRetrySeconds;

                SendWaitingTransport();
            }
        }

        /// <summary>Sends a free computer-driven ambulance or medic unit to each EMS incident that has none yet.</summary>
        private void SendWaitingTransport()
        {
            foreach (var id in _awaitingTransport.Keys.ToList())
            {
                var (record, target) = _awaitingTransport[id];

                var unit = FleetDispatcher.Choose(record, target, Candidates().Where(candidate => !candidate.IsPlayer));

                var driver = unit == null ? null : _fleet.Find(unit.Name);

                if (driver == null)
                {
                    continue;
                }

                driver.Dispatch(id, target, true, _assigned.TryGetValue(id, out var current) ? current.Length : 0);

                if (!driver.Sent)
                {
                    continue;
                }

                _awaitingTransport.Remove(id);

                var names = (_assigned.TryGetValue(id, out var existing) ? existing : new string[0]).Append(driver.UnitName).ToArray();

                _assigned[id] = names;

                _pending[id] = (_pending.TryGetValue(id, out var left) ? left : 0) + 1;

                _service.Assign(id, new AssignPayload(names, record.IncidentType, record.Address, record.Geo));
            }
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
            if (!entry.DispatchedToPlayer && !_matched.Contains(entry.Record.IncidentId))
            {
                Evaluate(entry);
            }
        }

        /// <summary>True when the incident would be dispatched to the player (so it skips the random trickle).</summary>
        private bool InvolvesPlayer(LiveIncidentRecord record)
        {
            var entry = new LiveBoardEntry(record, 0d);

            if (record.Agency != PoliceAgency)
            {
                var target = PlaceOnMap(entry);

                return target != null && FleetDispatcher.Match(record, target.Value, Candidates()).Any(unit => unit.IsPlayer);
            }

            return DispatchReason(entry).Length > 0 && entry.InMap;
        }

        /// <summary>The page agency code of a police incident (fire and EMS incidents are FD).</summary>
        private const string PoliceAgency = "PD";

        /// <summary>Places the incident on the map (sets InMap) and returns where it is on the ground, or null when it has no position or is off the map.</summary>
        private Vector2? PlaceOnMap(LiveBoardEntry entry)
        {
            if (!entry.Record.HasPosition)
            {
                return null;
            }

            var world = GeoProjection.ToWorld(entry.Record.Geo);

            var ground = new Vector2(world.x, world.z);

            entry.InMap = _playableArea == null || _playableArea.Value.Contains(ground);

            return entry.InMap ? ground : (Vector2?)null;
        }

        /// <summary>The units that could be sent now: the player's unit and the computer-driven ones.</summary>
        private List<FleetCandidate> Candidates()
        {
            var candidates = new List<FleetCandidate>();

            var name = ApparatusProfile.For(_player.gameObject).UnitName;

            var response = _player.GetComponent<FireResponse>();

            candidates.Add(new FleetCandidate(name, LiveDispatchRule.KindOf(name), new Vector2(_player.position.x, _player.position.z), response != null && response.IncidentActive, true));

            foreach (var unit in _fleet.Units)
            {
                candidates.Add(new FleetCandidate(unit.UnitName, LiveDispatchRule.KindOf(unit.UnitName), unit.Position, unit.Busy, false));
            }

            return candidates;
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
            if (entry.Record.Agency != PoliceAgency)
            {
                MatchUnits(entry);

                return;
            }

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

        /// <summary>
        /// Matches a fire or EMS incident to the units the game has and sends them: the player's unit through the incident manager, the
        /// others by driving. The service is told every unit that was sent.
        /// </summary>
        private void MatchUnits(LiveBoardEntry entry)
        {
            var record = entry.Record;

            var target = PlaceOnMap(entry);

            if (target == null)
            {
                return;
            }

            var matched = FleetDispatcher.Match(record, target.Value, Candidates());

            if (matched.Count == 0)
            {
                return;
            }

            var ems = LiveDispatchRule.TypeFits(record, LiveUnitKind.Ems);

            var names = new List<string>();

            var player = matched.FirstOrDefault(unit => unit.IsPlayer);

            foreach (var unit in matched.Where(unit => !unit.IsPlayer))
            {
                var driver = _fleet.Find(unit.Name);

                if (driver == null)
                {
                    continue;
                }

                driver.Dispatch(record.IncidentId, target.Value, ems, names.Count);

                if (driver.Sent)
                {
                    names.Add(unit.Name);
                }
            }

            var count = names.Count;

            if (player != null)
            {
                names.Add(player.Name);

                count++;
            }

            if (count == 0)
            {
                return;
            }

            _matched.Add(record.IncidentId);

            // An EMS call with no ambulance or medic unit among the units sent waits until one is free.
            if (ems && !matched.Any(unit => LiveDispatchRule.CanTransport(unit.Name)))
            {
                _awaitingTransport[record.IncidentId] = (record, target.Value);
            }

            _assigned[record.IncidentId] = names.ToArray();

            _pending[record.IncidentId] = count;

            if (player == null)
            {
                _service.Assign(record.IncidentId, new AssignPayload(names.ToArray(), record.IncidentType, record.Address, record.Geo));

                return;
            }

            var reason = record.Units.Any(code => LiveDispatchRule.UnitCodes(player.Name).Contains(code, System.StringComparer.OrdinalIgnoreCase)) ? LiveDispatchRule.UnitReason : LiveDispatchRule.FirstDueReason;

            var payload = LivePayloadBuilder.Build(record, RequiredUnit(player.Name, player.Kind));

            var result = _manager.Submit(JsonUtility.ToJson(payload), LivePayloadBuilder.SourceName);

            if (result.Accepted > 0)
            {
                entry.DispatchReason = reason;

                return;
            }

            // The manager did not take it: the player is not on this incident, so the others carry it (or it is cleared when they finish).
            _pending[record.IncidentId] = count - 1;

            names.Remove(player.Name);

            _assigned[record.IncidentId] = names.ToArray();

            if (names.Count > 0)
            {
                _service.Assign(record.IncidentId, new AssignPayload(names.ToArray(), record.IncidentType, record.Address, record.Geo));
            }
            else
            {
                _pending.Remove(record.IncidentId);

                _assigned.Remove(record.IncidentId);
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
                LiveUnitKind.Police => "PATROL",
                _ => unitName.StartsWith("Ladder", StringComparison.OrdinalIgnoreCase) ? "LADDER" : "ENGINE",
            };
        }

        private void OnDestroy()
        {
            if (_fleet != null)
            {
                _fleet.UnitFinished -= OnAiUnitFinished;

                _fleet.PatientLoaded -= OnPatientLoaded;
            }

            if (_manager != null)
            {
                _manager.Accepted -= OnAccepted;

                _manager.Completed -= OnCompleted;
            }

            if (_queue != null)
            {
                _queue.Released -= OnReleased;

                _queue.Cleared -= OnIncidentCleared;

                _queue.Updated -= OnUpdated;
            }
        }
    }
}
