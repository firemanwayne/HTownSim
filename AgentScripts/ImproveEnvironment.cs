using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Adds surface and streetscape detail to existing active scenery without rebuilding the street layout.
/// This pass detects the current Midtown district and leaves vehicles, stations, incident objects and UI alone.
/// </summary>
public static class ImproveEnvironment
{
    /// <summary>Owned asset output and scene marker.</summary>
    private const string Folder = "Assets/HTownSim/Environment";
    private const string RootName = "EnvironmentDetailPass";
    private const float DetailRadius = 160f;
    private const int MaximumSurfaces = 900;
    private const int MaximumShopfronts = 16;

    /// <summary>Materials and textures created only for scenery, never shared with apparatus.</summary>
    private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();
    private static readonly Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>();
    private static Transform _root;
    private static Vector3 _centre;
    private static Vector3 _incident;

    /// <summary>Applies the environment pass once and saves the active scene.</summary>
    public static string Apply()
    {
        if (Application.isPlaying)
        {
            throw new InvalidOperationException("Stop Play Mode before authoring environment details.");
        }

        var scene = EditorSceneManager.GetActiveScene();

        if (scene.name != "HoustonResponse")
        {
            throw new InvalidOperationException("Open HoustonResponse before running this authoring pass.");
        }

        var existing = scene.GetRootGameObjects().FirstOrDefault(root => root.name == RootName);

        if (existing != null)
        {
            if (existing.transform.Find("Completed") != null)
            {
                return "Environment detail pass already completed; no changes made.";
            }

            throw new InvalidOperationException("An incomplete environment pass exists. Inspect it or restore the scene backup before retrying.");
        }

        var engine = GameObject.Find("Engine07") ?? GameObject.Find("Engine08");

        if (engine == null)
        {
            throw new InvalidOperationException("No active engine found; refusing to guess the detail area.");
        }

        _centre = engine.transform.position;

        var incident = GameObject.Find("FireIncident");

        _incident = incident != null ? incident.transform.position : new Vector3(10000f, 0f, 10000f);

        var district = GameObject.Find("MidtownReal") ?? GameObject.Find("MidtownDistrict");

        var renderers = Object.FindObjectsByType<MeshRenderer>()
            .Where(r => r.gameObject.scene == scene && IsScenery(r.transform, district))
            .Where(r => Vector3.Distance(r.bounds.ClosestPoint(_centre), _centre) <= DetailRadius)
            .OrderBy(r => Vector3.Distance(r.bounds.center, _centre))
            .Take(MaximumSurfaces)
            .ToArray();

        Directory.CreateDirectory(Folder);

        AssetDatabase.Refresh();

        Materials.Clear();

        Textures.Clear();

        _root = new GameObject(RootName).transform;

        var surfaces = 0;

        foreach (var renderer in renderers)
        {
            var source = renderer.sharedMaterial;

            if (source == null)
            {
                continue;
            }

            var kind = SurfaceKind(renderer);

            if (kind == null)
            {
                continue;
            }

            renderer.sharedMaterial = SurfaceMaterial(source, kind);

            ProjectUvs(renderer, kind);

            EditorUtility.SetDirty(renderer);

            surfaces++;
        }

        var storefronts = renderers.Where(r => r.name == "Storefront").Take(MaximumShopfronts).ToArray();

        for (var i = 0; i < storefronts.Length; i++)
        {
            DetailShop(storefronts[i], i);
        }

        var trunks = renderers.Where(r => r.name == "OakTrunk").Take(24).ToArray();

        foreach (var trunk in trunks)
        {
            TreeGrate(trunk.bounds);
        }

        var lamps = renderers.Where(r => r.name == "LampPost")
            .OrderBy(r => r.transform.position.z)
            .Take(10)
            .ToArray();

        AddUtilityDetails(lamps);

        new GameObject("Completed").transform.SetParent(_root, false);

        EditorSceneManager.MarkSceneDirty(scene);

        EditorSceneManager.SaveScene(scene);

        AssetDatabase.SaveAssets();

        return $"Detailed {surfaces} active scenery surfaces, {storefronts.Length} storefronts and {trunks.Length} tree sites. Existing streets, stations, vehicles and incident layout preserved.";
    }

    /// <summary>Limits edits to active scenery and excludes all gameplay-owned ancestry.</summary>
    private static bool IsScenery(Transform item, GameObject district)
    {
        for (var parent = item; parent != null; parent = parent.parent)
        {
            if (parent.name.StartsWith("Engine", StringComparison.Ordinal) ||
                parent.name.StartsWith("Station", StringComparison.Ordinal) ||
                parent.name == "ResponseHud" || parent.name == "Traffic" ||
                parent.name == "Pedestrians" || parent.name == "LegacyCanalBlock" ||
                parent.name == RootName)
            {
                return false;
            }
        }

        if (district != null && !item.IsChildOf(district.transform))
        {
            return false;
        }

        return item.name == "Body" || item.name == "Storefront" || item.name == "Windows" ||
            item.name == "Window" || item.name == "OakTrunk" || item.name == "OakCanopy" ||
            item.name == "LampPost" || item.name.StartsWith("Slab_", StringComparison.Ordinal) ||
            item.name.StartsWith("Roof_", StringComparison.Ordinal) || item.name == "Roof" ||
            item.name == "Sidewalk" || item.name == "SidewalkNorth" || item.name == "CanalStreet" ||
            item.name == "CrossStreet" || item.name == "CANAL MARKET" || item.name == "BAYOU COFFEE" ||
            item.name == "EAST END / WORKSHOP" || item.name == "WAREHOUSE 08" ||
            item.name == "CANAL LOFTS" || item.name == "EAST END SUPPLY";
    }

    /// <summary>Classifies visible scenery materials without touching signal or painted-line materials.</summary>
    private static string SurfaceKind(Renderer renderer)
    {
        var name = renderer.sharedMaterial.name.ToLowerInvariant();

        if (renderer.name == "OakCanopy")
        {
            return "foliage";
        }

        if (renderer.name == "OakTrunk")
        {
            return "bark";
        }

        if (name.Contains("asphalt"))
        {
            return "asphalt";
        }

        if (name.Contains("brick"))
        {
            return "brick";
        }

        if (name.Contains("concrete") || name.Contains("limestone") || name.Contains("stucco") || name.Contains("granite"))
        {
            return "concrete";
        }

        if (name.Contains("glaz") || name.Contains("glass"))
        {
            return "glass";
        }

        if (renderer.name.StartsWith("Roof", StringComparison.Ordinal))
        {
            return "roof";
        }

        return null;
    }

    /// <summary>Creates a private copy of a scenery material with a repeating surface texture.</summary>
    private static Material SurfaceMaterial(Material source, string kind)
    {
        var key = source.name + "_" + kind;

        if (Materials.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var path = Folder + "/Surface_" + Hash(key) + ".mat";

        var material = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (material == null)
        {
            material = new Material(source) { name = "Environment " + source.name };

            material.mainTexture = Pattern(kind);

            material.mainTextureScale = Vector2.one;

            material.SetFloat("_Smoothness", kind == "glass" ? 0.68f : 0.18f);

            material.SetFloat("_Metallic", kind == "glass" ? 0.3f : 0f);

            AssetDatabase.CreateAsset(material, path);
        }

        Materials.Add(key, material);

        return material;
    }

    /// <summary>Creates repeatable aggregate, running-bond masonry, bark or glass variation.</summary>
    private static Texture2D Pattern(string kind)
    {
        if (Textures.TryGetValue(kind, out var cached))
        {
            return cached;
        }

        var path = Folder + "/" + kind + "Pattern.asset";

        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);

        if (texture == null)
        {
            const int size = 256;

            texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = kind + " surface",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4,
            };

            var pixels = new Color[size * size];

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var noise = Noise(x, y);

                    var value = 0.87f + noise * 0.13f;

                    if (kind == "brick")
                    {
                        var row = y / 16;

                        var shifted = (x + row % 2 * 16) % 32;

                        var mortar = y % 16 < 1 || shifted < 1;

                        value = mortar ? 0.47f : 0.72f + Noise((x + row % 2 * 16) / 32, row) * 0.2f + noise * 0.08f;
                    }
                    else if (kind == "asphalt")
                    {
                        value = 0.72f + noise * 0.26f;

                        if (noise > 0.97f)
                        {
                            value = 1.15f;
                        }
                    }
                    else if (kind == "concrete")
                    {
                        value = 0.89f + noise * 0.1f;
                    }
                    else if (kind == "bark")
                    {
                        value = 0.6f + Mathf.Abs(Mathf.Sin(x * 0.48f + Mathf.Sin(y * 0.08f))) * 0.35f;
                    }
                    else if (kind == "glass")
                    {
                        value = 0.78f + (y / (float)size) * 0.2f;
                    }
                    else if (kind == "foliage")
                    {
                        value = 0.68f + noise * 0.3f;
                    }
                    else if (kind == "roof")
                    {
                        value = y % 32 < 2 ? 0.65f : 0.86f + noise * 0.13f;
                    }

                    pixels[y * size + x] = new Color(value, value, value, 1f);
                }
            }

            texture.SetPixels(pixels);

            texture.Apply();

            AssetDatabase.CreateAsset(texture, path);
        }

        Textures.Add(kind, texture);

        return texture;
    }

    /// <summary>Gives surfaces metre-based UVs so large cubes and combined ground meshes do not stretch textures.</summary>
    private static void ProjectUvs(MeshRenderer renderer, string kind)
    {
        if (kind == "glass" || kind == "foliage")
        {
            return;
        }

        var filter = renderer.GetComponent<MeshFilter>();

        if (filter == null || filter.sharedMesh == null)
        {
            return;
        }

        var key = HierarchyKey(renderer.transform);

        var path = Folder + "/SurfaceMesh_" + Hash(key) + ".asset";

        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);

        if (mesh == null)
        {
            mesh = Object.Instantiate(filter.sharedMesh);

            mesh.name = renderer.name + " textured surface";

            var vertices = mesh.vertices;

            var normals = mesh.normals;

            var uv = new Vector2[vertices.Length];

            var metres = kind == "bark" ? 1.5f : 4f;

            for (var i = 0; i < vertices.Length; i++)
            {
                var point = renderer.transform.TransformPoint(vertices[i]);

                var normal = normals.Length == vertices.Length ? renderer.transform.TransformDirection(normals[i]) : Vector3.up;

                uv[i] = Mathf.Abs(normal.y) > 0.65f ? new Vector2(point.x, point.z) / metres :
                    Mathf.Abs(normal.x) > Mathf.Abs(normal.z) ? new Vector2(point.z, point.y) / metres : new Vector2(point.x, point.y) / metres;
            }

            mesh.uv = uv;

            AssetDatabase.CreateAsset(mesh, path);
        }

        filter.sharedMesh = mesh;

        EditorUtility.SetDirty(filter);
    }

    /// <summary>Adds window subdivisions, door handles and modest shop signs to existing glass frontage.</summary>
    private static void DetailShop(Renderer storefront, int index)
    {
        var bounds = storefront.bounds;

        if (Vector3.Distance(bounds.center, _incident) < 7f)
        {
            return;
        }

        var alongX = bounds.size.x > bounds.size.z;

        var side = alongX ? Vector3.right : Vector3.forward;

        var body = storefront.transform.parent != null ? storefront.transform.parent.Find("Body") : null;

        if (body == null)
        {
            return;
        }

        var outward = bounds.center - body.position;

        outward.y = 0f;

        outward = alongX ? Vector3.forward * Mathf.Sign(outward.z) : Vector3.right * Mathf.Sign(outward.x);

        var width = alongX ? bounds.size.x : bounds.size.z;

        var metal = Solid("Window frames", "35434A");

        var sign = Solid(index % 2 == 0 ? "Teal storefront signs" : "Bronze storefront signs", index % 2 == 0 ? "24484A" : "654634");

        var trim = Solid("Warm trim", "E1D6BC");

        for (var position = -width / 2f + 0.4f; position < width / 2f; position += 1.8f)
        {
            Box("GlazingMullion", (bounds.center + side * position + outward * 0.11f,
                alongX ? new Vector3(0.045f, bounds.size.y, 0.06f) : new Vector3(0.06f, bounds.size.y, 0.045f), metal));
        }

        var door = bounds.center + side * (-width * 0.22f) + outward * 0.14f;

        door.y = bounds.min.y + 1.1f;

        Box("DoorHeader", (door + Vector3.up * 1.1f, alongX ? new Vector3(1.1f, 0.09f, 0.08f) : new Vector3(0.08f, 0.09f, 1.1f), metal));

        Box("DoorPull", (door + outward * 0.04f + side * 0.4f, new Vector3(0.055f, 0.45f, 0.055f), trim));

        var signPosition = bounds.center + Vector3.up * (bounds.extents.y + 0.52f) + outward * 0.16f;

        Box("ShopSignboard", (signPosition, alongX ? new Vector3(Mathf.Min(width, 8f), 0.7f, 0.14f) : new Vector3(0.14f, 0.7f, Mathf.Min(width, 8f)), sign));

        var label = new GameObject("ShopIdentification").AddComponent<TextMeshPro>();

        label.transform.SetParent(_root, false);

        label.transform.position = signPosition + outward * 0.09f;

        label.transform.rotation = Quaternion.LookRotation(-outward, Vector3.up);

        label.rectTransform.sizeDelta = new Vector2(Mathf.Min(width, 8f) - 0.3f, 0.6f);

        label.text = new[] { "BAYOU COFFEE", "CORNER MARKET", "MIDTOWN REPAIR", "LOCAL GOODS" }[index % 4];

        label.fontSize = 4.8f;

        label.fontStyle = FontStyles.Bold;

        label.characterSpacing = 3f;

        label.color = trim.color;

        label.alignment = TextAlignmentOptions.Center;
    }

    /// <summary>Places thin noncolliding iron tree-grate bars around existing trunks.</summary>
    private static void TreeGrate(Bounds trunk)
    {
        if (Vector3.Distance(trunk.center, _incident) < 7f)
        {
            return;
        }

        var material = Solid("Tree grate iron", "303B3C");

        var centre = new Vector3(trunk.center.x, Mathf.Max(0.23f, trunk.min.y + 0.02f), trunk.center.z);

        for (var i = -4; i <= 4; i++)
        {
            if (Mathf.Abs(i) <= 1)
            {
                continue;
            }

            Box("TreeGrate", (centre + Vector3.right * (i * 0.18f), new Vector3(0.05f, 0.025f, 1.7f), material));
        }
    }

    /// <summary>Adds a small set of utility poles and sagging wires anchored to detected sidewalk lamps.</summary>
    private static void AddUtilityDetails(Renderer[] lamps)
    {
        var timber = Solid("Utility timber", "655445");

        var cable = Solid("Utility cable", "293236");

        var positions = new List<Vector3>();

        foreach (var lamp in lamps)
        {
            var point = lamp.bounds.center;

            point.y = 0.25f;

            if (Vector3.Distance(point, _incident) < 10f)
            {
                continue;
            }

            point += Vector3.right * 0.65f;

            Box("UtilityPole", (point + Vector3.up * 4.5f, new Vector3(0.2f, 9f, 0.2f), timber));

            Box("UtilityCrossarm", (point + Vector3.up * 8.4f, new Vector3(2.2f, 0.13f, 0.13f), timber));

            positions.Add(point);
        }

        for (var i = 1; i < positions.Count; i++)
        {
            var from = positions[i - 1] + Vector3.up * 8.5f;

            var to = positions[i] + Vector3.up * 8.5f;

            var distance = Vector3.Distance(from, to);

            if (distance < 8f || distance > 65f || Mathf.Min(Mathf.Abs(from.x - to.x), Mathf.Abs(from.z - to.z)) > 4f)
            {
                continue;
            }

            foreach (var offset in new[] { -0.7f, 0f, 0.7f })
            {
                var line = new GameObject("OverheadUtility").AddComponent<LineRenderer>();

                line.transform.SetParent(_root, false);

                line.sharedMaterial = cable;

                line.startWidth = line.endWidth = 0.025f;

                line.positionCount = 12;

                for (var segment = 0; segment < line.positionCount; segment++)
                {
                    var t = segment / (float)(line.positionCount - 1);

                    line.SetPosition(segment, Vector3.Lerp(from, to, t) + Vector3.right * offset - Vector3.up * (Mathf.Sin(t * Mathf.PI) * 0.7f));
                }
            }
        }
    }

    /// <summary>Creates one reusable solid detail material without altering existing project materials.</summary>
    private static Material Solid(string name, string hex)
    {
        var path = Folder + "/Detail_" + Hash(name) + ".mat";

        var material = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (material != null)
        {
            return material;
        }

        ColorUtility.TryParseHtmlString("#" + hex, out var color);

        material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name, color = color };

        material.SetFloat("_Smoothness", 0.25f);

        AssetDatabase.CreateAsset(material, path);

        return material;
    }

    /// <summary>Builds noncolliding static detail under the owned root.</summary>
    private static GameObject Box(string name, (Vector3 Position, Vector3 Scale, Material Material) spec)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);

        go.name = name;

        Object.DestroyImmediate(go.GetComponent<Collider>());

        go.transform.SetParent(_root, false);

        go.transform.position = spec.Position;

        go.transform.localScale = spec.Scale;

        go.GetComponent<Renderer>().sharedMaterial = spec.Material;

        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);

        return go;
    }

    /// <summary>Builds a stable hierarchy identity that distinguishes same-named siblings.</summary>
    private static string HierarchyKey(Transform transform)
    {
        var key = string.Empty;

        for (var item = transform; item != null; item = item.parent)
        {
            key = item.name + "[" + item.GetSiblingIndex() + "]/" + key;
        }

        return key;
    }

    /// <summary>Returns deterministic unsigned names for generated assets.</summary>
    private static uint Hash(string text)
    {
        unchecked
        {
            var hash = 2166136261u;

            foreach (var character in text)
            {
                hash = (hash ^ character) * 16777619u;
            }

            return hash;
        }
    }

    /// <summary>Produces repeatable scalar texture noise without changing Unity's random state.</summary>
    private static float Noise(int x, int y)
    {
        unchecked
        {
            var value = (uint)(x * 374761393 + y * 668265263 + 98731);

            value = (value ^ (value >> 13)) * 1274126177u;

            return (value & 65535) / 65535f;
        }
    }
}
