#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Puts the clean hard-surface Engine 07 (built in Blender by Blender\HFD Apparatus\Engines\build_engine07.py,
/// livery drawn by make_livery.py) on Engine07, replacing the lumpy Hi3D visual.
///
/// Model: Assets/HTownSim/Vehicles/Engine07Hi3D/Engine07Clean.fbx: Engine07_Shell (livery atlas), Engine07_Glass
/// (dark grey tint, 45% transmittance per the Spartan spec), Engine07_Trim (chrome and dark trim), Engine07_Lights
/// (a housing and coloured lens cap at every warning light) and four wheels pivoting on their axles.
///
/// The game's 91 ApparatusLights lenses stay on the old Engine43Visual (its other renderers stay hidden), but each is
/// turned into a box that exactly covers its fixture's lens cap, from lens_targets_engine07.json. While off, the lens
/// is invisible and the modelled cap shows; when it flashes, the glowing lens sits on the cap. Apply refuses to run with
/// unsaved scene changes or twice; Revert brings back the Hi3D install. Both reload the scene after saving.
/// </summary>
public static class InstallCleanEngine
{
    private const string Folder = "Assets/HTownSim/Vehicles/Engine07Hi3D/";

    private const string ModelPath = Folder + "Engine07Clean.fbx";

    private const string LiveryPath = Folder + "Engine07_Livery.png";

    /// <summary>HFD logo for both front doors, trimmed and resized by make_livery.py from Sample-Asset-Images.</summary>
    private const string LogoPath = Folder + "HFD_Logo.png";

    /// <summary>Texas state flag for both cab sides above the front wheel, cut out by make_livery.py.</summary>
    private const string TexasPath = Folder + "Texas_Flag.png";

    private static readonly string TargetsPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(UnityEngine.Application.dataPath, "..", "Blender", "HFD Apparatus", "Engines", "lens_targets_engine07.json"));

    private const string VisualName = "Engine07CleanVisual";

    private const string Hi3DName = "Engine07Hi3DVisual";

    private const string OldVisualName = "Engine43Visual";

    private const float VisualDrop = -0.15f;

    /// <summary>The flashing lens box is this much larger than the modelled cap, so it covers it when lit.</summary>
    private const float LensMargin = 0.006f;

    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    public static string Apply()
    {
        var scene = Check();

        var engine = GameObject.Find("Engine07").transform;

        if (engine.Find(VisualName) != null)
        {
            return "Already installed; run Revert first.";
        }

        ConfigureImports();

        var materials = BuildMaterials();

        // The Hi3D visual is deactivated rather than deleted, so Revert can bring it back.
        var hi3d = engine.Find(Hi3DName);

        if (hi3d != null)
        {
            hi3d.gameObject.SetActive(false);
        }

        var visual = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath), engine);

        visual.name = VisualName;

        visual.transform.localPosition = new Vector3(0f, VisualDrop, 0f);

        visual.transform.localRotation = Quaternion.identity;

        foreach (var renderer in visual.GetComponentsInChildren<MeshRenderer>())
        {
            renderer.sharedMaterials = MaterialsFor(renderer.name, materials);

            renderer.shadowCastingMode = renderer.name is "Engine07_Glass" or "Engine07_Decals" or "Engine07_BarDome" or "Engine07_SceneLenses"
                ? ShadowCastingMode.Off
                : ShadowCastingMode.On;
        }

        // Old visual back to its own size: its lenses are placed absolutely below.
        var old = engine.Find(OldVisualName);

        old.localScale = Vector3.one;

        old.localPosition = new Vector3(0f, VisualDrop, 0f);

        var lights = engine.GetComponent("ApparatusLights");

        var lenses = (Renderer[])lights.GetType().GetField("_lenses", Flags)!.GetValue(lights);

        var targets = ReadTargets();

        // Every lens is saved in its "off" state: ApparatusLights only swaps materials when a lens changes state, so a
        // lens saved lit would stay lit while the lights are off.
        var offMaterials = (Material[])lights.GetType().GetField("_off", Flags)!.GetValue(lights);

        for (var i = 0; i < lenses.Length; i++)
        {
            lenses[i].sharedMaterial = offMaterials[i];
        }

        var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");

        foreach (var target in targets)
        {
            var lens = lenses[target.Index];

            // Axis-aligned box: width across the face, height up, depth along the normal.
            var size = new Vector3(target.Size.x, target.Size.y, target.Size.z) + Vector3.one * LensMargin;

            var extent = Mathf.Abs(target.Normal.x) > 0.5f
                ? new Vector3(size.z, size.y, size.x)
                : new Vector3(size.x, size.y, size.z);

            lens.GetComponent<MeshFilter>().sharedMesh = cube;

            lens.transform.rotation = engine.rotation;

            lens.transform.position = engine.TransformPoint(target.Centre);

            var parentScale = lens.transform.parent.lossyScale;

            lens.transform.localScale = new Vector3(extent.x / parentScale.x, extent.y / parentScale.y, extent.z / parentScale.z);

            lens.shadowCastingMode = ShadowCastingMode.Off;

            lens.enabled = true;
        }

        return Save(scene, $"Installed {VisualName}; {targets.Count} of {lenses.Length} lenses moved onto their fixtures, Hi3D visual deactivated.");
    }

    public static string Revert()
    {
        var scene = Check();

        var engine = GameObject.Find("Engine07").transform;

        var visual = engine.Find(VisualName);

        if (visual != null)
        {
            UnityEngine.Object.DestroyImmediate(visual.gameObject);
        }

        var hi3d = engine.Find(Hi3DName);

        if (hi3d != null)
        {
            hi3d.gameObject.SetActive(true);
        }

        var lights = engine.GetComponent("ApparatusLights");

        var lenses = (Renderer[])lights.GetType().GetField("_lenses", Flags)!.GetValue(lights);

        foreach (var lens in lenses)
        {
            if (PrefabUtility.IsPartOfPrefabInstance(lens))
            {
                PrefabUtility.RevertObjectOverride(lens.transform, InteractionMode.AutomatedAction);

                PrefabUtility.RevertObjectOverride(lens.GetComponent<MeshFilter>(), InteractionMode.AutomatedAction);
            }
        }

        // Back to the Hi3D install's stretch (see InstallHi3DEngine).
        var old = engine.Find(OldVisualName);

        old.localScale = new Vector3(0.95f, 0.91f, 10.09f / 9.38f);

        old.localPosition = new Vector3(0f, VisualDrop, 0.005f + 0.23f * (10.09f / 9.38f));

        return Save(scene, "Removed the clean visual and restored the Hi3D install.");
    }

    private static Material[] MaterialsFor(string rendererName, IReadOnlyDictionary<string, Material> m)
    {
        return rendererName switch
        {
            "Engine07_Shell" => new[] { m["Paint"], m["Interior"] },
            "Engine07_Glass" => new[] { m["Glass"] },
            "Engine07_Trim" => new[] { m["Chrome"], m["DarkTrim"], m["RedTrim"], m["WhiteTrim"] },
            "Engine07_Lights" => new[] { m["LensRed"], m["LensClear"], m["LensAmber"], m["DarkTrim"] },
            "Engine07_Decals" => new[] { m["Logo"], m["Texas"] },
            "Engine07_BarDome" => new[] { m["BarDome"] },
            "Engine07_SceneLenses" => new[] { m["SceneLens"] },
            _ => new[] { m["Rubber"], m["Chrome"] },
        };
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

    private static void ConfigureImports()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        var model = (ModelImporter)AssetImporter.GetAtPath(ModelPath);

        model.materialImportMode = ModelImporterMaterialImportMode.None;

        model.importNormals = ModelImporterNormals.Import;

        model.importCameras = false;

        model.importLights = false;

        model.animationType = ModelImporterAnimationType.None;

        model.importAnimation = false;

        model.SaveAndReimport();

        var livery = (TextureImporter)AssetImporter.GetAtPath(LiveryPath);

        livery.sRGBTexture = true;

        livery.mipmapEnabled = true;

        livery.anisoLevel = 16;

        livery.maxTextureSize = 4096;

        livery.textureCompression = TextureImporterCompression.CompressedHQ;

        livery.SaveAndReimport();

        DecalTexture(LogoPath);

        DecalTexture(TexasPath);
    }

    /// <summary>Imports a decal texture: sRGB with alpha, clamped, mipmapped.</summary>
    private static void DecalTexture(string path)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);

        importer.sRGBTexture = true;

        importer.alphaSource = TextureImporterAlphaSource.FromInput;

        importer.alphaIsTransparency = true;

        importer.mipmapEnabled = true;

        importer.anisoLevel = 8;

        importer.wrapMode = TextureWrapMode.Clamp;

        importer.maxTextureSize = 1024;

        importer.textureCompression = TextureImporterCompression.CompressedHQ;

        importer.SaveAndReimport();
    }

    private static Dictionary<string, Material> BuildMaterials()
    {
        var lit = Shader.Find("Universal Render Pipeline/Lit");

        var m = new Dictionary<string, Material>();

        var paint = Material(lit, "Engine07Clean_Paint", Color.white, 0f, 0.78f);

        paint.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(LiveryPath));

        m["Paint"] = paint;

        m["Interior"] = Material(lit, "Engine07Clean_Interior", new Color(0.07f, 0.07f, 0.08f), 0f, 0.2f);

        m["Chrome"] = Material(lit, "Engine07Clean_Chrome", new Color(0.85f, 0.86f, 0.88f), 1f, 0.9f);

        m["DarkTrim"] = Material(lit, "Engine07Clean_DarkTrim", new Color(0.09f, 0.09f, 0.1f), 0f, 0.35f);

        m["Rubber"] = Material(lit, "Engine07Clean_Rubber", new Color(0.05f, 0.05f, 0.05f), 0f, 0.15f);

        m["RedTrim"] = Material(lit, "Engine07Clean_RedTrim", new Color(0.62f, 0.06f, 0.06f), 0f, 0.78f);

        m["WhiteTrim"] = Material(lit, "Engine07Clean_WhiteTrim", new Color(0.90f, 0.91f, 0.92f), 0f, 0.7f);

        m["LensRed"] = Material(lit, "Engine07Clean_LensRed", new Color(0.55f, 0.04f, 0.04f), 0f, 0.92f);

        m["LensClear"] = Material(lit, "Engine07Clean_LensClear", new Color(0.78f, 0.8f, 0.84f), 0f, 0.95f);

        m["LensAmber"] = Material(lit, "Engine07Clean_LensAmber", new Color(0.8f, 0.42f, 0.04f), 0f, 0.92f);

        // Dark grey tint, 45% transmittance (spec): see-through, glossy.
        var glass = Material(lit, "Engine07Clean_Glass", new Color(0.12f, 0.13f, 0.15f, 0.55f), 0f, 0.96f);

        Transparent(glass);

        m["Glass"] = glass;

        // Light bar dome: clear polycarbonate, so the flashing LED modules inside show through.
        var dome = Material(lit, "Engine07Clean_BarDome", new Color(0.92f, 0.94f, 0.97f, 0.22f), 0f, 0.97f);

        Transparent(dome);

        m["BarDome"] = dome;

        // Scene light lens while off; SceneLights swaps in its own emissive material when they are switched on.
        m["SceneLens"] = Material(lit, "Engine07Clean_SceneLens", new Color(0.80f, 0.82f, 0.85f), 0f, 0.92f);

        m["Logo"] = DecalMaterial(lit, "Engine07Clean_Logo", LogoPath);

        m["Texas"] = DecalMaterial(lit, "Engine07Clean_Texas", TexasPath);

        return m;
    }

    /// <summary>Decal: opaque with alpha clipping, so the outline is crisp and it sorts like paint.</summary>
    private static Material DecalMaterial(Shader shader, string name, string texturePath)
    {
        var material = Material(shader, name, Color.white, 0f, 0.6f);

        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));

        material.SetFloat("_AlphaClip", 1f);

        material.SetFloat("_Cutoff", 0.5f);

        material.EnableKeyword("_ALPHATEST_ON");

        material.SetOverrideTag("RenderType", "TransparentCutout");

        material.renderQueue = (int)RenderQueue.AlphaTest;

        EditorUtility.SetDirty(material);

        return material;
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

    /// <summary>Reads the lens targets (Engine07-local Unity coordinates) written by build_engine07.py.</summary>
    private static List<LensTarget> ReadTargets()
    {
        var json = File.ReadAllText(TargetsPath);

        var result = new List<LensTarget>();

        foreach (Match entry in Regex.Matches(json, @"\{[^{}]*\}"))
        {
            var text = entry.Value;

            result.Add(new LensTarget(
                int.Parse(Regex.Match(text, "\"i\":\\s*(\\d+)").Groups[1].Value, CultureInfo.InvariantCulture),
                Vector(text, "centre"),
                Vector(text, "normal"),
                Vector(text, "size")));
        }

        return result;
    }

    private static Vector3 Vector(string text, string key)
    {
        var values = Regex.Match(text, "\"" + key + "\":\\s*\\[([^\\]]*)\\]").Groups[1].Value
            .Split(',')
            .Select(s => float.Parse(s.Trim(), CultureInfo.InvariantCulture))
            .ToArray();

        return new Vector3(values[0], values[1], values[2]);
    }

    /// <summary>One lens's fixture: game lens index, cap centre and outward normal (Engine07 space), cap width/height/depth.</summary>
    private readonly struct LensTarget
    {
        public LensTarget(int index, Vector3 centre, Vector3 normal, Vector3 size)
        {
            Index = index;

            Centre = centre;

            Normal = normal;

            Size = size;
        }

        public int Index { get; }

        public Vector3 Centre { get; }

        public Vector3 Normal { get; }

        public Vector3 Size { get; }
    }
}
