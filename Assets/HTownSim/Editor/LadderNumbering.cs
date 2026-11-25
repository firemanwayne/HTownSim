#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Renumbers a parked copy of the Ladder 07 model. Its "07" is modelled into the chassis mesh (gold and reflective-white lettering on the
/// front, the cab doors, the cab sides and the rear), not painted on a texture. This finds each "07" (a "0" island of 960 triangles next to
/// a "7" island of 256 on the same face of the LOD0 chassis), writes chassis mesh assets for every LOD with the triangles inside those
/// boxes removed, and puts TextMeshPro lettering with the new number in each box, in the colour of the lettering it replaces. The
/// lettering objects are named <see cref="LabelPrefix"/>... so the game can copy them onto the driven ladder (ApparatusLoadout).
/// </summary>
public static class LadderNumbering
{
    /// <summary>Name prefix of the lettering objects (shared with ApparatusLoadout).</summary>
    internal const string LabelPrefix = "UnitNumber_";

    /// <summary>The lettering materials and the chassis meshes in the Ladder 07 model.</summary>
    private static readonly string[] LetteringMaterials = { "L07_Gold_graphics", "L07_Reflective_white", "L07_Fire_red_enamel" };

    /// <summary>The model parts that carry lettering: the chassis and the aerial sections.</summary>
    private static readonly string[] PartPrefixes = { "L07_chassis", "L07_aerial_" };

    /// <summary>Triangle counts of the "0" and "7" islands in the LOD0 lettering.</summary>
    private const int ZeroTriangles = 960;

    private const int SevenTriangles = 256;

    /// <summary>How far apart (metres) the two digits of one number can be.</summary>
    private const float PairDistance = 0.6f;

    /// <summary>How much the cut box is grown around the digits, so the coarser LODs lose theirs too.</summary>
    private const float CutMargin = 0.012f;

    /// <summary>One "07" on the truck: its box in chassis mesh space, the face it is on, and its colour.</summary>
    private readonly struct Number
    {
        internal Number(Bounds box, Vector3 outward, Color colour)
        {
            Box = box;

            Outward = outward;

            Colour = colour;
        }

        internal Bounds Box { get; }

        internal Vector3 Outward { get; }

        internal Color Colour { get; }
    }

    /// <summary>Renumbers the ladder model under <paramref name="model"/>; writes the meshes to <paramref name="folder"/>. Returns a summary.</summary>
    public static string Apply(Transform model, string number, string folder)
    {
        // Every lettered part, by its name without the _LODn suffix: the chassis and the aerial sections (the HOUSTON LADDER 07 sign).
        var parts = model.GetComponentsInChildren<MeshFilter>(true)
            .Where(filter => PartPrefixes.Any(prefix => filter.name.StartsWith(prefix, StringComparison.Ordinal)) && filter.name.Contains("_LOD"))
            .GroupBy(filter => filter.name.Substring(0, filter.name.LastIndexOf("_LOD", StringComparison.Ordinal)))
            .ToList();

        var places = 0;

        var index = 0;

        foreach (var part in parts)
        {
            var lod0 = part.FirstOrDefault(filter => filter.name.EndsWith("_LOD0", StringComparison.Ordinal));

            if (lod0 == null)
            {
                continue;
            }

            var numbers = FindNumbers(lod0);

            if (numbers.Count == 0)
            {
                continue;
            }

            foreach (var filter in part)
            {
                filter.sharedMesh = WithoutDigits(new CutRequest(filter, numbers, folder));
            }

            // Up in the part's own space (the model is imported turned), so the lettering stands upright.
            var up = lod0.transform.InverseTransformDirection(Vector3.up).normalized;

            foreach (var found in numbers)
            {
                AddLabel(new LabelRequest(lod0.transform, found, up, number, index++));
            }

            places += numbers.Count;
        }

        return "Renumbered " + places + " places on " + parts.Count + " lettered parts.";
    }

    /// <summary>The "07"s on the LOD0 chassis: islands of the lettering sub-meshes, paired "0" with "7" on the same face.</summary>
    private static List<Number> FindNumbers(MeshFilter lod0)
    {
        var renderer = lod0.GetComponent<MeshRenderer>();

        var mesh = lod0.sharedMesh;

        var vertices = mesh.vertices;

        var result = new List<Number>();

        for (var sub = 0; sub < renderer.sharedMaterials.Length; sub++)
        {
            var material = renderer.sharedMaterials[sub];

            if (material == null || Array.IndexOf(LetteringMaterials, material.name) < 0)
            {
                continue;
            }

            var islands = Islands(vertices, mesh.GetTriangles(sub));

            var zeros = islands.Where(island => island.Triangles == ZeroTriangles).ToList();

            var sevens = islands.Where(island => island.Triangles == SevenTriangles).ToList();

            foreach (var zero in zeros)
            {
                var seven = sevens
                    .Where(candidate => (candidate.Box.center - zero.Box.center).magnitude < PairDistance && ThinAxis(candidate.Box) == ThinAxis(zero.Box))
                    .OrderBy(candidate => (candidate.Box.center - zero.Box.center).magnitude)
                    .Cast<Island?>()
                    .FirstOrDefault();

                if (seven is null)
                {
                    continue;
                }

                sevens.Remove(seven.Value);

                var box = zero.Box;

                box.Encapsulate(seven.Value.Box);

                var axis = ThinAxis(box);

                var outward = Vector3.zero;

                outward[axis] = Mathf.Sign(box.center[axis]);

                result.Add(new Number(box, outward, material.color));
            }
        }

        return result;
    }

    /// <summary>A connected piece of a sub-mesh (vertices welded by position): its bounds and triangle count.</summary>
    private readonly struct Island
    {
        internal Island(Bounds box, int triangles)
        {
            Box = box;

            Triangles = triangles;
        }

        internal Bounds Box { get; }

        internal int Triangles { get; }
    }

    private static List<Island> Islands(Vector3[] vertices, int[] triangles)
    {
        var ids = new Dictionary<Vector3Int, int>();

        var parent = new List<int>();

        int Id(Vector3 p)
        {
            var key = new Vector3Int(Mathf.RoundToInt(p.x * 2000f), Mathf.RoundToInt(p.y * 2000f), Mathf.RoundToInt(p.z * 2000f));

            if (!ids.TryGetValue(key, out var id))
            {
                id = parent.Count;

                ids[key] = id;

                parent.Add(id);
            }

            return id;
        }

        int Root(int a)
        {
            while (parent[a] != a)
            {
                parent[a] = parent[parent[a]];

                a = parent[a];
            }

            return a;
        }

        var corner = new int[triangles.Length];

        for (var i = 0; i < triangles.Length; i++)
        {
            corner[i] = Id(vertices[triangles[i]]);
        }

        for (var i = 0; i < triangles.Length; i += 3)
        {
            var a = Root(corner[i]);

            parent[Root(corner[i + 1])] = a;

            parent[Root(corner[i + 2])] = a;
        }

        var groups = new Dictionary<int, (Bounds Box, int Count)>();

        for (var i = 0; i < triangles.Length; i += 3)
        {
            var root = Root(corner[i]);

            var box = groups.TryGetValue(root, out var existing) ? existing.Box : new Bounds(vertices[triangles[i]], Vector3.zero);

            box.Encapsulate(vertices[triangles[i]]);

            box.Encapsulate(vertices[triangles[i + 1]]);

            box.Encapsulate(vertices[triangles[i + 2]]);

            groups[root] = (box, existing.Count + 1);
        }

        return groups.Values.Select(group => new Island(group.Box, group.Count)).ToList();
    }

    /// <summary>The axis (0, 1, 2) along which a box is thinnest: the face's normal.</summary>
    private static int ThinAxis(Bounds box)
    {
        var size = box.size;

        return size.x <= size.y && size.x <= size.z ? 0 : size.y <= size.z ? 1 : 2;
    }

    /// <summary>Which chassis mesh to cut, the boxes to cut out of its lettering, and where the new mesh goes.</summary>
    private readonly struct CutRequest
    {
        internal CutRequest(MeshFilter filter, List<Number> numbers, string folder)
        {
            Filter = filter;

            Numbers = numbers;

            Folder = folder;
        }

        internal MeshFilter Filter { get; }

        internal List<Number> Numbers { get; }

        internal string Folder { get; }
    }

    /// <summary>A copy of the chassis mesh with the lettering triangles inside the number boxes removed, saved as an asset.</summary>
    private static Mesh WithoutDigits(CutRequest request)
    {
        var source = request.Filter.sharedMesh;

        var renderer = request.Filter.GetComponent<MeshRenderer>();

        var mesh = UnityEngine.Object.Instantiate(source);

        mesh.name = "L16_" + source.name;

        var vertices = mesh.vertices;

        for (var sub = 0; sub < mesh.subMeshCount; sub++)
        {
            var material = sub < renderer.sharedMaterials.Length ? renderer.sharedMaterials[sub] : null;

            if (material == null || Array.IndexOf(LetteringMaterials, material.name) < 0)
            {
                continue;
            }

            var triangles = mesh.GetTriangles(sub);

            var kept = new List<int>(triangles.Length);

            for (var i = 0; i < triangles.Length; i += 3)
            {
                var centre = (vertices[triangles[i]] + vertices[triangles[i + 1]] + vertices[triangles[i + 2]]) / 3f;

                if (request.Numbers.Any(number => Grown(number.Box).Contains(centre)))
                {
                    continue;
                }

                kept.Add(triangles[i]);

                kept.Add(triangles[i + 1]);

                kept.Add(triangles[i + 2]);
            }

            mesh.SetTriangles(kept, sub);
        }

        System.IO.Directory.CreateDirectory(request.Folder);

        var path = request.Folder + "/" + mesh.name + ".asset";

        AssetDatabase.DeleteAsset(path);

        AssetDatabase.CreateAsset(mesh, path);

        return mesh;
    }

    private static Bounds Grown(Bounds box)
    {
        box.Expand(CutMargin * 2f);

        return box;
    }

    /// <summary>Where one new number goes: the chassis it belongs to, the "07" it replaces, the chassis's up direction, the number and its index.</summary>
    private readonly struct LabelRequest
    {
        internal LabelRequest(Transform chassis, Number replaces, Vector3 up, string number, int index)
        {
            Chassis = chassis;

            Replaces = replaces;

            Up = up;

            Text = number;

            Index = index;
        }

        internal Transform Chassis { get; }

        internal Number Replaces { get; }

        internal Vector3 Up { get; }

        internal string Text { get; }

        internal int Index { get; }
    }

    /// <summary>TextMeshPro lettering in the box, a hair proud of the panel, facing out, sized to the old digits' height.</summary>
    private static void AddLabel(LabelRequest request)
    {
        var box = request.Replaces.Box;

        var outward = request.Replaces.Outward;

        // The digits' height is the box size along the up direction.
        var height = Mathf.Abs(Vector3.Dot(box.size, new Vector3(Mathf.Abs(request.Up.x), Mathf.Abs(request.Up.y), Mathf.Abs(request.Up.z))));

        var label = new GameObject(LabelPrefix + request.Index);

        label.transform.SetParent(request.Chassis, false);

        label.transform.localPosition = box.center + outward * (box.extents.magnitude * 0f + 0.006f);

        // TextMeshPro is read from its -Z side, so its forward points into the truck.
        label.transform.localRotation = Quaternion.LookRotation(-outward, request.Up);

        var text = label.AddComponent<TextMeshPro>();

        text.text = request.Text;

        text.fontStyle = FontStyles.Bold;

        text.alignment = TextAlignmentOptions.Center;

        text.textWrappingMode = TextWrappingModes.NoWrap;

        text.color = request.Replaces.Colour;

        text.fontSize = 10f;

        text.rectTransform.sizeDelta = new Vector2(height * 3f, height * 1.4f);

        // Scale the text so its height matches the old digits (TMP at font size 10 sets digits about 0.74 units tall at scale 1).
        var scale = height / 0.74f;

        label.transform.localScale = Vector3.one * scale;

        text.rectTransform.sizeDelta /= scale;

        var renderer = label.GetComponent<MeshRenderer>();

        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }
}
