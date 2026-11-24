#nullable enable
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Writes every procedural building (shops, apartments, towers and houses) under MidtownReal/Buildings and
/// Buildings_Exp to J:\Blender\Projects\HTown\data\unity_buildings.json, so the Blender building pass can rebuild them
/// on their real footprints at exactly the same heights and wall materials. Read-only: the scene is not changed.
/// </summary>
public static class ExportBuildings
{
    /// <summary>Where the Blender project reads the list from.</summary>
    private const string OutputPath = @"J:\Blender\Projects\HTown\data\unity_buildings.json";

    /// <summary>Section roots that hold the buildings.</summary>
    private static readonly string[] Roots = { "Buildings", "Buildings_Exp" };

    /// <summary>Building name prefixes that are exported (the style written by BuildRealMidtown).</summary>
    private static readonly string[] Styles = { "Shop", "Apartment", "Glass", "Striped", "House" };

    /// <summary>Exports the list and returns a summary.</summary>
    public static string Run()
    {
        var root = GameObject.Find("MidtownReal");

        if (root == null)
        {
            return "MidtownReal not found.";
        }

        var json = new StringBuilder("[\n");

        var count = 0;

        foreach (var sectionName in Roots)
        {
            var section = root.transform.Find(sectionName);

            if (section == null)
            {
                continue;
            }

            for (var i = 0; i < section.childCount; i++)
            {
                var building = section.GetChild(i);

                var style = StyleOf(building.name);

                var body = building.Find("Body");

                if (style is null || body == null)
                {
                    continue;
                }

                var renderer = body.GetComponent<Renderer>();

                var podium = building.Find("Podium");

                var facing = FacingOf(building, body);

                if (count > 0)
                {
                    json.Append(",\n");
                }

                json.Append("{\"key\":\"").Append(sectionName).Append('/').Append(i.ToString(CultureInfo.InvariantCulture)).Append("\",");

                json.Append("\"name\":\"").Append(building.name.Replace("\"", "")).Append("\",");

                json.Append("\"style\":\"").Append(style).Append("\",");

                json.Append("\"mat\":\"").Append(renderer.sharedMaterial.name).Append("\",");

                json.Append("\"body\":").Append(BoundsJson(renderer.bounds)).Append(',');

                json.Append("\"podium\":").Append(podium == null ? "null" : BoundsJson(podium.GetComponent<Renderer>().bounds)).Append(',');

                json.Append("\"facing\":[").Append(F(facing.x)).Append(',').Append(F(facing.z)).Append("]}");

                count++;
            }
        }

        json.Append("\n]\n");

        File.WriteAllText(OutputPath, json.ToString());

        return $"Exported {count} buildings to {OutputPath}.";
    }

    /// <summary>The style prefix of a building section name, or null for houses and anything else.</summary>
    private static string? StyleOf(string name)
    {
        foreach (var style in Styles)
        {
            if (name.StartsWith(style + "_", System.StringComparison.Ordinal))
            {
                return style;
            }
        }

        return null;
    }

    /// <summary>Street-facing direction, read from the storefront or window strip placed on that face.</summary>
    private static Vector3 FacingOf(Transform building, Transform body)
    {
        foreach (var part in new[] { "Storefront", "Windows" })
        {
            var face = building.Find(part);

            if (face == null)
            {
                continue;
            }

            var offset = face.position - body.position;

            offset.y = 0f;

            return Mathf.Abs(offset.x) > Mathf.Abs(offset.z) ? new Vector3(Mathf.Sign(offset.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(offset.z));
        }

        return Vector3.zero;
    }

    /// <summary>Axis-aligned bounds as {"c":[x,y,z],"s":[x,y,z]}.</summary>
    private static string BoundsJson(Bounds b)
    {
        return "{\"c\":[" + F(b.center.x) + "," + F(b.center.y) + "," + F(b.center.z) + "],\"s\":[" + F(b.size.x) + "," + F(b.size.y) + "," + F(b.size.z) + "]}";
    }

    /// <summary>Invariant two-decimal number.</summary>
    private static string F(float v)
    {
        return v.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
