#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using HTownSim.Runtime.Fleet;
using UnityEngine;

namespace HTownSim.Runtime
{
    /// <summary>
    /// The units the computer drives: every apparatus parked at the game's stations except the one the player is in. Each is found by
    /// its scene object and given an <see cref="AiUnit"/>, which drives it when the dispatcher sends it. The street network the units
    /// route on is built once from the City's street files (Resources: CityStreets, MedCenterStreets, SouthRoads), and the hospitals
    /// are the ER bays (objects named ER_Bay_*).
    /// </summary>
    internal sealed class AiFleet : MonoBehaviour
    {
        /// <summary>
        /// The object a player drives, which the saved scene fits as Ambulance 07 and parks in the third bay at Station 7. With no player
        /// (the city server) it is a fleet unit like the others; with a player it is the player's own truck and the fleet leaves it alone.
        /// </summary>
        private const string DrivenObject = "Engine07";

        /// <summary>The parked apparatus: scene object, the group object it is in (empty for a root object), and the unit name.</summary>
        private static readonly (string Object, string Group, string Name)[] Parked =
        {
            ("Engine07Parked", "", "Engine 07"),
            ("Ladder07", "", "Ladder 07"),
            (DrivenObject, "", "Ambulance 07"),
            ("Engine08", "Station8Apparatus", "Engine 08"),
            ("Engine01", "Station8Apparatus", "Engine 01"),
            ("Medic08", "Station8Apparatus", "Medic 08"),
            ("DistrictChief08", "Station8Apparatus", "District Chief 08"),
            ("Engine16", "Station16Apparatus", "Engine 16"),
            ("Ladder16", "Station16Apparatus", "Ladder 16"),
            ("Ambulance16", "Station16Apparatus", "Ambulance 16"),
            ("Supervisor16", "Station16Apparatus", "Supervisor 16"),
        };

        /// <summary>The street files the routes are built from, in Resources.</summary>
        private static readonly string[] StreetFiles = { "CityStreets", "MedCenterStreets", "SouthRoads" };

        private const string HospitalPrefix = "ER_Bay_";

        /// <summary>The units available for a test response to the active dispatch (key F9).</summary>
        private static readonly string[] TestUnits = { "Engine 07", "Ladder 07", "Engine 08", "Engine 16", "Ambulance 16" };

        private const string TestIncidentId = "TEST-RESPONSE";

        /// <summary>How many computer-driven patrol units (officers) the fleet has. Raise it for more police units (more can answer a call; the per-call maximum is FleetDispatcher.MaximumOfficers); lower it for fewer.</summary>
        private const int PatrolCount = 4;

        private const string PatrolPrefab = "HpdPatrol";

        /// <summary>How far apart (metres) the parked patrol units stand at the HPD substation. Raise it to spread them out; lower it to park them closer (too low and they overlap).</summary>
        private const float PatrolSpacing = 7f;

        /// <summary>How far (metres) around the HPD substation the search for free parking spots reaches. Raise it to find spots on a crowded apron; lower it to keep the cars close to the station.</summary>
        private const float PatrolSearchRadius = 22f;

        /// <summary>The ground clearance a parked patrol car needs: half its size (width, height, length) for the free-space check.</summary>
        private static readonly Vector3 PatrolHalfSize = new Vector3(1.4f, 1f, 2.8f);

        /// <summary>The HPD substation next to Station 7 (its apron; the patrol cars are parked around it).</summary>
        private static readonly Vector3 PatrolPlace = new Vector3(48f, 0f, -24f);

        private readonly List<AiUnit> _units = new List<AiUnit>();

        private readonly List<Vector2> _hospitals = new List<Vector2>();

        /// <summary>The units, parked or out.</summary>
        internal IReadOnlyList<AiUnit> Units => _units;

        /// <summary>A unit finished its part of an incident: the incident id and whether it took a patient to a hospital.</summary>
        internal event Action<string, bool>? UnitFinished;

        /// <summary>A unit loaded the patient of an incident and is leaving the scene for the hospital.</summary>
        internal event Action<string>? PatientLoaded;

        /// <summary>Finds the parked units (all but <paramref name="playerUnit"/>) and builds the street network.</summary>
        internal void Initialize(string? playerUnit)
        {
            var texts = StreetFiles.Select(name => Resources.Load<TextAsset>(name)).Where(asset => asset != null).Select(asset => asset.text);

            var graph = RoadGraph.Parse(string.Join("\n", texts));

            foreach (var candidate in FindObjectsByType<Transform>(FindObjectsInactive.Exclude))
            {
                if (candidate.name.StartsWith(HospitalPrefix, StringComparison.Ordinal))
                {
                    _hospitals.Add(new Vector2(candidate.position.x, candidate.position.z));
                }
            }

            var roots = gameObject.scene.GetRootGameObjects();

            foreach (var (objectName, group, name) in Parked)
            {
                if (string.Equals(name, playerUnit, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // With a player, the driven object is theirs, whatever unit they picked.
                if (objectName == DrivenObject && playerUnit != null)
                {
                    continue;
                }

                var parked = Find(roots, objectName, group);

                if (parked == null || !parked.gameObject.activeInHierarchy)
                {
                    continue;
                }

                var unit = parked.gameObject.AddComponent<AiUnit>();

                unit.Initialize(name, graph, NearestHospital);

                unit.Finished += OnUnitFinished;

                unit.PatientLoaded += id => PatientLoaded?.Invoke(id);

                _units.Add(unit);
            }

            SpawnPatrols(graph);

            RestoreCarryOver();

            Debug.Log("AI fleet: " + _units.Count + " units (" + string.Join(", ", _units.Select(unit => unit.UnitName)) + "), " + graph.NodeCount + " street nodes, " + _hospitals.Count + " hospital bays.");
        }

        /// <summary>
        /// Takes back the units that were out on calls when the player switched unit (see <see cref="CityCarryOver"/>), and sends the unit
        /// the player left on to the call it was on. A unit the player has now taken over is not in the fleet, so it is not restored.
        /// </summary>
        private void RestoreCarryOver()
        {
            if (!CityCarryOver.HasData)
            {
                return;
            }

            foreach (var state in CityCarryOver.Units)
            {
                Find(state.Name)?.Restore(state);
            }

            var left = CityCarryOver.Player;

            if (left != null)
            {
                Find(left.Name)?.ContinueFromPlayer(left);
            }
        }

        /// <summary>
        /// Parks the patrol units (officers) on the HPD substation's apron, each in a free spot at least <see cref="PatrolSpacing"/> from the
        /// others. They are copies of the patrol car prefab, and are not made when the prefab is missing.
        /// </summary>
        private void SpawnPatrols(RoadGraph graph)
        {
            var prefab = Resources.Load<GameObject>(PatrolPrefab);

            if (prefab == null)
            {
                return;
            }

            var spots = new List<Vector3>();

            var steps = Mathf.CeilToInt(PatrolSearchRadius / PatrolSpacing);

            for (var ring = 0; ring <= steps && spots.Count < PatrolCount; ring++)
            {
                for (var i = 0; i < Mathf.Max(1, ring * 6) && spots.Count < PatrolCount; i++)
                {
                    var angle = ring == 0 ? 0f : i * 2f * Mathf.PI / (ring * 6);

                    var guess = PatrolPlace + (new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (ring * PatrolSpacing));

                    var ground = GroundPoint(guess);

                    if (ground != null && Free(ground.Value) && spots.TrueForAll(spot => Vector3.Distance(spot, ground.Value) >= PatrolSpacing))
                    {
                        spots.Add(ground.Value);
                    }
                }
            }

            for (var i = 0; i < spots.Count; i++)
            {
                var car = Instantiate(prefab, spots[i], Quaternion.Euler(0f, 90f, 0f));

                car.name = "AiPatrol" + (i + 1);

                var unit = car.AddComponent<AiUnit>();

                unit.Initialize("HPD Unit " + (i + 1), graph, NearestHospital);

                unit.Finished += OnUnitFinished;

                _units.Add(unit);
            }
        }

        /// <summary>
        /// The ground under a point, or null: the first street, slab or ground surface the ray meets. Roofs and parked vehicles are not
        /// ground, or a car would be put on top of them.
        /// </summary>
        private static Vector3? GroundPoint(Vector3 point)
        {
            var hits = Physics.RaycastAll(point + (Vector3.up * 40f), Vector3.down, 80f, ~0, QueryTriggerInteraction.Ignore);

            Vector3? best = null;

            var bestDistance = float.MaxValue;

            foreach (var hit in hits)
            {
                var name = hit.collider.name;

                var surface = name == "Streets" || name.StartsWith("Slab", StringComparison.Ordinal) || name.StartsWith("Ground", StringComparison.Ordinal);

                if (surface && hit.distance < bestDistance)
                {
                    bestDistance = hit.distance;

                    best = hit.point;
                }
            }

            return best;
        }

        /// <summary>True when a patrol car fits at a ground point with nothing solid in the way.</summary>
        private static bool Free(Vector3 ground)
        {
            return !Physics.CheckBox(ground + (Vector3.up * (PatrolHalfSize.y + 0.2f)), PatrolHalfSize, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// Sends available test units to the player's active dispatch marker (key F9).
        /// </summary>
        internal int SendTestResponse(Vector3? dispatchPosition)
        {
            if (dispatchPosition is not Vector3 destination)
            {
                Debug.Log("AI fleet: no active dispatch to respond to.");
                return 0;
            }

            var scene = new Vector2(destination.x, destination.z);

            var slot = 0;

            foreach (var name in TestUnits)
            {
                var unit = Find(name);

                if (unit == null || unit.Busy)
                {
                    continue;
                }

                unit.Dispatch(TestIncidentId, scene, AiUnit.Call.Fire, slot);

                if (unit.Sent)
                {
                    slot++;
                }
                else
                {
                    Debug.LogWarning("AI fleet: no route to the active dispatch for " + name);
                }
            }

            Debug.Log("AI fleet: " + slot + " test units responding to the active dispatch at " + scene);

            return slot;
        }

        /// <summary>The scene object for a parked unit's name (as the fleet finds it), or null; a spectator uses it to pose the unit it sees streamed.</summary>
        internal static Transform? FindParkedObject(GameObject[] roots, string unitName)
        {
            foreach (var (objectName, group, name) in Parked)
            {
                if (string.Equals(name, unitName, StringComparison.OrdinalIgnoreCase))
                {
                    return Find(roots, objectName, group);
                }
            }

            return null;
        }

        /// <summary>How many units are stopped at the scene of an incident now.</summary>
        internal int UnitsOnScene(string incidentId)
        {
            return _units.Count(unit => unit.Current == AiUnit.Phase.OnScene && unit.IncidentId == incidentId);
        }

        /// <summary>The unit with a name, or null.</summary>
        internal AiUnit? Find(string name)
        {
            return _units.FirstOrDefault(unit => string.Equals(unit.UnitName, name, StringComparison.OrdinalIgnoreCase));
        }

        private void OnUnitFinished(string incidentId, bool transported)
        {
            UnitFinished?.Invoke(incidentId, transported);
        }

        /// <summary>The ER bay nearest a point (ground x, z), or null when the scene has none.</summary>
        private Vector2? NearestHospital(Vector2 from)
        {
            Vector2? best = null;

            var bestDistance = float.MaxValue;

            foreach (var bay in _hospitals)
            {
                var distance = Vector2.Distance(bay, from);

                if (distance < bestDistance)
                {
                    bestDistance = distance;

                    best = bay;
                }
            }

            return best;
        }

        private static Transform? Find(GameObject[] roots, string objectName, string group)
        {
            foreach (var root in roots)
            {
                if (group.Length == 0)
                {
                    if (root.name == objectName)
                    {
                        return root.transform;
                    }

                    continue;
                }

                if (root.name == group)
                {
                    return root.transform.Find(objectName);
                }
            }

            return null;
        }
    }
}
