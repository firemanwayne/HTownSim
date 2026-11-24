#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HTownSim.Runtime
{
    /// <summary>
    /// Turns the driven object (named Engine07 whatever it is) into any of the six apparatus while the scene loads. This is the
    /// run-time twin of the editor scripts DriveLadder07 and DriveAmbulance07 (Station 7) and of AddStation8Apparatus (Station 8): the
    /// driven object moves to the chosen apparatus's bay, wears its model, takes its ApparatusProfile and beacon positions, and the
    /// apparatus it replaces is parked in its own bay. Nothing here is saved to the scene; the scene's own truck is only the starting
    /// point. A Station 8 apparatus borrows the look of the parked unit that AddStation8Apparatus placed in its bay (livery and number).
    /// </summary>
    internal static class ApparatusLoadout
    {
        /// <summary>Scene object names shared with the editor scripts.</summary>
        private const string LadderVisual = "Ladder07Visual";

        private const string AmbulanceVisual = "Ambulance07Visual";

        private const string LadderProp = "Ladder07";

        private const string MedicProp = "Medic07";

        private const string ParkedName = "Engine07Parked";

        private const string BeaconsName = "LadderBeacons";

        private const string EngineCleanVisual = "Engine07CleanVisual";

        private const string EngineOldVisual = "Engine43Visual";

        private const string EngineLighting = "EmergencyLighting";

        /// <summary>The group AddStation8Apparatus fills, and the unit names inside it.</summary>
        private const string Station8Group = "Station8Apparatus";

        private const string Engine08Unit = "Engine08";

        private const string Engine01Unit = "Engine01";

        private const string Medic08Unit = "Medic08";

        /// <summary>The group AddStation16Apparatus fills, and the unit names inside it.</summary>
        private const string Station16Group = "Station16Apparatus";

        private const string Engine16Unit = "Engine16";

        private const string Ladder16Unit = "Ladder16";

        private const string Ambulance16Unit = "Ambulance16";

        private const string Supervisor16Unit = "Supervisor16";

        /// <summary>The visuals' drop below the driven object, so every apparatus's tyres sit on the floor the same way.</summary>
        private const float VisualDrop = -0.15f;

        /// <summary>Where the ladder prop's model sits on its root.</summary>
        private static readonly Vector3 PropVisualPosition = new Vector3(0f, -0.045f, 0.05f);

        /// <summary>Beacon positions (right side; the left mirrors in x) on each apparatus, in the driven object's space.</summary>
        private static readonly Vector3 EngineBeacon = new Vector3(1f, 3.05f, 3.2f);

        private static readonly Vector3 LadderBeacon = new Vector3(0.95f, 3.25f, 5.4f);

        private static readonly Vector3 AmbulanceBeacon = new Vector3(0.8f, 3.1f, 0.7f);

        /// <summary>The Suburban's roof light bar.</summary>
        private static readonly Vector3 SuvBeacon = new Vector3(0.55f, 2.35f, 0.15f);

        /// <summary>Ladder 07's fit-out (0.558 m tyres, front axle +3.80, tandem -1.76/-3.13).</summary>
        private static readonly ApparatusProfileValues LadderValues = new ApparatusProfileValues(
            "Ladder 07", 0.558f, 6.25f, new[] { "L07_wheel_0_" }, new[] { "L07_wheel_1_", "L07_wheel_2_" }, 2.5f, 1.25f,
            new Vector3(0f, 3.5f, 5.8f), new Vector3(0.85f, 2.8f, 5.85f), false,
            new[] { "L07_Red_lenses" }, new[] { "L07_Clear_LED_lenses" }, new[] { "L07_Amber_lenses" }, true, true, "", false);

        /// <summary>Ladder 07's fit-out under another unit name (Ladder 16): the same aerial rig.</summary>
        private static ApparatusProfileValues LadderAs(string unitName)
        {
            return new ApparatusProfileValues(
                unitName, 0.558f, 6.25f, new[] { "L07_wheel_0_" }, new[] { "L07_wheel_1_", "L07_wheel_2_" }, 2.5f, 1.25f,
                new Vector3(0f, 3.5f, 5.8f), new Vector3(0.85f, 2.8f, 5.85f), false,
                new[] { "L07_Red_lenses" }, new[] { "L07_Clear_LED_lenses" }, new[] { "L07_Amber_lenses" }, true, true, "", false);
        }

        /// <summary>Supervisor 16's fit-out: a Chevrolet Suburban (0.40 m tyres, wheelbase 3.3 m), a tight turn, no water, EMS tones and the plain horn.</summary>
        private static ApparatusProfileValues SupervisorAs(string unitName)
        {
            return new ApparatusProfileValues(
                unitName, 0.40f, 3.3f, new[] { "SubWheelFront" }, new[] { "SubWheelRear" }, 7f, 0.8f,
                new Vector3(0f, 2.2f, -1.5f), new Vector3(0.9f, 1.9f, -0.5f), false,
                new string[0], new string[0], new string[0], false, false, "", true);
        }

        /// <summary>Which apparatus the driven object currently is: a run-time profile names a Station 8 unit, else the fitted model decides.</summary>
        internal static StartingTruck Detect(Transform driven)
        {
            var profile = driven.GetComponent<ApparatusProfile>();

            if (profile != null)
            {
                switch (profile.Values.UnitName)
                {
                    case "Engine 08":
                        return StartingTruck.Engine08;
                    case "Engine 01":
                        return StartingTruck.Engine01;
                    case "Medic 08":
                        return StartingTruck.Medic08;
                    case "Engine 16":
                        return StartingTruck.Engine16;
                    case "Ladder 16":
                        return StartingTruck.Ladder16;
                    case "Ambulance 16":
                        return StartingTruck.Ambulance16;
                    case "Supervisor 16":
                        return StartingTruck.Supervisor16;
                }
            }

            if (driven.Find(AmbulanceVisual) != null)
            {
                return StartingTruck.Ambulance;
            }

            return driven.Find(LadderVisual) != null ? StartingTruck.Ladder : StartingTruck.Engine;
        }

        /// <summary>True for the apparatus that live at Station 8.</summary>
        internal static bool IsStation8(StartingTruck truck)
        {
            return StartingTruckChoice.StationOf(truck) == StartingStation.Station8;
        }

        /// <summary>True for the apparatus that live at Station 16.</summary>
        internal static bool IsStation16(StartingTruck truck)
        {
            return StartingTruckChoice.StationOf(truck) == StartingStation.Station16;
        }

        /// <summary>True when the scene holds the parked Station 16 units (made by the editor command HTownSim > Station 16 > Add Apparatus).</summary>
        internal static bool Station16Ready(Scene scene)
        {
            var group = scene.GetRootGameObjects().FirstOrDefault(root => root.name == Station16Group);

            return group != null
                && group.transform.Find(Engine16Unit) != null
                && group.transform.Find(Ladder16Unit) != null
                && group.transform.Find(Ambulance16Unit) != null
                && group.transform.Find(Supervisor16Unit) != null;
        }

        /// <summary>True when the scene holds the parked Station 8 units (made by the editor command HTownSim > Station 8 > Add Apparatus).</summary>
        internal static bool Station8Ready(Scene scene)
        {
            var group = scene.GetRootGameObjects().FirstOrDefault(root => root.name == Station8Group);

            return group != null
                && group.transform.Find(Engine08Unit) != null
                && group.transform.Find(Engine01Unit) != null
                && group.transform.Find(Medic08Unit) != null;
        }

        /// <summary>Makes the driven object the requested apparatus; a no-op when it already is. Logs and leaves the scene as it is if the scene lacks a piece.</summary>
        internal static void Fit(LoadoutRequest request)
        {
            var driven = request.Apparatus.transform;

            var current = Detect(driven);

            if (current == request.Target)
            {
                return;
            }

            var controller = driven.GetComponent<CharacterController>();

            controller.enabled = false;

            try
            {
                if (current == StartingTruck.Ambulance)
                {
                    RevertAmbulance(driven);

                    SetProfile(driven, LadderValues);

                    current = StartingTruck.Ladder;
                }

                if (IsStation8(request.Target))
                {
                    if (current == StartingTruck.Ladder)
                    {
                        RevertLadder(request);
                    }

                    ApplyStation8(request);
                }
                else if (IsStation16(request.Target))
                {
                    if (current == StartingTruck.Ladder)
                    {
                        RevertLadder(request);
                    }

                    ApplyStation16(request);
                }
                else if (request.Target == StartingTruck.Engine)
                {
                    if (current == StartingTruck.Ladder)
                    {
                        RevertLadder(request);
                    }
                }
                else
                {
                    if (current == StartingTruck.Engine)
                    {
                        ApplyLadder(request);
                    }

                    if (request.Target == StartingTruck.Ambulance)
                    {
                        ApplyAmbulance(request);
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            finally
            {
                controller.enabled = true;

                Physics.SyncTransforms();
            }
        }

        /// <summary>Engine to a Station 8 apparatus: Engine 07 is parked at Station 7 and the driven object takes the Station 8 unit's bay and look.</summary>
        private static void ApplyStation8(LoadoutRequest request)
        {
            var driven = request.Apparatus.transform;

            var unitName = request.Target switch
            {
                StartingTruck.Engine01 => Engine01Unit,
                StartingTruck.Medic08 => Medic08Unit,
                _ => Engine08Unit,
            };

            var group = FindRoot(driven, Station8Group);

            var unit = group.Find(unitName) ?? throw new InvalidOperationException(Station8Group + " has no " + unitName + ".");

            var model = unit.childCount > 0 ? unit.GetChild(0) : throw new InvalidOperationException(unitName + " has no model.");

            var engineVisual = driven.Find(EngineCleanVisual) ?? throw new InvalidOperationException("Engine07 has no " + EngineCleanVisual + ".");

            // Station 7 keeps its engine: a static copy goes in the bay the driven object is leaving.
            ParkEngine(driven);

            driven.SetPositionAndRotation(unit.position, unit.rotation);

            if (request.Target == StartingTruck.Medic08)
            {
                engineVisual.gameObject.SetActive(false);

                var oldVisual = driven.Find(EngineOldVisual) ?? throw new InvalidOperationException("Engine07 has no " + EngineOldVisual + ".");

                MoveBeacons(new BeaconMove(request.Apparatus, AmbulanceBeacon));

                oldVisual.gameObject.SetActive(false);

                model.SetParent(driven, true);

                SetProfile(driven, AmbulanceAs("Medic 08"));
            }
            else
            {
                CopyMaterials(engineVisual, model);

                SetProfile(driven, ApparatusProfile.EngineAs(StartingTruckChoice.DisplayName(request.Target)));
            }

            unit.gameObject.SetActive(false);
        }

        /// <summary>Engine to a Station 16 apparatus: Engine 07 is parked at Station 7 and the driven object takes the Station 16 unit's bay and look.</summary>
        private static void ApplyStation16(LoadoutRequest request)
        {
            var driven = request.Apparatus.transform;

            var unitName = request.Target switch
            {
                StartingTruck.Ladder16 => Ladder16Unit,
                StartingTruck.Ambulance16 => Ambulance16Unit,
                StartingTruck.Supervisor16 => Supervisor16Unit,
                _ => Engine16Unit,
            };

            var group = FindRoot(driven, Station16Group);

            var unit = group.Find(unitName) ?? throw new InvalidOperationException(Station16Group + " has no " + unitName + ".");

            if (request.Target == StartingTruck.Ladder16)
            {
                // The aerial rig of Ladder 07 moves to the Ladder 16 bay and takes the Station 16 unit's name.
                ApplyLadder(request, unit, "Ladder 16");

                unit.gameObject.SetActive(false);

                return;
            }

            var model = unit.childCount > 0 ? unit.GetChild(0) : throw new InvalidOperationException(unitName + " has no model.");

            var engineVisual = driven.Find(EngineCleanVisual) ?? throw new InvalidOperationException("Engine07 has no " + EngineCleanVisual + ".");

            ParkEngine(driven);

            driven.SetPositionAndRotation(unit.position, unit.rotation);

            if (request.Target == StartingTruck.Engine16)
            {
                CopyMaterials(engineVisual, model);

                SetProfile(driven, ApparatusProfile.EngineAs("Engine 16"));
            }
            else
            {
                engineVisual.gameObject.SetActive(false);

                var oldVisual = driven.Find(EngineOldVisual) ?? throw new InvalidOperationException("Engine07 has no " + EngineOldVisual + ".");

                var suburban = request.Target == StartingTruck.Supervisor16;

                MoveBeacons(new BeaconMove(request.Apparatus, suburban ? SuvBeacon : AmbulanceBeacon));

                oldVisual.gameObject.SetActive(false);

                model.SetParent(driven, true);

                SetProfile(driven, suburban ? SupervisorAs("Supervisor 16") : AmbulanceAs("Ambulance 16"));
            }

            unit.gameObject.SetActive(false);
        }

        /// <summary>Gives the renderers of one model the materials of the same-named renderers of another (the other unit's livery and number).</summary>
        private static void CopyMaterials(Transform target, Transform source)
        {
            var sources = source.GetComponentsInChildren<Renderer>(true)
                .GroupBy(renderer => renderer.name)
                .ToDictionary(group => group.Key, group => group.First());

            foreach (var renderer in target.GetComponentsInChildren<Renderer>(true))
            {
                if (sources.TryGetValue(renderer.name, out var original))
                {
                    renderer.sharedMaterials = original.sharedMaterials;
                }
            }
        }

        /// <summary>Parks a static copy of the engine model (with a box to drive around) in the bay the driven object is in now.</summary>
        private static void ParkEngine(Transform driven)
        {
            var source = driven.Find(EngineCleanVisual) ?? throw new InvalidOperationException("Engine07 has no " + EngineCleanVisual + ".");

            var parked = new GameObject(ParkedName).transform;

            parked.SetPositionAndRotation(driven.position, driven.rotation);

            UnityEngine.Object.Instantiate(source.gameObject, parked, false);

            var box = parked.gameObject.AddComponent<BoxCollider>();

            box.center = new Vector3(0f, 1.45f, 0f);

            box.size = new Vector3(2.6f, 3.1f, 10.1f);
        }

        /// <summary>Engine to ladder: the engine is parked in its bay and the driven object takes the ladder bay and model.</summary>
        private static void ApplyLadder(LoadoutRequest request, Transform? bay = null, string unitName = "")
        {
            var driven = request.Apparatus.transform;

            var prop = FindRoot(driven, LadderProp);

            var ladder = prop.Find(LadderVisual) ?? throw new InvalidOperationException("Ladder07 has no " + LadderVisual + ".");

            var source = driven.Find(EngineCleanVisual) ?? throw new InvalidOperationException("Engine07 has no " + EngineCleanVisual + ".");

            ParkEngine(driven);

            // The driven object moves to the ladder bay and becomes the ladder.
            driven.SetPositionAndRotation(bay != null ? bay.position : prop.position, bay != null ? bay.rotation : prop.rotation);

            source.gameObject.SetActive(false);

            var oldVisual = driven.Find(EngineOldVisual) ?? throw new InvalidOperationException("Engine07 has no " + EngineOldVisual + ".");

            MoveBeacons(new BeaconMove(request.Apparatus, LadderBeacon));

            oldVisual.gameObject.SetActive(false);

            ladder.SetParent(driven, false);

            ladder.localPosition = new Vector3(0f, VisualDrop, 0f);

            ladder.localRotation = Quaternion.identity;

            prop.gameObject.SetActive(false);

            SetProfile(driven, unitName.Length == 0 ? LadderValues : LadderAs(unitName));
        }

        /// <summary>Puts the beacon lights under a new LadderBeacons object on the driven object, at the apparatus's roof positions.</summary>
        private static void MoveBeacons(BeaconMove move)
        {
            var driven = move.Apparatus.transform;

            var beaconRoot = new GameObject(BeaconsName).transform;

            beaconRoot.SetParent(driven, false);

            foreach (var beacon in BeaconLights(move.Apparatus))
            {
                var side = Mathf.Sign(driven.InverseTransformPoint(beacon.transform.position).x);

                beacon.transform.SetParent(beaconRoot, false);

                beacon.transform.localPosition = new Vector3(side * move.Position.x, move.Position.y, move.Position.z);
            }
        }

        /// <summary>Ladder to engine: the ladder goes back on its prop, the parked engine's bay becomes the driven object's again.</summary>
        private static void RevertLadder(LoadoutRequest request)
        {
            var driven = request.Apparatus.transform;

            var ladder = driven.Find(LadderVisual) ?? throw new InvalidOperationException("Not driving Ladder 07.");

            var prop = FindRoot(driven, LadderProp);

            prop.gameObject.SetActive(true);

            ladder.SetParent(prop, false);

            ladder.localPosition = PropVisualPosition;

            ladder.localRotation = Quaternion.identity;

            var oldVisual = driven.Find(EngineOldVisual) ?? throw new InvalidOperationException("Engine07 has no " + EngineOldVisual + ".");

            oldVisual.gameObject.SetActive(true);

            var beaconRoot = driven.Find(BeaconsName);

            var lighting = oldVisual.Find(EngineLighting);

            if (beaconRoot != null)
            {
                foreach (var beacon in beaconRoot.Cast<Transform>().ToList())
                {
                    var side = Mathf.Sign(beacon.localPosition.x);

                    beacon.SetParent(lighting, false);

                    beacon.position = driven.TransformPoint(new Vector3(side * EngineBeacon.x, EngineBeacon.y, EngineBeacon.z));
                }

                UnityEngine.Object.DestroyImmediate(beaconRoot.gameObject);
            }

            driven.Find(EngineCleanVisual)?.gameObject.SetActive(true);

            var parked = FindRootOrNull(driven, ParkedName);

            if (parked != null)
            {
                driven.SetPositionAndRotation(parked.position, parked.rotation);

                UnityEngine.Object.DestroyImmediate(parked.gameObject);
            }

            var profile = driven.GetComponent<ApparatusProfile>();

            if (profile != null)
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }

            // The beacons were placed before the engine went back to its bay; place them again relative to it.
            foreach (var beacon in BeaconLights(request.Apparatus))
            {
                var side = Mathf.Sign(driven.InverseTransformPoint(beacon.transform.position).x);

                beacon.transform.position = driven.TransformPoint(new Vector3(side * EngineBeacon.x, EngineBeacon.y, EngineBeacon.z));
            }
        }

        /// <summary>Ladder to ambulance: the ladder goes back on its prop in the ladder bay and the driven object takes the medic bay and model.</summary>
        private static void ApplyAmbulance(LoadoutRequest request)
        {
            var driven = request.Apparatus.transform;

            var medic = FindRoot(driven, MedicProp);

            var prop = FindRoot(driven, LadderProp);

            var ladder = driven.Find(LadderVisual) ?? throw new InvalidOperationException("Not driving Ladder 07.");

            var ambulance = medic.Find(AmbulanceVisual) ?? throw new InvalidOperationException("Medic07 has no " + AmbulanceVisual + ".");

            var ladderBay = driven.position;

            var ladderRotation = driven.rotation;

            prop.gameObject.SetActive(true);

            prop.SetPositionAndRotation(ladderBay, ladderRotation);

            ladder.SetParent(prop, false);

            ladder.localPosition = PropVisualPosition;

            ladder.localRotation = Quaternion.identity;

            var target = new Vector3(medic.position.x, driven.position.y, medic.position.z);

            driven.SetPositionAndRotation(target, medic.rotation);

            ambulance.SetParent(driven, true);

            medic.gameObject.SetActive(false);

            var beaconRoot = driven.Find(BeaconsName) ?? throw new InvalidOperationException("Beacon root missing.");

            foreach (Transform beacon in beaconRoot)
            {
                var side = Mathf.Sign(beacon.localPosition.x);

                beacon.localPosition = new Vector3(side * AmbulanceBeacon.x, AmbulanceBeacon.y, AmbulanceBeacon.z);
            }

            SetProfile(driven, AmbulanceAs("Ambulance 07"));
        }

        /// <summary>Ambulance to ladder: the model goes back to its prop and the ladder takes the driver's seat (the caller rewrites the profile).</summary>
        private static void RevertAmbulance(Transform driven)
        {
            var medic = FindRoot(driven, MedicProp);

            var prop = FindRoot(driven, LadderProp);

            var ambulance = driven.Find(AmbulanceVisual) ?? throw new InvalidOperationException("Not driving Ambulance 07.");

            medic.gameObject.SetActive(true);

            ambulance.SetParent(medic, true);

            driven.SetPositionAndRotation(new Vector3(prop.position.x, driven.position.y, prop.position.z), prop.rotation);

            var ladder = prop.Find(LadderVisual) ?? throw new InvalidOperationException("Ladder visual missing from its prop.");

            ladder.SetParent(driven, false);

            ladder.localPosition = new Vector3(0f, VisualDrop, 0f);

            ladder.localRotation = Quaternion.identity;

            prop.gameObject.SetActive(false);

            var beaconRoot = driven.Find(BeaconsName);

            if (beaconRoot != null)
            {
                foreach (Transform beacon in beaconRoot)
                {
                    var side = Mathf.Sign(beacon.localPosition.x);

                    beacon.localPosition = new Vector3(side * LadderBeacon.x, LadderBeacon.y, LadderBeacon.z);
                }
            }
        }

        /// <summary>Ambulance 07's fit-out under a unit name (Ambulance 07, Medic 08): 0.41 m tyres, axles 2.56 m ahead and 1.62 m behind the origin, no water.</summary>
        private static ApparatusProfileValues AmbulanceAs(string unitName)
        {
            return new ApparatusProfileValues(
                unitName, 0.41f, 4.18f, new[] { "WheelFront" }, new[] { "WheelRear" }, 5.5f, 0.95f,
                new Vector3(0f, 3f, 3f), new Vector3(0.9f, 2.1f, 3f), false,
                new[] { "Amb07_LensRedWarn", "Amb07_LensAmberWarn" }, new[] { "Amb07_LensRedWarn2" },
                new[] { "Amb07_LensAmber", "Amb07_LensRed", "Amb07_LensClear" }, false, false, "Amb07Lenses", true);
        }

        /// <summary>Gives the driven object a profile (adding the component when the scene has none) carrying the apparatus's values.</summary>
        private static void SetProfile(Transform driven, ApparatusProfileValues values)
        {
            var profile = driven.GetComponent<ApparatusProfile>();

            if (profile == null)
            {
                profile = driven.gameObject.AddComponent<ApparatusProfile>();
            }

            profile.Use(values);
        }

        /// <summary>The beacon lights wherever they currently are.</summary>
        private static List<Light> BeaconLights(FireResponse apparatus)
        {
            return apparatus.Beacons
                .Where(light => light != null)
                .ToList();
        }

        /// <summary>A root object of the driven object's scene, found even while inactive; null when absent.</summary>
        private static Transform? FindRootOrNull(Transform driven, string name)
        {
            return driven.gameObject.scene
                .GetRootGameObjects()
                .Where(root => root.name == name)
                .Select(root => root.transform)
                .FirstOrDefault();
        }

        /// <summary>A root object of the driven object's scene, found even while inactive.</summary>
        private static Transform FindRoot(Transform driven, string name)
        {
            return FindRootOrNull(driven, name) ?? throw new InvalidOperationException(name + " not found in the scene.");
        }
    }
}
