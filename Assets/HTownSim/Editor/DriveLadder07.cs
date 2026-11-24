#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Makes Ladder 07 the truck you drive, and parks Engine 07 in its bay (until a character picks the apparatus).
///
/// The driven object keeps its name (Engine07) and all its gameplay (FireResponse, ApparatusLights, sounds, HUD, map);
/// what changes is what it is:
///   - it moves to the Ladder 7 bay, so that is where runs start and restarts return to;
///   - the Ladder07Visual model (installed by InstallLadder07) moves onto it, and the parked ladder prop is switched off;
///   - the engine's visuals (Engine07CleanVisual, and Engine43Visual with its warning-light lenses) are switched off,
///     and the two beacon lights move to the ladder's front roof;
///   - an ApparatusProfile gives the ladder's tyres, wheelbase, wheel names, wider turning circle, camera distance,
///     water origin, scene light spots and flashing lens materials, and turns off the engine's add-on brake panels.
/// A static Engine07Parked (the clean Engine 07 model, materials copied) goes into the Engine 7 bay with a collider.
/// Apply refuses with unsaved scene changes or twice; Revert puts everything back.
/// </summary>
public static class DriveLadder07
{
    private const string ParkedName = "Engine07Parked";

    private const string BeaconsName = "LadderBeacons";

    private const string LadderVisual = "Ladder07Visual";

    private const string EngineModel = "Assets/HTownSim/Vehicles/Engine07Hi3D/Engine07Clean.fbx";

    /// <summary>The engine visuals' drop below the driven object, also used for the ladder so its tyres sit the same.</summary>
    private const float VisualDrop = -0.15f;

    /// <summary>Ladder beacon positions on its front roof, and the beacons' original spot on the engine.</summary>
    private static readonly Vector3 LadderBeacon = new Vector3(0.95f, 3.25f, 5.4f);

    private static readonly Vector3 EngineBeacon = new Vector3(1f, 3.05f, 3.2f);

    /// <summary>Where InstallLadder07 parks the ladder model on the prop.</summary>
    private static readonly Vector3 PropVisualPosition = new Vector3(0f, -0.045f, 0.05f);

    /// <summary>The runtime's ApparatusProfile component (internal to the game assembly, so found by name).</summary>
    private static Type ProfileType => GameObject.Find("Engine07").GetComponent("FireResponse").GetType().Assembly
        .GetType("HTownSim.Runtime.ApparatusProfile", true)!;

    /// <summary>Ladder 07's fit-out (model measurements: 0.558 m tyres, front axle +3.80, tandem -1.76/-3.13).</summary>
    private static void WriteLadderProfile(Component profile)
    {
        var data = new SerializedObject(profile);

        data.FindProperty("_unitName").stringValue = "Ladder 07";

        data.FindProperty("_wheelRadius").floatValue = 0.558f;

        data.FindProperty("_wheelbase").floatValue = 6.25f;

        Strings(data, "_frontWheelPrefixes", "L07_wheel_0_");

        Strings(data, "_rearWheelPrefixes", "L07_wheel_1_", "L07_wheel_2_");

        // 2.5 degrees of yaw per metre at full lock: about a 23 m turning radius (wider than the engine's ~14 m).
        data.FindProperty("_turnRate").floatValue = 2.5f;

        data.FindProperty("_cameraDistanceScale").floatValue = 1.25f;

        data.FindProperty("_waterOrigin").vector3Value = new Vector3(0f, 3.5f, 5.8f);

        data.FindProperty("_sceneLight").vector3Value = new Vector3(0.85f, 2.8f, 5.85f);

        data.FindProperty("_rearLamps").boolValue = false;

        data.FindProperty("_airHorn").boolValue = true;

        data.FindProperty("_canSprayWater").boolValue = true;

        data.FindProperty("_emsAlert").boolValue = false;

        data.FindProperty("_lensHalos").stringValue = "";

        Strings(data, "_flashGroupA", "L07_Red_lenses");

        Strings(data, "_flashGroupB", "L07_Clear_LED_lenses");

        Strings(data, "_steadyLenses", "L07_Amber_lenses");

        data.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void Strings(SerializedObject data, string field, params string[] values)
    {
        var property = data.FindProperty(field);

        property.arraySize = values.Length;

        for (var i = 0; i < values.Length; i++)
        {
            property.GetArrayElementAtIndex(i).stringValue = values[i];
        }
    }

    public static string Apply()
    {
        var scene = Check();

        var driven = GameObject.Find("Engine07").transform;

        var prop = GameObject.Find("Ladder07").transform;

        if (driven.Find(LadderVisual) != null)
        {
            return "Already driving Ladder 07; run Revert first.";
        }

        var ladder = prop.Find(LadderVisual) ?? throw new InvalidOperationException("Run InstallLadder07.Apply first.");

        var engineBay = driven.position;

        var engineRotation = driven.rotation;

        // Park a static Engine 07 in its bay, dressed like the driven one was.
        var parked = new GameObject(ParkedName).transform;

        parked.SetPositionAndRotation(engineBay, engineRotation);

        var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(EngineModel), parked);

        model.transform.localPosition = new Vector3(0f, VisualDrop, 0f);

        model.transform.localRotation = Quaternion.identity;

        var source = driven.Find("Engine07CleanVisual");

        var sourceMaterials = source.GetComponentsInChildren<Renderer>(true).ToDictionary(r => r.name, r => r.sharedMaterials);

        foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            if (sourceMaterials.TryGetValue(renderer.name, out var materials))
            {
                renderer.sharedMaterials = materials;
            }

            renderer.shadowCastingMode = renderer.name is "Engine07_Glass" or "Engine07_Decals" or "Engine07_BarDome" or "Engine07_SceneLenses"
                ? UnityEngine.Rendering.ShadowCastingMode.Off
                : UnityEngine.Rendering.ShadowCastingMode.On;
        }

        var box = parked.gameObject.AddComponent<BoxCollider>();

        box.center = new Vector3(0f, 1.45f, 0f);

        box.size = new Vector3(2.6f, 3.1f, 10.1f);

        // The driven object moves to the ladder bay and becomes the ladder.
        driven.SetPositionAndRotation(prop.position, prop.rotation);

        source.gameObject.SetActive(false);

        var oldVisual = driven.Find("Engine43Visual");

        var beacons = BeaconLights(driven);

        var beaconRoot = new GameObject(BeaconsName).transform;

        beaconRoot.SetParent(driven, false);

        foreach (var beacon in beacons)
        {
            // A previous return to the engine may have attached this light to its model prefab.
            // Unpack the scene instance before moving the light out of that hierarchy again.
            var prefabRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(beacon.gameObject);

            if (prefabRoot != null)
            {
                PrefabUtility.UnpackPrefabInstance(prefabRoot, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            }

            var side = Mathf.Sign(driven.InverseTransformPoint(beacon.transform.position).x);

            beacon.transform.SetParent(beaconRoot, false);

            beacon.transform.localPosition = new Vector3(side * LadderBeacon.x, LadderBeacon.y, LadderBeacon.z);
        }

        oldVisual.gameObject.SetActive(false);

        ladder.SetParent(driven, false);

        ladder.localPosition = new Vector3(0f, VisualDrop, 0f);

        ladder.localRotation = Quaternion.identity;

        prop.gameObject.SetActive(false);

        var profile = driven.GetComponent(ProfileType) ?? driven.gameObject.AddComponent(ProfileType);

        WriteLadderProfile(profile);

        return Save(scene, $"Driving Ladder 07 from the ladder bay {driven.position}; Engine 07 parked at {engineBay}; {beacons.Count} beacons moved.");
    }

    /// <summary>Updates an already-driven ladder's profile to the current values (e.g. after a new profile field).</summary>
    public static string Refresh()
    {
        var scene = Check();

        var driven = GameObject.Find("Engine07");

        var profile = driven.GetComponent(ProfileType) ?? throw new InvalidOperationException("Not driving Ladder 07; run Apply.");

        WriteLadderProfile(profile);

        return Save(scene, "Ladder 07 profile refreshed.");
    }

    public static string Revert()
    {
        var scene = Check();

        var driven = GameObject.Find("Engine07").transform;

        var ladder = driven.Find(LadderVisual);

        var prop = FindInactiveRoot("Ladder07");

        var parked = GameObject.Find(ParkedName);

        if (ladder == null || prop == null)
        {
            return "Not driving Ladder 07; nothing to revert.";
        }

        prop.gameObject.SetActive(true);

        ladder.SetParent(prop, false);

        ladder.localPosition = PropVisualPosition;

        ladder.localRotation = Quaternion.identity;

        var oldVisual = driven.Find("Engine43Visual");

        oldVisual.gameObject.SetActive(true);

        var beaconRoot = driven.Find(BeaconsName);

        var lighting = oldVisual.Find("EmergencyLighting");

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

        driven.Find("Engine07CleanVisual").gameObject.SetActive(true);

        if (parked != null)
        {
            driven.SetPositionAndRotation(parked.transform.position, parked.transform.rotation);

            UnityEngine.Object.DestroyImmediate(parked);
        }

        var profile = driven.GetComponent(ProfileType);

        if (profile != null)
        {
            UnityEngine.Object.DestroyImmediate(profile);
        }

        // Beacons were moved before the engine went back to its bay; place them again relative to it.
        foreach (var beacon in BeaconLights(driven))
        {
            var side = Mathf.Sign(driven.InverseTransformPoint(beacon.transform.position).x);

            beacon.transform.position = driven.TransformPoint(new Vector3(side * EngineBeacon.x, EngineBeacon.y, EngineBeacon.z));
        }

        return Save(scene, "Driving Engine 07 again; Ladder 07 parked in its bay.");
    }

    /// <summary>The FireResponse beacon lights, wherever they currently are.</summary>
    private static List<Light> BeaconLights(Transform driven)
    {
        var response = driven.GetComponent("FireResponse");

        var field = response.GetType().GetField("_beacons", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        return ((Light[])field.GetValue(response)).Where(l => l != null).ToList();
    }

    private static Transform? FindInactiveRoot(string name)
    {
        return EditorSceneManager.GetActiveScene()
            .GetRootGameObjects()
            .Where(g => g.name == name)
            .Select(g => g.transform)
            .FirstOrDefault();
    }

    private static UnityEngine.SceneManagement.Scene Check()
    {
        var scene = EditorSceneManager.GetActiveScene();

        if (EditorApplication.isPlaying || (scene.isDirty && !HTownSim.Editor.StartingTruckMenu.IsSwitching))
        {
            throw new InvalidOperationException("Exit Play Mode and save or discard scene changes first.");
        }

        return scene;
    }

    private static string Save(UnityEngine.SceneManagement.Scene scene, string message)
    {
        EditorSceneManager.MarkSceneDirty(scene);

        if (HTownSim.Editor.StartingTruckMenu.IsSwitching)
        {
            return message;
        }

        if (!EditorSceneManager.SaveScene(scene))
        {
            throw new InvalidOperationException("Could not save the truck selection.");
        }

        AssetDatabase.SaveAssets();

        return message + " Scene saved.";
    }
}
