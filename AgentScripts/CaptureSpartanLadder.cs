#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Renders the two Spartan ladders from the open city scene to PNG files in the project's Temp folder, to look at the model, its number
/// and the lightbars (lit in the first flash phase, then the second). It photographs a temporary copy with the real ladder's renderers
/// switched off for the moment, so the scene and the materials are not changed.
/// Run: unity command script run --file AgentScripts/CaptureSpartanLadder.cs --entry CaptureSpartanLadder.Capture
/// </summary>
public static class CaptureSpartanLadder
{
    private const int Width = 1280;

    private const int Height = 720;

    private static readonly Color RedGlow = new Color(1f, 0.1f, 0.05f);

    private static readonly Color WhiteGlow = new Color(1f, 0.95f, 0.85f);

    public static string Capture()
    {
        var roots = SceneManager.GetActiveScene().GetRootGameObjects();

        var folder = Path.Combine(Directory.GetCurrentDirectory(), "Temp");

        Directory.CreateDirectory(folder);

        var written = new List<string>();

        var prop = roots.First(root => root.name == "Ladder07").transform;

        var unit16 = roots.First(root => root.name == "Station16Apparatus").transform.Find("Ladder16");

        foreach (var (label, source) in new[] { ("16", unit16.Find("Ladder07Visual")), ("07", prop.Find("Ladder07Visual")) })
        {
            // The real ladder is hidden while its lit copy is photographed, and shown again below.
            var hidden = source.GetComponentsInChildren<Renderer>().Where(renderer => renderer.enabled).ToList();

            foreach (var renderer in hidden)
            {
                renderer.enabled = false;
            }

            foreach (var phase in new[] { "A", "B" })
            {
                var copy = Object.Instantiate(source.gameObject, source.position, source.rotation);

                copy.name = "TempCapture" + label;

                copy.hideFlags = HideFlags.HideAndDontSave;

                foreach (var renderer in copy.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.enabled = true;
                }

                // Phase A: the driver's red panel and the officer's white one; phase B: the other two (the profile's flash groups).
                if (phase == "A")
                {
                    Light(copy, "Spartan_LB_Red_Driver", RedGlow, 8f);

                    Light(copy, "Spartan_LB_White_Officer", WhiteGlow, 8f);
                }
                else
                {
                    Light(copy, "Spartan_LB_Red_Officer", RedGlow, 8f);

                    Light(copy, "Spartan_LB_White_Driver", WhiteGlow, 8f);
                }

                var forward = copy.transform.forward;

                var right = copy.transform.right;

                var bars = copy.GetComponentsInChildren<Renderer>().Where(renderer => renderer.name.StartsWith("Spartan_Lightbar_")).ToList();

                var barCentre = bars.Aggregate(Vector3.zero, (sum, renderer) => sum + renderer.bounds.center) / bars.Count;

                written.Add(Shoot(folder, "ladder" + label + "_lightbars_" + phase, barCentre + (forward * 4.5f) + (Vector3.up * 2.2f), barCentre, 38f));

                if (phase == "A")
                {
                    var bounds = copy.GetComponentsInChildren<Renderer>().Aggregate(new Bounds(copy.transform.position, Vector3.zero), (box, renderer) =>
                    {
                        box.Encapsulate(renderer.bounds);

                        return box;
                    });

                    written.Add(Shoot(folder, "ladder" + label + "_side", bounds.center + (right * 17f) + (Vector3.up * 3f) + (forward * -2f), bounds.center + (Vector3.up * 1.6f), 45f));

                    written.Add(Shoot(folder, "ladder" + label + "_rear", bounds.center + (forward * -17f) + (right * 5f) + (Vector3.up * 3.5f), bounds.center + (Vector3.up * 1.6f), 45f));
                }

                Object.DestroyImmediate(copy);
            }

            foreach (var renderer in hidden)
            {
                renderer.enabled = true;
            }
        }

        return string.Join("\n", written);
    }

    /// <summary>Lights every renderer's material of that name (on the temporary copy only) so a flashing lens shows as it does lit.</summary>
    private static void Light(GameObject copy, string materialName, Color colour, float strength)
    {
        foreach (var renderer in copy.GetComponentsInChildren<Renderer>(true))
        {
            var materials = renderer.sharedMaterials;

            var changed = false;

            for (var i = 0; i < materials.Length; i++)
            {
                if (materials[i] == null || materials[i].name != materialName)
                {
                    continue;
                }

                var lit = new Material(materials[i]);

                lit.EnableKeyword("_EMISSION");

                lit.SetColor("_EmissionColor", colour * strength);

                materials[i] = lit;

                changed = true;
            }

            if (changed)
            {
                renderer.sharedMaterials = materials;
            }
        }
    }

    private static string Shoot(string folder, string name, Vector3 from, Vector3 at, float fieldOfView)
    {
        var holder = new GameObject("TempCaptureCamera") { hideFlags = HideFlags.HideAndDontSave };

        var camera = holder.AddComponent<Camera>();

        camera.transform.position = from;

        camera.transform.LookAt(at);

        camera.fieldOfView = fieldOfView;

        camera.nearClipPlane = 0.2f;

        camera.farClipPlane = 400f;

        camera.clearFlags = CameraClearFlags.SolidColor;

        camera.backgroundColor = new Color(0.55f, 0.65f, 0.75f);

        var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);

        camera.targetTexture = target;

        camera.Render();

        RenderTexture.active = target;

        var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);

        image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);

        image.Apply();

        var path = Path.Combine(folder, name + ".png");

        File.WriteAllBytes(path, image.EncodeToPNG());

        RenderTexture.active = null;

        camera.targetTexture = null;

        Object.DestroyImmediate(target);

        Object.DestroyImmediate(image);

        Object.DestroyImmediate(holder);

        return path;
    }
}
