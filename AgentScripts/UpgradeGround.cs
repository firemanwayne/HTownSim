#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Replaces the flat lot colours with the Blender ground pass (Blender\HTown, scripts/build_ground.py ->
/// data/upgraded_ground.txt): real lot surfaces from City of Houston land use (lawns, parking asphalt, vacant ground,
/// parks), driveways and front walks, mulch beds with shrubs, wood privacy fences and painted parking stalls. Surfaces
/// use Poly Haven CC0 scans (Textures/Ground) through the project's WorldTriplanar shader; sidewalks switch to the
/// jointed paver scan.
///
/// The old lot slabs (Slab_LotGrass, Slab_LotAsphalt, Slab_ParkGrass) are hidden, never deleted; the sidewalk slab keeps
/// its collider. New parking renderers are added to EnvironmentWeather's road list so lots get wet in the rain.
/// Apply can be run again; Revert restores everything. Both refuse to run with unsaved scene changes.
/// </summary>
public static class UpgradeGround
{
    private static readonly string DataPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(UnityEngine.Application.dataPath, "..", "Blender", "HTown", "data", "upgraded_ground.txt"));

    private const string MeshAssetPath = "Assets/HTownSim/Generated/UpgradedGround.asset";

    private const string MaterialFolder = "Assets/HTownSim/Generated/Ground";

    private const string TextureFolder = "Assets/HTownSim/Textures/Ground";

    private const string GeneratedFolder = "Assets/HTownSim/Generated";

    private const string RootName = "GroundUpgrade";

    private const string SidewalkMaterialName = "GroundSidewalk";

    private const string OriginalSidewalkMaterial = "Concrete";

    /// <summary>Old lot slabs replaced by the new ground.</summary>
    private static readonly string[] HiddenSlabs = { "Slab_LotGrass", "Slab_LotAsphalt", "Slab_ParkGrass" };

    /// <summary>World-projected ground materials: name, detail texture, tint, tile size in metres (the scan's real size),
    /// normal strength and detail strength (paving is kept subtle so the panels do not read as a checkerboard).</summary>
    private static readonly (string Name, string Texture, string Tint, float Tile, float Bump, float Detail)[] Surfaces =
    {
        ("GroundLawn", "GroundLawn", "7A9560", 2.0f, 0.7f, 1f),
        ("GroundPark", "GroundLawn", "6A8F58", 2.0f, 0.7f, 1f),
        ("GroundVacant", "GroundVacant", "9A9068", 2.51f, 0.8f, 1f),
        ("GroundParking", "GroundParking", "59626A", 4.04f, 0.6f, 1f),
        ("GroundPaving", "GroundPaving", "B3AFA4", 2.1f, 0.5f, 0.55f),
        ("GroundMulch", "GroundMulch", "6A4A34", 2.0f, 0.9f, 1f),
        (SidewalkMaterialName, "GroundPaving", "AAA697", 2.1f, 0.5f, 0.55f),
    };

    /// <summary>One material's triangle list.</summary>
    private sealed class Sub
    {
        public string mat = "";

        public float[] v = Array.Empty<float>();

        public float[] uv = Array.Empty<float>();
    }

    /// <summary>One ground tile.</summary>
    private sealed class Entry
    {
        public string key = "";

        public string name = "";

        public List<Sub> subs = new List<Sub>();
    }

    /// <summary>Builds the ground and returns a summary.</summary>
    public static string Apply()
    {
        var blocked = Blocked();

        if (blocked is not null)
        {
            return blocked;
        }

        var midtown = GameObject.Find("MidtownReal").transform;

        Restore(midtown);

        var materials = LoadMaterials();

        AssetDatabase.DeleteAsset(MeshAssetPath);

        var container = new Mesh { name = "UpgradedGround" };

        AssetDatabase.CreateAsset(container, MeshAssetPath);

        var root = new GameObject(RootName).transform;

        root.SetParent(midtown, false);

        var parking = new List<Renderer>();

        var objects = 0;

        foreach (var entry in ReadEntries())
        {
            var tile = new GameObject(entry.name).transform;

            tile.SetParent(root, false);

            foreach (var sub in entry.subs)
            {
                var mesh = BuildMesh(entry.name + "_" + sub.mat, sub);

                AssetDatabase.AddObjectToAsset(mesh, container);

                var go = new GameObject(sub.mat, typeof(MeshFilter), typeof(MeshRenderer));

                go.transform.SetParent(tile, false);

                go.GetComponent<MeshFilter>().sharedMesh = mesh;

                var renderer = go.GetComponent<MeshRenderer>();

                renderer.sharedMaterial = materials.TryGetValue(sub.mat, out var m) ? m : materials["GroundLawn"];

                // Flat ground does not need to cast shadows; fences and shrubs do.
                renderer.shadowCastingMode = sub.mat is "Fence" or "Shrub" ? ShadowCastingMode.On : ShadowCastingMode.Off;

                // Not batching-static: the tiles are already few, large meshes, and combining ~0.7M ground vertices with
                // the buildings at Play Mode start is the step that coincided with an Editor crash.
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.OccludeeStatic);

                if (sub.mat == "GroundParking")
                {
                    parking.Add(renderer);
                }

                objects++;
            }
        }

        SetSlabs(midtown, false, materials[SidewalkMaterialName]);

        SetWeatherRoads(parking, true);

        AssetDatabase.SaveAssets();

        // Read before Save(): it reloads the scene, which destroys these in-memory objects.
        var tiles = root.childCount;

        var parkingCount = parking.Count;

        Save();

        return $"Ground built: {tiles} tiles, {objects} renderers ({parkingCount} parking renderers added to the weather). Scene saved.";
    }

    /// <summary>Removes the new ground and brings the old lot slabs and sidewalk material back.</summary>
    public static string Revert()
    {
        var blocked = Blocked();

        if (blocked is not null)
        {
            return blocked;
        }

        Restore(GameObject.Find("MidtownReal").transform);

        AssetDatabase.DeleteAsset(MeshAssetPath);

        Save();

        return "Ground restored. Scene saved.";
    }

    /// <summary>Undoes a previous Apply (safe when there is none).</summary>
    private static void Restore(Transform midtown)
    {
        var old = midtown.Find(RootName);

        if (old != null)
        {
            UnityEngine.Object.DestroyImmediate(old.gameObject);
        }

        SetSlabs(midtown, true, AssetDatabase.LoadAssetAtPath<Material>($"{GeneratedFolder}/{OriginalSidewalkMaterial}.mat"));

        SetWeatherRoads(new List<Renderer>(), false);
    }

    /// <summary>Shows or hides the old lot slabs and sets the sidewalk slab's material.</summary>
    private static void SetSlabs(Transform midtown, bool oldVisible, Material sidewalk)
    {
        foreach (var name in new[] { "ParcelSlabs", "ParcelSlabs_Exp" })
        {
            var slabs = midtown.Find(name);

            if (slabs == null)
            {
                continue;
            }

            foreach (Transform slab in slabs)
            {
                if (Array.IndexOf(HiddenSlabs, slab.name) >= 0)
                {
                    slab.gameObject.SetActive(oldVisible);
                }
                else if (slab.name == "Slab_Concrete")
                {
                    slab.GetComponent<MeshRenderer>().sharedMaterial = sidewalk;
                }
            }
        }
    }

    /// <summary>Adds (or, with add false, removes missing and ground) renderers in EnvironmentWeather's road list.</summary>
    private static void SetWeatherRoads(List<Renderer> parking, bool add)
    {
        var weather = GameObject.Find("EnvironmentWeather")?.GetComponent("EnvironmentWeather");

        if (weather == null)
        {
            return;
        }

        var data = new SerializedObject(weather);

        var array = data.FindProperty("_roads");

        var list = new List<UnityEngine.Object>();

        for (var i = 0; i < array.arraySize; i++)
        {
            var r = array.GetArrayElementAtIndex(i).objectReferenceValue;

            if (r != null && !(r is Renderer renderer && renderer.transform.parent != null && renderer.transform.parent.parent != null && renderer.transform.parent.parent.name == RootName))
            {
                list.Add(r);
            }
        }

        if (add)
        {
            list.AddRange(parking);
        }

        array.arraySize = list.Count;

        for (var i = 0; i < list.Count; i++)
        {
            array.GetArrayElementAtIndex(i).objectReferenceValue = list[i];
        }

        data.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>Null when it is safe to edit and save the scene, otherwise the reason not to.</summary>
    private static string? Blocked()
    {
        if (EditorApplication.isPlaying)
        {
            return "Exit Play Mode first.";
        }

        var scene = EditorSceneManager.GetActiveScene();

        return scene.isDirty ? $"Scene {scene.name} has unsaved changes; save or discard them first. Nothing was changed." : null;
    }

    /// <summary>
    /// Saves, then reopens the scene from disk. Apply deletes and recreates the mesh asset; without a reload the GPU
    /// Resident Drawer keeps draw commands for the deleted meshes ("invalid Batch, Mesh, or Material ID ... MeshID
    /// &lt;null&gt;"), which coincided with an Editor crash on the next Play Mode start.
    /// </summary>
    private static void Save()
    {
        var scene = EditorSceneManager.GetActiveScene();

        EditorSceneManager.MarkSceneDirty(scene);

        EditorSceneManager.SaveScene(scene);

        EditorSceneManager.OpenScene(scene.path, OpenSceneMode.Single);
    }

    /// <summary>Ground and sidewalk materials (created or updated), plus the existing fence wood and stall paint.</summary>
    private static Dictionary<string, Material> LoadMaterials()
    {
        if (!AssetDatabase.IsValidFolder(MaterialFolder))
        {
            AssetDatabase.CreateFolder(GeneratedFolder, "Ground");
        }

        var triplanar = Shader.Find("HTownSim/WorldTriplanar");

        var map = new Dictionary<string, Material>();

        foreach (var surface in Surfaces)
        {
            ConfigureTexture($"{TextureFolder}/{surface.Texture}.png", false);

            ConfigureTexture($"{TextureFolder}/{surface.Texture}_N.png", true);

            var material = GetOrCreate(surface.Name, triplanar);

            material.SetColor("_BaseColor", Hex(surface.Tint));

            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureFolder}/{surface.Texture}.png"));

            material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureFolder}/{surface.Texture}_N.png"));

            material.SetFloat("_TileSize", surface.Tile);

            material.SetFloat("_BumpScale", surface.Bump);

            material.SetFloat("_DetailStrength", surface.Detail);

            material.SetFloat("_MacroStrength", 0.35f);

            material.SetFloat("_Smoothness", surface.Name == "GroundParking" ? 0.2f : 0.1f);

            material.SetFloat("_Metallic", 0f);

            EditorUtility.SetDirty(material);

            map[surface.Name] = material;
        }

        var shrub = GetOrCreate("Shrub", Shader.Find("Universal Render Pipeline/Lit"));

        shrub.SetColor("_BaseColor", Hex("41603C"));

        shrub.SetFloat("_Smoothness", 0.08f);

        EditorUtility.SetDirty(shrub);

        map["Shrub"] = shrub;

        map["Fence"] = AssetDatabase.LoadAssetAtPath<Material>($"{GeneratedFolder}/UtilityWood.mat");

        map["PaintWhite"] = AssetDatabase.LoadAssetAtPath<Material>($"{GeneratedFolder}/PaintWhite.mat");

        return map;
    }

    private static Material GetOrCreate(string name, Shader shader)
    {
        var path = $"{MaterialFolder}/{name}.mat";

        var material = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (material == null)
        {
            material = new Material(shader) { name = name };

            AssetDatabase.CreateAsset(material, path);
        }

        material.shader = shader;

        return material;
    }

    private static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out var color);

        return color;
    }

    private static void ConfigureTexture(string path, bool normal)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

        var importer = (TextureImporter)AssetImporter.GetAtPath(path);

        importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;

        importer.sRGBTexture = !normal;

        importer.mipmapEnabled = true;

        importer.wrapMode = TextureWrapMode.Repeat;

        importer.filterMode = FilterMode.Trilinear;

        importer.anisoLevel = 8;

        importer.textureCompression = TextureImporterCompression.CompressedHQ;

        importer.SaveAndReimport();
    }

    /// <summary>Reads the Blender text file: "B key name" starts a tile, "M material positions uvs" adds a submesh.</summary>
    private static List<Entry> ReadEntries()
    {
        var entries = new List<Entry>();

        foreach (var line in File.ReadLines(DataPath))
        {
            var fields = line.Split('\t');

            if (fields[0] == "B")
            {
                entries.Add(new Entry { key = fields[1], name = fields[2] });
            }
            else if (fields[0] == "M" && entries.Count > 0)
            {
                entries[entries.Count - 1].subs.Add(new Sub { mat = fields[1], v = Numbers(fields[2]), uv = Numbers(fields[3]) });
            }
        }

        return entries;
    }

    private static float[] Numbers(string text)
    {
        var parts = text.Split(',');

        var values = new float[parts.Length];

        for (var i = 0; i < parts.Length; i++)
        {
            values[i] = float.Parse(parts[i], CultureInfo.InvariantCulture);
        }

        return values;
    }

    /// <summary>One mesh for one material of one tile.</summary>
    private static Mesh BuildMesh(string name, Sub sub)
    {
        var count = sub.v.Length / 3;

        var vertices = new Vector3[count];

        var uvs = new Vector2[count];

        var indices = new int[count];

        for (var i = 0; i < count; i++)
        {
            vertices[i] = new Vector3(sub.v[i * 3], sub.v[i * 3 + 1], sub.v[i * 3 + 2]);

            uvs[i] = new Vector2(sub.uv[i * 2], sub.uv[i * 2 + 1]);

            indices[i] = i;
        }

        var mesh = new Mesh { name = name, indexFormat = count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };

        mesh.SetVertices(vertices);

        mesh.SetUVs(0, uvs);

        mesh.SetTriangles(indices, 0);

        mesh.RecalculateNormals();

        mesh.RecalculateBounds();

        return mesh;
    }
}
