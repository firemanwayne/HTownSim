#nullable enable
using System.IO;
using UnityEngine;

/// <summary>
/// Renders fixed comparison views of the city to PNGs (run in Play Mode so sky, traffic and lighting are live).
/// Entries <c>CaptureViews.RunBefore</c> / <c>RunAfter</c> write Blender\HTown\renders\unity_&lt;tag&gt;_&lt;view&gt;.png.
/// Uses a temporary camera that is destroyed afterwards; nothing in the scene changes.
/// </summary>
public static class CaptureViews
{
    /// <summary>Output folder.</summary>
    private static readonly string Folder = System.IO.Path.GetFullPath(System.IO.Path.Combine(UnityEngine.Application.dataPath, "..", "Blender", "HTown", "renders"));

    /// <summary>Output width; height follows 16:9.</summary>
    private const int Width = 1600;

    /// <summary>Renders the views tagged "before".</summary>
    public static string RunBefore()
    {
        return Capture("before", null);
    }

    /// <summary>Renders the views tagged "after".</summary>
    public static string RunAfter()
    {
        return Capture("after", null);
    }

    /// <summary>Renders only the house views, tagged "before".</summary>
    public static string HousesBefore()
    {
        return Capture("before", "houses");
    }

    /// <summary>Renders only the house views, tagged "after".</summary>
    public static string HousesAfter()
    {
        return Capture("after", "houses");
    }

    /// <summary>Named viewpoints: position and a point to look at (scene metres).</summary>
    private static readonly (string Name, Vector3 Eye, Vector3 Target)[] Views =
    {
        // Apartment block, 3011 San Jacinto St.
        ("apartment", new Vector3(-155.7f, 15.6f, 38.8f), new Vector3(-185.3f, 9.6f, 104.6f)),
        // Large single-storey shop, 1504 Anita St.
        ("shop", new Vector3(215f, 24f, 175f), new Vector3(155.35f, 2.25f, 118.4f)),
        // 28-floor glass tower, 2850 Fannin St.
        ("tower", new Vector3(-250f, 30f, 180f), new Vector3(-371.6f, 45f, 289.5f)),
        // 13-floor stone tower, 2000 Crawford St.
        ("stone", new Vector3(240f, 20f, 895f), new Vector3(146.6f, 20f, 986.3f)),
        // Townhome blocks around 1507 Tuam St.
        ("houses", new Vector3(60f, 45f, 230f), new Vector3(130f, 0f, 291f)),
        // Street level among the houses on Tuam St.
        ("houses_street", new Vector3(95f, 3f, 262f), new Vector3(160f, 4f, 300f)),
    };

    /// <summary>Renders the views whose name starts with <paramref name="only"/> (all views when null).</summary>
    private static string Capture(string tag, string? only)
    {
        var main = Camera.main;

        var go = new GameObject("CaptureCam") { hideFlags = HideFlags.HideAndDontSave };

        var camera = go.AddComponent<Camera>();

        if (main != null)
        {
            camera.CopyFrom(main);
        }

        camera.fieldOfView = 55f;

        camera.nearClipPlane = 0.3f;

        camera.farClipPlane = 4000f;

        var height = Width * 9 / 16;

        var texture = new RenderTexture(Width, height, 24) { antiAliasing = 4 };

        camera.targetTexture = texture;

        var read = new Texture2D(Width, height, TextureFormat.RGB24, false);

        var written = new System.Text.StringBuilder();

        foreach (var view in Views)
        {
            if (only is not null && !view.Name.StartsWith(only, System.StringComparison.Ordinal))
            {
                continue;
            }

            go.transform.position = view.Eye;

            go.transform.rotation = Quaternion.LookRotation(view.Target - view.Eye);

            camera.Render();

            RenderTexture.active = texture;

            read.ReadPixels(new Rect(0, 0, Width, height), 0, 0);

            read.Apply();

            var path = Path.Combine(Folder, $"unity_{tag}_{view.Name}.png");

            File.WriteAllBytes(path, read.EncodeToPNG());

            written.AppendLine(path);
        }

        RenderTexture.active = null;

        camera.targetTexture = null;

        Object.DestroyImmediate(texture);

        Object.DestroyImmediate(read);

        Object.DestroyImmediate(go);

        return written.ToString();
    }
}
