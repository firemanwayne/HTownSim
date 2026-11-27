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
/// Puts the Spartan rear-mount ladder truck on every ladder. The Blender asset (ArtSource\Ladder07\spartan_rearmount_laddertruck.blend)
/// is exported once per unit number by ArtSource\Ladder07\export_spartan_unity.py (HFD_UNIT_NUMBER=7 and 16) to
/// Assets/HTownSim/Vehicles/SpartanLadder/SpartanLadder07.fbx and SpartanLadder16.fbx, each with its number modelled in.
///
/// Apply imports both FBXs (URP Lit materials from SpartanLadder_materials.json, remapped onto the FBX's material slots; the clear
/// lightbar lens is transparent), then swaps the model on:
///   - the Ladder07 prop in Station 7's ladder bay (Ladder07Visual, which the game moves onto the driven object when the player drives
///     Ladder 07), and
///   - the parked Ladder16 at Station 16 (replacing the Ladder 07 copy that had its "07" cut out and TextMeshPro lettering added).
/// The two models have the old Ladder 07 model's bounds, so the bays, colliders and offsets are unchanged. The game's flash groups name
/// the lightbar materials (see ApparatusLoadout). Apply refuses with unsaved scene changes; it can be run again to re-import after the
/// asset is exported again. Both reopen the scene after saving.
///
/// Run: unity command script run --file AgentScripts/InstallSpartanLadder.cs --entry InstallSpartanLadder.Apply
/// </summary>
public static class InstallSpartanLadder
{
    private const string Folder = "Assets/HTownSim/Vehicles/SpartanLadder/";

    private const string MaterialsJson = Folder + "SpartanLadder_materials.json";

    private const string CityScene = "Assets/Scenes/HoustonResponse.unity";

    private const string VisualName = "Ladder07Visual";

    private const string Ladder07Root = "Ladder07";

    private const string Station16Group = "Station16Apparatus";

    private const string Ladder16Unit = "Ladder16";

    /// <summary>An alpha under this makes a Blender material a transparent one (the lightbar's clear lens is 0.13).</summary>
    private const float OpaqueAlpha = 0.99f;

    public static string Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();

        if (EditorApplication.isPlaying || scene.isDirty)
        {
            throw new InvalidOperationException("Exit Play Mode and save or discard scene changes first.");
        }

        // The city scene holds the ladders; open it when another scene is showing.
        if (scene.path != CityScene)
        {
            scene = EditorSceneManager.OpenScene(CityScene, OpenSceneMode.Single);
        }

        var materials = ReadMaterials();

        foreach (var number in new[] { "07", "16" })
        {
            ConfigureImport(Folder + "SpartanLadder" + number + ".fbx", materials);
        }

        var roots = scene.GetRootGameObjects();

        var report = new List<string>
        {
            Swap(Find(roots, Ladder07Root), VisualName, "07"),
            Swap(Find(roots, Station16Group).Find(Ladder16Unit) ?? throw new InvalidOperationException(Station16Group + " has no " + Ladder16Unit + "."), VisualName, "16"),
        };

        EditorSceneManager.MarkSceneDirty(scene);

        EditorSceneManager.SaveScene(scene);

        AssetDatabase.SaveAssets();

        EditorSceneManager.OpenScene(scene.path, OpenSceneMode.Single);

        return string.Join(" ", report) + " Scene saved.";
    }

    private static Transform Find(GameObject[] roots, string name)
    {
        return roots.FirstOrDefault(root => root.name == name)?.transform ?? throw new InvalidOperationException("Root object " + name + " not found.");
    }

    /// <summary>Replaces what is under <paramref name="parent"/> as the ladder's model with the numbered Spartan model, in the same place.</summary>
    private static string Swap(Transform parent, string visualName, string number)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "SpartanLadder" + number + ".fbx") ?? throw new InvalidOperationException("SpartanLadder" + number + ".fbx is not imported.");

        // The model's place on its root: the existing model's (the ladder prop's visual, or the parked unit's first child).
        var old = parent.Find(visualName) ?? (parent.childCount > 0 ? parent.GetChild(0) : null);

        var position = Vector3.zero;

        var rotation = Quaternion.identity;

        var scale = Vector3.one;

        var index = parent.childCount;

        if (old != null)
        {
            position = old.localPosition;

            rotation = old.localRotation;

            scale = old.localScale;

            index = old.GetSiblingIndex();
        }

        // Everything the old model brought (it, and for Ladder 16 the cut-out number lettering is inside it) goes; the root's own collider stays.
        var previous = parent.Cast<Transform>().Where(child => child == old || child.name.StartsWith("UnitNumber_", StringComparison.Ordinal)).ToList();

        foreach (var child in previous)
        {
            UnityEngine.Object.DestroyImmediate(child.gameObject);
        }

        var visual = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);

        visual.name = visualName;

        visual.transform.SetSiblingIndex(Mathf.Min(index, parent.childCount - 1));

        visual.transform.localPosition = position;

        visual.transform.localRotation = rotation;

        visual.transform.localScale = scale;

        var lod = visual.GetComponent<LODGroup>();

        var lightbars = visual.GetComponentsInChildren<Renderer>(true).Count(renderer => renderer.name.StartsWith("Spartan_Lightbar_", StringComparison.Ordinal));

        return "Ladder " + number + " on " + parent.name + ": " + (lod != null ? lod.GetLODs().Length : 0) + " LOD levels, " + lightbars + " lightbars.";
    }

    /// <summary>Imports one FBX with its LODs and remaps every Blender material name onto a URP Lit material.</summary>
    private static void ConfigureImport(string modelPath, IReadOnlyList<MaterialInfo> materials)
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

        var model = (ModelImporter)AssetImporter.GetAtPath(modelPath);

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
        var colour = info.Colour.gamma;

        colour.a = info.Texture is null ? info.Alpha : 1f;

        material.SetColor("_BaseColor", colour);

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
        else if (info.Alpha < OpaqueAlpha)
        {
            // A see-through lens (the lightbar's clear polycarbonate): alpha blended, not written to depth, so the LEDs under it show.
            material.SetFloat("_Surface", 1f);

            material.SetFloat("_Blend", 0f);

            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);

            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);

            material.SetFloat("_ZWrite", 0f);

            material.SetOverrideTag("RenderType", "Transparent");

            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
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

    /// <summary>Reads the material list written by the Blender export.</summary>
    private static List<MaterialInfo> ReadMaterials()
    {
        var json = File.ReadAllText(MaterialsJson);

        var list = new List<MaterialInfo>();

        // Only the "materials" object: it is followed by "bounds".
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
                Number(body, "alpha"),
                new Color(emission[0], emission[1], emission[2]),
                texture.Success ? texture.Groups[1].Value : null));
        }

        return list;
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

    /// <summary>One Blender material: linear base colour, metallic, roughness, alpha, linear emission, optional texture.</summary>
    private sealed class MaterialInfo
    {
        public MaterialInfo(string name, Color colour, float metallic, float roughness, float alpha, Color emission, string? texture)
        {
            Name = name;

            Colour = colour;

            Metallic = metallic;

            Roughness = roughness;

            Alpha = alpha;

            Emission = emission;

            Texture = texture;
        }

        public string Name { get; }

        public Color Colour { get; }

        public float Metallic { get; }

        public float Roughness { get; }

        public float Alpha { get; }

        public Color Emission { get; }

        public string? Texture { get; }
    }
}
