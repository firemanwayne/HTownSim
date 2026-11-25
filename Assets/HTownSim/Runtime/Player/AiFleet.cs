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
        /// <summary>The parked apparatus: scene object, the group object it is in (empty for a root object), and the unit name.</summary>
        private static readonly (string Object, string Group, string Name)[] Parked =
        {
            ("Engine07Parked", "", "Engine 07"),
            ("Ladder07", "", "Ladder 07"),
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

        private readonly List<AiUnit> _units = new List<AiUnit>();

        private readonly List<Vector2> _hospitals = new List<Vector2>();

        /// <summary>The units, parked or out.</summary>
        internal IReadOnlyList<AiUnit> Units => _units;

        /// <summary>A unit finished its part of an incident: the incident id and whether it took a patient to a hospital.</summary>
        internal event Action<string, bool>? UnitFinished;

        /// <summary>A unit loaded the patient of an incident and is leaving the scene for the hospital.</summary>
        internal event Action<string>? PatientLoaded;

        /// <summary>Finds the parked units (all but <paramref name="playerUnit"/>) and builds the street network.</summary>
        internal void Initialize(string playerUnit)
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

            Debug.Log("AI fleet: " + _units.Count + " units (" + string.Join(", ", _units.Select(unit => unit.UnitName)) + "), " + graph.NodeCount + " street nodes, " + _hospitals.Count + " hospital bays.");
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
