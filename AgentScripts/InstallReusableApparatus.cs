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
/// Puts the reusable Spartan engine and Frazer ambulance on every engine and ambulance, as InstallSpartanLadder does for the ladders. The
/// Blender assets (Blender\HFD Apparatus\Engines\spartan_reusable_engine.blend and EMS\frazer_reusable_ambulance.blend) are exported once
/// per unit by Blender\HFD Apparatus\export_reusable_unity.py (HFD_UNIT_NUMBER, and HFD_MEDIC_LABEL=1 for Medic 08) to
/// Assets/HTownSim/Vehicles/SpartanEngine/SpartanEngine{07,08,01,16}.fbx and Assets/HTownSim/Vehicles/FrazerAmbulance/FrazerAmbulance{07,16}.fbx
/// and FrazerMedic08.fbx. Each has its number baked into its own paint texture (Engine07_Paint is named per unit), while every other
/// material is shared.
///
/// Apply imports the FBXs (URP Lit materials from each <c>*_materials.json</c>, remapped onto the FBX's material slots), then swaps the model
/// on every engine and ambulance in the city scene, in the same place and under the same name:
///   - the driven object's Engine07CleanVisual (Engine 07) and Ambulance07Visual (Ambulance 07),
///   - the parked Engine 07 at Station 7, Engine 08, Engine 01 and Medic 08 at Station 8, Engine 16 and Ambulance 16 at Station 16.
/// The game keeps finding the units by those names; the models take their numbers from their own paint. Apply refuses with unsaved scene
/// changes and can be run again to re-import after the assets are exported again; it reopens the scene after saving.
///
/// Run: unity command script run --file AgentScripts/InstallReusableApparatus.cs --entry InstallReusableApparatus.Apply
/// </summary>
public static class InstallReusableApparatus
{
    private const string VehicleRoot = "Assets/HTownSim/Vehicles/";

    private const string CityScene = "Assets/Scenes/HoustonResponse.unity";

    /// <summary>An alpha under this makes a Blender material a transparent one (glass, the bar dome).</summary>
    private const float OpaqueAlpha = 0.99f;

    /// <summary>Where each unit's model is in the scene: the object holding it, the model's current name there, and the FBX that replaces it.</summary>
    private static readonly (string Parent, string Model, string Fbx)[] Swaps =
    {
        ("Engine07", "Engine07CleanVisual", "SpartanEngine/SpartanEngine07"),
        ("Engine07", "Ambulance07Visual", "FrazerAmbulance/FrazerAmbulance07"),
        ("Engine07Parked", "Engine07Clean", "SpartanEngine/SpartanEngine07"),
        ("Station8Apparatus/Engine08", "Engine07Clean", "SpartanEngine/SpartanEngine08"),
        ("Station8Apparatus/Engine01", "Engine07Clean", "SpartanEngine/SpartanEngine01"),
        ("Station8Apparatus/Medic08", "Ambulance07", "FrazerAmbulance/FrazerMedic08"),
        ("Station16Apparatus/Engine16", "Engine07Clean", "SpartanEngine/SpartanEngine16"),
        ("Station16Apparatus/Ambulance16", "Ambulance07", "FrazerAmbulance/FrazerAmbulance16"),
    };

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

        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        foreach (var fbx in Swaps.Select(swap => swap.Fbx).Distinct())
        {
            ConfigureImport(fbx);
        }

        var report = new List<string>();

        foreach (var (parent, model, fbx) in Swaps)
        {
            report.Add(Swap(scene, parent, model, fbx));
        }

        EditorSceneManager.MarkSceneDirty(scene);

        EditorSceneManager.SaveScene(scene);

        AssetDatabase.SaveAssets();

        EditorSceneManager.OpenScene(scene.path, OpenSceneMode.Single);

        return string.Join("\n", report) + "\nScene saved.";
    }

    /// <summary>Finds an object by its path from a root object of the scene (inactive objects included).</summary>
    private static Transform FindPath(UnityEngine.SceneManagement.Scene scene, string path)
    {
        var parts = path.Split('/');

        var current = scene.GetRootGameObjects().FirstOrDefault(root => root.name == parts[0])?.transform
            ?? throw new InvalidOperationException("Root object " + parts[0] + " not found.");

        foreach (var part in parts.Skip(1))
        {
            current = current.Find(part) ?? throw new InvalidOperationException(path + ": no " + part + ".");
        }

        return current;
    }

    /// <summary>Replaces one model with the numbered one, in the same place, under the same name and in the same active state.</summary>
    private static string Swap(UnityEngine.SceneManagement.Scene scene, string parentPath, string modelName, string fbx)
    {
        var parent = FindPath(scene, parentPath);

        var old = parent.Find(modelName) ?? throw new InvalidOperationException(parentPath + " has no " + modelName + ".");

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(VehicleRoot + fbx + ".fbx") ?? throw new InvalidOperationException(fbx + ".fbx is not imported.");

        var position = old.localPosition;

        var rotation = old.localRotation;

        var scale = old.localScale;

        var index = old.GetSiblingIndex();

        var active = old.gameObject.activeSelf;

        var before = LocalBounds(old, parent);

        UnityEngine.Object.DestroyImmediate(old.gameObject);

        var visual = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);

        visual.name = modelName;

        visual.transform.SetSiblingIndex(Mathf.Min(index, parent.childCount - 1));

        visual.transform.localPosition = position;

        visual.transform.localRotation = rotation;

        visual.transform.localScale = scale;

        visual.SetActive(active);

        var after = LocalBounds(visual.transform, parent);

        var drift = Vector3.Distance(before.center, after.center) + Vector3.Distance(before.size, after.size);

        return parentPath + "/" + modelName + " <- " + fbx + ": size " + Format(after.size) + " (was " + Format(before.size) + "), centre " + Format(after.center)
            + " (was " + Format(before.center) + ")" + (drift > 0.5f ? "  CHECK: the footprint changed" : "");
    }

    private static string Format(Vector3 v)
    {
        return "(" + v.x.ToString("0.00", CultureInfo.InvariantCulture) + ", " + v.y.ToString("0.00", CultureInfo.InvariantCulture) + ", " + v.z.ToString("0.00", CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>The bounds of a model's renderers in its parent's space, whatever the model's active state.</summary>
    private static Bounds LocalBounds(Transform model, Transform parent)
    {
        var bounds = new Bounds();

        var any = false;

        foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null)
            {
                continue;
            }

            var box = filter.sharedMesh.bounds;

            var toParent = parent.worldToLocalMatrix * filter.transform.localToWorldMatrix;

            foreach (var x in new[] { box.min.x, box.max.x })
            {
                foreach (var y in new[] { box.min.y, box.max.y })
                {
                    foreach (var z in new[] { box.min.z, box.max.z })
                    {
                        var corner = toParent.MultiplyPoint3x4(new Vector3(x, y, z));

                        if (!any)
                        {
                            bounds = new Bounds(corner, Vector3.zero);

                            any = true;
                        }
                        else
                        {
                            bounds.Encapsulate(corner);
                        }
                    }
                }
            }
        }

        return bounds;
    }

    /// <summary>Imports one FBX and remaps every Blender material name onto a URP Lit material.</summary>
    private static void ConfigureImport(string fbx)
    {
        var modelPath = VehicleRoot + fbx + ".fbx";

        var folder = Path.GetDirectoryName(modelPath)!.Replace('\\', '/') + "/";

        var materials = ReadMaterials(folder + Path.GetFileNameWithoutExtension(modelPath) + "_materials.json");

        foreach (var info in materials.Where(m => m.Texture is not null))
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(folder + info.Texture);

            importer.sRGBTexture = true;

            importer.wrapMode = TextureWrapMode.Clamp;

            if (info.Cutout)
            {
                importer.alphaSource = TextureImporterAlphaSource.FromInput;

                importer.alphaIsTransparency = true;

                importer.maxTextureSize = 1024;
            }
            else
            {
                // A baked paint: opaque, and the biggest texture of the model.
                importer.alphaSource = TextureImporterAlphaSource.None;

                importer.alphaIsTransparency = false;

                importer.maxTextureSize = 2048;
            }

            importer.SaveAndReimport();
        }

        var model = (ModelImporter)AssetImporter.GetAtPath(modelPath);

        model.importCameras = false;

        model.importLights = false;

        model.importBlendShapes = false;

        model.animationType = ModelImporterAnimationType.None;

        model.importAnimation = false;

        model.importNormals = ModelImporterNormals.Import;

        model.bakeAxisConversion = true;

        model.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;

        model.materialLocation = ModelImporterMaterialLocation.InPrefab;

        foreach (var info in materials)
        {
            model.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), info.Name), BuildMaterial(folder, info));
        }

        model.SaveAndReimport();
    }

    private static Material BuildMaterial(string folder, MaterialInfo info)
    {
        if (!AssetDatabase.IsValidFolder(folder + "Materials"))
        {
            AssetDatabase.CreateFolder(folder.TrimEnd('/'), "Materials");
        }

        var path = folder + "Materials/" + Regex.Replace(info.Name, "[^A-Za-z0-9_]+", "_") + ".mat";

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
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(folder + info.Texture));

            if (info.Cutout)
            {
                material.SetFloat("_AlphaClip", 1f);

                material.SetFloat("_Cutoff", 0.5f);

                material.EnableKeyword("_ALPHATEST_ON");

                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            }
        }
        else if (info.Alpha < OpaqueAlpha)
        {
            // A see-through material (glass, the light bar dome): alpha blended and not written to depth.
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
    private static List<MaterialInfo> ReadMaterials(string path)
    {
        var json = File.ReadAllText(path);

        var list = new List<MaterialInfo>();

        // Only the "materials" object: it is followed by "unit_number".
        var start = json.IndexOf("\"materials\"", StringComparison.Ordinal);

        var block = json.Substring(start, json.IndexOf("\"unit_number\"", StringComparison.Ordinal) - start);

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
                texture.Success ? texture.Groups[1].Value : null,
                body.Contains("\"cutout\": true")));
        }

        return list;
    }

    private static float[] Numbers(string body, string key)
    {
        return Regex.Match(body, "\"" + key + "\":\\s*\\[([^\\]]*)\\]").Groups[1].Value
            .Split(',')
            .Select(s => float.Parse(s.Trim(), CultureInfo.InvariantCulture))
            .ToArray();
    }

    private static float Number(string body, string key)
    {
        return float.Parse(Regex.Match(body, "\"" + key + "\":\\s*([-0-9.eE]+)").Groups[1].Value, CultureInfo.InvariantCulture);
    }

    /// <summary>One Blender material: linear base colour, metallic, roughness, alpha, linear emission, optional texture (and whether it is a cut-out one).</summary>
    private sealed class MaterialInfo
    {
        public MaterialInfo(string name, Color colour, float metallic, float roughness, float alpha, Color emission, string? texture, bool cutout)
        {
            Name = name;

            Colour = colour;

            Metallic = metallic;

            Roughness = roughness;

            Alpha = alpha;

            Emission = emission;

            Texture = texture;

            Cutout = cutout;
        }

        public string Name { get; }

        public Color Colour { get; }

        public float Metallic { get; }

        public float Roughness { get; }

        public float Alpha { get; }

        public Color Emission { get; }

        public string? Texture { get; }

        public bool Cutout { get; }
    }
}
