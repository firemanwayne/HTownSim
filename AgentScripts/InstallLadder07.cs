#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Swaps the parked Ladder 07 in Station 7's ladder bay for the rigged Blender model in ArtSource\Ladder07
/// (exported stowed by ArtSource\Ladder07\export_ladder07_unity.py to Assets/HTownSim/Vehicles/Ladder07/Ladder07.fbx).
///
/// The FBX carries all three detail levels (_LOD0/_LOD1/_LOD2), so the importer builds a LODGroup. Materials are URP
/// Lit, made from the Blender material values in Ladder07_materials.json and remapped onto the FBX's material slots.
/// The old procedural parts are kept, inactive, under PreviousLadderVisuals; the model goes in as Ladder07Visual,
/// tyres on the bay floor, and the root's box collider is resized to it. Apply refuses to run with unsaved scene
/// changes or twice; Revert restores the procedural ladder. Both reopen the scene after saving.
/// </summary>
public static class InstallLadder07
{
    private const string Folder = "Assets/HTownSim/Vehicles/Ladder07/";

    private const string ModelPath = Folder + "Ladder07.fbx";

    private const string MaterialsJson = Folder + "Ladder07_materials.json";

    private const string VisualName = "Ladder07Visual";

    private const string PreviousName = "PreviousLadderVisuals";

    /// <summary>Lengthwise offset that centres the 12.8 m model in the bay (rear wall and door both clear by ~0.4 m).</summary>
    private const float CentreZ = 0.05f;

    /// <summary>Collider width: the body (2.54 m) plus a little, not the mirrors.</summary>
    private const float ColliderWidth = 2.6f;

    /// <summary>The procedural ladder's collider, restored by Revert.</summary>
    private static readonly Vector3 OldColliderCentre = new Vector3(0f, 1.75f, 0f);

    private static readonly Vector3 OldColliderSize = new Vector3(2.7f, 3.5f, 10.4f);

    public static string Apply()
    {
        var scene = Check();

        var ladder = GameObject.Find("Ladder07").transform;

        if (ladder.Find(VisualName) != null)
        {
            return "Already installed; run Revert first.";
        }

        var (materials, bounds) = ReadMaterials();

        ConfigureImport(materials);

        // Park the procedural parts, inactive, under one container.
        var previous = new GameObject(PreviousName).transform;

        previous.SetParent(ladder, false);

        foreach (var child in ladder.Cast<Transform>().Where(t => t != previous).ToList())
        {
            child.SetParent(previous, true);
        }

        previous.gameObject.SetActive(false);

        // Tyres on the bay floor.
        var floorY = ladder.position.y;

        var collider = ladder.GetComponent<BoxCollider>();

        collider.enabled = false;

        if (Physics.Raycast(ladder.position + Vector3.up * 3f, Vector3.down, out var hit, 10f, ~0, QueryTriggerInteraction.Ignore))
        {
            floorY = hit.point.y;
        }

        collider.enabled = true;

        var visual = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath), ladder);

        visual.name = VisualName;

        visual.transform.localRotation = Quaternion.identity;

        visual.transform.localPosition = new Vector3(0f, floorY - ladder.position.y, CentreZ);

        var height = bounds.max.y - bounds.min.y;

        var length = bounds.max.z - bounds.min.z;

        collider.center = new Vector3(0f, visual.transform.localPosition.y + height / 2f, CentreZ);

        collider.size = new Vector3(ColliderWidth, height, length);

        var lod = visual.GetComponent<LODGroup>();

        var levels = lod != null ? lod.GetLODs().Length : 0;

        return Save(scene, $"Installed {VisualName}: {levels} LOD levels, floor at y {floorY:0.###}, collider {collider.size}.");
    }

    public static string Revert()
    {
        var scene = Check();

        var ladder = GameObject.Find("Ladder07").transform;

        var visual = ladder.Find(VisualName);

        if (visual != null)
        {
            UnityEngine.Object.DestroyImmediate(visual.gameObject);
        }

        var previous = ladder.Find(PreviousName);

        if (previous != null)
        {
            foreach (var child in previous.Cast<Transform>().ToList())
            {
                child.SetParent(ladder, true);
            }

            UnityEngine.Object.DestroyImmediate(previous.gameObject);
        }

        var collider = ladder.GetComponent<BoxCollider>();

        collider.center = OldColliderCentre;

        collider.size = OldColliderSize;

        return Save(scene, "Restored the procedural Ladder 07.");
    }

    private static UnityEngine.SceneManagement.Scene Check()
    {
        var scene = EditorSceneManager.GetActiveScene();

        if (EditorApplication.isPlaying || scene.isDirty)
        {
            throw new InvalidOperationException("Exit Play Mode and save or discard scene changes first.");
        }

        return scene;
    }

    private static string Save(UnityEngine.SceneManagement.Scene scene, string message)
    {
        EditorSceneManager.MarkSceneDirty(scene);

        EditorSceneManager.SaveScene(scene);

        AssetDatabase.SaveAssets();

        EditorSceneManager.OpenScene(scene.path, OpenSceneMode.Single);

        return message + " Scene saved.";
    }

    /// <summary>Imports the FBX with its LODs and remaps every Blender material name onto a URP Lit material.</summary>
    private static void ConfigureImport(IReadOnlyList<MaterialInfo> materials)
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        foreach (var texture in materials.Where(m => m.Texture is not null).Select(m => m.Texture!).Distinct())
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(Folder + texture);

            importer.sRGBTexture = true;

            importer.alphaSource = TextureImporterAlphaSource.FromInput;

            importer.alphaIsTransparency = true;

            importer.wrapMode = TextureWrapMode.Clamp;

            importer.maxTextureSize = 1024;

            importer.SaveAndReimport();
        }

        var model = (ModelImporter)AssetImporter.GetAtPath(ModelPath);

        model.importCameras = false;

        model.importLights = false;

        model.importBlendShapes = false;

        model.animationType = ModelImporterAnimationType.None;

        model.importAnimation = false;

        model.importNormals = ModelImporterNormals.Import;

        // The FBX keeps the rig's pivot hierarchy; Unity bakes the axis conversion so every pivot comes in clean.
        model.bakeAxisConversion = true;

        model.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;

        model.materialLocation = ModelImporterMaterialLocation.InPrefab;

        foreach (var info in materials)
        {
            model.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), info.Name), BuildMaterial(info));
        }

        model.SaveAndReimport();
    }

    private static Material BuildMaterial(MaterialInfo info)
    {
        if (!AssetDatabase.IsValidFolder(Folder + "Materials"))
        {
            AssetDatabase.CreateFolder(Folder.TrimEnd('/'), "Materials");
        }

        var path = Folder + "Materials/" + Regex.Replace(info.Name, "[^A-Za-z0-9_]+", "_") + ".mat";

        var shader = Shader.Find("Universal Render Pipeline/Lit");

        var material = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (material == null)
        {
            material = new Material(shader);

            AssetDatabase.CreateAsset(material, path);
        }

        material.shader = shader;

        // Blender stores linear colours; Unity material colours are sRGB.
        material.SetColor("_BaseColor", info.Colour.gamma);

        material.SetFloat("_Metallic", info.Metallic);

        material.SetFloat("_Smoothness", 1f - info.Roughness);

        if (info.Texture is not null)
        {
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + info.Texture));

            material.SetFloat("_AlphaClip", 1f);

            material.SetFloat("_Cutoff", 0.5f);

            material.EnableKeyword("_ALPHATEST_ON");

            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
        }

        if (info.Emission.maxColorComponent > 0f)
        {
            material.EnableKeyword("_EMISSION");

            material.SetColor("_EmissionColor", info.Emission);

            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }

        EditorUtility.SetDirty(material);

        return material;
    }

    /// <summary>Reads the material list and model bounds written by the Blender export (Unity axes: x, z up -> y, -y -> z).</summary>
    private static (List<MaterialInfo> Materials, Bounds Bounds) ReadMaterials()
    {
        var json = File.ReadAllText(MaterialsJson);

        var list = new List<MaterialInfo>();

        // Only the "materials" object: it is followed by "bounds" and "lod_triangles".
        var start = json.IndexOf("\"materials\"", StringComparison.Ordinal);

        var block = json.Substring(start, json.IndexOf("\"bounds\"", StringComparison.Ordinal) - start);

        foreach (Match m in Regex.Matches(block, "\"([^\"]+)\":\\s*\\{([^{}]*)\\}"))
        {
            var body = m.Groups[2].Value;

            var colour = Numbers(body, "color");

            var emission = Numbers(body, "emission");

            var texture = Regex.Match(body, "\"texture\":\\s*\"([^\"]+)\"");

            list.Add(new MaterialInfo(
                m.Groups[1].Value,
                new Color(colour[0], colour[1], colour[2]),
                Number(body, "metallic"),
                Number(body, "roughness"),
                new Color(emission[0], emission[1], emission[2]),
                texture.Success ? texture.Groups[1].Value : null));
        }

        var b = Regex.Match(json, "\"bounds\":\\s*\\[\\s*\\[([^\\]]*)\\],\\s*\\[([^\\]]*)\\]");

        var lo = Parse(b.Groups[1].Value);

        var hi = Parse(b.Groups[2].Value);

        var bounds = new Bounds();

        bounds.SetMinMax(new Vector3(-hi[0], lo[2], -hi[1]), new Vector3(-lo[0], hi[2], -lo[1]));

        return (list, bounds);
    }

    private static float[] Numbers(string body, string key)
    {
        return Parse(Regex.Match(body, "\"" + key + "\":\\s*\\[([^\\]]*)\\]").Groups[1].Value);
    }

    private static float Number(string body, string key)
    {
        return float.Parse(Regex.Match(body, "\"" + key + "\":\\s*([-0-9.eE]+)").Groups[1].Value, CultureInfo.InvariantCulture);
    }

    private static float[] Parse(string csv)
    {
        return csv
            .Split(',')
            .Select(s => float.Parse(s.Trim(), CultureInfo.InvariantCulture))
            .ToArray();
    }

    /// <summary>One Blender material: linear base colour, metallic, roughness, linear emission, optional texture.</summary>
    private sealed class MaterialInfo
    {
        public MaterialInfo(string name, Color colour, float metallic, float roughness, Color emission, string? texture)
        {
            Name = name;

            Colour = colour;

            Metallic = metallic;

            Roughness = roughness;

            Emission = emission;

            Texture = texture;
        }

        public string Name { get; }

        public Color Colour { get; }

        public float Metallic { get; }

        public float Roughness { get; }

        public Color Emission { get; }

        public string? Texture { get; }
    }
}
