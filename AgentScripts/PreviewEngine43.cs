using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Provides a separate studio scene and validation for the reusable engine asset.</summary>
public static class PreviewEngine43
{
    /// <summary>Asset folder owned by the engine authoring workflow.</summary>
    private const string Folder = "Assets/HTownSim/Vehicles/Engine43/";

    /// <summary>Creates a studio without editing the gameplay scene.</summary>
    public static string Create()
    {
        if (EditorApplication.isPlaying || EditorSceneManager.GetActiveScene().isDirty)
        {
            throw new InvalidOperationException("Stop Play Mode and save the current scene before opening the preview.");
        }

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "HoustonEngine43.prefab");

        if (prefab == null)
        {
            throw new InvalidOperationException("Build HoustonEngine43.prefab first.");
        }

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(Folder + "Engine43Preview.unity") != null)
        {
            EditorSceneManager.OpenScene(Folder + "Engine43Preview.unity");

            return "Opened existing preview.";
        }

        var previous = EditorSceneManager.GetActiveScene().path;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        PrefabUtility.InstantiatePrefab(prefab, scene);

        var camera = new GameObject("Engine43PreviewCamera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();

        camera.tag = "MainCamera";

        camera.clearFlags = CameraClearFlags.SolidColor;

        camera.backgroundColor = new Color(0.12f, 0.15f, 0.19f);

        camera.fieldOfView = 34f;

        camera.nearClipPlane = 0.1f;

        camera.farClipPlane = 100f;

        camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;

        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);

        floor.name = "StudioFloor";

        floor.transform.localScale = Vector3.one * 10f;

        var floorMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));

        floorMaterial.name = "StudioFloor";

        floorMaterial.color = new Color(0.2f, 0.24f, 0.28f);

        floorMaterial.SetFloat("_Smoothness", 0.25f);

        AssetDatabase.CreateAsset(floorMaterial, Folder + "StudioFloor.mat");

        floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;

        RenderSettings.ambientMode = AmbientMode.Trilight;

        RenderSettings.ambientSkyColor = new Color(0.7f, 0.78f, 0.9f);

        RenderSettings.ambientEquatorColor = new Color(0.48f, 0.52f, 0.6f);

        RenderSettings.ambientGroundColor = new Color(0.22f, 0.24f, 0.28f);

        RenderSettings.fog = false;

        var reflection = new Cubemap(32, TextureFormat.RGBA32, false);

        reflection.name = "StudioReflection";

        for (var face = 0; face < 6; face++)
        {
            var pixels = new Color[32 * 32];

            for (var y = 0; y < 32; y++)
            {
                for (var x = 0; x < 32; x++)
                {
                    var softbox = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(x - 15.5f) / 13f), 2f);

                    var brightness = face == 3 ? 0.12f : 0.3f + softbox * 0.65f;

                    pixels[y * 32 + x] = new Color(brightness * 0.92f, brightness * 0.96f, brightness);
                }
            }

            reflection.SetPixels(pixels, (CubemapFace)face);
        }

        reflection.Apply();

        AssetDatabase.CreateAsset(reflection, Folder + "StudioReflection.asset");

        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;

        RenderSettings.customReflectionTexture = reflection;

        Light("KeyLight", (new Vector3(45, -35, 0), new Color(1f, 0.92f, 0.81f), 2f));

        Light("FillLight", (new Vector3(25, 140, 0), new Color(0.7f, 0.82f, 1f), 0.8f));

        var volume = new GameObject("StudioGrade").AddComponent<Volume>();

        volume.isGlobal = true;

        var profile = ScriptableObject.CreateInstance<VolumeProfile>();

        profile.Add<Tonemapping>().mode.Override(TonemappingMode.ACES);

        AssetDatabase.CreateAsset(profile, Folder + "StudioGrade.asset");

        foreach (var component in profile.components)
        {
            AssetDatabase.AddObjectToAsset(component, profile);
        }

        volume.sharedProfile = profile;

        SetView("front");

        EditorSceneManager.SaveScene(scene, Folder + "Engine43Preview.unity");

        AssetDatabase.SaveAssets();

        return "Preview saved. Previous scene: " + previous;
    }

    /// <summary>Sets a useful review angle without entering Play Mode.</summary>
    public static string SetView(string view)
    {
        var camera = GameObject.Find("Engine43PreviewCamera").GetComponent<Camera>();

        camera.transform.position = view == "rear" ? new Vector3(11, 6.8f, -14) : view == "side" ? new Vector3(17, 5, 0) : new Vector3(11, 6.8f, 14);

        camera.transform.LookAt(new Vector3(0, 1.65f, 0));

        return view;
    }

    /// <summary>Checks persisted meshes, materials, script references, size and default light state.</summary>
    public static string Validate()
    {
        var path = Folder + "HoustonEngine43.prefab";

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        var root = PrefabUtility.LoadPrefabContents(path);

        try
        {
            var meshes = root.GetComponentsInChildren<MeshFilter>(true);

            var renderers = root.GetComponentsInChildren<Renderer>(true);

            var invalidMeshes = meshes.Count(m => m.GetComponent<TMPro.TMP_Text>() == null && (m.sharedMesh == null || !EditorUtility.IsPersistent(m.sharedMesh)));

            var invalidMaterials = renderers.Sum(r => r.sharedMaterials.Count(m => m == null || m.shader == null || !m.shader.isSupported));

            var missingScripts = root.GetComponentsInChildren<Transform>(true).Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));

            var lightsOn = root.GetComponentsInChildren<Light>(true).Count(l => l.enabled && l.intensity > 0f);

            var bounds = renderers[0].bounds;

            foreach (var renderer in renderers)
            {
                bounds.Encapsulate(renderer.bounds);
            }

            if (invalidMeshes + invalidMaterials + missingScripts + lightsOn > 0)
            {
                throw new InvalidOperationException($"Invalid meshes={invalidMeshes}; invalid materials={invalidMaterials}; missing scripts={missingScripts}; active lights={lightsOn}");
            }

            return $"PASS: persistent mesh assets, supported materials, no missing scripts, emergency lights off. Renderers={renderers.Length}; mesh instances={meshes.Length}; triangles={meshes.Sum(m => m.sharedMesh.triangles.Length / 3)}; bounds={bounds.size}";
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>Sets a studio directional light.</summary>
    private static void Light(string name, (Vector3 Rotation, Color Color, float Intensity) spec)
    {
        var light = new GameObject(name).AddComponent<Light>();

        light.type = LightType.Directional;

        light.transform.rotation = Quaternion.Euler(spec.Rotation);

        light.color = spec.Color;

        light.intensity = spec.Intensity;

        light.shadows = name == "KeyLight" ? LightShadows.Soft : LightShadows.None;
    }
}

