#nullable enable
using System.Collections.Generic;
using HTownSim.Runtime.Incidents.Managers;
using HTownSim.Runtime.Incidents.Models;
using HTownSim.Runtime.Incidents.Projections;
using HTownSim.Runtime.Incidents.Sources;
using UnityEngine;

namespace HTownSim.Runtime
{
    /// <summary>
    /// Parks an HPD patrol unit, lightbar flashing, at every live police incident inside the map once it is on the board, so the scene
    /// of a police call looks like one. The unit is put on the nearest free stretch of street and lined up with it; an incident's
    /// position is usually a house on the block, so the car is moved out to the road. Like <see cref="LiveIncidentPatients"/>, this
    /// never decides when a scene goes: the incident service does, and the car is removed when the queue reports the incident cleared.
    /// Only listens to the live feed.
    /// </summary>
    internal sealed class LiveIncidentPolice : MonoBehaviour
    {
        /// <summary>The page agency code of a police incident.</summary>
        private const string PoliceAgency = "PD";

        /// <summary>The patrol unit prefab, in a Resources folder.</summary>
        private const string PrefabResource = "HpdPatrol";

        /// <summary>The name of the collider the scene's streets are made of (the parcels and sidewalks are other colliders).</summary>
        private const string StreetCollider = "Streets";

        /// <summary>The most parked scene patrol cars at police incidents at once (used only when the fleet has no computer-driven patrol units). Raise it for more scenes with a car; lower it to save frame rate.</summary>
        private const int MaximumUnits = 12;

        /// <summary>How far (metres) from the incident the search for a free stretch of street starts. Raise it to park further from the address; lower it to park closer.</summary>
        private const float FirstRing = 4f;

        /// <summary>How far (metres) from the incident the search for street gives up. Raise it to find streets for more remote incidents; lower it to skip those with no street near.</summary>
        private const float LastRing = 40f;

        /// <summary>The step (metres) between search rings. Raise it for a coarser, faster search; lower it for a finer one that finds the nearest free spot more exactly.</summary>
        private const float RingStep = 4f;

        /// <summary>How many spots are tried on each search ring. Raise it for a finer search; lower it for a faster one.</summary>
        private const int SpotsPerRing = 12;

        /// <summary>The probe for the ground starts this far above the spot and reaches this far below it.</summary>
        private const float ProbeUp = 60f;

        private const float ProbeDown = 120f;

        /// <summary>Street direction: the angle step of the search (degrees) and how far along each way it looks for street (metres).</summary>
        private const float AxisStepDegrees = 10f;

        private static readonly float[] AxisSamples = { 4f, 8f, 12f };

        /// <summary>Half the size of the car (width, height, length), for the clearance check.</summary>
        private static readonly Vector3 HalfExtents = new Vector3(1.4f, 1f, 2.7f);

        private LiveIncidentFeed _feed = null!;

        private GameObject? _prefab;

        /// <summary>The patrol units out, by incident id.</summary>
        private readonly Dictionary<string, GameObject> _units = new Dictionary<string, GameObject>();

        /// <summary>Number of patrol units out now, for verification.</summary>
        internal int Count => _units.Count;

        /// <summary>Follows the feed.</summary>
        internal void Initialize(LiveIncidentFeed feed)
        {
            _feed = feed;

            _prefab = Resources.Load<GameObject>(PrefabResource);

            if (_prefab == null)
            {
                Debug.LogWarning("HPD patrol prefab '" + PrefabResource + "' not found in Resources; police incidents get no patrol unit. Run AgentScripts/BuildHpdPatrolPrefab.cs.");
            }

            feed.Queue.Released += OnReleased;

            feed.Queue.Cleared += OnCleared;
        }

        /// <summary>Parks a patrol unit at a police incident that was released onto the board.</summary>
        private void OnReleased(LiveBoardEntry entry)
        {
            var record = entry.Record;

            var id = record.IncidentId;

            // The fleet's patrol units drive to police calls themselves: the parked scene car is only for a game without them.
            if (_prefab == null || _units.ContainsKey(id) || _units.Count >= MaximumUnits || entry.DispatchedToPlayer || _feed.HasPatrols)
            {
                return;
            }

            if (record.Agency != PoliceAgency || !entry.InMap || !record.HasPosition)
            {
                return;
            }

            var hash = Hash(id);

            var incident = GeoProjection.ToWorld(record.Geo);

            // Facing is settled before the spot is checked for room: the clearance box turns with the car.
            var spot = FindOnStreet(incident, hash, out var yaw) ?? FindAnywhere(incident, hash, out yaw);

            if (spot is null)
            {
                return;
            }

            // Not parented: this component lives on the player's truck, and a scene must stay where it is when the truck drives off.
            var unit = Instantiate(_prefab, spot.Value, Quaternion.Euler(0f, yaw, 0f));

            unit.name = "LivePatrol_" + id;

            unit.AddComponent<PatrolLightbar>();

            _units[id] = unit;
        }

        /// <summary>The incident is cleared: its patrol unit leaves.</summary>
        private void OnCleared(string id)
        {
            if (!_units.TryGetValue(id, out var unit))
            {
                return;
            }

            _units.Remove(id);

            if (unit != null)
            {
                Destroy(unit);
            }
        }

        /// <summary>The free ground point on a street nearest the incident, with the car turned along that street; null when no street is near.</summary>
        private static Vector3? FindOnStreet(Vector3 incident, int hash, out float yaw)
        {
            yaw = 0f;

            for (var ring = FirstRing; ring <= LastRing; ring += RingStep)
            {
                for (var i = 0; i < SpotsPerRing; i++)
                {
                    var angle = (((hash % 360) + (i * 360f / SpotsPerRing)) % 360f) * Mathf.Deg2Rad;

                    var candidate = incident + (new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * ring);

                    if (!TryGround(candidate, out var hit) || hit.collider.name != StreetCollider)
                    {
                        continue;
                    }

                    var along = StreetAxis(hit.point) + ((hash & 1) * 180f);

                    if (IsFree(hit.point, along))
                    {
                        yaw = along;

                        return hit.point;
                    }
                }
            }

            return null;
        }

        /// <summary>When no street is near: the nearest free ground point of any kind, car turned by the incident's hash.</summary>
        private static Vector3? FindAnywhere(Vector3 incident, int hash, out float yaw)
        {
            yaw = (hash % 24) * 15f;

            for (var ring = 0f; ring <= LastRing; ring += RingStep * 2f)
            {
                var count = ring == 0f ? 1 : SpotsPerRing;

                for (var i = 0; i < count; i++)
                {
                    var angle = (((hash % 360) + (i * 360f / count)) % 360f) * Mathf.Deg2Rad;

                    var candidate = incident + (new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * ring);

                    if (TryGround(candidate, out var hit) && IsFree(hit.point, yaw))
                    {
                        return hit.point;
                    }
                }
            }

            return null;
        }

        /// <summary>The compass direction (degrees, 0 to 180) a street runs in at a point: the way with the most street on both sides.</summary>
        private static float StreetAxis(Vector3 point)
        {
            var best = 0f;

            var bestCount = -1;

            for (var degrees = 0f; degrees < 180f; degrees += AxisStepDegrees)
            {
                var direction = new Vector3(Mathf.Sin(degrees * Mathf.Deg2Rad), 0f, Mathf.Cos(degrees * Mathf.Deg2Rad));

                var count = 0;

                foreach (var distance in AxisSamples)
                {
                    count += OnStreet(point + (direction * distance)) ? 1 : 0;

                    count += OnStreet(point - (direction * distance)) ? 1 : 0;
                }

                if (count > bestCount)
                {
                    bestCount = count;

                    best = degrees;
                }
            }

            return best;
        }

        private static bool OnStreet(Vector3 point)
        {
            return TryGround(point, out var hit) && hit.collider.name == StreetCollider;
        }

        /// <summary>The first solid surface below a point.</summary>
        private static bool TryGround(Vector3 point, out RaycastHit hit)
        {
            return Physics.Raycast(point + (Vector3.up * ProbeUp), Vector3.down, out hit, ProbeUp + ProbeDown, ~0, QueryTriggerInteraction.Ignore);
        }

        /// <summary>True when the car fits at a ground point, facing <paramref name="yaw"/>, with nothing solid in its way.</summary>
        private static bool IsFree(Vector3 ground, float yaw)
        {
            var centre = ground + (Vector3.up * (HalfExtents.y + 0.05f));

            return !Physics.CheckBox(centre, HalfExtents, Quaternion.Euler(0f, yaw, 0f), ~0, QueryTriggerInteraction.Ignore);
        }

        /// <summary>A stable non-negative number from an incident id (picks which way the car faces along the street and where the search starts).</summary>
        private static int Hash(string id)
        {
            var value = 17;

            foreach (var character in id)
            {
                value = unchecked((value * 31) + character);
            }

            return value & 0x7fffffff;
        }

        private void OnDestroy()
        {
            if (_feed != null && _feed.Queue != null)
            {
                _feed.Queue.Released -= OnReleased;

                _feed.Queue.Cleared -= OnCleared;
            }
        }
    }
}
