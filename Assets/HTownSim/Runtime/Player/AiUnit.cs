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

        /// <summary>The top speed (metres per second) of a computer-driven unit responding with lights and siren (17 is about 38 mph). Raise it so units reach calls sooner; lower it for slower responses. Units slow for corners and for the end of the route regardless.</summary>
        private const float ResponseSpeed = 17f;

        /// <summary>The speed (metres per second) of a computer-driven unit driving back to its station. Raise it so units are back in service sooner (free again for the next call); lower it to keep them out longer.</summary>
        private const float ReturnSpeed = 11f;

        /// <summary>How fast a computer-driven unit picks up speed, in metres per second every second. Raise it for a quicker getaway; lower it for a heavier, slower build-up.</summary>
        private const float Acceleration = 4.5f;

        /// <summary>How hard a computer-driven unit slows for corners and for the end of its route, in metres per second every second. Raise it so units brake later and harder; lower it so they start slowing earlier.</summary>
        private const float Braking = 9f;

        /// <summary>The turn angle (degrees) at which a unit slows to its slowest corner speed; gentler bends slow it proportionally less. Raise it so only very sharp turns slow it fully; lower it so even moderate bends do.</summary>
        private const float SharpCornerDegrees = 110f;

        /// <summary>The slowest speed through a corner, as a fraction (0 to 1) of the unit's top speed. Raise it so units keep speed through corners; lower it so they crawl round them.</summary>
        private const float CornerSpeedFraction = 0.3f;

        /// <summary>How far (metres) before a corner a unit starts slowing for it. Raise it for earlier, gentler slowing; lower it for late braking.</summary>
        private const float CornerLookAhead = 16f;

        /// <summary>How fast (degrees per second) a computer-driven unit turns to face the way it is going. Raise it for sharper, snappier turning; lower it for a slow swing round a corner (a wider turning circle).</summary>
        private const float TurnDegreesPerSecond = 160f;

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
        private const float SlotMetres = 11f;

        /// <summary>How far above the unit (metres) the ground check for its height starts. Raise it for roads that rise steeply; lower it to avoid hitting roofs.</summary>
        private const float ProbeUp = 40f;

        /// <summary>How far down (metres) the ground check for the unit's height reaches. Raise it for deep drops; lower it to ignore far-below surfaces.</summary>
        private const float ProbeDown = 80f;

        private readonly RaycastHit[] _hits = new RaycastHit[12];

        private RoadGraph _graph = null!;

        private Vector3 _homePosition;

        private Quaternion _homeRotation;

        private AiEmergencyLights? _lights;

        private List<Vector2> _route = new List<Vector2>();

        private int _next;

        private float _speed;

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

            _lights = gameObject.AddComponent<AiEmergencyLights>();

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

            _hasPatient = call == Call.Ems && LiveDispatchRule.CanTransport(UnitName);

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

            Begin(Phase.Responding, route, ResponseSpeed);

            _lights?.Set(true, true);
        }

        /// <summary>True when the unit was sent (a route was found).</summary>
        internal bool Sent => Busy;

        private void Update()
        {
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

            _speed = Mathf.MoveTowards(_speed, wanted, (wanted < _speed ? Braking : Acceleration) * Time.deltaTime);

            var step = Mathf.Min(distance, _speed * Time.deltaTime);

            var moved = position + (heading * step);

            var rotation = Quaternion.LookRotation(new Vector3(heading.x, 0f, heading.y), Vector3.up);

            transform.rotation = Quaternion.RotateTowards(transform.rotation, rotation, TurnDegreesPerSecond * Time.deltaTime);

            transform.position = new Vector3(moved.x, GroundHeight(moved), moved.y);
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
                limit = Mathf.Min(limit, Mathf.Sqrt(2f * Braking * Mathf.Max(0f, remaining - 1f)) + 0.5f);
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

                    Begin(Phase.ToHospital, route, ResponseSpeed);

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

            Begin(Phase.Returning, route, ReturnSpeed);
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
