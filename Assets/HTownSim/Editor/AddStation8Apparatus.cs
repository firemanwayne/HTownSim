#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Parks Engine 08, Engine 01 and Medic 08 in Fire Station 8's three bays. They are the Engine 07 and Ambulance 07 models with
/// their own livery (the number drawn by the art scripts under Blender/HFD Apparatus, with HFD_UNIT_NUMBER set), standing
/// where Station 7's bays are relative to the station building: Medic 08 on the left, Engine 01 in the middle (where Station 7
/// keeps its ladder) and Engine 08 on the right, seen from the street. Each is a static copy with a box collider and no lights.
///
/// Apply (menu HTownSim > Station 8 > Add Apparatus) can be run again and replaces what it made. Revert removes it and puts the
/// middle bay sign back. The textures come from <c>Assets/HTownSim/Vehicles/Station8</c>; the new materials are written beside them.
/// Refuses to run in Play Mode or with unsaved scene changes, and saves the scene when it is done.
/// </summary>
public static class AddStation8Apparatus
{
    private const string GroupName = "Station8Apparatus";

    private const string StationSource = "Station07";

    private const string StationCopy = "FireStation8";

    private const string LadderProp = "Ladder07";

    private const string VehicleRoot = "Assets/HTownSim/Vehicles";

    private const string Station8Folder = VehicleRoot + "/Station8";

    private const string MaterialFolder = Station8Folder + "/Materials";

    private const string EngineModel = VehicleRoot + "/Engine07Hi3D/Engine07Clean.fbx";

    private const string AmbulanceModel = VehicleRoot + "/Ambulance07/Ambulance07.fbx";

    private const string EngineLivery = VehicleRoot + "/Engine07Hi3D/Engine07_Livery.png";

    private const string EngineDecals = VehicleRoot + "/Engine07Hi3D/Engine07_Decals.png";

    private const string AmbulanceLivery = VehicleRoot + "/Ambulance07/Ambulance07_Livery.png";

    /// <summary>District Chief 08's model (Blender/HFD Apparatus/Chiefs/export_dc08.py) and where its game materials are written.</summary>
    private const string DistrictChiefModel = VehicleRoot + "/DistrictChief08/DistrictChief08.fbx";

    private const string DistrictChiefMaterials = VehicleRoot + "/DistrictChief08/Materials";

    private const string DistrictChiefTextures = VehicleRoot + "/DistrictChief08/Textures";

    /// <summary>Station 7's bay x positions (left, middle, right from the street) and the signs Station 8's copy carries.</summary>
    private const float LeftBay = 20.3f;

    private const float MiddleBay = 12.3f;

    private const float RightBay = 4.3f;

    /// <summary>District Chief 08 stands on the apron beside the left-hand (medic) bay.</summary>
    private const float ApronBay = 29.5f;

    private const string MiddleSignOld = "LADDER 8";

    private const string MiddleSignNew = "ENGINE 1";

    /// <summary>One apparatus to park: its name, the model, where its look comes from, and its new textures.</summary>
    private sealed class Unit
    {
        internal Unit(string name, float bayX, string model, string sourceVisual, string[] textureSwaps)
        {
            Name = name;

            BayX = bayX;

            Model = model;

            SourceVisual = sourceVisual;

            TextureSwaps = textureSwaps;
        }

        internal string Name { get; }

        internal float BayX { get; }

        internal string Model { get; }

        /// <summary>The name of the scene object (the existing apparatus's model) whose materials and pose are copied.</summary>
        internal string SourceVisual { get; }

        /// <summary>Pairs of (original texture path, new texture path).</summary>
        internal string[] TextureSwaps { get; }
    }

    private static readonly Unit[] Units =
    {
        new Unit("Medic08", LeftBay, AmbulanceModel, "Ambulance07Visual", new[] { AmbulanceLivery, Station8Folder + "/Medic08_Livery.png" }),
        new Unit("Engine01", MiddleBay, EngineModel, "Engine07CleanVisual", new[] { EngineLivery, Station8Folder + "/Engine01_Livery.png", EngineDecals, Station8Folder + "/Engine01_Decals.png" }),
        new Unit("Engine08", RightBay, EngineModel, "Engine07CleanVisual", new[] { EngineLivery, Station8Folder + "/Engine08_Livery.png", EngineDecals, Station8Folder + "/Engine08_Decals.png" }),
        new Unit("DistrictChief08", ApronBay, DistrictChiefModel, string.Empty, new string[0]),
    };

    [MenuItem("HTownSim/Station 8/Add Apparatus")]
    private static void AddFromMenu()
    {
        Report(Apply());
    }

    [MenuItem("HTownSim/Station 8/Remove Apparatus")]
    private static void RemoveFromMenu()
    {
        Report(Revert());
    }

    /// <summary>Parks the three apparatus (replacing any made before) and saves the scene.</summary>
    public static string Apply()
    {
        var scene = Check();

        var station7 = FindRoot(scene, StationSource);

        var station8 = FindRoot(scene, StationCopy);

        var reference = FindRoot(scene, LadderProp);

        RemoveGroup(scene);

        var group = new GameObject(GroupName).transform;

        foreach (var unit in Units)
        {
            ImportLikeOriginals(unit);
        }

        AssetDatabase.Refresh();

        foreach (var unit in Units)
        {
            Park(new ParkRequest(unit, group, new BayFrame(station7, station8, reference)));
        }

        RelabelMiddleSign(station8, MiddleSignOld, MiddleSignNew);

        return Save(scene, "Parked Medic 08, Engine 01, Engine 08 and District Chief 08 at Station 8.");
    }

    /// <summary>Removes the parked apparatus and restores the middle bay sign.</summary>
    public static string Revert()
    {
        var scene = Check();

        RemoveGroup(scene);

        var station8 = FindRoot(scene, StationCopy);

        RelabelMiddleSign(station8, MiddleSignNew, MiddleSignOld);

        return Save(scene, "Removed the Station 8 apparatus.");
    }

    /// <summary>What <see cref="Park"/> needs: the unit, the object to put it under, and the bay geometry.</summary>
    private readonly struct ParkRequest
    {
        internal ParkRequest(Unit unit, Transform group, BayFrame frame)
        {
            Unit = unit;

            Group = group;

            Frame = frame;
        }

        internal Unit Unit { get; }

        internal Transform Group { get; }

        internal BayFrame Frame { get; }
    }

    /// <summary>The two stations and the reference apparatus root, which together place a Station 7 bay at Station 8.</summary>
    private readonly struct BayFrame
    {
        internal BayFrame(Transform station7, Transform station8, Transform reference)
        {
            Station7 = station7;

            Station8 = station8;

            Reference = reference;
        }

        internal Transform Station7 { get; }

        internal Transform Station8 { get; }

        /// <summary>An object standing in a Station 7 bay: its z and facing are the bays'.</summary>
        internal Transform Reference { get; }

        /// <summary>Where a Station 7 bay (x, with the given height) is at Station 8.</summary>
        internal Vector3 Position(float bayX, float height)
        {
            var bay = new Vector3(bayX, height, Reference.position.z);

            return Station8.TransformPoint(Station7.InverseTransformPoint(bay));
        }

        /// <summary>The facing of a Station 7 bay at Station 8.</summary>
        internal Quaternion Rotation => Station8.rotation * Quaternion.Inverse(Station7.rotation) * Reference.rotation;
    }

    /// <summary>Instances the model in its bay with its own materials, and gives it a collider that fits.</summary>
    private static void Park(ParkRequest request)
    {
        var unit = request.Unit;

        var source = unit.SourceVisual.Length == 0 ? null : FindVisual(request.Group.gameObject.scene, unit.SourceVisual);

        var root = new GameObject(unit.Name).transform;

        root.SetParent(request.Group, false);

        root.SetPositionAndRotation(request.Frame.Position(unit.BayX, source == null ? 0f : source.parent.position.y), request.Frame.Rotation);

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(unit.Model) ?? throw new InvalidOperationException("Model not found: " + unit.Model);

        var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);

        if (source == null)
        {
            LitMaterials(model.transform);

            FitCollider(root);

            return;
        }

        model.transform.localPosition = source.localPosition;

        model.transform.localRotation = source.localRotation;

        model.transform.localScale = source.localScale;

        var sourceRenderers = source.GetComponentsInChildren<Renderer>(true)
            .GroupBy(renderer => renderer.name)
            .ToDictionary(group => group.Key, group => group.First());

        var variants = new Dictionary<Material, Material>();

        foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            if (!sourceRenderers.TryGetValue(renderer.name, out var original))
            {
                continue;
            }

            renderer.sharedMaterials = original.sharedMaterials
                .Select(material => Variant(material, unit, variants))
                .ToArray();

            renderer.shadowCastingMode = original.shadowCastingMode;
        }

        FitCollider(root);
    }

    /// <summary>
    /// Gives a model with no existing look to copy (District Chief 08) universal-pipeline Lit materials with the colours and textures of its
    /// imported ones, one asset per material, named exactly as in the model so ApparatusProfile and SignalLamps can find the lamps by name.
    /// Lamps start dark; the game lights them.
    /// </summary>
    private static void LitMaterials(Transform model)
    {
        var made = new Dictionary<string, Material>();

        System.IO.Directory.CreateDirectory(DistrictChiefMaterials);

        foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            renderer.sharedMaterials = renderer.sharedMaterials
                .Select(original => original == null ? original! : made.TryGetValue(original.name, out var existing) ? existing : made[original.name] = Lit(original))
                .ToArray();
        }
    }

    /// <summary>One Lit material asset matching an imported material: base colour and texture, chrome and glass made shiny.</summary>
    private static Material Lit(Material original)
    {
        var path = DistrictChiefMaterials + "/" + original.name + ".mat";

        var lit = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (lit != null)
        {
            return lit;
        }

        lit = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? original.shader) { name = original.name };

        lit.SetColor("_BaseColor", original.color);

        var name = original.name;

        var shiny = name.Contains("chrome") || name.Contains("clearcoat") || name.Contains("glass") || name.Contains("lens");

        lit.SetFloat("_Smoothness", shiny ? 0.8f : 0.35f);

        lit.SetFloat("_Metallic", name.Contains("chrome") ? 0.9f : 0f);

        // The FBX carries no images: the HFD shield and the door crest come from the copies beside the model.
        if (name.Contains("HFD_shield"))
        {
            // The shield artwork has a transparent surround: cut it out so no white box shows.
            lit.SetColor("_BaseColor", Color.white);

            lit.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(DistrictChiefTextures + "/DC08_HFD_Shield.png"));

            lit.SetFloat("_AlphaClip", 1f);

            lit.SetFloat("_Cutoff", 0.5f);

            lit.EnableKeyword("_ALPHATEST_ON");

            lit.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
        }
        else if (name.Contains("photographic_decal"))
        {
            lit.SetColor("_BaseColor", Color.white);

            lit.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(DistrictChiefTextures + "/DC08_Door_Crest.jpg"));
        }
        else if (name.Contains("tinted_glass"))
        {
            // Windshield and windows: tinted, see-through glass.
            var tint = original.color;

            tint.a = 0.75f;

            lit.SetColor("_BaseColor", tint);

            lit.SetFloat("_Smoothness", 0.95f);

            lit.SetFloat("_Surface", 1f);

            lit.SetFloat("_Blend", 0f);

            lit.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);

            lit.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);

            lit.SetFloat("_ZWrite", 0f);

            lit.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

            lit.SetOverrideTag("RenderType", "Transparent");

            lit.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        else if (original.mainTexture != null)
        {
            lit.SetTexture("_BaseMap", original.mainTexture);
        }

        // Glass and door decals are single sheets whose faces may point into the truck: draw both sides.
        if (name.Contains("glass") || name.Contains("HFD_shield") || name.Contains("photographic_decal"))
        {
            lit.SetFloat("_Cull", 0f);
        }

        AssetDatabase.CreateAsset(lit, path);

        return lit;
    }

    /// <summary>A copy of the material that wears the unit's new textures, or the material itself when it uses none of the swapped ones.</summary>
    private static Material Variant(Material material, Unit unit, Dictionary<Material, Material> made)
    {
        if (material == null)
        {
            return material!;
        }

        if (made.TryGetValue(material, out var existing))
        {
            return existing;
        }

        Material? copy = null;

        for (var i = 0; i + 1 < unit.TextureSwaps.Length; i += 2)
        {
            var original = AssetDatabase.LoadAssetAtPath<Texture2D>(unit.TextureSwaps[i]);

            var replacement = AssetDatabase.LoadAssetAtPath<Texture2D>(unit.TextureSwaps[i + 1]) ?? throw new InvalidOperationException("Texture not imported yet: " + unit.TextureSwaps[i + 1]);

            foreach (var property in material.GetTexturePropertyNames())
            {
                if (material.GetTexture(property) == original)
                {
                    copy ??= new Material(material);

                    copy.SetTexture(property, replacement);
                }
            }
        }

        if (copy is null)
        {
            made[material] = material;

            return material;
        }

        System.IO.Directory.CreateDirectory(MaterialFolder);

        var path = AssetDatabase.GenerateUniqueAssetPath(MaterialFolder + "/" + unit.Name + "_" + material.name + ".mat");

        AssetDatabase.CreateAsset(copy, path);

        made[material] = copy;

        return copy;
    }

    /// <summary>Adds a box collider that hugs the model's meshes, in the root's own space.</summary>
    private static void FitCollider(Transform root)
    {
        Bounds? bounds = null;

        foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null)
            {
                continue;
            }

            var box = filter.sharedMesh.bounds;

            foreach (var x in new[] { box.min.x, box.max.x })
            {
                foreach (var y in new[] { box.min.y, box.max.y })
                {
                    foreach (var z in new[] { box.min.z, box.max.z })
                    {
                        var corner = root.InverseTransformPoint(filter.transform.TransformPoint(new Vector3(x, y, z)));

                        if (bounds is null)
                        {
                            bounds = new Bounds(corner, Vector3.zero);
                        }
                        else
                        {
                            var grown = bounds.Value;

                            grown.Encapsulate(corner);

                            bounds = grown;
                        }
                    }
                }
            }
        }

        if (bounds is null)
        {
            return;
        }

        var collider = root.gameObject.AddComponent<BoxCollider>();

        collider.center = bounds.Value.center;

        collider.size = bounds.Value.size;
    }

    /// <summary>Gives each new texture the import settings of the one it replaces (size, compression, colour space), so the paint looks the same.</summary>
    private static void ImportLikeOriginals(Unit unit)
    {
        for (var i = 0; i + 1 < unit.TextureSwaps.Length; i += 2)
        {
            var original = AssetImporter.GetAtPath(unit.TextureSwaps[i]) as TextureImporter;

            AssetDatabase.ImportAsset(unit.TextureSwaps[i + 1], ImportAssetOptions.ForceSynchronousImport);

            var replacement = AssetImporter.GetAtPath(unit.TextureSwaps[i + 1]) as TextureImporter;

            if (original == null || replacement == null)
            {
                continue;
            }

            var settings = new TextureImporterSettings();

            original.ReadTextureSettings(settings);

            replacement.SetTextureSettings(settings);

            replacement.maxTextureSize = original.maxTextureSize;

            replacement.textureCompression = original.textureCompression;

            replacement.anisoLevel = original.anisoLevel;

            replacement.SaveAndReimport();
        }
    }

    /// <summary>Changes the text of the bay sign that reads <paramref name="from"/>.</summary>
    private static void RelabelMiddleSign(Transform station8, string from, string to)
    {
        foreach (var text in station8.GetComponentsInChildren<TMP_Text>(true))
        {
            if (text.text == from)
            {
                text.text = to;

                EditorUtility.SetDirty(text);
            }
        }
    }

    /// <summary>Deletes the group made by an earlier Apply.</summary>
    private static void RemoveGroup(UnityEngine.SceneManagement.Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects().Where(root => root.name == GroupName).ToList())
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    /// <summary>The existing apparatus model with this name, wherever it currently is (active or not).</summary>
    private static Transform FindVisual(UnityEngine.SceneManagement.Scene scene, string name)
    {
        var matches = Resources.FindObjectsOfTypeAll<Transform>()
            .Where(transform => transform.name == name && transform.gameObject.scene == scene && transform.parent != null)
            .ToList();

        return matches.FirstOrDefault(transform => transform.parent.name == "Engine07")
            ?? matches.FirstOrDefault()
            ?? throw new InvalidOperationException(name + " not found in the scene.");
    }

    private static Transform FindRoot(UnityEngine.SceneManagement.Scene scene, string name)
    {
        return scene.GetRootGameObjects()
            .Where(root => root.name == name)
            .Select(root => root.transform)
            .FirstOrDefault() ?? throw new InvalidOperationException(name + " not found in the scene.");
    }

    private static UnityEngine.SceneManagement.Scene Check()
    {
        var scene = EditorSceneManager.GetActiveScene();

        if (EditorApplication.isPlayingOrWillChangePlaymode || scene.isDirty)
        {
            throw new InvalidOperationException("Exit Play Mode and save or discard scene changes first.");
        }

        return scene;
    }

    private static string Save(UnityEngine.SceneManagement.Scene scene, string message)
    {
        EditorSceneManager.MarkSceneDirty(scene);

        if (!EditorSceneManager.SaveScene(scene))
        {
            throw new InvalidOperationException("Could not save the scene.");
        }

        AssetDatabase.SaveAssets();

        return message + " Scene saved.";
    }

    private static void Report(string message)
    {
        Debug.Log(message);

        EditorUtility.DisplayDialog("Station 8", message, "OK");
    }
}
