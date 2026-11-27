#nullable enable
using System;
using System.Collections.Generic;
using HTownSim.Runtime.Fleet;
using HTownSim.Runtime.Incidents.Managers;
using UnityEngine;

namespace HTownSim.Runtime
{
    /// <summary>
    /// A computer-driven unit: it sits parked at its station until it is sent, then drives the street route to the scene with lights and
    /// siren, works the scene, and, when it is an ambulance or a medic unit with a patient, takes the patient to the nearest hospital's ER
    /// bay before it drives back to its station. It moves along the route as a body on the ground (no physics); the traffic clears cars
    /// out of its way through <see cref="EmergencyTraffic"/>.
    /// </summary>
    internal sealed class AiUnit : MonoBehaviour
    {
        /// <summary>What the unit is doing.</summary>
        internal enum Phase
        {
            /// <summary>At its station.</summary>
            Parked,

            /// <summary>Driving to the scene.</summary>
            Responding,

            /// <summary>Stopped at the scene.</summary>
            OnScene,

            /// <summary>Driving a patient to the hospital.</summary>
            ToHospital,

            /// <summary>Stopped at the hospital.</summary>
            AtHospital,

            /// <summary>Driving back to the station.</summary>
            Returning,
        }

        /// <summary>What kind of incident the unit is on, which sets how long it works the scene.</summary>
        internal enum Call
        {
            /// <summary>A fire or rescue.</summary>
            Fire,

            /// <summary>An EMS call (a patient; only an ambulance or medic unit takes them to the hospital).</summary>
            Ems,

            /// <summary>A police call.</summary>
            Police,
        }

        /// <summary>The turn angle (degrees) at which a unit slows to its slowest corner speed; gentler bends slow it proportionally less. Raise it so only very sharp turns slow it fully; lower it so even moderate bends do.</summary>
        private const float SharpCornerDegrees = 110f;

        /// <summary>The slowest speed through a corner, as a fraction (0 to 1) of the unit's top speed. Raise it so units keep speed through corners; lower it so they crawl round them.</summary>
        private const float CornerSpeedFraction = 0.3f;

        /// <summary>How far (metres) before a corner a unit starts slowing for it. Raise it for earlier, gentler slowing; lower it for late braking.</summary>
        private const float CornerLookAhead = 16f;

        /// <summary>How close (metres) a unit must get to a route point to count it as reached and aim for the next. Raise it to cut corners; lower it to follow the route line closely.</summary>
        private const float ReachDistance = 1.5f;

        /// <summary>How long (seconds) a computer-driven unit works the scene of a fire or rescue. Raise it for longer scene time per call (units stay out longer); lower it for quick calls.</summary>
        private const float FireSceneSeconds = 110f;

        /// <summary>How long (seconds) a unit works a call that is not a fire or a patient pick-up (a police call, or an engine at a medical call). Raise it for longer scene time; lower it for quicker clears.</summary>
        private const float SupportSceneSeconds = 60f;

        /// <summary>How long (seconds) an ambulance or medic unit works the scene before it loads the patient. Raise it for longer patient care; lower it to load and go sooner.</summary>
        private const float PatientSceneSeconds = 45f;

        /// <summary>How long (seconds) a transporting unit stays at the hospital before returning. Raise it to keep ambulances tied up longer; lower it to free them sooner.</summary>
        private const float HospitalSeconds = 25f;

        /// <summary>How far apart (metres) the units at one scene stop along the street, each one this much further back than the one before. Raise it to spread units out more; lower it to bunch them (too low and they overlap).</summary>
        private const float SlotMetres = 20f;

        /// <summary>How far above the unit (metres) the ground check for its height starts. Raise it for roads that rise steeply; lower it to avoid hitting roofs.</summary>
        private const float ProbeUp = 40f;

        /// <summary>How far down (metres) the ground check for the unit's height reaches. Raise it for deep drops; lower it to ignore far-below surfaces.</summary>
        private const float ProbeDown = 80f;

        /// <summary>Seconds each unit after the first waits before it pulls out, so units leaving one station do not drive into each other. Raise it for a longer gap between departures; lower it to leave together.</summary>
        private const float DepartStaggerSeconds = 3f;

        /// <summary>How close (metres) another unit ahead may be before this one slows for it. Raise it to keep a longer following gap; lower it to follow closer.</summary>
        private const float FollowDistance = 45f;

        /// <summary>The distance (metres, centre to centre) at which a unit stops for the one ahead. A ladder is about 12 m long, so 20 leaves a clear gap of several metres between bumpers. Raise it for more space; lower it to close up.</summary>
        private const float StopGap = 20f;

        /// <summary>How long (seconds) a unit waits behind another before it drives on regardless, so two units can never lock each other in place. Raise it to wait longer; lower it to push through sooner.</summary>
        private const float BlockedPatienceSeconds = 4f;

        /// <summary>How long (seconds) a unit that lost patience ignores the one ahead, to get past it.</summary>
        private const float PushThroughSeconds = 3f;

        /// <summary>How far (metres) straight out of its bay a unit drives before it joins the street route (and the point it comes back in through). The stations' aprons are open for at least 34 m. Raise it to clear a deeper station front; lower it for a shorter run.</summary>
        private const float BayApronMetres = 16f;

        /// <summary>The way the unit faces in its bay (ground x, z): out of the station door.</summary>
        private Vector2 _homeForward = Vector2.up;

        private static readonly List<AiUnit> AllUnits = new List<AiUnit>();

        private float _departDelay;

        private float _blockedSeconds;

        private float _ignoreUntil;

        private readonly RaycastHit[] _hits = new RaycastHit[12];

        private RoadGraph _graph = null!;

        private Vector3 _homePosition;

        private Quaternion _homeRotation;

        private AiEmergencyLights? _lights;

        /// <summary>How this kind of unit flashes, sounds and drives: the same values the player's truck of this kind uses.</summary>
        private UnitBehavior _behavior = UnitBehavior.Engine;

        /// <summary>True when the unit's kind takes patients to a hospital (from its profile).</summary>
        private bool _canTransport;

        /// <summary>The unit's horn, blown at intersections and at vehicles in its way.</summary>
        private AiHorn? _horn;

        /// <summary>Which points of the route are junctions of the street network (index for index), and the last one the horn was blown for.</summary>
        private bool[] _junction = new bool[0];

        private int _honkedJunction = -1;

        /// <summary>Seconds before the horn may be blown again.</summary>
        private float _hornCooldown;

        /// <summary>True while another unit ahead has slowed this one (set by the following-distance check).</summary>
        private bool _blockedAhead;

        /// <summary>How far (metres) ahead along its route a unit looks for an intersection to blow the horn for. Raise it to honk earlier; lower it to honk closer to the junction.</summary>
        private const float HornApproachMetres = 45f;

        /// <summary>How far (metres) ahead a traffic car counts as in the unit's way, and how far to either side of its line.</summary>
        private const float HornBlockedMetres = 24f;

        private const float HornBlockedHalfWidth = 2.6f;

        /// <summary>Seconds between horn blasts: a unit does not honk again sooner than this. Raise it for a quieter unit; lower it for a more insistent one.</summary>
        private const float HornCooldownSeconds = 4f;

        /// <summary>Blasts at an intersection (a warning to cross traffic) and at a vehicle in the way.</summary>
        private const int IntersectionBlasts = 2;

        private const int BlockedBlasts = 1;

        private List<Vector2> _route = new List<Vector2>();

        private int _next;

        private float _speed;

        private bool _playerDriving;

        private const float ManualSpeed = 8f;

        private float _cruise;

        private float _timer;

        private bool _hasPatient;

        private Call _call;

        private Func<Vector2, Vector2?>? _hospitalFor;

        /// <summary>The unit's radio name, e.g. Ambulance 16.</summary>
        internal string UnitName { get; private set; } = "";

        /// <summary>What the unit is doing now.</summary>
        internal Phase Current { get; private set; }

        /// <summary>The incident the unit is on, or empty.</summary>
        internal string IncidentId { get; private set; } = "";

        /// <summary>True when the unit is on a call (it is not sent again until it is back at its station).</summary>
        internal bool Busy => Current != Phase.Parked;

        /// <summary>Lights, siren and horn as flags for a spectator: 1 when the lights flash, 2 when the siren sounds, 4 when the horn sounds.</summary>
        internal int LightFlags => (_lights == null ? 0 : (_lights.IsOn ? 1 : 0) | (_lights.SirenOn ? 2 : 0)) | (_horn != null && _horn.Sounding ? 4 : 0);

        /// <summary>Where the unit is now (ground x, z).</summary>
        internal Vector2 Position => new Vector2(transform.position.x, transform.position.z);

        /// <summary>The patient is loaded: the unit leaves the scene for the hospital (the incident id).</summary>
        internal event Action<string>? PatientLoaded;

        /// <summary>The unit finished its part of an incident (its scene work, or the hospital): the incident id and whether it took a patient.</summary>
        internal event Action<string, bool>? Finished;

        /// <summary>Sets the unit up where it is parked.</summary>
        internal void Initialize(string unitName, RoadGraph graph, Func<Vector2, Vector2?> hospitalFor)
        {
            UnitName = unitName;

            _graph = graph;

            _hospitalFor = hospitalFor;

            _homePosition = transform.position;

            _homeRotation = transform.rotation;

            var facing = _homeRotation * Vector3.forward;

            _homeForward = new Vector2(facing.x, facing.z).normalized;

            _behavior = UnitBehavior.For(unitName);

            var profile = ApparatusLoadout.ProfileFor(unitName);

            _canTransport = profile.CanTransport;

            _lights = gameObject.AddComponent<AiEmergencyLights>();

            _lights.Configure(_behavior);

            _horn = gameObject.AddComponent<AiHorn>();

            _horn.Configure(profile.AirHorn, _behavior.HornVolume);

            gameObject.AddComponent<AiWheelSpin>();
        }

        /// <summary>Sends the unit to an incident.</summary>
        /// <param name="incidentId">The incident.</param>
        /// <param name="scene">Where it is (ground x, z).</param>
        /// <param name="call">What kind of incident it is.</param>
        /// <param name="slot">How many units are already stopping at the scene: each one stops a vehicle length further back along the street.</param>
        internal void Dispatch(string incidentId, Vector2 scene, Call call, int slot)
        {
            if (Busy)
            {
                return;
            }

            IncidentId = incidentId;

            _call = call;

            _hasPatient = call == Call.Ems && _canTransport;

            var route = _graph.Route(Position, scene);

            if (route == null)
            {
                // No street connects: the unit is not sent (Sent stays false), so the dispatcher can pick another.
                IncidentId = "";

                return;
            }

            // The route ends at the street nearest the scene (the scene itself may be inside a lot), and units after the first stop short of it.
            if (route.Count > 2)
            {
                route.RemoveAt(route.Count - 1);
            }

            Trim(route, slot * SlotMetres);

            // Pull straight out of the bay before joining the route: a straight line from the bay to the nearest street cuts through the station's walls and its neighbours.
            route.Insert(1, ApronPoint());

            Begin(Phase.Responding, route, _behavior.ResponseSpeed);

            // Each unit after the first waits a little longer, lights on, before it pulls out.
            _departDelay = slot * DepartStaggerSeconds;

            _lights?.Set(true, true);
        }

        /// <summary>True when the unit was sent (a route was found).</summary>
        internal bool Sent => Busy;

        internal bool TryTakeDriverControl()
        {
            if (_playerDriving || Current != Phase.Responding || _route.Count < 2)
            {
                return false;
            }

            _speed = 0f;
            _playerDriving = true;
            return true;
        }

        internal void DriveWithPlayerInput(float throttle, float steering, float deltaTime)
        {
            if (!_playerDriving || deltaTime <= 0f)
            {
                return;
            }

            var wanted = Mathf.Clamp(throttle, -1f, 1f) * ManualSpeed;
            _speed = Mathf.MoveTowards(_speed, wanted, _behavior.AiAcceleration * deltaTime);
            if (Mathf.Abs(_speed) < 0.01f)
            {
                return;
            }

            transform.Rotate(0f, steering * Mathf.Sign(_speed) * _behavior.TurnDegreesPerSecond * deltaTime, 0f);
            var next = transform.position + transform.forward * (_speed * deltaTime);
            var ground = new Vector2(next.x, next.z);
            transform.position = new Vector3(next.x, GroundHeight(ground), next.z);
        }

        internal bool TryReturnDriverControl()
        {
            if (!_playerDriving)
            {
                return false;
            }

            var route = _graph.Route(Position, _route[_route.Count - 1]);
            if (route == null)
            {
                return false;
            }

            _playerDriving = false;
            _speed = 0f;
            Begin(Current, route, _cruise);
            return true;
        }

        private void Update()
        {
            if (_playerDriving)
            {
                return;
            }

            switch (Current)
            {
                case Phase.Responding:
                case Phase.ToHospital:
                case Phase.Returning:
                    Drive();

                    break;
                case Phase.OnScene:
                case Phase.AtHospital:
                    _timer -= Time.deltaTime;

                    if (_timer <= 0f)
                    {
                        Leave();
                    }

                    break;
            }
        }

        /// <summary>Shortens a route by a distance from its end (never to fewer than two points).</summary>
        private static void Trim(List<Vector2> route, float metres)
        {
            while (metres > 0f && route.Count > 2)
            {
                var last = route[route.Count - 1];

                var before = route[route.Count - 2];

                var length = Vector2.Distance(last, before);

                if (length <= metres)
                {
                    route.RemoveAt(route.Count - 1);

                    metres -= length;
                }
                else
                {
                    route[route.Count - 1] = Vector2.Lerp(last, before, metres / length);

                    return;
                }
            }
        }

        private void Begin(Phase phase, List<Vector2> route, float cruise)
        {
            Current = phase;

            _route = route;

            _next = 1;

            _cruise = cruise;

            _departDelay = 0f;

            _blockedSeconds = 0f;

            // Mark the junctions on the route now, so the horn can be blown on the approach to each one.
            _junction = new bool[route.Count];

            for (var i = 1; i < route.Count - 1; i++)
            {
                _junction[i] = _graph.IsJunction(route[i]);
            }

            _honkedJunction = -1;

            _hornCooldown = 0f;

            if (phase != Phase.Returning)
            {
                EmergencyTraffic.Register(transform);
            }
        }

        /// <summary>Moves along the route: speeds up on the straights, slows for corners and for the end, and faces the way it goes.</summary>
        private void Drive()
        {
            if (_next >= _route.Count)
            {
                Arrive();

                return;
            }

            if (_departDelay > 0f)
            {
                _departDelay -= Time.deltaTime;

                return;
            }

            var position = Position;

            var target = _route[_next];

            var offset = target - position;

            var distance = offset.magnitude;

            if (distance < ReachDistance)
            {
                _next++;

                return;
            }

            var heading = offset / distance;

            var wanted = Mathf.Min(_cruise, CornerLimit(position));

            wanted = Mathf.Min(wanted, FollowLimit(position, heading));

            UpdateHorn(position, heading);

            _speed = Mathf.MoveTowards(_speed, wanted, (wanted < _speed ? _behavior.Braking : _behavior.AiAcceleration) * Time.deltaTime);

            var step = Mathf.Min(distance, _speed * Time.deltaTime);

            var moved = position + (heading * step);

            var rotation = Quaternion.LookRotation(new Vector3(heading.x, 0f, heading.y), Vector3.up);

            transform.rotation = Quaternion.RotateTowards(transform.rotation, rotation, _behavior.TurnDegreesPerSecond * Time.deltaTime);

            transform.position = new Vector3(moved.x, GroundHeight(moved), moved.y);
        }

        /// <summary>
        /// The speed the unit may keep given the unit in front of it: it eases off inside <see cref="FollowDistance"/> and stops at
        /// <see cref="StopGap"/>. A unit that has waited too long drives on regardless for a moment, so two units never lock up. Parked
        /// units do not count (they stand in their bays).
        /// </summary>
        private float FollowLimit(Vector2 position, Vector2 heading)
        {
            if (Time.time < _ignoreUntil)
            {
                _blockedAhead = false;

                return _cruise;
            }

            var limit = _cruise;

            foreach (var other in AllUnits)
            {
                if (other == this || other.Current == Phase.Parked)
                {
                    continue;
                }

                var delta = other.Position - position;

                var gap = delta.magnitude;

                if (gap > FollowDistance || gap < 0.01f || Vector2.Dot(delta / gap, heading) < 0.5f)
                {
                    continue;
                }

                limit = Mathf.Min(limit, _cruise * Mathf.Clamp01((gap - StopGap) / (FollowDistance - StopGap)));
            }

            _blockedAhead = limit < _cruise * 0.6f;

            if (limit < 0.5f)
            {
                _blockedSeconds += Time.deltaTime;

                if (_blockedSeconds > BlockedPatienceSeconds)
                {
                    _blockedSeconds = 0f;

                    _ignoreUntil = Time.time + PushThroughSeconds;
                }
            }
            else
            {
                _blockedSeconds = 0f;
            }

            return limit;
        }

        /// <summary>
        /// Blows the horn like a crew driving with lights and siren: two blasts on the approach to an intersection, one at a vehicle in
        /// the way (another unit that has slowed this one, or a traffic car close ahead). Not while driving home without a call.
        /// </summary>
        private void UpdateHorn(Vector2 position, Vector2 heading)
        {
            if (_horn == null || Current == Phase.Returning)
            {
                return;
            }

            _hornCooldown -= Time.deltaTime;

            if (_hornCooldown > 0f)
            {
                return;
            }

            var travelled = 0f;

            var from = position;

            for (var i = _next; i < _route.Count - 1 && i < _junction.Length; i++)
            {
                travelled += Vector2.Distance(from, _route[i]);

                from = _route[i];

                if (travelled > HornApproachMetres)
                {
                    break;
                }

                if (_junction[i] && i != _honkedJunction)
                {
                    _honkedJunction = i;

                    _hornCooldown = HornCooldownSeconds;

                    _horn.Honk(IntersectionBlasts);

                    return;
                }
            }

            if (_blockedAhead || TrafficSystem.CarAhead(transform.position, new Vector3(heading.x, 0f, heading.y), HornBlockedMetres, HornBlockedHalfWidth))
            {
                _hornCooldown = HornCooldownSeconds;

                _horn.Honk(BlockedBlasts);
            }
        }

        private void OnEnable()
        {
            AllUnits.Add(this);
        }

        private void OnDisable()
        {
            AllUnits.Remove(this);
        }

        /// <summary>The speed the unit may keep given the corner ahead and the end of the route.</summary>
        private float CornerLimit(Vector2 position)
        {
            var limit = _cruise;

            var remaining = Vector2.Distance(position, _route[_next]);

            if (_next + 1 < _route.Count)
            {
                var into = (_route[_next] - position).normalized;

                var out_ = (_route[_next + 1] - _route[_next]).normalized;

                var angle = Vector2.Angle(into, out_);

                if (remaining < CornerLookAhead)
                {
                    limit = Mathf.Lerp(_cruise, _cruise * CornerSpeedFraction, Mathf.Clamp01(angle / SharpCornerDegrees));
                }
            }
            else
            {
                // The last point: come to a stop at it.
                limit = Mathf.Min(limit, Mathf.Sqrt(2f * _behavior.Braking * Mathf.Max(0f, remaining - 1f)) + 0.5f);
            }

            return Mathf.Max(limit, 2f);
        }

        /// <summary>The route is finished.</summary>
        private void Arrive()
        {
            _speed = 0f;

            switch (Current)
            {
                case Phase.Responding:
                    Current = Phase.OnScene;

                    _timer = _hasPatient ? PatientSceneSeconds : _call == Call.Fire ? FireSceneSeconds : SupportSceneSeconds;

                    _lights?.Set(true, false);

                    break;
                case Phase.ToHospital:
                    Current = Phase.AtHospital;

                    _timer = HospitalSeconds;

                    _lights?.Set(true, false);

                    break;
                case Phase.Returning:
                    Park();

                    break;
            }
        }

        /// <summary>The unit's time at a stop is over: a unit with a patient drives to the hospital, any other goes home.</summary>
        private void Leave()
        {
            if (Current == Phase.OnScene && _hasPatient)
            {
                var hospital = _hospitalFor?.Invoke(Position);

                var route = hospital == null ? null : _graph.Route(Position, hospital.Value);

                if (route != null)
                {
                    PatientLoaded?.Invoke(IncidentId);

                    Begin(Phase.ToHospital, route, _behavior.ResponseSpeed);

                    _lights?.Set(true, true);

                    return;
                }
            }

            var finished = IncidentId;

            var transported = Current == Phase.AtHospital;

            IncidentId = "";

            if (finished.Length > 0)
            {
                Finished?.Invoke(finished, transported);
            }

            GoHome();
        }

        private void GoHome()
        {
            var route = _graph.Route(Position, new Vector2(_homePosition.x, _homePosition.z));

            _lights?.Set(false, false);

            EmergencyTraffic.Unregister(transform);

            if (route == null)
            {
                transform.SetPositionAndRotation(_homePosition, _homeRotation);

                Current = Phase.Parked;

                return;
            }

            // Come back in through the front of the bay, not across the station's walls.
            route.Insert(route.Count - 1, ApronPoint());

            Begin(Phase.Returning, route, _behavior.ReturnSpeed);
        }

        /// <summary>The point straight out of the unit's bay (the way it faces when parked), where it joins or leaves the street route.</summary>
        private Vector2 ApronPoint()
        {
            return new Vector2(_homePosition.x, _homePosition.z) + (_homeForward * BayApronMetres);
        }

        private void Park()
        {
            transform.SetPositionAndRotation(_homePosition, _homeRotation);

            Current = Phase.Parked;

            _speed = 0f;
        }

        /// <summary>The ground height under a point: the first solid surface that is not this unit.</summary>
        private float GroundHeight(Vector2 point)
        {
            var count = Physics.RaycastNonAlloc(new Vector3(point.x, transform.position.y + ProbeUp, point.y), Vector3.down, _hits, ProbeUp + ProbeDown, ~0, QueryTriggerInteraction.Ignore);

            var best = float.MaxValue;

            var height = transform.position.y;

            for (var i = 0; i < count; i++)
            {
                var hit = _hits[i];

                if (hit.collider.transform.IsChildOf(transform) || hit.distance >= best)
                {
                    continue;
                }

                // Vehicles, people and roofs are not ground: only the street, a slab or the ground sheet is.
                if (hit.collider.name == "Streets" || hit.collider.name.StartsWith("Slab", StringComparison.Ordinal) || hit.collider.name.StartsWith("Ground", StringComparison.Ordinal))
                {
                    best = hit.distance;

                    height = hit.point.y;
                }
            }

            return height;
        }

        private void OnDestroy()
        {
            EmergencyTraffic.Unregister(transform);
        }
    }
}
