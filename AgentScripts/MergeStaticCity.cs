using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Merges the static city (buildings, lot slabs, curbs, roads, markings, trees, parked cars, the freeway) into a few large meshes, one per
/// 200 m chunk and material, so the renderer count drops from tens of thousands to a few thousand. The original renderers are switched off,
/// not deleted, so colliders and scripts keep their objects, and <see cref="Unmerge"/> brings them back. Flat pieces (under 0.3 m tall:
/// markings, crosswalks, slabs) stop casting shadows. Anything a script points at (signals, signs, lenses, incident sites, apparatus) is left
/// alone, as is anything that moves. Run after any city rebuild:
/// <c>unity command script run --file AgentScripts/MergeStaticCity.cs --entry MergeStaticCity.Merge</c>.
/// </summary>
public static class MergeStaticCity
{
    /// <summary>The object that holds the merged meshes.</summary>
    private const string MergedRootName = "MergedCity";

    /// <summary>Where the merged mesh assets are written.</summary>
    private const string MeshFolder = "Assets/HTownSim/Generated/MergedCity";

    /// <summary>Chunk size in metres.</summary>
    private const float ChunkSize = 200f;

    /// <summary>The roots whose contents are merged.</summary>
    private static readonly string[] Roots = { "MidtownReal", "ParkedCars", "ParkedCars_Exp", "StreetTrees", "StreetTrees_Exp", "StreetProps", "StreetProps_Exp" };

    /// <summary>Children (by name) that are never merged: they move, flash, change or are driven.</summary>
    private static readonly string[] SkipNames = { "Traffic", "Signal", "Lens", "Glow", "Beacon", "FireIncident", "IncidentSite", "Sign", "ER_Bay", "Apparatus", "Engine", "Ladder", "Medic", "Ambulance", "Supervisor" };

    /// <summary>The list of renderers Merge switched off (global object ids), kept outside Assets so it is not imported.</summary>
    private const string SwitchedOffList = "Library/MergedCity_SwitchedOff.txt";

    /// <summary>Vertex budget per merged mesh (32-bit indices allow more, but smaller pieces cull better).</summary>
    private const int MaxVertices = 1_000_000;

    /// <summary>Merges the static city and saves the scene.</summary>
    public static string Merge()
    {
        if (EditorApplication.isPlaying)
        {
            return "Exit Play Mode first.";
        }

        Unmerge();

        var scene = SceneManager.GetActiveScene();

        var referenced = ReferencedObjects(scene);

        var groups = new Dictionary<(Material Material, ShadowCastingMode Shadows, int Layer, int ChunkX, int ChunkZ), List<CombineInstance>>();

        var vertexCounts = new Dictionary<(Material, ShadowCastingMode, int, int, int), int>();

        var switchedOff = new List<MeshRenderer>();

        var skipped = 0;

        foreach (var rootName in Roots)
        {
            foreach (var root in scene.GetRootGameObjects().Where(go => go.name == rootName))
            {
                foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(false))
                {
                    if (!Mergeable(renderer, referenced))
                    {
                        skipped++;

                        continue;
                    }

                    var filter = renderer.GetComponent<MeshFilter>();

                    var mesh = filter.sharedMesh;

                    var bounds = renderer.bounds;

                    var shadows = bounds.size.y < 0.3f ? ShadowCastingMode.Off : renderer.shadowCastingMode;

                    var key = (renderer.sharedMaterial, shadows, renderer.gameObject.layer, Mathf.FloorToInt(bounds.center.x / ChunkSize), Mathf.FloorToInt(bounds.center.z / ChunkSize));

                    vertexCounts.TryGetValue(key, out var count);

                    if (count + mesh.vertexCount > MaxVertices)
                    {
                        skipped++;

                        continue;
                    }

                    vertexCounts[key] = count + mesh.vertexCount;

                    if (!groups.TryGetValue(key, out var list))
                    {
                        list = new List<CombineInstance>();

                        groups[key] = list;
                    }

                    list.Add(new CombineInstance { mesh = mesh, subMeshIndex = 0, transform = renderer.transform.localToWorldMatrix });

                    switchedOff.Add(renderer);
                }
            }
        }

        Directory.CreateDirectory(MeshFolder);

        var mergedRoot = new GameObject(MergedRootName);

        mergedRoot.isStatic = true;

        var made = 0;

        foreach (var pair in groups)
        {
            var key = pair.Key;

            var combined = new Mesh { indexFormat = IndexFormat.UInt32, name = "Merged_" + key.Material.name + "_" + key.ChunkX + "_" + key.ChunkZ + "_" + made };

            combined.CombineMeshes(pair.Value.ToArray(), true, true, false);

            combined.RecalculateBounds();

            combined.UploadMeshData(false);

            AssetDatabase.CreateAsset(combined, MeshFolder + "/" + combined.name + ".asset");

            var go = new GameObject(combined.name);

            go.transform.SetParent(mergedRoot.transform, false);

            go.layer = key.Layer;

            go.isStatic = true;

            go.AddComponent<MeshFilter>().sharedMesh = combined;

            var renderer = go.AddComponent<MeshRenderer>();

            renderer.sharedMaterial = key.Material;

            renderer.shadowCastingMode = key.Shadows;

            made++;
        }

        foreach (var renderer in switchedOff)
        {
            renderer.enabled = false;
        }

        // Remember exactly which renderers were switched off, so Unmerge turns back on only those.
        File.WriteAllLines(SwitchedOffList, switchedOff.Select(renderer => GlobalObjectId.GetGlobalObjectIdSlow(renderer).ToString()));

        AssetDatabase.SaveAssets();

        EditorSceneManager.MarkSceneDirty(scene);

        EditorSceneManager.SaveScene(scene);

        return "Merged " + switchedOff.Count + " renderers into " + made + " meshes (" + skipped + " left as they are).";
    }

    /// <summary>Removes the merged meshes and switches the original renderers back on.</summary>
    public static string Unmerge()
    {
        var scene = SceneManager.GetActiveScene();

        var removed = 0;

        foreach (var root in scene.GetRootGameObjects().Where(go => go.name == MergedRootName).ToList())
        {
            UnityEngine.Object.DestroyImmediate(root);

            removed++;
        }

        var restored = 0;

        if (File.Exists(SwitchedOffList))
        {
            foreach (var line in File.ReadAllLines(SwitchedOffList))
            {
                if (GlobalObjectId.TryParse(line, out var id) && GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) is Renderer renderer && !renderer.enabled)
                {
                    renderer.enabled = true;

                    restored++;
                }
            }

            File.Delete(SwitchedOffList);
        }

        if (AssetDatabase.IsValidFolder(MeshFolder))
        {
            AssetDatabase.DeleteAsset(MeshFolder);
        }

        if (removed > 0 || restored > 0)
        {
            EditorSceneManager.MarkSceneDirty(scene);
        }

        return "Removed " + removed + " merged roots; switched " + restored + " renderers back on.";
    }

    /// <summary>True for a plain, still, single-material renderer that no script points at.</summary>
    private static bool Mergeable(MeshRenderer renderer, HashSet<UnityEngine.Object> referenced)
    {
        if (!renderer.enabled || renderer.sharedMaterials.Length != 1 || renderer.sharedMaterial == null)
        {
            return false;
        }

        var filter = renderer.GetComponent<MeshFilter>();

        if (filter == null || filter.sharedMesh == null || filter.sharedMesh.subMeshCount != 1 || !filter.sharedMesh.isReadable && !Application.isEditor)
        {
            return false;
        }

        if (renderer.GetComponentInParent<LODGroup>() != null || renderer.GetComponentInParent<Rigidbody>() != null || renderer.GetComponentInParent<Animator>() != null)
        {
            return false;
        }

        // Objects with behaviours of their own (other than the renderer and colliders) may change at run time.
        if (renderer.GetComponents<Component>().Any(component => component is MonoBehaviour))
        {
            return false;
        }

        if (referenced.Contains(renderer) || referenced.Contains(renderer.gameObject) || referenced.Contains(renderer.transform))
        {
            return false;
        }

        for (var t = renderer.transform; t != null; t = t.parent)
        {
            if (t != renderer.transform && (referenced.Contains(t.gameObject) || referenced.Contains(t)) && t.parent != null && t.parent.parent != null)
            {
                // A script points at an ancestor below the top level (an incident site, a station, a sign): leave its pieces alone.
                return false;
            }

            if (SkipNames.Any(name => t.name.Contains(name, StringComparison.Ordinal)) && t.name != "TrafficCalming")
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Every renderer, game object and transform that a script in the scene points at (the weather's road list excepted: it is rewired by RepairAfterExpand).</summary>
    private static HashSet<UnityEngine.Object> ReferencedObjects(Scene scene)
    {
        var result = new HashSet<UnityEngine.Object>();

        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null || behaviour.GetType().Name == "EnvironmentWeather")
                {
                    continue;
                }

                var serialized = new SerializedObject(behaviour);

                var property = serialized.GetIterator();

                while (property.Next(true))
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference || property.objectReferenceValue == null)
                    {
                        continue;
                    }

                    var value = property.objectReferenceValue;

                    if (value is Renderer || value is GameObject || value is Transform)
                    {
                        result.Add(value);
                    }
                }
            }
        }

        return result;
    }
}
