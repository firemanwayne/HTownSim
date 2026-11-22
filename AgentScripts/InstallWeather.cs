using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Installs the first ambience and weather pass in the saved Houston scene.</summary>
public static class InstallWeather
{
    /// <summary>Owns only generated environment assets.</summary>
    private const string Folder = "Assets/HTownSim/Environment/";

    /// <summary>Creates weather particles, replaceable audio clips and a compact HUD control hint.</summary>
    public static string Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();

        if (EditorApplication.isPlaying || scene.isDirty || scene.path != "Assets/Scenes/HoustonResponse.unity")
        {
            throw new InvalidOperationException("Expected saved HoustonResponse in Edit Mode.");
        }

        if (GameObject.Find("EnvironmentWeather") != null)
        {
            throw new InvalidOperationException("Weather is already installed.");
        }

        Directory.CreateDirectory(Folder);

        foreach (var kind in new[] { "City", "Wind", "Rain", "Diesel" })
        {
            WriteAudio(kind);
        }

        AssetDatabase.Refresh();

        var root = new GameObject("EnvironmentWeather");

        var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("HTownSim.Runtime.EnvironmentWeather")).First(t => t != null);

        var controller = root.AddComponent(type);

        var rain = new GameObject("LocalRain").AddComponent<ParticleSystem>();

        rain.transform.SetParent(root.transform, false);

        rain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = rain.main;

        main.playOnAwake = false;

        main.loop = true;

        main.simulationSpace = ParticleSystemSimulationSpace.World;

        main.maxParticles = 1500;

        main.startColor = new Color(0.72f, 0.82f, 0.92f, 0.5f);

        main.startSize = 0.035f;

        main.startLifetime = 1.1f;

        var emission = rain.emission;

        emission.enabled = false;

        var shape = rain.shape;

        shape.enabled = false;

        var collision = rain.collision;

        collision.enabled = true;

        collision.type = ParticleSystemCollisionType.World;

        collision.mode = ParticleSystemCollisionMode.Collision3D;

        collision.quality = ParticleSystemCollisionQuality.High;

        collision.collidesWith = ~0;

        collision.lifetimeLoss = 1;

        collision.enableDynamicColliders = false;

        collision.maxCollisionShapes = 256;

        var renderer = rain.GetComponent<ParticleSystemRenderer>();

        renderer.renderMode = ParticleSystemRenderMode.Stretch;

        renderer.velocityScale = 0.028f;

        renderer.lengthScale = 3;

        renderer.shadowCastingMode = ShadowCastingMode.Off;

        renderer.receiveShadows = false;

        var material = new Material(Shader.Find("Sprites/Default")) { name = "RainDrop" };

        var texture = new Texture2D(16, 16, TextureFormat.RGBA32, false);

        var pixels = new Color[256];

        for (var y = 0; y < 16; y++)
        {
            for (var x = 0; x < 16; x++)
            {
                var distance = new Vector2((x - 7.5f) / 8, (y - 7.5f) / 8).magnitude;

                pixels[y * 16 + x] = new Color(1, 1, 1, Mathf.Clamp01(1 - distance));
            }
        }

        texture.SetPixels(pixels);

        texture.Apply();

        AssetDatabase.CreateAsset(texture, Folder + "RainDrop.asset");

        material.mainTexture = texture;

        AssetDatabase.CreateAsset(material, Folder + "RainDrop.mat");

        renderer.sharedMaterial = material;

        var canvas = Object.FindObjectsByType<Canvas>().First(c => c.isRootCanvas);

        var panel = new GameObject("WeatherControls", typeof(RectTransform), typeof(Image));

        panel.transform.SetParent(canvas.transform, false);

        var rect = panel.GetComponent<RectTransform>();

        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;

        rect.anchoredPosition = new Vector2(-24, -24);

        rect.sizeDelta = new Vector2(570, 48);

        panel.GetComponent<Image>().color = new Color(0.015f, 0.05f, 0.065f, 0.92f);

        panel.GetComponent<Image>().raycastTarget = false;

        var label = new GameObject("WeatherLabel", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();

        label.transform.SetParent(panel.transform, false);

        label.rectTransform.anchorMin = Vector2.zero;

        label.rectTransform.anchorMax = Vector2.one;

        label.rectTransform.offsetMin = new Vector2(15, 5);

        label.rectTransform.offsetMax = new Vector2(-15, -5);

        label.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");

        label.fontSize = 19;

        label.fontStyle = FontStyles.Bold;

        label.alignment = TextAlignmentOptions.Center;

        label.color = new Color(1, 0.72f, 0.34f);

        label.raycastTarget = false;

        label.text = "F6  WEATHER: CLEAR    F7  AMBIENCE: ON";

        var data = new SerializedObject(controller);

        data.FindProperty("_response").objectReferenceValue = GameObject.Find("Engine07").GetComponent("FireResponse");

        data.FindProperty("_sun").objectReferenceValue = GameObject.Find("DuskSun").GetComponent<Light>();

        data.FindProperty("_rain").objectReferenceValue = rain;

        data.FindProperty("_label").objectReferenceValue = label;

        foreach (var kind in new[] { "City", "Wind", "Rain", "Diesel" })
        {
            data.FindProperty("_" + kind.ToLowerInvariant() + "Clip").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(Folder + kind + ".wav");
        }

        var roads = Object.FindObjectsByType<Renderer>().Where(r => r.sharedMaterial != null && (r.sharedMaterial.name == "Asphalt" || r.sharedMaterial.name == "LotAsphalt") && r.sharedMaterial.HasProperty("_Smoothness")).ToArray();

        var roadArray = data.FindProperty("_roads");

        roadArray.arraySize = roads.Length;

        for (var i = 0; i < roads.Length; i++)
        {
            roadArray.GetArrayElementAtIndex(i).objectReferenceValue = roads[i];
        }

        data.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);

        EditorSceneManager.SaveScene(scene);

        AssetDatabase.SaveAssets();

        return "Installed clear/overcast/rain, 4 synthesized audio loops, F6/F7 HUD, and " + roads.Length + " wet-road renderers.";
    }

    /// <summary>Writes original mono placeholder sound with a short loop-boundary fade.</summary>
    private static void WriteAudio(string kind)
    {
        const int rate = 22050;

        const int seconds = 8;

        var random = new System.Random(kind == "City" ? 31 : kind == "Wind" ? 42 : kind == "Rain" ? 53 : 64);

        var path = Folder + kind + ".wav";

        using (var writer = new BinaryWriter(File.Create(path)))
        {
            var count = rate * seconds;

            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));

            writer.Write(36 + count * 2);

            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));

            writer.Write(16);

            writer.Write((short)1);

            writer.Write((short)1);

            writer.Write(rate);

            writer.Write(rate * 2);

            writer.Write((short)2);

            writer.Write((short)16);

            writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));

            writer.Write(count * 2);

            var low = 0f;

            for (var i = 0; i < count; i++)
            {
                var time = i / (float)rate;

                var noise = (float)random.NextDouble() * 2 - 1;

                low += (noise - low) * (kind == "Rain" ? 0.3f : 0.035f);

                var phase = time * Mathf.PI * 2;

                var sample = kind == "Diesel" ? Mathf.Sin(phase * 40) * 0.35f + Mathf.Sin(phase * 80) * 0.14f + Mathf.Sin(phase * 120) * 0.06f + low * 0.35f : kind == "Rain" ? (noise * 0.4f + low * 0.4f) : low * (1.6f + 0.5f * Mathf.Sin(phase / 8));

                if (kind == "City")
                {
                    sample += 0.055f * Mathf.Sin(phase * 72) * (0.5f + 0.5f * Mathf.Sin(phase / 4));
                }

                var edge = Mathf.Min(1, Mathf.Min(time, seconds - time) / 0.02f);

                writer.Write((short)(Mathf.Clamp(sample * edge, -0.9f, 0.9f) * short.MaxValue));
            }
        }
    }
}
