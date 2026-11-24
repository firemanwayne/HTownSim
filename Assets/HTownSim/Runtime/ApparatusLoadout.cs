#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HTownSim.Runtime
{
    /// <summary>
    /// Turns the driven object (named Engine07 whatever it is) into Engine 07, Ladder 07 or Ambulance 07 while the scene
    /// loads. This is the run-time twin of the editor scripts DriveLadder07 and DriveAmbulance07: the driven object moves to
    /// the chosen apparatus's bay, wears its model, takes its ApparatusProfile and beacon positions, and the apparatus it
    /// replaces is parked in its own bay. Nothing here is saved to the scene; the scene's own truck is only the starting point.
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

        /// <summary>The visuals' drop below the driven object, so every apparatus's tyres sit on the floor the same way.</summary>
        private const float VisualDrop = -0.15f;

        /// <summary>Where the ladder prop's model sits on its root.</summary>
        private static readonly Vector3 PropVisualPosition = new Vector3(0f, -0.045f, 0.05f);

        /// <summary>Beacon positions (right side; the left mirrors in x) on each apparatus, in the driven object's space.</summary>
        private static readonly Vector3 EngineBeacon = new Vector3(1f, 3.05f, 3.2f);

        private static readonly Vector3 LadderBeacon = new Vector3(0.95f, 3.25f, 5.4f);

        private static readonly Vector3 AmbulanceBeacon = new Vector3(0.8f, 3.1f, 0.7f);

        /// <summary>Ladder 07's fit-out (0.558 m tyres, front axle +3.80, tandem -1.76/-3.13).</summary>
        private static readonly ApparatusProfileValues LadderValues = new ApparatusProfileValues(
            "Ladder 07", 0.558f, 6.25f, new[] { "L07_wheel_0_" }, new[] { "L07_wheel_1_", "L07_wheel_2_" }, 2.5f, 1.25f,
            new Vector3(0f, 3.5f, 5.8f), new Vector3(0.85f, 2.8f, 5.85f), false,
            new[] { "L07_Red_lenses" }, new[] { "L07_Clear_LED_lenses" }, new[] { "L07_Amber_lenses" }, true, true, "", false);

        /// <summary>Ambulance 07's fit-out (0.41 m tyres, axles 2.56 m ahead and 1.62 m behind the origin, no water).</summary>
        private static readonly ApparatusProfileValues AmbulanceValues = new ApparatusProfileValues(
            "Ambulance 07", 0.41f, 4.18f, new[] { "WheelFront" }, new[] { "WheelRear" }, 5.5f, 0.95f,
            new Vector3(0f, 3f, 3f), new Vector3(0.9f, 2.1f, 3f), false,
            new[] { "Amb07_LensRedWarn", "Amb07_LensAmberWarn" }, new[] { "Amb07_LensRedWarn2" },
            new[] { "Amb07_LensAmber", "Amb07_LensRed", "Amb07_LensClear" }, false, false, "Amb07Lenses", true);

        /// <summary>Which apparatus the driven object currently is, read from the fitted model.</summary>
        internal static StartingTruck Detect(Transform driven)
        {
            if (driven.Find(AmbulanceVisual) != null)
            {
                return StartingTruck.Ambulance;
            }

            return driven.Find(LadderVisual) != null ? StartingTruck.Ladder : StartingTruck.Engine;
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

                if (request.Target == StartingTruck.Engine)
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

        /// <summary>Engine to ladder: the engine is parked in its bay and the driven object takes the ladder bay and model.</summary>
        private static void ApplyLadder(LoadoutRequest request)
        {
            var driven = request.Apparatus.transform;

            var prop = FindRoot(driven, LadderProp);

            var ladder = prop.Find(LadderVisual) ?? throw new InvalidOperationException("Ladder07 has no " + LadderVisual + ".");

            var engineBay = driven.position;

            var engineRotation = driven.rotation;

            var source = driven.Find(EngineCleanVisual) ?? throw new InvalidOperationException("Engine07 has no " + EngineCleanVisual + ".");

            // Park a static copy of the engine model in the engine bay, with a box to drive around.
            var parked = new GameObject(ParkedName).transform;

            parked.SetPositionAndRotation(engineBay, engineRotation);

            UnityEngine.Object.Instantiate(source.gameObject, parked, false);

            var box = parked.gameObject.AddComponent<BoxCollider>();

            box.center = new Vector3(0f, 1.45f, 0f);

            box.size = new Vector3(2.6f, 3.1f, 10.1f);

            // The driven object moves to the ladder bay and becomes the ladder.
            driven.SetPositionAndRotation(prop.position, prop.rotation);

            source.gameObject.SetActive(false);

            var oldVisual = driven.Find(EngineOldVisual) ?? throw new InvalidOperationException("Engine07 has no " + EngineOldVisual + ".");

            var beaconRoot = new GameObject(BeaconsName).transform;

            beaconRoot.SetParent(driven, false);

            foreach (var beacon in BeaconLights(request.Apparatus))
            {
                var side = Mathf.Sign(driven.InverseTransformPoint(beacon.transform.position).x);

                beacon.transform.SetParent(beaconRoot, false);

                beacon.transform.localPosition = new Vector3(side * LadderBeacon.x, LadderBeacon.y, LadderBeacon.z);
            }

            oldVisual.gameObject.SetActive(false);

            ladder.SetParent(driven, false);

            ladder.localPosition = new Vector3(0f, VisualDrop, 0f);

            ladder.localRotation = Quaternion.identity;

            prop.gameObject.SetActive(false);

            SetProfile(driven, LadderValues);
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

            SetProfile(driven, AmbulanceValues);
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
