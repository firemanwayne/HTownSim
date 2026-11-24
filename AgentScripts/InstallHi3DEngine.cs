#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Puts the Hi3D-generated engine (prepared in Blender: Blender\HFD Apparatus\Engines\Engine07_Hi3D.blend)
/// on Engine07, numbered 07.
///
/// Model: Assets/HTownSim/Vehicles/Engine07Hi3D/Engine07Hi3D.fbx, 10.1 m long, wheelbase 5.05 m like the gameplay
/// truck: body (90k triangles), four wheels pivoting on their axles (WheelFront*/WheelRear*, picked up by WheelSpin),
/// and seven "07" decals over the generator's painted "43"s. 2K textures: base colour, metallic (paint is non-metal,
/// chrome is metal) with smoothness in alpha, and the decal atlas.
///
/// The existing Engine43Visual stays for its gameplay parts: its 91 warning lenses (ApparatusLights) and two beacons
/// (FireResponse) are kept, every other renderer of it is hidden, and it is stretched to the new truck's length and
/// height so the lenses sit on the new body. Lens "off" materials become invisible, so the painted lights of the Hi3D
/// texture show until the emergency lights flash. Apply refuses to run with unsaved scene changes or twice; Revert
/// undoes everything. Both reload the scene after saving (GPU Resident Drawer, see UpgradeBuildings.Save).
/// </summary>
public static class InstallHi3DEngine
{
    private const string Folder = "Assets/HTownSim/Vehicles/Engine07Hi3D/";

    private const string ModelPath = Folder + "Engine07Hi3D.fbx";

    private const string VisualName = "Engine07Hi3DVisual";

    private const string OldVisualName = "Engine43Visual";

    /// <summary>Same vertical offset the procedural visual uses, so the tyres touch the road.</summary>
    private const float VisualDrop = -0.15f;

    /// <summary>The old visual spans z -4.92..4.46 and is 3.41 m tall; the Hi3D truck spans -5.04..5.05 and 3.10 m.</summary>
    private static readonly Vector3 OldStretch = new Vector3(0.95f, 0.91f, 10.09f / 9.38f);

    private static readonly Vector3 OldStretchedPosition = new Vector3(0f, VisualDrop, 0.005f + 0.23f * (10.09f / 9.38f));

    private const string HiddenLensMaterial = Folder + "Materials/LensHidden.mat";

    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    public static string Apply()
    {
        var scene = Check();

        var engine = GameObject.Find("Engine07");

        if (engine.transform.Find(VisualName) != null)
        {
            return "Already installed; run Revert first.";
        }

        var old = engine.transform.Find(OldVisualName);

        ConfigureImports();

        var materials = BuildMaterials();

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);

        var visual = (GameObject)PrefabUtility.InstantiatePrefab(model, engine.transform);

        visual.name = VisualName;

        visual.transform.localPosition = new Vector3(0f, VisualDrop, 0f);

        visual.transform.localRotation = Quaternion.identity;

        foreach (var renderer in visual.GetComponentsInChildren<MeshRenderer>())
        {
            renderer.sharedMaterial = renderer.name == "Engine07_Decals" ? materials.Decals : materials.Paint;
        }

        // Keep the old visual for its lenses and beacons only.
        var lights = engine.GetComponent("ApparatusLights");

        var lenses = (Renderer[])lights.GetType().GetField("_lenses", Flags).GetValue(lights);

        var halos = (Renderer[]?)lights.GetType().GetField("_haloRenderers", Flags)?.GetValue(lights) ?? Array.Empty<Renderer>();

        var keep = new HashSet<Renderer>(lenses.Concat(halos.Where(h => h != null)));

        foreach (var renderer in old.GetComponentsInChildren<Renderer>(true))
        {
            if (!keep.Contains(renderer))
            {
                renderer.enabled = false;
            }
        }

        old.localScale = OldStretch;

        old.localPosition = OldStretchedPosition;

        // Lenses are invisible while off; ApparatusLights swaps to its "On" materials when they flash.
        var data = new SerializedObject(lights);

        var off = data.FindProperty("_off");

        for (var i = 0; i < off.arraySize; i++)
        {
            off.GetArrayElementAtIndex(i).objectReferenceValue = materials.Hidden;
        }

        data.ApplyModifiedPropertiesWithoutUndo();

        foreach (var lens in lenses)
        {
            if (!lens.sharedMaterial.name.EndsWith("On"))
            {
                lens.sharedMaterial = materials.Hidden;
            }
        }

        var front = visual.GetComponentsInChildren<Transform>().First(t => t.name.StartsWith("WheelFront")).localPosition;

        return Save(scene, $"Installed {VisualName}: front wheel at {front}, {lenses.Length} lenses kept (hidden while off), old body hidden.");
    }

    public static string Revert()
    {
        var scene = Check();

        var engine = GameObject.Find("Engine07");

        var visual = engine.transform.Find(VisualName);

        if (visual != null)
        {
            UnityEngine.Object.DestroyImmediate(visual.gameObject);
        }

        var old = engine.transform.Find(OldVisualName);

        foreach (var renderer in old.GetComponentsInChildren<Renderer>(true))
        {
            renderer.enabled = true;
        }

        old.localScale = Vector3.one;

        old.localPosition = new Vector3(0f, VisualDrop, 0f);

        // Restore each lens's own "off" material (the lens renderers keep their original sharedMaterial names in
        // the prefab, so revert the instance overrides).
        var lights = engine.GetComponent("ApparatusLights");

        var lenses = (Renderer[])lights.GetType().GetField("_lenses", Flags).GetValue(lights);

        var data = new SerializedObject(lights);

        var off = data.FindProperty("_off");

        for (var i = 0; i < lenses.Length; i++)
        {
            var source = PrefabUtility.GetCorrespondingObjectFromSource(lenses[i]);

            var material = source != null ? source.sharedMaterial : lenses[i].sharedMaterial;

            off.GetArrayElementAtIndex(i).objectReferenceValue = material;

            lenses[i].sharedMaterial = material;
        }

        data.ApplyModifiedPropertiesWithoutUndo();

        return Save(scene, "Restored the procedural Engine 43 visual.");
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
        // The FBX and textures are written from Blender, outside Unity, so make sure they are imported.
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        var model = (ModelImporter)AssetImporter.GetAtPath(ModelPath);

        model.materialImportMode = ModelImporterMaterialImportMode.None;

        model.importNormals = ModelImporterNormals.Import;

        model.importCameras = false;

        model.importLights = false;

        model.animationType = ModelImporterAnimationType.None;

        model.importAnimation = false;

        model.SaveAndReimport();

        Texture(Folder + "Engine07_BaseColor.png", true, false);

        Texture(Folder + "Engine07_MetallicSmoothness.png", false, false);

        Texture(Folder + "Engine07_Decals.png", true, true);
    }

    private static void Texture(string path, bool srgb, bool alpha)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);

        importer.sRGBTexture = srgb;

        importer.alphaSource = TextureImporterAlphaSource.FromInput;

        importer.alphaIsTransparency = alpha;

        importer.mipmapEnabled = true;

        importer.anisoLevel = 8;

        importer.maxTextureSize = 2048;

        importer.textureCompression = TextureImporterCompression.CompressedHQ;

        importer.SaveAndReimport();
    }

    private static (Material Paint, Material Decals, Material Hidden) BuildMaterials()
    {
        if (!AssetDatabase.IsValidFolder(Folder + "Materials"))
        {
            AssetDatabase.CreateFolder(Folder.TrimEnd('/'), "Materials");
        }

        var lit = Shader.Find("Universal Render Pipeline/Lit");

        var paint = Material(lit, "Engine07_Paint");

        paint.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "Engine07_BaseColor.png"));

        paint.SetColor("_BaseColor", Color.white);

        paint.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "Engine07_MetallicSmoothness.png"));

        paint.EnableKeyword("_METALLICSPECGLOSSMAP");

        paint.SetFloat("_Smoothness", 1f);

        paint.SetFloat("_SmoothnessTextureChannel", 0f);

        EditorUtility.SetDirty(paint);

        var decals = Material(lit, "Engine07_Decals");

        decals.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "Engine07_Decals.png"));

        decals.SetColor("_BaseColor", Color.white);

        decals.SetFloat("_Smoothness", 0.45f);

        Transparent(decals, 1f);

        var hidden = Material(lit, "LensHidden");

        hidden.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0f));

        Transparent(hidden, 0f);

        return (paint, decals, hidden);
    }

    private static Material Material(Shader shader, string name)
    {
        var path = Folder + "Materials/" + name + ".mat";

        var material = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (material == null)
        {
            material = new Material(shader) { name = name };

            AssetDatabase.CreateAsset(material, path);
        }

        material.shader = shader;

        return material;
    }

    /// <summary>URP Lit, alpha-blended (decals fade into the paint; the hidden lens material draws nothing).</summary>
    private static void Transparent(Material material, float alpha)
    {
        var colour = material.GetColor("_BaseColor");

        colour.a = alpha;

        material.SetColor("_BaseColor", colour);

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
