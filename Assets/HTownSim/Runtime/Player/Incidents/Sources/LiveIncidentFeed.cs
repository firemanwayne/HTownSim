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
    /// goes to the game unit of that name; a listed unit the game does not have gets nobody. The player's unit, when it is one of them,
    /// is sent as above; the others are driven by the computer (<see cref="AiFleet"/>). The incident is cleared when every unit on it is done.
    /// </summary>
    internal sealed class LiveIncidentFeed : MonoBehaviour
    {
        /// <summary>The subfolder of the incident inbox the harvester writes to.</summary>
        internal const string FolderName = "Live";

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

        private Transform? _player;

        private Rect? _playableArea;

        private string _folder = "";

        private float _nextScan;

        private float _lastFeedTime = float.NegativeInfinity;

        /// <summary>True when the computer-driven fleet has patrol units, which then answer the police calls (the parked scene car is not used).</summary>
        internal bool HasPatrols => _fleet != null && _fleet.Units.Any(unit => unit.UnitName.StartsWith("HPD", System.StringComparison.OrdinalIgnoreCase));

        /// <summary>Computer-driven units were sent to a fire (no one in the game is on it): the incident id and where it is, for the flames.</summary>
        internal event System.Action<string, Vector3>? FireDispatched;

        /// <summary>The queue and board (read by the HUD).</summary>
        internal LiveIncidentQueue Queue => _queue;

        /// <summary>The scene's computer-driven fleet.</summary>
        internal AiFleet Fleet => _fleet;

        /// <summary>The settings in use.</summary>
        internal LiveFeedSettings Settings => _settings;

        /// <summary>True while feed files are arriving (the random fallback calls stay off).</summary>
        internal bool IsLive => Time.time - _lastFeedTime < _settings.LiveWindowSeconds;

        /// <summary>The folder the harvester writes to.</summary>
        internal string Folder => _folder;

        /// <summary>How far north of Station 7 (degrees of latitude) a test fire is made, about 330 m. Raise it for a longer drive; lower it for a shorter one.</summary>
        private const double TestFireLatitudeOffset = 0.003d;

        /// <summary>How far east of Station 7 (degrees of longitude) a test fire is made, about 140 m.</summary>
        private const double TestFireLongitudeOffset = 0.0015d;

        /// <summary>Station 7's geocode, the scene origin.</summary>
        private const double Station7Latitude = 29.73948387d;

        private const double Station7Longitude = -95.3734589d;

        private int _testFires;

        /// <summary>
        /// Starts a made-up structure fire near Station 7 and sends the units the dispatcher picks for it, as a call from the City's feed
        /// would (a spectator's test). Returns the incident id, where it is and the units sent, or null when no unit could be sent.
        /// </summary>
        internal (string Id, Vector3 World, IReadOnlyList<string> Units)? SubmitTestFire()
        {
            _testFires++;

            var id = "TEST-FIRE-" + _testFires + "-" + DateTime.UtcNow.ToString("HHmmss", System.Globalization.CultureInfo.InvariantCulture);

            var json = "{\"incidentId\":\"" + id + "\",\"agency\":\"FD\",\"incidentType\":\"STRUCTURE FIRE\",\"address\":\"TEST FIRE\",\"alarmLevel\":\"1\","
                + "\"units\":[\"E007\",\"L007\",\"E008\",\"E016\",\"A007\",\"A016\"],"
                + "\"lat\":" + (Station7Latitude + TestFireLatitudeOffset).ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                + ",\"lon\":" + (Station7Longitude + TestFireLongitudeOffset).ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "}";

            var record = JsonUtility.FromJson<LiveIncidentRecord>(json);

            Evaluate(new LiveBoardEntry(record, 0d));

            if (!_assigned.TryGetValue(id, out var units))
            {
                return null;
            }

            return (id, GeoProjection.ToWorld(record.Geo), units);
        }

        /// <summary>Sets the feed up; called once by FireResponse after the incident manager.</summary>
        internal void Initialize(LiveFeedSetup setup)
        {
            _settings = LiveFeedSettings.Load();

            _manager = setup.Manager;

            _player = setup.Player;

            _playableArea = setup.PlayableArea;

            _folder = Path.Combine(setup.InboxDirectory, FolderName);

            Directory.CreateDirectory(_folder);

            _queue = new LiveIncidentQueue(new LiveQueueSettings(_settings.MinimumGapSeconds, _settings.MaximumGapSeconds, _settings.BoardSize, _settings.RecentMinutes, _settings.MaximumAgeMinutes), new System.Random());

            _queue.Priority = InvolvesPlayer;

            _queue.Released += OnReleased;

            _queue.Updated += OnUpdated;

            // The incident service owns the lifecycle (created, assigned, handled, cleared); the feed folder stays as the fallback.
            _service = gameObject.AddComponent<IncidentServiceClient>();

            _service.Received += OnServiceFeed;

            _service.Begin();

            // The computer-driven units: every apparatus parked at the stations except the one the player is in.
            _fleet = gameObject.AddComponent<AiFleet>();

            _fleet.Initialize(_player != null ? ApparatusProfile.For(_player.gameObject).UnitName : null);

            _fleet.UnitFinished += OnAiUnitFinished;

            _fleet.PatientLoaded += OnPatientLoaded;

            _queue.Cleared += OnIncidentCleared;

            _manager.Accepted += OnAccepted;

            _manager.Completed += OnCompleted;

            RestoreCarryOver();

            Debug.Log("Live incident feed: run the HoustonIncidents harvester; it writes to " + _folder);
        }

        /// <summary>Copies the record of which units are on which incident, for a scene reload (see <see cref="CityCarryOver"/>).</summary>
        internal void SaveTo(Dictionary<string, string[]> assigned, Dictionary<string, int> pending, HashSet<string> matched)
        {
            foreach (var pair in _assigned)
            {
                assigned[pair.Key] = pair.Value;
            }

            foreach (var pair in _pending)
            {
                pending[pair.Key] = pair.Value;
            }

            foreach (var id in _matched)
            {
                matched.Add(id);
            }
        }

        /// <summary>
        /// Takes back the record saved before a unit switch, so incidents already matched are not matched again and are cleared when their
        /// last unit finishes. A unit the player has taken over (it was out on a call, and is no longer computer-driven) counts as done with it.
        /// </summary>
        private void RestoreCarryOver()
        {
            if (!CityCarryOver.HasData)
            {
                return;
            }

            // The board and the waiting list come back first, so the service's next answer finds each incident as it was (no second release, no second dispatch).
            _queue.Restore(CityCarryOver.Board, CityCarryOver.Waiting);

            foreach (var pair in CityCarryOver.Assigned)
            {
                _assigned[pair.Key] = pair.Value;
            }

            foreach (var pair in CityCarryOver.Pending)
            {
                _pending[pair.Key] = pair.Value;
            }

            foreach (var id in CityCarryOver.Matched)
            {
                _matched.Add(id);
            }

            foreach (var state in CityCarryOver.Units)
            {
                if (_fleet.Find(state.Name) == null && state.IncidentId.Length > 0)
                {
                    FinishUnit(state.IncidentId, "cleared");
                }
            }
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
            if (_player == null)
            {
                return;
            }

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
            // An incident matched before a unit switch comes back from the service; its units are already out.
            if (_matched.Contains(entry.Record.IncidentId))
            {
                return;
            }

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

            var target = PlaceOnMap(entry);

            return target != null && MatchFor(record, target.Value).Any(unit => unit.IsPlayer);
        }

        /// <summary>The units to send to an incident: the listed units the game has (fire and EMS) or the closest free patrol units (police).</summary>
        private IReadOnlyList<FleetCandidate> MatchFor(LiveIncidentRecord record, Vector2 target)
        {
            var candidates = Candidates();

            return record.Agency == PoliceAgency ? FleetDispatcher.MatchPolice(record, target, candidates) : FleetDispatcher.Match(record, candidates, new MatchRequest(target, _settings.SubstituteUnits));
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

            if (_player != null)
            {
                var name = ApparatusProfile.For(_player.gameObject).UnitName;
                var response = _player.GetComponent<FireResponse>();
                candidates.Add(new FleetCandidate(name, LiveDispatchRule.KindOf(name), new Vector2(_player.position.x, _player.position.z), response != null && response.IncidentActive, true));
            }

            foreach (var unit in _fleet.Units)
            {
                // A seated unit is free until it has a call; its player drives it there.
                candidates.Add(new FleetCandidate(unit.UnitName, LiveDispatchRule.KindOf(unit.UnitName), unit.Position, unit.Seated ? unit.SeatIncident.Length > 0 : unit.Busy, false));
            }

            return candidates;
        }

        /// <summary>Matches the incident to units and sends them (the player when it is one of them, the computer-driven units otherwise).</summary>
        private void Evaluate(LiveBoardEntry entry)
        {
            MatchUnits(entry);
        }

        /// <summary>
        /// Matches an incident to the units the game has and sends them: the player's unit through the incident manager, the
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

            var matched = MatchFor(record, target.Value);

            if (matched.Count == 0)
            {
                return;
            }

            var call = record.Agency == PoliceAgency ? AiUnit.Call.Police : LiveDispatchRule.TypeFits(record, LiveUnitKind.Ems) ? AiUnit.Call.Ems : AiUnit.Call.Fire;

            var names = new List<string>();

            var player = matched.FirstOrDefault(unit => unit.IsPlayer);

            foreach (var unit in matched.Where(unit => !unit.IsPlayer))
            {
                var driver = _fleet.Find(unit.Name);

                if (driver == null)
                {
                    continue;
                }

                if (driver.Seated)
                {
                    // A connected player drives this unit: its client is told the call and drives there.
                    driver.AssignSeat(record.IncidentId, target.Value, call, JsonUtility.ToJson(record));

                    names.Add(unit.Name);

                    continue;
                }

                driver.Dispatch(record.IncidentId, target.Value, call, names.Count);

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

            if (call == AiUnit.Call.Fire && player == null && record.IncidentType.ToUpperInvariant().Contains("FIRE"))
            {
                FireDispatched?.Invoke(record.IncidentId, GeoProjection.ToWorld(record.Geo));
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

        /// <summary>The player's rig in payload terms, so the call shows them assigned.</summary>
        internal static string RequiredUnit(string unitName, LiveUnitKind kind)
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
