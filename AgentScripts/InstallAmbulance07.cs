#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Swaps the parked Medic 07 prop in Station 7's third bay for the clean Ambulance 07 model
/// (Assets/HTownSim/Vehicles/Ambulance07/Ambulance07.fbx, built by Blender\HFD Apparatus\EMS\build_ambulance07.py:
/// a Silverado 3500HD cab with a Frazer-type box, 7.3 m long, with real glass, round wheels on their axles (WheelFront* /
/// WheelRear*, duals at the rear), and a coloured lens cap on every warning, marker and tail light).
///
/// The procedural parts (127 of them) are kept, inactive, under PreviousMedicVisuals; the model goes in as
/// Ambulance07Visual with its tyres on the bay floor, and the root's box collider is resized to it. Apply refuses to run
/// with unsaved scene changes or twice; Revert restores the procedural ambulance. Both reopen the scene after saving.
/// </summary>
public static class InstallAmbulance07
{
    private const string Folder = "Assets/HTownSim/Vehicles/Ambulance07/";

    private const string ModelPath = Folder + "Ambulance07.fbx";

    private const string LiveryPath = Folder + "Ambulance07_Livery.png";

    private const string LogoPath = Folder + "HFD_Logo.png";

    private const string VisualName = "Ambulance07Visual";

    private const string PreviousName = "PreviousMedicVisuals";

    /// <summary>The model's size: 7.30 m long, 2.44 m wide (box), 2.95 m tall including the roof lights and mirrors.</summary>
    private static readonly Vector3 ColliderSize = new Vector3(2.6f, 3.0f, 7.4f);

    /// <summary>The procedural prop's collider, restored by Revert.</summary>
    private static readonly Vector3 OldColliderCentre = new Vector3(0f, 1.5f, -0.15f);

    private static readonly Vector3 OldColliderSize = new Vector3(2.3f, 3.0f, 7.1f);

    public static string Apply()
    {
        var scene = Check();

        var medic = GameObject.Find("Medic07").transform;

        if (medic.Find(VisualName) != null)
        {
            return "Already installed; run Revert first.";
        }

        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        ConfigureTextures();

        var materials = BuildMaterials();

        // Materials are mapped by NAME on import: Unity numbers a mesh's submeshes in order of first use, not in Blender's
        // slot order, so assigning by position put the paint on the dark recess faces.
        ConfigureModel(materials);

        // Park the procedural parts, inactive, under one container.
        var previous = new GameObject(PreviousName).transform;

        previous.SetParent(medic, false);

        foreach (var child in medic.Cast<Transform>().Where(t => t != previous).ToList())
        {
            child.SetParent(previous, true);
        }

        previous.gameObject.SetActive(false);

        // Tyres on the bay floor.
        var collider = medic.GetComponent<BoxCollider>();

        collider.enabled = false;

        var floorY = medic.position.y;

        if (Physics.Raycast(medic.position + Vector3.up * 3f, Vector3.down, out var hit, 10f, ~0, QueryTriggerInteraction.Ignore))
        {
            floorY = hit.point.y;
        }

        collider.enabled = true;

        var visual = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath), medic);

        visual.name = VisualName;

        visual.transform.localRotation = Quaternion.identity;

        visual.transform.localPosition = new Vector3(0f, floorY - medic.position.y, 0f);

        foreach (var renderer in visual.GetComponentsInChildren<MeshRenderer>(true))
        {
            renderer.shadowCastingMode = renderer.name is "Amb07_Glass" or "Amb07_Decals" ? ShadowCastingMode.Off : ShadowCastingMode.On;
        }

        collider.center = new Vector3(0f, visual.transform.localPosition.y + ColliderSize.y / 2f, 0f);

        collider.size = ColliderSize;

        var wheels = visual.GetComponentsInChildren<Transform>(true).Count(t => t.name.StartsWith("Wheel"));

        return Save(scene, $"Installed {VisualName}: {visual.GetComponentsInChildren<MeshRenderer>(true).Length} parts, {wheels} wheels, floor at y {floorY:0.###}, collider {collider.size}.");
    }

    public static string Revert()
    {
        var scene = Check();

        var medic = GameObject.Find("Medic07").transform;

        var visual = medic.Find(VisualName);

        if (visual != null)
        {
            UnityEngine.Object.DestroyImmediate(visual.gameObject);
        }

        var previous = medic.Find(PreviousName);

        if (previous != null)
        {
            foreach (var child in previous.Cast<Transform>().ToList())
            {
                child.SetParent(medic, true);
            }

            UnityEngine.Object.DestroyImmediate(previous.gameObject);
        }

        var collider = medic.GetComponent<BoxCollider>();

        collider.center = OldColliderCentre;

        collider.size = OldColliderSize;

        return Save(scene, "Restored the procedural Medic 07.");
    }

    private static UnityEngine.SceneManagement.Scene Check()
    {
        var scene = EditorSceneManager.GetActiveScene();

        if (EditorApplication.isPlaying || scene.isDirty)
        {
            throw new InvalidOperationException("Exit Play Mode and save or discard scene changes first.");
        }

        return scene;
    }

    private static string Save(UnityEngine.SceneManagement.Scene scene, string message)
    {
        EditorSceneManager.MarkSceneDirty(scene);

        EditorSceneManager.SaveScene(scene);

        AssetDatabase.SaveAssets();

        EditorSceneManager.OpenScene(scene.path, OpenSceneMode.Single);

        return message + " Scene saved.";
    }

    private static void ConfigureTextures()
    {
        Texture(LiveryPath, 4096, false);

        Texture(LogoPath, 1024, true);
    }

    /// <summary>Imports the FBX and remaps each Blender material name onto the matching URP Lit material asset.</summary>
    private static void ConfigureModel(IReadOnlyDictionary<string, Material> materials)
    {
        var model = (ModelImporter)AssetImporter.GetAtPath(ModelPath);

        model.importNormals = ModelImporterNormals.Import;

        model.importCameras = false;

        model.importLights = false;

        model.animationType = ModelImporterAnimationType.None;

        model.importAnimation = false;

        model.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;

        model.materialLocation = ModelImporterMaterialLocation.InPrefab;

        foreach (var pair in materials)
        {
            model.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "Amb07_" + pair.Key), pair.Value);
        }

        model.SaveAndReimport();
    }

    private static void Texture(string path, int size, bool alpha)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);

        importer.sRGBTexture = true;

        importer.alphaSource = TextureImporterAlphaSource.FromInput;

        importer.alphaIsTransparency = alpha;

        importer.mipmapEnabled = true;

        importer.anisoLevel = 16;

        importer.wrapMode = alpha ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;

        importer.maxTextureSize = size;

        importer.textureCompression = TextureImporterCompression.CompressedHQ;

        importer.SaveAndReimport();
    }

    private static Dictionary<string, Material> BuildMaterials()
    {
        if (!AssetDatabase.IsValidFolder(Folder + "Materials"))
        {
            AssetDatabase.CreateFolder(Folder.TrimEnd('/'), "Materials");
        }

        var lit = Shader.Find("Universal Render Pipeline/Lit");

        var m = new Dictionary<string, Material>();

        var paint = Material(lit, "Amb07_Paint", Color.white, 0f, 0.8f);

        paint.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(LiveryPath));

        m["Paint"] = paint;

        m["Interior"] = Material(lit, "Amb07_Interior", new Color(0.07f, 0.07f, 0.08f), 0f, 0.2f);

        m["Chrome"] = Material(lit, "Amb07_Chrome", new Color(0.85f, 0.86f, 0.88f), 1f, 0.9f);

        m["DarkTrim"] = Material(lit, "Amb07_DarkTrim", new Color(0.09f, 0.09f, 0.1f), 0f, 0.35f);

        m["WhiteTrim"] = Material(lit, "Amb07_WhiteTrim", new Color(0.72f, 0.74f, 0.76f), 0f, 0.55f);

        m["Rubber"] = Material(lit, "Amb07_Rubber", new Color(0.05f, 0.05f, 0.05f), 0f, 0.15f);

        m["LensRed"] = Material(lit, "Amb07_LensRed", new Color(0.62f, 0.06f, 0.06f), 0f, 0.92f);

        m["LensClear"] = Material(lit, "Amb07_LensClear", new Color(0.78f, 0.8f, 0.84f), 0f, 0.95f);

        m["LensAmber"] = Material(lit, "Amb07_LensAmber", new Color(0.8f, 0.42f, 0.04f), 0f, 0.92f);

        // The warning-light lenses are separate materials so the profile can flash just those.
        m["LensRedWarn"] = Material(lit, "Amb07_LensRedWarn", new Color(0.62f, 0.06f, 0.06f), 0f, 0.92f);

        m["LensRedWarn2"] = Material(lit, "Amb07_LensRedWarn2", new Color(0.62f, 0.06f, 0.06f), 0f, 0.92f);

        m["LensAmberWarn"] = Material(lit, "Amb07_LensAmberWarn", new Color(0.8f, 0.42f, 0.04f), 0f, 0.92f);

        // Dark grey tint, see-through.
        var glass = Material(lit, "Amb07_Glass", new Color(0.12f, 0.13f, 0.15f, 0.55f), 0f, 0.96f);

        Transparent(glass);

        m["Glass"] = glass;

        // The HFD shield on the cab doors: opaque with alpha clipping, so its outline is crisp.
        var logo = Material(lit, "Amb07_Logo", Color.white, 0f, 0.6f);

        logo.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(LogoPath));

        logo.SetFloat("_AlphaClip", 1f);

        logo.SetFloat("_Cutoff", 0.5f);

        logo.EnableKeyword("_ALPHATEST_ON");

        logo.SetOverrideTag("RenderType", "TransparentCutout");

        logo.renderQueue = (int)RenderQueue.AlphaTest;

        EditorUtility.SetDirty(logo);

        m["Logo"] = logo;

        return m;
    }

    private static Material Material(Shader shader, string name, Color colour, float metallic, float smoothness)
    {
        var path = Folder + "Materials/" + name + ".mat";

        var material = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (material == null)
        {
            material = new Material(shader) { name = name };

            AssetDatabase.CreateAsset(material, path);
        }

        material.shader = shader;

        material.SetColor("_BaseColor", colour);

        material.SetFloat("_Metallic", metallic);

        material.SetFloat("_Smoothness", smoothness);

        EditorUtility.SetDirty(material);

        return material;
    }

    private static void Transparent(Material material)
    {
        material.SetFloat("_Surface", 1f);

        material.SetFloat("_Blend", 0f);

        material.SetFloat("_ZWrite", 0f);

        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);

        material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);

        material.SetOverrideTag("RenderType", "Transparent");

        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

        material.renderQueue = (int)RenderQueue.Transparent;

        material.SetShaderPassEnabled("ShadowCaster", false);

        EditorUtility.SetDirty(material);
    }
}
