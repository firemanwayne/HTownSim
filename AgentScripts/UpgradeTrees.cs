#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Replaces the generic street trees with the City of Houston tree inventory (HPARD Urban Forestry): every tree at its
/// real position, as its species (live oak, crape myrtle, laurel/water oak, broadleaf, bald cypress, magnolia, palm), sized
/// from its trunk-diameter class. Models come from Blender\HTown (scripts/build_trees.py ->
/// data/tree_models.txt and data/tree_places.txt); trunks use the Poly Haven bark scan.
///
/// The old StreetTrees and StreetTrees_Exp sections are switched off, never deleted. Each new tree has a trunk
/// CapsuleCollider like the old ones. Apply can be run again; Revert restores the old trees. Both refuse to run with
/// unsaved scene changes, and reload the scene after saving (see UpgradeBuildings.Save).
/// </summary>
public static class UpgradeTrees
{
    private static readonly string ModelPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(UnityEngine.Application.dataPath, "..", "Blender", "HTown", "data", "tree_models.txt"));

    private static readonly string PlacePath = System.IO.Path.GetFullPath(System.IO.Path.Combine(UnityEngine.Application.dataPath, "..", "Blender", "HTown", "data", "tree_places.txt"));

    private const string MeshAssetPath = "Assets/HTownSim/Generated/InventoryTrees.asset";

    private const string MaterialFolder = "Assets/HTownSim/Generated/Trees";

    private const string GeneratedFolder = "Assets/HTownSim/Generated";

    private const string BarkTexture = "Assets/HTownSim/Textures/Ground/Bark.png";

    private const string RootName = "InventoryTrees";

    /// <summary>Height of the lot/sidewalk surface the trees stand on.</summary>
    private const float GroundY = 0.215f;

    private static readonly string[] OldTrees = { "StreetTrees", "StreetTrees_Exp" };

    /// <summary>Flat-colour foliage and bark materials: name, colour, smoothness.</summary>
    private static readonly (string Name, string Hex, float Smoothness)[] Flat =
    {
        ("BarkSmooth", "9C8A78", 0.15f),
        ("BarkPalm", "7A6A58", 0.1f),
        ("LeafOak", "4F6B45", 0.12f),
        ("LeafCrape", "5E7A4A", 0.12f),
        ("Bloom", "D9709A", 0.2f),
        ("LeafBroad", "6A8A4E", 0.12f),
        ("LeafCypress", "3E5A3C", 0.1f),
        ("LeafMagnolia", "34503A", 0.4f),
        ("LeafPalm", "6E8A4A", 0.15f),
    };

    private sealed class Sub
    {
        public string mat = "";

        public float[] v = Array.Empty<float>();

        public float[] uv = Array.Empty<float>();
    }

    /// <summary>Builds the inventory trees and returns a summary.</summary>
    public static string Apply()
    {
        var blocked = Blocked();

        if (blocked is not null)
        {
            return blocked;
        }

        Restore();

        var materials = LoadMaterials();

        AssetDatabase.DeleteAsset(MeshAssetPath);

        var container = new Mesh { name = "InventoryTrees" };

        AssetDatabase.CreateAsset(container, MeshAssetPath);

        var models = new Dictionary<string, (Mesh Mesh, Material[] Materials)>();

        foreach (var model in ReadModels())
        {
            var mesh = BuildMesh(model.Key, model.Value);

            AssetDatabase.AddObjectToAsset(mesh, container);

            var slots = new Material[model.Value.Count];

            for (var i = 0; i < slots.Length; i++)
            {
                slots[i] = materials[model.Value[i].mat];
            }

            models[model.Key] = (mesh, slots);
        }

        var root = new GameObject(RootName).transform;

        root.SetParent(GameObject.Find("MidtownReal").transform, false);

        var count = 0;

        foreach (var line in File.ReadLines(PlacePath))
        {
            var f = line.Split('\t');

            if (f[0] != "T" || !models.TryGetValue(f[1], out var model))
            {
                continue;
            }

            var x = F(f[2]);

            var z = F(f[3]);

            var scale = F(f[5]);

            var go = new GameObject(f[1], typeof(MeshFilter), typeof(MeshRenderer));

            go.transform.SetParent(root, false);

            go.transform.SetPositionAndRotation(new Vector3(x, GroundY, z), Quaternion.Euler(0f, F(f[4]), 0f));

            go.transform.localScale = Vector3.one * scale;

            go.GetComponent<MeshFilter>().sharedMesh = model.Mesh;

            go.GetComponent<MeshRenderer>().sharedMaterials = model.Materials;

            var trunk = go.AddComponent<CapsuleCollider>();

            trunk.radius = f[1].Contains("crape") ? 0.5f : 0.35f;

            trunk.height = 3f;

            trunk.center = new Vector3(0f, 1.5f, 0f);

            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);

            count++;
        }

        SetOldTrees(false);

        AssetDatabase.SaveAssets();

        Save();

        return $"Placed {count} inventory trees from {models.Count} models; old street trees hidden. Scene saved.";
    }

    /// <summary>Removes the inventory trees and brings the old street trees back.</summary>
    public static string Revert()
    {
        var blocked = Blocked();

        if (blocked is not null)
        {
            return blocked;
        }

        Restore();

        AssetDatabase.DeleteAsset(MeshAssetPath);

        Save();

        return "Old street trees restored. Scene saved.";
    }

    private static void Restore()
    {
        var old = GameObject.Find("MidtownReal").transform.Find(RootName);

        if (old != null)
        {
            UnityEngine.Object.DestroyImmediate(old.gameObject);
        }

        SetOldTrees(true);
    }

    /// <summary>Shows or hides the old tree sections, wherever they sit in the hierarchy (inactive ones included).</summary>
    private static void SetOldTrees(bool visible)
    {
        foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
        {
            if (Array.IndexOf(OldTrees, t.name) >= 0 && t.gameObject.scene == EditorSceneManager.GetActiveScene())
            {
                t.gameObject.SetActive(visible);
            }
        }
    }

    private static string? Blocked()
    {
        if (EditorApplication.isPlaying)
        {
            return "Exit Play Mode first.";
        }

        var scene = EditorSceneManager.GetActiveScene();

        return scene.isDirty ? $"Scene {scene.name} has unsaved changes; save or discard them first. Nothing was changed." : null;
    }

    /// <summary>Saves, then reloads the scene so the GPU Resident Drawer drops references to the deleted meshes.</summary>
    private static void Save()
    {
        var scene = EditorSceneManager.GetActiveScene();

        EditorSceneManager.MarkSceneDirty(scene);

        EditorSceneManager.SaveScene(scene);

        EditorSceneManager.OpenScene(scene.path, OpenSceneMode.Single);
    }

    private static Dictionary<string, Material> LoadMaterials()
    {
        if (!AssetDatabase.IsValidFolder(MaterialFolder))
        {
            AssetDatabase.CreateFolder(GeneratedFolder, "Trees");
        }

        var lit = Shader.Find("Universal Render Pipeline/Lit");

        var map = new Dictionary<string, Material>();

        foreach (var flat in Flat)
        {
            var m = GetOrCreate(flat.Name, lit);

            ColorUtility.TryParseHtmlString("#" + flat.Hex, out var color);

            m.SetColor("_BaseColor", color);

            m.SetFloat("_Smoothness", flat.Smoothness);

            m.enableInstancing = true;

            EditorUtility.SetDirty(m);

            map[flat.Name] = m;
        }

        var bark = GetOrCreate("Bark", lit);

        bark.SetColor("_BaseColor", new Color(0.62f, 0.55f, 0.47f));

        bark.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(BarkTexture));

        bark.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/HTownSim/Textures/Ground/Bark_N.png"));

        bark.EnableKeyword("_NORMALMAP");

        // Trunk UVs run 0..1 around and one unit per metre up; the scan covers 1 m.
        bark.SetTextureScale("_BaseMap", new Vector2(2f, 1f));

        bark.SetFloat("_Smoothness", 0.1f);

        bark.enableInstancing = true;

        EditorUtility.SetDirty(bark);

        map["Bark"] = bark;

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

    /// <summary>Models by name: "B key name" then "M material positions uvs" lines.</summary>
    private static Dictionary<string, List<Sub>> ReadModels()
    {
        var models = new Dictionary<string, List<Sub>>();

        List<Sub>? current = null;

        foreach (var line in File.ReadLines(ModelPath))
        {
            var fields = line.Split('\t');

            if (fields[0] == "B")
            {
                current = new List<Sub>();

                models[fields[2]] = current;
            }
            else if (fields[0] == "M" && current is not null)
            {
                current.Add(new Sub { mat = fields[1], v = Numbers(fields[2]), uv = Numbers(fields[3]) });
            }
        }

        return models;
    }

    private static float F(string text)
    {
        return float.Parse(text, CultureInfo.InvariantCulture);
    }

    private static float[] Numbers(string text)
    {
        var parts = text.Split(',');

        var values = new float[parts.Length];

        for (var i = 0; i < parts.Length; i++)
        {
            values[i] = F(parts[i]);
        }

        return values;
    }

    /// <summary>One mesh per model, one submesh per material; unshared vertices give the flat low-poly shading.</summary>
    private static Mesh BuildMesh(string name, List<Sub> subs)
    {
        var vertices = new List<Vector3>();

        var uvs = new List<Vector2>();

        var triangles = new List<int[]>();

        foreach (var sub in subs)
        {
            var start = vertices.Count;

            var count = sub.v.Length / 3;

            var indices = new int[count];

            for (var i = 0; i < count; i++)
            {
                vertices.Add(new Vector3(sub.v[i * 3], sub.v[i * 3 + 1], sub.v[i * 3 + 2]));

                uvs.Add(new Vector2(sub.uv[i * 2], sub.uv[i * 2 + 1]));

                indices[i] = start + i;
            }

            triangles.Add(indices);
        }

        var mesh = new Mesh { name = name };

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
}
