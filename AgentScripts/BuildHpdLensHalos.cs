using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Prepares the HPD patrol unit's lamps for the game, in two parts:
///  - writes Assets/HTownSim/Resources/HpdLenses.json, the glow list LensFlasher reads (the camera-facing halos that give the lightbar
///    and the headlamps their bloom): the model's lamps are in its mesh, so each lamp material's vertices are grouped into cells and
///    each cell becomes one glow, with a size from the cell's extent;
///  - gives the model's clear lens material (HPD_Clear_optic, which the Blender export left glowing a flat white, so the lightbar's clear
///    segments read as a white light in the middle of the bar) a plain unlit-looking glass material, through the model's import remap.
/// Can be run again after re-exporting the model. Run: unity command script run --file AgentScripts/BuildHpdLensHalos.cs --entry BuildHpdLensHalos.Run
/// </summary>
public static class BuildHpdLensHalos
{
    private const string ModelPath = "Assets/HTownSim/Vehicles/HpdPatrol/HpdPatrol.fbx";

    private const string JsonPath = "Assets/HTownSim/Resources/HpdLenses.json";

    private const string Folder = "Assets/HTownSim/Vehicles/HpdPatrol/";

    /// <summary>Materials the Blender export left too bright, and what replaces them: the clear lens segments glowed a flat white and the
    /// chrome rail on top of the lightbar mirrored the sky as a white strip; both read as a white light in the middle of the bar.</summary>
    private static readonly (string Name, Color Colour, float Smoothness, float Metallic)[] Replacements =
    {
        ("HPD_Clear_optic", new Color(0.30f, 0.36f, 0.42f, 1f), 0.85f, 0f),
        ("HPD_Chrome", new Color(0.36f, 0.38f, 0.41f, 1f), 0.5f, 0.6f),
    };

    private const string BodyName = "HPD_Body";

    /// <summary>Material name, glow group, and the cell size lamps are grouped into (metres).</summary>
    private static readonly (string Material, string Group, float Cell)[] Lamps =
    {
        ("HPD_Flash_Red", "Warning", 0.16f),
        ("HPD_Flash_Blue", "Warning", 0.16f),
        ("HPD_Steady_Headlight", "Headlight", 0.3f),
        ("HPD_Steady_Fog", "Fog", 0.3f),
    };

    /// <summary>How far above the top of the lightbar modules the warning glows are centred (metres).</summary>
    private const float WarningLift = 0.04f;

    /// <summary>The smallest glow size the list carries (the game clamps again).</summary>
    private const float MinimumSize = 0.1f;

    public static string Run()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);

        if (model == null)
        {
            return "Missing " + ModelPath;
        }

        var body = model.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(filter => filter.name == BodyName);

        if (body == null)
        {
            return "No " + BodyName + " in the model.";
        }

        var materials = body.GetComponent<MeshRenderer>().sharedMaterials;

        var mesh = body.sharedMesh;

        var vertices = mesh.vertices;

        var entries = new List<string>();

        foreach (var (material, group, cell) in Lamps)
        {
            var slot = System.Array.FindIndex(materials, m => m != null && m.name == material);

            if (slot < 0 || slot >= mesh.subMeshCount)
            {
                continue;
            }

            var cells = new Dictionary<(int, int, int), List<Vector3>>();

            foreach (var index in mesh.GetIndices(slot))
            {
                var v = vertices[index];

                // The lamps sit in rows (a lightbar, a pair of headlamps): cells are narrow across the row and tall along the other axes.
                var key = (Mathf.FloorToInt(v.x / cell), 0, Mathf.FloorToInt(v.z / (cell * 4f)));

                if (!cells.TryGetValue(key, out var list))
                {
                    list = new List<Vector3>();

                    cells[key] = list;
                }

                list.Add(v);
            }

            var n = 0;

            foreach (var points in cells.Values.OrderBy(p => p[0].x))
            {
                var min = points.Aggregate(Vector3.Min);

                var max = points.Aggregate(Vector3.Max);

                var centre = (min + max) / 2f;

                // A glow centred inside the lightbar is half hidden by its housing: lift the warning glows to the top of the bar.
                if (group == "Warning")
                {
                    centre.y = max.y + WarningLift;
                }

                var size = Mathf.Max(MinimumSize, (max - min).magnitude / 2f);

                entries.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "  {{\"name\": \"{0}_{1}\", \"group\": \"{2}\", \"material\": \"{0}\", \"x\": {3:0.###}, \"y\": {4:0.###}, \"z\": {5:0.###}, \"size\": {6:0.###}}}",
                    material, n++, group, centre.x, centre.y, centre.z, size));
            }
        }

        var json = new StringBuilder();

        json.AppendLine("{");

        json.AppendLine(" \"root\": \"" + BodyName + "\",");

        json.AppendLine(" \"lenses\": [");

        json.AppendLine(string.Join(",\n", entries));

        json.AppendLine(" ]");

        json.AppendLine("}");

        File.WriteAllText(JsonPath, json.ToString());

        var remapped = RemapClearOptic();

        AssetDatabase.Refresh();

        return "HpdLenses.json: " + entries.Count + " glows. Clear optic " + remapped;
    }

    /// <summary>Remaps the clear lens material to smoked glass without emission, and the chrome to a duller metal.</summary>
    private static string RemapClearOptic()
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);

        foreach (var (name, colour, smoothness, metallic) in Replacements)
        {
            var path = Folder + name + ".mat";

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };

                AssetDatabase.CreateAsset(material, path);
            }

            material.SetColor("_BaseColor", colour);

            material.SetFloat("_Smoothness", smoothness);

            material.SetFloat("_Metallic", metallic);

            material.SetColor("_EmissionColor", Color.black);

            material.DisableKeyword("_EMISSION");

            EditorUtility.SetDirty(material);

            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), name), material);
        }

        AssetDatabase.SaveAssets();

        importer.SaveAndReimport();

        return "remapped " + string.Join(", ", Replacements.Select(r => r.Name));
    }
}
