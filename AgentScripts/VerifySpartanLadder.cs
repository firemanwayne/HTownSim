#nullable enable
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Read-only check of the Spartan ladders in the open city scene (see InstallSpartanLadder). Run: unity command script run --file AgentScripts/VerifySpartanLadder.cs --entry VerifySpartanLadder.Report</summary>
public static class VerifySpartanLadder
{
    public static string Report()
    {
        var scene = SceneManager.GetActiveScene();

        var roots = scene.GetRootGameObjects();

        var text = new StringBuilder("scene " + scene.path + " dirty=" + scene.isDirty + "\n");

        Describe(text, roots.First(root => root.name == "Ladder07").transform);

        var group = roots.First(root => root.name == "Station16Apparatus").transform;

        Describe(text, group.Find("Ladder16"));

        return text.ToString();
    }

    private static void Describe(StringBuilder text, Transform unit)
    {
        text.AppendLine("== " + unit.name + " children: " + string.Join(", ", unit.Cast<Transform>().Select(child => child.name + (child.gameObject.activeSelf ? "" : "(off)"))));

        var visual = unit.Cast<Transform>().FirstOrDefault(child => child.name == "Ladder07Visual");

        if (visual == null)
        {
            text.AppendLine("   NO Ladder07Visual");

            return;
        }

        var source = PrefabUtility.GetCorrespondingObjectFromSource(visual.gameObject);

        text.AppendLine("   prefab: " + (source != null ? AssetDatabase.GetAssetPath(source) : "none") + "  localPos " + visual.localPosition + "  localRot " + visual.localEulerAngles);

        var lod = visual.GetComponent<LODGroup>();

        text.AppendLine("   LODGroup levels: " + (lod != null ? lod.GetLODs().Length : 0));

        var renderers = visual.GetComponentsInChildren<Renderer>(true);

        text.AppendLine("   renderers: " + renderers.Length);

        var missing = renderers.Where(renderer => renderer.sharedMaterials.Any(material => material == null || material.shader == null || material.shader.name == "Hidden/InternalErrorShader")).Select(renderer => renderer.name).ToList();

        text.AppendLine("   renderers with a missing or error material: " + missing.Count + (missing.Count > 0 ? " e.g. " + string.Join(", ", missing.Take(5)) : ""));

        foreach (var bar in renderers.Where(renderer => renderer.name.StartsWith("Spartan_Lightbar_")))
        {
            text.AppendLine("   " + bar.name + ": " + string.Join(" | ", bar.sharedMaterials.Select(material => material.name + (material.renderQueue >= 3000 ? "[transparent]" : ""))));
        }

        var markings = renderers.OfType<MeshRenderer>().Where(renderer => renderer.name.Contains("marking")).ToList();

        var vertices = markings.Select(renderer => renderer.GetComponent<MeshFilter>().sharedMesh.vertexCount).Sum();

        text.AppendLine("   unit markings: " + markings.Count + ", " + vertices + " vertices");

        var pivots = new[] { "CTRL_Turntable", "CTRL_Aerial_Elevation", "CTRL_Aerial_Fly_1", "CTRL_Aerial_Fly_2", "CTRL_Aerial_Fly_3" };

        text.AppendLine("   aerial pivots found: " + pivots.Count(name => visual.GetComponentsInChildren<Transform>(true).Any(t => t.name == name)) + "/" + pivots.Length);

        var wheels = visual.GetComponentsInChildren<Transform>(true).Count(t => t.name.StartsWith("L07_wheel_"));

        text.AppendLine("   wheel parts (L07_wheel_*): " + wheels);

        var box = unit.GetComponent<BoxCollider>();

        text.AppendLine("   collider: " + (box != null ? box.center + " " + box.size : "none"));
    }
}
