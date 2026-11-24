#nullable enable
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

/// <summary>
/// Writes Engine07's warning-light lenses (ApparatusLights._lenses) to
/// J:\Blender\Projects\HFD Apparatus\Engines\lenses_engine07.json: index, object name, mesh name, a role group, and the
/// lens centre and size in the procedural Engine43Visual's own unstretched frame, expressed in Engine07 space
/// (x right, y up, z forward). The Blender rebuild maps each group onto the new truck's lights and models a fixture at
/// every lens; InstallCleanEngine then moves each lens to its fixture. Read-only.
/// </summary>
public static class ExportEngineLenses
{
    private const string OutPath = @"J:\Blender\Projects\HFD Apparatus\Engines\lenses_engine07.json";

    public static string Run()
    {
        var engine = GameObject.Find("Engine07").transform;

        var visual = engine.Find("Engine43Visual");

        var lights = engine.GetComponent("ApparatusLights");

        var lenses = (Renderer[])lights.GetType().GetField("_lenses", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(lights);

        var json = new StringBuilder("[\n");

        for (var i = 0; i < lenses.Length; i++)
        {
            var lens = lenses[i];

            var mesh = lens.GetComponent<MeshFilter>()?.sharedMesh;

            // Centre and size in the visual's local frame (undoes any stretch), then the visual's normal placement.
            var localBounds = mesh != null ? mesh.bounds : new Bounds(Vector3.zero, Vector3.one * 0.1f);

            var centre = visual.InverseTransformPoint(lens.transform.TransformPoint(localBounds.center)) + new Vector3(0f, -0.15f, 0f);

            var size = Vector3.Scale(localBounds.size, Abs(visual.InverseTransformVector(lens.transform.TransformVector(Vector3.one))));

            json.Append(i > 0 ? ",\n" : "").Append("{\"i\":").Append(i)
                .Append(",\"name\":\"").Append(lens.name).Append("\",\"mesh\":\"").Append(mesh != null ? mesh.name : "").Append('"')
                .Append(",\"group\":\"").Append(Group(lens.name, mesh != null ? mesh.name : "")).Append('"')
                .Append(",\"c\":[").Append(F(centre.x)).Append(',').Append(F(centre.y)).Append(',').Append(F(centre.z)).Append(']')
                .Append(",\"s\":[").Append(F(size.x)).Append(',').Append(F(size.y)).Append(',').Append(F(size.z)).Append("]}");
        }

        File.WriteAllText(OutPath, json.Append("\n]\n").ToString());

        return $"Exported {lenses.Length} lenses to {OutPath}.";
    }

    private static string Group(string name, string mesh)
    {
        if (mesh.StartsWith("EmergencyLighting_Lens"))
        {
            return "bar";
        }

        foreach (var prefix in new[] { "FrontLens", "TurnSignal", "ClearanceLight", "SideMarker", "RearLamp", "RearUpperMarker" })
        {
            if (name.StartsWith(prefix))
            {
                return prefix;
            }
        }

        return "other";
    }

    private static Vector3 Abs(Vector3 v)
    {
        return new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
    }

    private static string F(float v)
    {
        return v.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
