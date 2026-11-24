#nullable enable
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Makes Ambulance 07 the truck you drive (run after DriveLadder07 and InstallAmbulance07), and parks Ladder 07 in its bay.
///
/// Like DriveLadder07, the driven object keeps its name (Engine07) and all its gameplay (FireResponse, lights, sounds, HUD,
/// map); what changes is what it is:
///   - it moves to the Ambulance 7 bay (x 20.3), so runs start and restarts return there;
///   - the Ambulance07Visual model moves onto it from Medic07 (which is switched off), and the Ladder07Visual goes back on
///     the Ladder07 prop in the ladder bay (the aerial controller finds no turntable on the ambulance and stays off);
///   - the two beacon lights move to the ambulance's box-front roof;
///   - the ApparatusProfile becomes Ambulance 07's: 0.41 m tyres, 4.18 m wheelbase, a tighter turn, the electric horn
///     (no air horn), NO WATER (the E key and the HUD water hint are off), and only the warning lenses flash.
/// Apply refuses with unsaved scene changes or twice; Revert puts the ladder back in the driver's seat.
/// </summary>
public static class DriveAmbulance07
{
    private const string BeaconsName = "LadderBeacons";

    private const string LadderVisual = "Ladder07Visual";

    private const string AmbulanceVisual = "Ambulance07Visual";

    private const float VisualDrop = -0.15f;

    /// <summary>Where the ladder prop's model sits on its root (the same as InstallLadder07).</summary>
    private static readonly Vector3 PropVisualPosition = new Vector3(0f, -0.045f, 0.05f);

    private static readonly Vector3 LadderBeacon = new Vector3(0.95f, 3.25f, 5.4f);

    private static readonly Vector3 AmbulanceBeacon = new Vector3(0.8f, 3.1f, 0.7f);

    private static Type ProfileType => GameObject.Find("Engine07").GetComponent("FireResponse").GetType().Assembly
        .GetType("HTownSim.Runtime.ApparatusProfile", true)!;

    /// <summary>Ambulance 07's fit-out (model measurements: 0.41 m tyres, axles 2.56 m ahead and 1.62 m behind the origin).</summary>
    private static void WriteAmbulanceProfile(Component profile)
    {
        var data = new SerializedObject(profile);

        data.FindProperty("_unitName").stringValue = "Ambulance 07";

        data.FindProperty("_wheelRadius").floatValue = 0.41f;

        data.FindProperty("_wheelbase").floatValue = 4.18f;

        Strings(data, "_frontWheelPrefixes", "WheelFront");

        Strings(data, "_rearWheelPrefixes", "WheelRear");

        // A short van turns tighter than the engine: 5.5 degrees of yaw per metre, about a 10 m radius.
        data.FindProperty("_turnRate").floatValue = 5.5f;

        data.FindProperty("_cameraDistanceScale").floatValue = 0.95f;

        data.FindProperty("_waterOrigin").vector3Value = new Vector3(0f, 3f, 3f);

        data.FindProperty("_sceneLight").vector3Value = new Vector3(0.9f, 2.1f, 3.0f);

        data.FindProperty("_rearLamps").boolValue = false;

        data.FindProperty("_airHorn").boolValue = false;

        data.FindProperty("_canSprayWater").boolValue = false;

        data.FindProperty("_lensHalos").stringValue = "Amb07Lenses";

        data.FindProperty("_emsAlert").boolValue = true;

        Strings(data, "_flashGroupA", "Amb07_LensRedWarn", "Amb07_LensAmberWarn");

        Strings(data, "_flashGroupB", "Amb07_LensRedWarn2");

        Strings(data, "_steadyLenses", "Amb07_LensAmber", "Amb07_LensRed", "Amb07_LensClear");

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

        var medic = FindRoot("Medic07") ?? throw new InvalidOperationException("Medic07 not found.");

        var prop = FindRoot("Ladder07") ?? throw new InvalidOperationException("Ladder07 prop not found.");

        var ladder = driven.Find(LadderVisual) ?? throw new InvalidOperationException("Not driving Ladder 07; run DriveLadder07.Apply first.");

        var ambulance = medic.Find(AmbulanceVisual) ?? throw new InvalidOperationException("Run InstallAmbulance07.Apply first.");

        var ladderBay = driven.position;

        var ladderRotation = driven.rotation;

        // The ladder goes back on its prop in the ladder bay.
        prop.gameObject.SetActive(true);

        prop.SetPositionAndRotation(ladderBay, ladderRotation);

        ladder.SetParent(prop, false);

        ladder.localPosition = PropVisualPosition;

        ladder.localRotation = Quaternion.identity;

        // The driven object moves to the ambulance bay (keeping its own height) and takes the ambulance model, tyres unchanged on the floor.
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

        var profile = driven.GetComponent(ProfileType) ?? driven.gameObject.AddComponent(ProfileType);

        WriteAmbulanceProfile(profile);

        return Save(scene, $"Driving Ambulance 07 from {driven.position}; Ladder 07 parked at {ladderBay}.");
    }

    /// <summary>Rewrites the profile of an already-driven ambulance with the current values (after a new profile field).</summary>
    public static string Refresh()
    {
        var scene = Check();

        var driven = GameObject.Find("Engine07");

        var profile = driven.GetComponent(ProfileType) ?? throw new InvalidOperationException("Not driving Ambulance 07; run Apply.");

        WriteAmbulanceProfile(profile);

        return Save(scene, "Ambulance 07 profile refreshed.");
    }

    public static string Revert()
    {
        var scene = Check();

        var driven = GameObject.Find("Engine07").transform;

        var medic = FindRoot("Medic07");

        var prop = FindRoot("Ladder07");

        var ambulance = driven.Find(AmbulanceVisual);

        if (ambulance == null || medic == null || prop == null)
        {
            return "Not driving Ambulance 07; nothing to revert.";
        }

        var ambulanceBay = driven.position;

        // The model goes back to the Medic07 root, in place.
        medic.gameObject.SetActive(true);

        ambulance.SetParent(medic, true);

        // The driven object takes the ladder back.
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

        // The ladder's profile comes back with DriveLadder07.Refresh (written below by the caller if needed).
        return Save(scene, $"Driving Ladder 07 again (ambulance back at {ambulanceBay}). Run DriveLadder07.Refresh to restore the ladder profile.");
    }

    private static Transform? FindRoot(string name)
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
