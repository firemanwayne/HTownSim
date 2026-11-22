using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TMPro;

/// <summary>Installs Engine 43 visuals on the existing gameplay root without breaking scene references.</summary>
public static class InstallEngine43
{
    /// <summary>Prefab and scene assets used by this integration.</summary>
    private const string Folder = "Assets/HTownSim/Vehicles/Engine43/";

    /// <summary>Replaces visuals and wires existing emergency-light control.</summary>
    public static string Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();

        if (EditorApplication.isPlaying || scene.path != "Assets/Scenes/HoustonResponse.unity" || scene.isDirty)
        {
            throw new InvalidOperationException("Expected the saved gameplay scene in Edit Mode.");
        }

        var engine = GameObject.Find("Engine07");

        if (engine == null || engine.transform.Find("Engine43Visual") != null)
        {
            throw new InvalidOperationException("Expected the original engine, with no previous installation.");
        }

        var response = engine.GetComponent("FireResponse");

        var flasher = engine.GetComponent("ApparatusLights");

        var oldChildren = engine.transform.Cast<Transform>().ToArray();

        var archive = new GameObject("PreviousEngineVisuals").transform;

        archive.SetParent(engine.transform, false);

        foreach (var child in oldChildren)
        {
            child.SetParent(archive, false);
        }

        archive.gameObject.SetActive(false);

        var visual = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "HoustonEngine43.prefab"), engine.transform);

        visual.name = "Engine43Visual";

        visual.transform.localPosition = new Vector3(0, -0.15f, 0);

        var lights = visual.GetComponentsInChildren<Light>();

        var responseData = new SerializedObject(response);

        var beacons = responseData.FindProperty("_beacons");

        beacons.arraySize = lights.Length;

        for (var index = 0; index < lights.Length; index++)
        {
            var light = lights[index];

            light.enabled = true;

            light.intensity = 0;

            light.type = LightType.Spot;

            light.spotAngle = 105;

            light.range = 35;

            light.transform.localRotation = Quaternion.Euler(16, index == 0 ? -12 : 12, 0);

            beacons.GetArrayElementAtIndex(index).objectReferenceValue = light;
        }

        responseData.ApplyModifiedPropertiesWithoutUndo();

        var lenses = visual.GetComponentsInChildren<Renderer>().Where(r => r.name == "TwinLEDLensOff" || r.name == "SideMarkerLensOff" || r.name == "RearUpperMarkerLensOff" || r.name == "RearLampLensOff").ToArray();

        var red = FlashMaterial("Engine43RedOn", new Color(1, 0.035f, 0.015f));

        var amber = FlashMaterial("Engine43AmberOn", new Color(1, 0.45f, 0.015f));

        var white = FlashMaterial("Engine43WhiteOn", new Color(1, 0.94f, 0.82f));

        var data = new SerializedObject(flasher);

        data.FindProperty("_response").objectReferenceValue = response;

        foreach (var field in new[] { "_lenses", "_on", "_off", "_group" })
        {
            data.FindProperty(field).arraySize = lenses.Length;
        }

        for (var index = 0; index < lenses.Length; index++)
        {
            var lens = lenses[index];

            var point = visual.transform.InverseTransformPoint(lens.transform.position);

            data.FindProperty("_lenses").GetArrayElementAtIndex(index).objectReferenceValue = lens;

            data.FindProperty("_off").GetArrayElementAtIndex(index).objectReferenceValue = lens.sharedMaterial;

            data.FindProperty("_on").GetArrayElementAtIndex(index).objectReferenceValue = lens.name == "RearUpperMarkerLensOff" ? amber : Mathf.Abs(point.x) < 0.4f ? white : red;

            data.FindProperty("_group").GetArrayElementAtIndex(index).intValue = point.x < 0 ? 0 : 1;
        }

        data.ApplyModifiedPropertiesWithoutUndo();

        foreach (var label in UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Exclude))
        {
            if (!label.transform.IsChildOf(visual.transform))
            {
                label.text = label.text.Replace("ENGINE 7", "ENGINE 43").Replace("ENGINE 07", "ENGINE 43");
            }
        }

        PrefabUtility.RecordPrefabInstancePropertyModifications(visual.transform);

        EditorSceneManager.MarkSceneDirty(scene);

        EditorSceneManager.SaveScene(scene);

        AssetDatabase.SaveAssets();

        return "Installed Engine43Visual; archived old visuals; preserved Engine07 gameplay root and bay pose; wired " + lenses.Length + " warning lenses and " + lights.Length + " beacons.";
    }

    /// <summary>Creates an emissive material only used while a warning lens is lit.</summary>
    private static Material FlashMaterial(string name, Color color)
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name, color = color };

        material.EnableKeyword("_EMISSION");

        material.SetColor("_EmissionColor", color * 7);

        AssetDatabase.CreateAsset(material, Folder + "Materials/" + name + ".mat");

        return material;
    }
}
