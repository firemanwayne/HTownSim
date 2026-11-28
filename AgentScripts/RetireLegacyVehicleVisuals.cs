#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Removes the hidden procedural and Hi3D vehicle visuals the city scene still carries now that every engine, ladder and ambulance uses the
/// reusable Blender models (see InstallSpartanLadder and InstallReusableApparatus): Engine07's PreviousEngineVisuals, Engine43Visual and
/// Engine07Hi3DVisual, Ladder07's PreviousLadderVisuals and Medic07's PreviousMedicVisuals. The engine's two emergency beacon point lights
/// lived under Engine43Visual (a prefab instance, whose children cannot be moved out); they are made again under the driven object's
/// LadderBeacons root first (the same red point lights, at the engine's roof positions) and FireResponse is pointed at them, and the
/// ApparatusLights component, which flashed lens boxes on that old model, is removed (the engine's lenses are flashed by name now).
/// Refuses with unsaved scene changes; reports any object reference the removal left missing.
///
/// Run: unity command script run --file AgentScripts/RetireLegacyVehicleVisuals.cs --entry RetireLegacyVehicleVisuals.Apply
/// </summary>
public static class RetireLegacyVehicleVisuals
{
    private const string CityScene = "Assets/Scenes/HoustonResponse.unity";

    private static readonly (string Root, string Child)[] Legacy =
    {
        ("Engine07", "PreviousEngineVisuals"),
        ("Engine07", "Engine43Visual"),
        ("Engine07", "Engine07Hi3DVisual"),
        ("Ladder07", "PreviousLadderVisuals"),
        ("Medic07", "PreviousMedicVisuals"),
    };

    /// <summary>The engine's roof beacons, in the driven object's local space (the side takes the sign of the light's own side).</summary>
    private static readonly Vector3 EngineBeacon = new Vector3(1f, 3.05f, 3.2f);

    public static string Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();

        if (EditorApplication.isPlaying || scene.isDirty)
        {
            throw new InvalidOperationException("Exit Play Mode and save or discard scene changes first.");
        }

        if (scene.path != CityScene)
        {
            scene = EditorSceneManager.OpenScene(CityScene, OpenSceneMode.Single);
        }

        var report = new List<string> { "Missing references before: " + MissingReferences(scene) };

        var driven = Root(scene, "Engine07");

        var beaconRoot = driven.Find("LadderBeacons") ?? throw new InvalidOperationException("Engine07 has no LadderBeacons.");

        report.Add(EnsureBeacons(driven, beaconRoot));

        foreach (var component in driven.GetComponents<MonoBehaviour>().Where(c => c != null && c.GetType().Name == "ApparatusLights").ToList())
        {
            UnityEngine.Object.DestroyImmediate(component);

            report.Add("Removed ApparatusLights from Engine07.");
        }

        foreach (var (rootName, childName) in Legacy)
        {
            var child = Root(scene, rootName).Find(childName);

            if (child == null)
            {
                report.Add(rootName + "/" + childName + " already gone.");

                continue;
            }

            var count = child.GetComponentsInChildren<Transform>(true).Length;

            UnityEngine.Object.DestroyImmediate(child.gameObject);

            report.Add("Removed " + rootName + "/" + childName + " (" + count + " objects).");
        }

        report.Add("Missing references after: " + MissingReferences(scene));

        EditorSceneManager.MarkSceneDirty(scene);

        EditorSceneManager.SaveScene(scene);

        AssetDatabase.SaveAssets();

        EditorSceneManager.OpenScene(scene.path, OpenSceneMode.Single);

        return string.Join("\n", report) + "\nScene saved.";
    }

    /// <summary>
    /// Makes the engine's two roof beacon point lights (left and right) under <paramref name="beaconRoot"/> when it has none, and points
    /// FireResponse's beacons at them. They are the old engine prefab's lights: red, 14 m range, no shadows, off until the game lights them.
    /// </summary>
    private static string EnsureBeacons(Transform driven, Transform beaconRoot)
    {
        var lights = beaconRoot.GetComponentsInChildren<Light>(true).ToList();

        if (lights.Count == 0)
        {
            foreach (var (name, side) in new[] { ("EmergencyLightLeft", -1f), ("EmergencyLightRight", 1f) })
            {
                var beacon = new GameObject(name);

                beacon.transform.SetParent(beaconRoot, false);

                beacon.transform.localPosition = new Vector3(side * EngineBeacon.x, EngineBeacon.y, EngineBeacon.z);

                var light = beacon.AddComponent<Light>();

                light.type = LightType.Point;

                light.color = new Color(1f, 0.025f, 0.015f, 1f);

                light.range = 14f;

                light.intensity = 0f;

                light.shadows = LightShadows.None;

                lights.Add(light);
            }
        }

        var response = driven.GetComponents<MonoBehaviour>().First(c => c != null && c.GetType().Name == "FireResponse");

        var beacons = new SerializedObject(response).FindProperty("_beacons");

        beacons.arraySize = 2;

        beacons.GetArrayElementAtIndex(0).objectReferenceValue = lights.First(light => light.name.EndsWith("Left", StringComparison.Ordinal));

        beacons.GetArrayElementAtIndex(1).objectReferenceValue = lights.First(light => light.name.EndsWith("Right", StringComparison.Ordinal));

        beacons.serializedObject.ApplyModifiedProperties();

        return "Engine beacons: " + string.Join(", ", lights.Select(light => light.name)) + ".";
    }

    private static Transform Root(UnityEngine.SceneManagement.Scene scene, string name)
    {
        return scene.GetRootGameObjects().FirstOrDefault(root => root.name == name)?.transform ?? throw new InvalidOperationException("Root object " + name + " not found.");
    }

    /// <summary>How many object references in the scene's components point at something that no longer exists.</summary>
    private static int MissingReferences(UnityEngine.SceneManagement.Scene scene)
    {
        var missing = 0;

        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null)
                {
                    missing++;

                    continue;
                }

                var property = new SerializedObject(component).GetIterator();

                while (property.NextVisible(true))
                {
                    if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue == null && !property.objectReferenceEntityIdValue.Equals(default(UnityEngine.EntityId)))
                    {
                        missing++;
                    }
                }
            }
        }

        return missing;
    }
}
