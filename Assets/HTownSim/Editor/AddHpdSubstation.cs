#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Builds the HPD Midtown substation next to Fire Station 7 and parks two vehicles on its apron for the start screen: the HPD patrol
/// unit 46837 (Blender/HPD, exported by export_hpd.py) in the group HpdApparatus, and a citizen car (a SimplePoly car) in the group
/// CitizenVehicles. At run time <c>ApparatusLoadout</c> moves the driven object into whichever the player picks; nothing here moves.
///
/// The substation is a plain low-poly building (a box with a glass strip, door and sign) on a dark apron. Apply (menu
/// HTownSim > HPD > Add Substation) can be run again and replaces what it made; Remove takes it all out. Refuses to run in Play Mode,
/// and saves the scene when it is done.
/// </summary>
public static class AddHpdSubstation
{
    private const string StationName = "HpdSubstation";

    private const string PatrolGroup = "HpdApparatus";

    private const string PatrolUnit = "Patrol46837";

    private const string CitizenGroup = "CitizenVehicles";

    private const string CitizenUnit = "CitizenCar";

    /// <summary>The old name of the citizen group, removed on the way so no stale copy is left in the scene.</summary>
    private const string LegacyCivilianGroup = "CivilianVehicles";

    private const string PatrolModel = "Assets/HTownSim/Vehicles/HpdPatrol/HpdPatrol.fbx";

    private const string CitizenModel = "Assets/SimplePoly City - Low Poly Assets/Prefab/Vehicles/Vehicle with Separated Wheels/Vehicle_Car_color01_separate.prefab";

    private const string MaterialFolder = "Assets/HTownSim/Vehicles/HpdPatrol/SubstationMaterials";

    /// <summary>The lot beside Station 7: its centre on the ground (found free by a search of the scene) and which way its front faces (+Z, to the street).</summary>
    private static readonly Vector3 LotCentre = new Vector3(48f, 0.2f, -24f);

    /// <summary>The citizen car is a SimplePoly car, larger than a real one; it is scaled down to this.</summary>
    private const float CitizenScale = 0.85f;

    [MenuItem("HTownSim/HPD/Add Substation")]
    private static void AddFromMenu()
    {
        Debug.Log(Apply());
    }

    [MenuItem("HTownSim/HPD/Remove Substation")]
    private static void RemoveFromMenu()
    {
        Debug.Log(Remove());
    }

    /// <summary>Builds the substation and parks the two vehicles (replacing any made before) and saves the scene.</summary>
    public static string Apply()
    {
        if (EditorApplication.isPlaying)
        {
            return "Stop Play Mode first.";
        }

        var scene = EditorSceneManager.GetActiveScene();

        RemoveAll(scene);

        System.IO.Directory.CreateDirectory(MaterialFolder);

        var materials = new Materials();

        BuildStation(materials);

        ParkPatrol();

        ParkCitizen();

        EditorSceneManager.MarkSceneDirty(scene);

        EditorSceneManager.SaveScene(scene);

        AssetDatabase.SaveAssets();

        return "HPD substation added next to Station 7; patrol unit 46837 and a citizen car are parked on its apron.";
    }

    /// <summary>Removes everything Apply made and saves the scene.</summary>
    public static string Remove()
    {
        if (EditorApplication.isPlaying)
        {
            return "Stop Play Mode first.";
        }

        var scene = EditorSceneManager.GetActiveScene();

        RemoveAll(scene);

        EditorSceneManager.MarkSceneDirty(scene);

        EditorSceneManager.SaveScene(scene);

        return "HPD substation removed.";
    }

    private static void RemoveAll(UnityEngine.SceneManagement.Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects().Where(root => root.name is StationName or PatrolGroup or CitizenGroup or LegacyCivilianGroup).ToList())
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    /// <summary>The building, the apron, the parking lines and the sign, in the lot's own frame (+Z toward the street).</summary>
    private static void BuildStation(Materials materials)
    {
        var root = new GameObject(StationName).transform;

        root.position = LotCentre;

        // The apron: a dark pad over the lot's slab, from the building's front to the street, with no collider (the slab is the floor).
        var apron = Box(root, "Apron", new Vector3(22f, 0.05f, 14f), new Vector3(0f, 0.025f, 5f), materials.Asphalt);

        UnityEngine.Object.DestroyImmediate(apron.GetComponent<Collider>());

        // Parking lines for the two vehicles (white strips across the apron's back edge).
        foreach (var x in new[] { -8f, 0f, 8f })
        {
            var line = Box(root, "BayLine", new Vector3(0.18f, 0.02f, 5.5f), new Vector3(x, 0.06f, 5f), materials.Paint);

            UnityEngine.Object.DestroyImmediate(line.GetComponent<Collider>());
        }

        // The building: 18 m wide, 9 m deep, 5 m tall, facing +Z.
        Box(root, "Walls", new Vector3(18f, 5f, 9f), new Vector3(0f, 2.5f, -6.5f), materials.Wall);

        Box(root, "Roof", new Vector3(18.8f, 0.4f, 9.8f), new Vector3(0f, 5.2f, -6.5f), materials.Roof);

        Box(root, "Stripe", new Vector3(18.1f, 0.55f, 9.1f), new Vector3(0f, 3.7f, -6.5f), materials.Stripe);

        var glass = Box(root, "Windows", new Vector3(10f, 1.5f, 0.2f), new Vector3(-3f, 2.2f, -1.95f), materials.Glass);

        UnityEngine.Object.DestroyImmediate(glass.GetComponent<Collider>());

        var door = Box(root, "Door", new Vector3(2.4f, 2.5f, 0.2f), new Vector3(6f, 1.25f, -1.95f), materials.Glass);

        UnityEngine.Object.DestroyImmediate(door.GetComponent<Collider>());

        Box(root, "SignBoard", new Vector3(11f, 1.5f, 0.25f), new Vector3(-2f, 4.45f, -1.9f), materials.Sign);

        AddText(root, "SignTitle", "HOUSTON POLICE", new Vector3(-2f, 4.9f, -1.74f), 4.2f);

        AddText(root, "SignSubtitle", "MIDTOWN SUBSTATION", new Vector3(-2f, 4.1f, -1.74f), 2.6f);

        // A flag pole on the apron's corner, to read as a public building from the street.
        Box(root, "FlagPole", new Vector3(0.12f, 7f, 0.12f), new Vector3(-10.2f, 3.5f, 9f), materials.Steel);
    }

    private static void ParkPatrol()
    {
        var group = new GameObject(PatrolGroup).transform;

        var unit = new GameObject(PatrolUnit).transform;

        unit.SetParent(group, false);

        unit.SetPositionAndRotation(LotCentre + new Vector3(-4f, 0f, 6f), Quaternion.identity);

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PatrolModel) ?? throw new InvalidOperationException("Model not found: " + PatrolModel + ". Run Blender/HPD/export_hpd.py.");

        PrefabUtility.InstantiatePrefab(prefab, unit);

        AddCollider(unit, new Vector3(0f, 1.0f, 0.06f), new Vector3(2.4f, 1.9f, 5.1f));
    }

    private static void ParkCitizen()
    {
        var group = new GameObject(CitizenGroup).transform;

        var unit = new GameObject(CitizenUnit).transform;

        unit.SetParent(group, false);

        unit.SetPositionAndRotation(LotCentre + new Vector3(4f, 0f, 6f), Quaternion.identity);

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CitizenModel) ?? throw new InvalidOperationException("Model not found: " + CitizenModel);

        var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, unit);

        model.transform.localScale = Vector3.one * CitizenScale;

        // The SimplePoly car's pivot is a metre below its body, so the car is lowered until its wheels touch the lot.
        var renderers = model.GetComponentsInChildren<Renderer>();

        var lowest = renderers.Min(renderer => renderer.bounds.min.y);

        model.transform.position += Vector3.up * (unit.position.y - lowest);

        AddCollider(unit, new Vector3(0f, 0.95f, 0.1f), new Vector3(2.3f, 1.7f, 5.2f));
    }

    private static void AddCollider(Transform unit, Vector3 centre, Vector3 size)
    {
        var box = unit.gameObject.AddComponent<BoxCollider>();

        box.center = centre;

        box.size = size;
    }

    /// <summary>A cube with a size and a position in the parent's frame, painted with a material.</summary>
    private static GameObject Box(Transform parent, string name, Vector3 size, Vector3 position, Material material)
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);

        cube.name = name;

        cube.transform.SetParent(parent, false);

        cube.transform.localPosition = position;

        cube.transform.localScale = size;

        cube.GetComponent<Renderer>().sharedMaterial = material;

        return cube;
    }

    private static void AddText(Transform parent, string name, string text, Vector3 position, float size)
    {
        var label = new GameObject(name).AddComponent<TextMeshPro>();

        label.transform.SetParent(parent, false);

        label.transform.localPosition = position;

        // TextMeshPro text faces -Z; the sign is read from the street, which is +Z, so it is turned round.
        label.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

        label.text = text;

        label.fontSize = size;

        label.fontStyle = FontStyles.Bold;

        label.alignment = TextAlignmentOptions.Center;

        label.color = new Color(0.95f, 0.97f, 1f);

        label.rectTransform.sizeDelta = new Vector2(11f, 1.2f);
    }

    /// <summary>The building's materials, made once as assets.</summary>
    private sealed class Materials
    {
        internal Material Wall { get; } = Make("Wall", new Color(0.16f, 0.2f, 0.27f), 0.1f);

        internal Material Roof { get; } = Make("Roof", new Color(0.08f, 0.1f, 0.13f), 0.1f);

        internal Material Stripe { get; } = Make("Stripe", new Color(0.8f, 0.82f, 0.86f), 0.1f);

        internal Material Glass { get; } = Make("Glass", new Color(0.1f, 0.22f, 0.3f), 0.8f);

        internal Material Sign { get; } = Make("Sign", new Color(0.04f, 0.06f, 0.1f), 0.1f);

        internal Material Asphalt { get; } = Make("Asphalt", new Color(0.09f, 0.1f, 0.11f), 0.05f);

        internal Material Paint { get; } = Make("Paint", new Color(0.85f, 0.85f, 0.8f), 0.1f);

        internal Material Steel { get; } = Make("Steel", new Color(0.6f, 0.62f, 0.65f), 0.7f);

        private static Material Make(string name, Color colour, float smoothness)
        {
            var path = MaterialFolder + "/HpdSubstation_" + name + ".mat";

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));

                AssetDatabase.CreateAsset(material, path);
            }

            material.SetColor("_BaseColor", colour);

            material.SetFloat("_Smoothness", smoothness);

            EditorUtility.SetDirty(material);

            return material;
        }
    }
}
