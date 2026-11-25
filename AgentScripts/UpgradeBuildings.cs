#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Replaces the box visuals of the procedural shops, apartments and towers with the Blender rebuild from
/// Blender\HTown (scripts/build_buildings.py -> data/upgraded_buildings.json): real 2018 LiDAR outlines at
/// the same heights and wall materials, windows on every street wall, storefront bands, curtain-wall towers,
/// parapets that follow the outline and rooftop units inside the real roof.
///
/// Gameplay objects are untouched: incident sites, signs, awnings, tower crowns and antennas stay. The old Body and
/// Podium boxes keep their GameObjects but lose their renderer and collider; each building gets an "Upgraded" child
/// with the new mesh (collision stays on the old box). Houses are not changed.
///
/// Apply rebuilds from the JSON (safe to run again); Revert restores the boxes. Both refuse to run with unsaved
/// scene changes, then save the scene.
/// </summary>
public static class UpgradeBuildings
{
    /// <summary>Blender output.</summary>
    private static readonly string DataPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(UnityEngine.Application.dataPath, "..", "Blender", "HTown", "data", "upgraded_buildings.txt"));

    /// <summary>Holds every generated mesh as sub-assets.</summary>
    private const string MeshAssetPath = "Assets/HTownSim/Generated/UpgradedBuildings.asset";

    /// <summary>Facade materials are created here.</summary>
    private const string FacadeFolder = "Assets/HTownSim/Generated/Facades";

    /// <summary>Facade textures copied from the Blender project.</summary>
    private const string TextureFolder = "Assets/HTownSim/Textures/Facades";

    /// <summary>Existing wall, roof and trim materials.</summary>
    private const string GeneratedFolder = "Assets/HTownSim/Generated";

    /// <summary>Name of the child that carries the new mesh.</summary>
    private const string UpgradedName = "Upgraded";

    /// <summary>Old box parts that the new mesh replaces (disabled, never deleted).</summary>
    private static readonly string[] ReplacedParts =
    {
        "Windows", "Balcony", "Storefront", "Parapet", "RoofSurface", "Floor", "Fin", "RooftopUnit", "Penthouse",
        "WaterTank", "RoofPlant", "Chimney",
    };

    /// <summary>
    /// Combined pitched-roof meshes (house roofs and the metal gables of some shops). They cannot be hidden per
    /// building, and every building they cover is rebuilt with its own roof, so the whole sections are switched off.
    /// </summary>
    private static readonly string[] RoofSections = { "Roofs", "Roofs_Exp" };

    /// <summary>Old boxes whose GameObject stays (other scripts may look them up) but whose visuals and collider go.</summary>
    private static readonly string[] Shells = { "Body", "Podium" };

    /// <summary>Facade materials: texture file, alpha-clipped overlay or opaque, smoothness.</summary>
    private static readonly (string Name, string Texture, bool Cutout, float Smoothness)[] Facades =
    {
        ("FacadeRes", "facade_res.png", true, 0.55f),
        ("FacadeShop", "facade_shop.png", true, 0.55f),
        ("Storefront", "storefront.png", true, 0.6f),
        ("TowerWindows", "tower_windows.png", true, 0.55f),
        ("CurtainWall", "curtain_wall.png", false, 0.75f),
        ("HouseFront", "house_front.png", true, 0.5f),
        ("HouseSide", "house_side.png", true, 0.5f),
    };

    /// <summary>HDR strength of the warm glow in lit window panes (the facade textures' _emit maps).</summary>
    private const float WindowGlow = 1.6f;

    /// <summary>
    /// Wall finishes beyond the five BuildRealMidtown materials, as tinted copies of an existing WorldTriplanar
    /// material (build_buildings.py picks one per building). Siding uses the Poly Haven lap-siding scan.
    /// </summary>
    private static readonly (string Name, string CloneOf, string Tint, bool Siding)[] WallVariants =
    {
        ("BrickDark", "Brick", "6B4436", false),
        ("BrickBuff", "Brick", "B39A78", false),
        ("StuccoTerracotta", "Stucco", "C08A68", false),
        ("StuccoWhite", "Stucco", "E2DCCD", false),
        ("SidingWhite", "Stucco", "E8E4DA", true),
        ("SidingCream", "Stucco", "E3D6B6", true),
        ("SidingYellow", "Stucco", "E2CF8E", true),
        ("SidingBlue", "Stucco", "9FB2BE", true),
        ("SidingSage", "Stucco", "A8B59A", true),
        ("SidingGrey", "Stucco", "A9A9A3", true),
    };

    /// <summary>Plain URP Lit parts: balcony railings and apartment entrance doors.</summary>
    private static readonly (string Name, string Hex, float Smoothness)[] Plain =
    {
        ("Railing", "2E3438", 0.3f),
        ("DoorGlass", "22394A", 0.8f),
    };

    /// <summary>One material's triangle list: positions (x, y, z) and UVs (u, v), three vertices per triangle.</summary>
    private sealed class Sub
    {
        public string mat = "";

        public float[] v = Array.Empty<float>();

        public float[] uv = Array.Empty<float>();
    }

    /// <summary>One building: its section/index key, section name and per-material triangles.</summary>
    private sealed class Entry
    {
        public string key = "";

        public string name = "";

        public List<Sub> subs = new List<Sub>();
    }

    /// <summary>Reads the Blender text file: "B key name" starts a building, "M material positions uvs" adds a submesh.</summary>
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

    /// <summary>Comma-separated invariant floats.</summary>
    private static float[] Numbers(string text)
    {
        var parts = text.Split(',');

        var values = new float[parts.Length];

        for (var i = 0; i < parts.Length; i++)
        {
            values[i] = float.Parse(parts[i], System.Globalization.CultureInfo.InvariantCulture);
        }

        return values;
    }

    /// <summary>Builds the upgraded buildings and returns a summary.</summary>
    public static string Apply()
    {
        var blocked = Blocked();

        if (blocked is not null)
        {
            return blocked;
        }

        var root = GameObject.Find("MidtownReal").transform;

        RestoreAll(root);

        var entries = ReadEntries();

        var materials = LoadMaterials();

        AssetDatabase.DeleteAsset(MeshAssetPath);

        var container = new Mesh { name = "UpgradedBuildings" };

        AssetDatabase.CreateAsset(container, MeshAssetPath);

        var built = 0;

        var missing = new List<string>();

        foreach (var entry in entries)
        {
            var building = FindBuilding(root, entry);

            if (building is null)
            {
                missing.Add(entry.name);

                continue;
            }

            var mesh = BuildMesh(entry);

            AssetDatabase.AddObjectToAsset(mesh, container);

            var go = new GameObject(UpgradedName, typeof(MeshFilter), typeof(MeshRenderer));

            go.transform.SetParent(building, false);

            go.transform.position = Vector3.zero;

            go.transform.rotation = Quaternion.identity;

            go.transform.localScale = Vector3.one;

            go.layer = building.Find("Body")?.gameObject.layer ?? 0;

            go.GetComponent<MeshFilter>().sharedMesh = mesh;

            var slots = new Material[entry.subs.Count];

            for (var i = 0; i < slots.Length; i++)
            {
                slots[i] = materials.TryGetValue(entry.subs[i].mat, out var m) ? m : materials["Concrete"];
            }

            go.GetComponent<MeshRenderer>().sharedMaterials = slots;

            // No MeshCollider: the old shell keeps its BoxCollider (SetShell only hides its renderer), which is cheaper and fits the footprint.

            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.OccluderStatic);

            HideOld(building);

            built++;
        }

        SetRoofSections(root, false);

        AssetDatabase.SaveAssets();

        Save();

        var note = missing.Count == 0 ? "" : $" {missing.Count} not found in the scene (first: {missing[0]}).";

        return $"Upgraded {built} of {entries.Count} buildings.{note} Scene saved.";
    }

    /// <summary>Removes the upgrade and brings the original boxes back.</summary>
    public static string Revert()
    {
        var blocked = Blocked();

        if (blocked is not null)
        {
            return blocked;
        }

        var root = GameObject.Find("MidtownReal").transform;

        var restored = RestoreAll(root);

        SetRoofSections(root, true);

        AssetDatabase.DeleteAsset(MeshAssetPath);

        Save();

        return $"Restored {restored} buildings. Scene saved.";
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
    /// Saves, then reopens the scene from disk so the GPU Resident Drawer drops draw commands for the meshes this run
    /// deleted and recreated (stale ones log "invalid Batch, Mesh, or Material ID" and coincided with an Editor crash).
    /// </summary>
    private static void Save()
    {
        var scene = EditorSceneManager.GetActiveScene();

        EditorSceneManager.MarkSceneDirty(scene);

        EditorSceneManager.SaveScene(scene);

        EditorSceneManager.OpenScene(scene.path, OpenSceneMode.Single);
    }

    /// <summary>The building section for an entry: by section/index key, falling back to a name search.</summary>
    private static Transform? FindBuilding(Transform root, Entry entry)
    {
        var parts = entry.key.Split('/');

        var section = root.Find(parts[0]);

        if (section == null)
        {
            return null;
        }

        var index = int.Parse(parts[1]);

        if (index < section.childCount && section.GetChild(index).name == entry.name)
        {
            return section.GetChild(index);
        }

        for (var i = 0; i < section.childCount; i++)
        {
            if (section.GetChild(i).name == entry.name)
            {
                return section.GetChild(i);
            }
        }

        return null;
    }

    /// <summary>One mesh per building, one submesh per material.</summary>
    private static Mesh BuildMesh(Entry entry)
    {
        var vertices = new List<Vector3>();

        var uvs = new List<Vector2>();

        var triangles = new List<int[]>();

        foreach (var sub in entry.subs)
        {
            var start = vertices.Count;

            var count = sub.v.Length / 3;

            for (var i = 0; i < count; i++)
            {
                vertices.Add(new Vector3(sub.v[i * 3], sub.v[i * 3 + 1], sub.v[i * 3 + 2]));

                uvs.Add(new Vector2(sub.uv[i * 2], sub.uv[i * 2 + 1]));
            }

            var indices = new int[count];

            for (var i = 0; i < count; i++)
            {
                indices[i] = start + i;
            }

            triangles.Add(indices);
        }

        var mesh = new Mesh { name = entry.name, indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };

        mesh.SetVertices(vertices);

        mesh.SetUVs(0, uvs);

        mesh.subMeshCount = triangles.Count;

        for (var i = 0; i < triangles.Count; i++)
        {
            mesh.SetTriangles(triangles[i], i);
        }

        mesh.RecalculateNormals();

        mesh.RecalculateTangents();

        mesh.RecalculateBounds();

        return mesh;
    }

    /// <summary>Disables the old box visuals and colliders of a building.</summary>
    private static void HideOld(Transform building)
    {
        foreach (Transform child in building)
        {
            if (Array.IndexOf(Shells, child.name) >= 0)
            {
                SetShell(child, false);
            }
            else if (Array.IndexOf(ReplacedParts, child.name) >= 0)
            {
                child.gameObject.SetActive(false);
            }
        }
    }

    /// <summary>Removes every Upgraded child and re-enables the old parts. Returns how many buildings were restored.</summary>
    private static int RestoreAll(Transform root)
    {
        var restored = 0;

        foreach (var sectionName in new[] { "Buildings", "Buildings_Exp" })
        {
            var section = root.Find(sectionName);

            if (section == null)
            {
                continue;
            }

            foreach (Transform building in section)
            {
                var upgraded = building.Find(UpgradedName);

                if (upgraded == null)
                {
                    continue;
                }

                UnityEngine.Object.DestroyImmediate(upgraded.gameObject);

                foreach (Transform child in building)
                {
                    if (Array.IndexOf(Shells, child.name) >= 0)
                    {
                        SetShell(child, true);
                    }
                    else if (Array.IndexOf(ReplacedParts, child.name) >= 0)
                    {
                        child.gameObject.SetActive(true);
                    }
                }

                restored++;
            }
        }

        return restored;
    }

    /// <summary>Shows or hides the combined pitched-roof sections.</summary>
    private static void SetRoofSections(Transform root, bool on)
    {
        foreach (var name in RoofSections)
        {
            root.Find(name)?.gameObject.SetActive(on);
        }
    }

    /// <summary>Turns a box's renderer and collider on or off.</summary>
    private static void SetShell(Transform shell, bool on)
    {
        var renderer = shell.GetComponent<Renderer>();

        if (renderer != null)
        {
            renderer.enabled = on;
        }

        // The shell's primitive BoxCollider stays on either way: it is the building's collision volume under the upgraded mesh.
        var collider = shell.GetComponent<Collider>();

        if (collider != null)
        {
            collider.enabled = true;
        }
    }

    /// <summary>Existing wall/roof/trim materials by name, plus the facade materials (created or updated).</summary>
    private static Dictionary<string, Material> LoadMaterials()
    {
        var map = new Dictionary<string, Material>();

        foreach (var name in new[] { "Brick", "Stucco", "SageStucco", "Limestone", "Concrete", "RoofTar", "RoofWhite", "Steel", "RoofShingle", "ShingleBrown", "ShingleBlack", "ShingleTan" })
        {
            map[name] = AssetDatabase.LoadAssetAtPath<Material>($"{GeneratedFolder}/{name}.mat");
        }

        if (!AssetDatabase.IsValidFolder(FacadeFolder))
        {
            AssetDatabase.CreateFolder(GeneratedFolder, "Facades");
        }

        var lit = Shader.Find("Universal Render Pipeline/Lit");

        foreach (var facade in Facades)
        {
            var texturePath = $"{TextureFolder}/{facade.Texture}";

            ConfigureTexture(texturePath, facade.Cutout);

            var path = $"{FacadeFolder}/{facade.Name}.mat";

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material == null)
            {
                material = new Material(lit) { name = facade.Name };

                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = lit;

            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));

            material.SetColor("_BaseColor", Color.white);

            material.SetFloat("_Smoothness", facade.Smoothness);

            material.SetFloat("_AlphaClip", facade.Cutout ? 1f : 0f);

            material.SetFloat("_Cutoff", 0.5f);

            if (facade.Cutout)
            {
                material.EnableKeyword("_ALPHATEST_ON");

                material.renderQueue = (int)RenderQueue.AlphaTest;
            }
            else
            {
                material.DisableKeyword("_ALPHATEST_ON");

                material.renderQueue = (int)RenderQueue.Geometry;
            }

            // Lit panes glow warm (dusk mood); the _emit map is black everywhere else.
            var emitPath = texturePath.Replace(".png", "_emit.png");

            ConfigureTexture(emitPath, false);

            material.SetTexture("_EmissionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(emitPath));

            material.SetColor("_EmissionColor", Color.white * WindowGlow);

            material.EnableKeyword("_EMISSION");

            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;

            EditorUtility.SetDirty(material);

            map[facade.Name] = material;
        }

        foreach (var variant in WallVariants)
        {
            var source = map[variant.CloneOf];

            var path = $"{FacadeFolder}/{variant.Name}.mat";

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material == null)
            {
                material = new Material(source) { name = variant.Name };

                AssetDatabase.CreateAsset(material, path);
            }

            material.CopyPropertiesFromMaterial(source);

            ColorUtility.TryParseHtmlString("#" + variant.Tint, out var tint);

            material.SetColor("_BaseColor", tint);

            if (variant.Siding)
            {
                ConfigureTexture(SidingTexture, false);

                ConfigureNormal(SidingNormal);

                material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(SidingTexture));

                material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(SidingNormal));

                // The scan covers 1 m: boards about 0.17 m apart, like real lap siding.
                material.SetFloat("_TileSize", 1f);

                material.SetFloat("_BumpScale", 0.8f);

                material.SetFloat("_DetailStrength", 1f);
            }

            EditorUtility.SetDirty(material);

            map[variant.Name] = material;
        }

        foreach (var plain in Plain)
        {
            var path = $"{FacadeFolder}/{plain.Name}.mat";

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material == null)
            {
                material = new Material(lit) { name = plain.Name };

                AssetDatabase.CreateAsset(material, path);
            }

            ColorUtility.TryParseHtmlString("#" + plain.Hex, out var color);

            material.SetColor("_BaseColor", color);

            material.SetFloat("_Smoothness", plain.Smoothness);

            EditorUtility.SetDirty(material);

            map[plain.Name] = material;
        }

        return map;
    }

    /// <summary>Lap-siding detail and normal maps (Poly Haven brown_planks_05, converted by convert_ground_textures.py).</summary>
    private const string SidingTexture = "Assets/HTownSim/Textures/Ground/Siding.png";

    private const string SidingNormal = "Assets/HTownSim/Textures/Ground/Siding_N.png";

    /// <summary>Imports a normal map as one.</summary>
    private static void ConfigureNormal(string path)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

        var importer = (TextureImporter)AssetImporter.GetAtPath(path);

        importer.textureType = TextureImporterType.NormalMap;

        importer.mipmapEnabled = true;

        importer.wrapMode = TextureWrapMode.Repeat;

        importer.SaveAndReimport();
    }

    /// <summary>Repeat-wrapped, mipmapped facade texture; cutouts keep their alpha coverage in the smaller mips.</summary>
    private static void ConfigureTexture(string path, bool cutout)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

        var importer = (TextureImporter)AssetImporter.GetAtPath(path);

        importer.textureType = TextureImporterType.Default;

        importer.sRGBTexture = true;

        importer.alphaSource = cutout ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;

        importer.alphaIsTransparency = cutout;

        importer.mipmapEnabled = true;

        importer.mipMapsPreserveCoverage = cutout;

        importer.alphaTestReferenceValue = 0.5f;

        importer.wrapMode = TextureWrapMode.Repeat;

        importer.filterMode = FilterMode.Trilinear;

        importer.anisoLevel = 8;

        importer.npotScale = TextureImporterNPOTScale.None;

        importer.textureCompression = TextureImporterCompression.CompressedHQ;

        importer.SaveAndReimport();
    }
}
