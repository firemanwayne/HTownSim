using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>Authors a self-contained, reference-inspired Houston Engine 43 visual prefab.</summary>
public static class BuildEngine43
{
    /// <summary>Owns only this new apparatus asset folder.</summary>
    private const string Folder = "Assets/HTownSim/Vehicles/Engine43";
    private const string PrefabPath = Folder + "/HoustonEngine43.prefab";

    /// <summary>Temporary authoring state; generated meshes are saved after combination.</summary>
    private static Transform _root;
    private static Transform _section;
    private static readonly List<Transform> Sections = new List<Transform>();
    private static readonly HashSet<Mesh> TemporaryMeshes = new HashSet<Mesh>();
    private static readonly Dictionary<string, Mesh> RoundedMeshes = new Dictionary<string, Mesh>();
    private static Material _red, _white, _chrome, _dark, _tire, _glass, _plate, _yellow, _amber, _redLens, _clearLens, _hose, _blue, _gauge;

    /// <summary>Creates the reusable asset without changing any existing apparatus or scene.</summary>
    public static string Build()
    {
        if (EditorApplication.isPlaying)
        {
            throw new InvalidOperationException("Exit Play Mode before authoring the prefab.");
        }

        if (Directory.Exists(Folder) && AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
        {
            throw new InvalidOperationException("Engine43 folder exists without a prefab. Inspect the partial output before retrying.");
        }

        Directory.CreateDirectory(Folder + "/Materials");

        Directory.CreateDirectory(Folder + "/Meshes");

        AssetDatabase.Refresh();

        Sections.Clear();

        TemporaryMeshes.Clear();

        RoundedMeshes.Clear();

        var authoringScene = EditorSceneManager.NewPreviewScene();

        _root = new GameObject("HoustonEngine43").transform;

        SceneManager.MoveGameObjectToScene(_root.gameObject, authoringScene);

        try
        {
            CreateMaterials();

            Chassis();

            Cab();

            Body();

            Pump(-1);

            Pump(1);

            Rear();

            Hosebed();

            Lighting();

            foreach (var side in new[] { -1, 1 })
            {
                Wheel(side, 2.4f);

                Wheel(side, -2.65f);
            }

            CombineSections();

            var prefab = PrefabUtility.SaveAsPrefabAsset(_root.gameObject, PrefabPath);

            if (prefab == null)
            {
                throw new InvalidOperationException("Unity did not save the apparatus prefab.");
            }

            AssetDatabase.SaveAssets();

            var renderers = prefab.GetComponentsInChildren<Renderer>();

            var triangles = prefab.GetComponentsInChildren<MeshFilter>().Where(f => f.sharedMesh != null).Sum(f => f.sharedMesh.triangles.Length / 3);

            return PrefabPath + " | " + renderers.Length + " renderers | " + triangles + " triangles | +Z forward, metres, ground-centred pivot, lights disabled";
        }
        finally
        {
            Object.DestroyImmediate(_root.gameObject);

            EditorSceneManager.ClosePreviewScene(authoringScene);

            foreach (var mesh in TemporaryMeshes)
            {
                if (mesh != null && !AssetDatabase.Contains(mesh))
                {
                    Object.DestroyImmediate(mesh);
                }
            }
        }
    }

    /// <summary>Creates dedicated URP materials rather than changing the city palette assets.</summary>
    private static void CreateMaterials()
    {
        _red = Material("HoustonRed", ("B4101C", 0.15f, 0.65f));

        _white = Material("ReflectiveWhite", ("E8E9E5", 0.08f, 0.52f));

        _chrome = Material("PolishedChrome", ("A8B4BD", 0.88f, 0.82f));

        _dark = Material("BlackHardware", ("11191D", 0.2f, 0.35f));

        _tire = Material("TireRubber", ("17191B", 0f, 0.19f));

        _glass = Material("CabGlass", ("17303B", 0.42f, 0.93f));

        _plate = Material("DiamondPlate", ("A5ADB2", 0.7f, 0.58f));

        _yellow = Material("ChevronYellow", ("F0B922", 0.06f, 0.42f));

        _amber = Material("AmberLensOff", ("9D5B14", 0.15f, 0.75f));

        _redLens = Material("RedLensOff", ("700A13", 0.2f, 0.85f));

        _clearLens = Material("ClearLensOff", ("899DA7", 0.28f, 0.9f));

        _hose = Material("HoseCanvas", ("B9AA85", 0f, 0.23f));

        _blue = Material("TexasBlue", ("103C80", 0.1f, 0.4f));

        _gauge = Material("GaugeFace", ("D8D9C9", 0.05f, 0.5f));

        var texture = new Texture2D(256, 256, TextureFormat.RGBA32, true) { name = "DiamondPlatePattern", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };

        var pixels = new Color[256 * 256];

        for (var y = 0; y < 256; y++)
        {
            for (var x = 0; x < 256; x++)
            {
                var cellX = x % 32 - 16;

                var cellY = y % 32 - 16;

                var flip = ((x / 32 + y / 32) % 2 == 0) ? 1 : -1;

                var ridge = Mathf.Abs(cellX + flip * cellY) < 3 && Mathf.Abs(cellX - flip * cellY) < 18;

                var tone = ridge ? 0.96f : 0.64f + 0.045f * Mathf.PerlinNoise(x * 0.4f, y * 0.4f);

                pixels[y * 256 + x] = new Color(tone, tone, tone, 1);
            }
        }

        texture.SetPixels(pixels);

        texture.Apply();

        texture = Persist(texture, Folder + "/Materials/DiamondPlatePattern.asset");

        _plate.mainTexture = texture;

        _plate.mainTextureScale = new Vector2(3, 3);
    }

    /// <summary>Creates a persistently stored material.</summary>
    private static Material Material(string name, (string Hex, float Metallic, float Smoothness) spec)
    {
        ColorUtility.TryParseHtmlString("#" + spec.Hex, out var color);

        var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name, color = color, enableInstancing = true };

        material.SetFloat("_Metallic", spec.Metallic);

        material.SetFloat("_Smoothness", spec.Smoothness);

        return Persist(material, Folder + "/Materials/" + name + ".mat");
    }

    /// <summary>Updates generated asset content while retaining references and GUIDs.</summary>
    private static T Persist<T>(T asset, string path) where T : Object
    {
        var existing = AssetDatabase.LoadAssetAtPath<T>(path);

        if (existing == null)
        {
            AssetDatabase.CreateAsset(asset, path);

            return asset;
        }

        if (asset is Mesh source && existing is Mesh target)
        {
            target.Clear();

            target.indexFormat = source.indexFormat;

            target.vertices = source.vertices;

            target.normals = source.normals;

            target.uv = source.uv;

            target.triangles = source.triangles;

            target.RecalculateBounds();
        }
        else
        {
            EditorUtility.CopySerialized(asset, existing);
        }

        EditorUtility.SetDirty(existing);

        Object.DestroyImmediate(asset);

        return existing;
    }

    /// <summary>Begins an independently inspectable assembly.</summary>
    private static void Section(string name)
    {
        _section = new GameObject(name).transform;

        _section.SetParent(_root, false);

        Sections.Add(_section);
    }

    /// <summary>Builds the frame, running boards, and bumper supports.</summary>
    private static void Chassis()
    {
        Section("Chassis");

        foreach (var side in new[] { -1, 1 })
        {
            Part("FrameRail", (new Vector3(side * 0.66f, 0.68f, -0.1f), new Vector3(0.18f, 0.3f, 7.8f), _dark, 0.025f));

            Part("SideRunningBoard", (new Vector3(side * 1.25f, 0.78f, -1.45f), new Vector3(0.3f, 0.1f, 5.15f), _plate, 0.025f));

            Part("CabStepLower", (new Vector3(side * 1.28f, 0.48f, 1.22f), new Vector3(0.32f, 0.13f, 1.02f), _plate, 0.025f));

            Part("CabStepUpper", (new Vector3(side * 1.25f, 0.72f, 1.22f), new Vector3(0.32f, 0.11f, 1.02f), _plate, 0.025f));
        }

        foreach (var z in new[] { 2.4f, -2.65f })
        {
            Cylinder("Axle", (new Vector3(0, 0.65f, z), Vector3.right, 0.1f, 2.6f, _dark));
        }

        Part("FrontBumper", (new Vector3(0, 0.67f, 4.15f), new Vector3(2.75f, 0.38f, 0.55f), _red, 0.09f));

        Part("FrontBumperTread", (new Vector3(0, 0.875f, 4.1f), new Vector3(2.7f, 0.06f, 0.59f), _plate, 0.025f));

        Part("RearStep", (new Vector3(0, 0.48f, -4.42f), new Vector3(2.72f, 0.16f, 1f), _plate, 0.04f));

        Part("RearRubStrip", (new Vector3(0, 0.4f, -4.46f), new Vector3(2.68f, 0.09f, 0.05f), _dark, 0.015f));
    }

    /// <summary>Builds the rounded crew cab, split glazing, doors, and front hardware.</summary>
    private static void Cab()
    {
        Section("CrewCab");

        Part("RoundedCabShell", (new Vector3(0, 1.78f, 2.36f), new Vector3(2.6f, 2.15f, 2.98f), _red, 0.14f));

        Part("RaisedCrewRoof", (new Vector3(0, 2.99f, 1.65f), new Vector3(2.55f, 0.54f, 1.5f), _red, 0.1f));

        Label("RoofUnit43", ("43", new Vector3(0, 3.265f, 1.65f), 0, new Vector2(1.3f, 0.8f), 6f, _white.color));

        _section.Find("RoofUnit43").localRotation = Quaternion.Euler(90, 0, 0);

        Part("FrontRoofLip", (new Vector3(0, 2.85f, 3.1f), new Vector3(2.56f, 0.16f, 1.35f), _red, 0.07f));

        foreach (var side in new[] { -1, 1 })
        {
            var window = Part("WindshieldSeal", (new Vector3(side * 0.625f, 2.28f, 3.854f), new Vector3(1.19f, 1.04f, 0.05f), _dark, 0.105f));

            window.transform.localRotation = Quaternion.Euler(-5, 0, 0);

            var pane = Part("Windshield", (new Vector3(side * 0.625f, 2.28f, 3.886f), new Vector3(1.11f, 0.95f, 0.028f), _glass, 0.09f));

            pane.transform.localRotation = Quaternion.Euler(-5, 0, 0);

            Beam("WiperArm", (new Vector3(side * 0.12f, 1.81f, 3.94f), new Vector3(side * 0.94f, 1.95f, 3.94f), 0.016f, _dark));

            Beam("WiperBlade", (new Vector3(side * 0.63f, 1.9f, 3.95f), new Vector3(side * 1.1f, 2.03f, 3.95f), 0.023f, _dark));

            Part("DriverDoorSeal", (new Vector3(side * 1.306f, 1.84f, 2.92f), new Vector3(0.025f, 1.91f, 1.37f), _dark, 0.025f));

            Part("DriverDoor", (new Vector3(side * 1.324f, 1.84f, 2.92f), new Vector3(0.025f, 1.86f, 1.32f), _red, 0.022f));

            Part("CrewDoorSeal", (new Vector3(side * 1.306f, 1.93f, 1.42f), new Vector3(0.025f, 2.1f, 1.27f), _dark, 0.025f));

            Part("CrewDoor", (new Vector3(side * 1.324f, 1.93f, 1.42f), new Vector3(0.025f, 2.05f, 1.22f), _red, 0.022f));

            Part("DriverWindowSeal", (new Vector3(side * 1.347f, 2.29f, 2.92f), new Vector3(0.035f, 0.94f, 1.12f), _dark, 0.08f));

            Part("DriverWindow", (new Vector3(side * 1.37f, 2.29f, 2.92f), new Vector3(0.02f, 0.85f, 1.03f), _glass, 0.065f));

            Part("CrewWindowSeal", (new Vector3(side * 1.347f, 2.27f, 1.42f), new Vector3(0.035f, 0.88f, 0.95f), _dark, 0.08f));

            Part("CrewWindow", (new Vector3(side * 1.37f, 2.27f, 1.42f), new Vector3(0.02f, 0.78f, 0.86f), _glass, 0.065f));

            foreach (var z in new[] { 1.03f, 2.44f })
            {
                Part("DoorLatch", (new Vector3(side * 1.37f, 1.75f, z), new Vector3(0.065f, 0.09f, 0.28f), _chrome, 0.025f));
            }

            Beam("CrewGrabRail", (new Vector3(side * 1.41f, 1.1f, 0.99f), new Vector3(side * 1.41f, 2.7f, 0.99f), 0.025f, _chrome));

            Beam("MirrorSupport", (new Vector3(side * 1.35f, 1.95f, 3.5f), new Vector3(side * 1.73f, 2.12f, 3.69f), 0.028f, _chrome));

            Beam("MirrorUpright", (new Vector3(side * 1.73f, 2.12f, 3.69f), new Vector3(side * 1.73f, 2.75f, 3.69f), 0.026f, _chrome));

            Part("MirrorHousing", (new Vector3(side * 1.73f, 2.53f, 3.67f), new Vector3(0.19f, 0.54f, 0.19f), _chrome, 0.06f));

            Part("MirrorGlass", (new Vector3(side * 1.73f, 2.53f, 3.57f), new Vector3(0.15f, 0.45f, 0.018f), _glass, 0.04f));

            foreach (var y in new[] { 1.07f, 1.18f, 1.4f })
            {
                Part("CabReflectiveStripe", (new Vector3(side * 1.352f, y, 2.37f), new Vector3(0.018f, y == 1.18f ? 0.17f : 0.035f, 2.84f), _white, 0.004f));
            }

            // A simple shield and Texas-colour identification without tracing the supplied crest artwork.
            Part("DoorShield", (new Vector3(side * 1.365f, 1.58f, 3.06f), new Vector3(0.025f, 0.31f, 0.27f), _blue, 0.07f));

            Label("DoorIdentification", ("HFD", new Vector3(side * 1.385f, 1.58f, 3.06f), side < 0 ? 90f : -90f, new Vector2(0.24f, 0.2f), 1.3f, _white.color));

            Label("CrewUnit", ("43", new Vector3(side * 1.385f, 1.6f, 1.45f), side < 0 ? 90f : -90f, new Vector2(0.4f, 0.3f), 2f, _white.color));
        }

        Part("GrilleSurround", (new Vector3(0, 1.33f, 3.9f), new Vector3(1.65f, 1.02f, 0.12f), _chrome, 0.1f));

        Part("GrilleRecess", (new Vector3(0, 1.33f, 3.967f), new Vector3(1.49f, 0.87f, 0.025f), _dark, 0.055f));

        for (var row = 0; row < 9; row++)
        {
            Part("GrilleHorizontalBar", (new Vector3(0, 0.97f + row * 0.09f, 3.99f), new Vector3(1.48f, 0.027f, 0.032f), _chrome, 0.008f));
        }

        for (var column = -3; column <= 3; column++)
        {
            Part("GrilleVerticalBar", (new Vector3(column * 0.21f, 1.33f, 4.003f), new Vector3(0.028f, 0.85f, 0.03f), _chrome, 0.007f));
        }

        foreach (var side in new[] { -1, 1 })
        {
            Part("HeadlampHousing", (new Vector3(side * 1.05f, 1.28f, 3.91f), new Vector3(0.43f, 0.65f, 0.14f), _chrome, 0.06f));

            foreach (var y in new[] { 1.13f, 1.42f })
            {
                Part("HeadlampGlass", (new Vector3(side * 1.05f, y, 3.995f), new Vector3(0.32f, 0.18f, 0.035f), _clearLens, 0.04f));
            }

            Part("FrontTurnSignal", (new Vector3(side * 1.05f, 1.76f, 3.93f), new Vector3(0.32f, 0.14f, 0.05f), _amber, 0.03f));

            Cylinder("BumperTowEye", (new Vector3(side * 0.92f, 0.69f, 4.44f), Vector3.forward, 0.065f, 0.035f, _dark));
        }

        Cylinder("MechanicalSirenBody", (new Vector3(-0.88f, 0.98f, 4.17f), Vector3.forward, 0.16f, 0.3f, _chrome));

        Cylinder("MechanicalSirenFace", (new Vector3(-0.88f, 0.98f, 4.33f), Vector3.forward, 0.132f, 0.015f, _dark));

        for (var i = 0; i < 7; i++)
        {
            Part("SirenGrille", (new Vector3(-0.88f, 0.88f + i * 0.032f, 4.35f), new Vector3(0.18f, 0.01f, 0.012f), _chrome, 0.002f));
        }

        Part("FrontPlate", (new Vector3(0, 0.66f, 4.44f), new Vector3(0.38f, 0.18f, 0.015f), _white, 0.012f));

        Label("FrontUnit", ("43", new Vector3(1.12f, 0.67f, 4.448f), 180, new Vector2(0.3f, 0.28f), 2.3f, _white.color));

        Label("Plate", ("TEXAS", new Vector3(0, 0.66f, 4.455f), 180, new Vector2(0.35f, 0.15f), 1.05f, _blue.color));
    }

    /// <summary>Builds the red compartment body and white stepped striping.</summary>
    private static void Body()
    {
        Section("CompartmentBody");

        Part("BodyShell", (new Vector3(0, 1.67f, -2.05f), new Vector3(2.58f, 1.9f, 4.55f), _red, 0.06f));

        foreach (var side in new[] { -1, 1 })
        {
            Part("UpperDiamondTrim", (new Vector3(side * 1.3f, 2.67f, -2.05f), new Vector3(0.08f, 0.16f, 4.6f), _plate, 0.015f));

            for (var door = 0; door < 3; door++)
            {
                var z = -0.65f - door * 1.25f;

                Part("LockerSeal", (new Vector3(side * 1.3f, 1.77f, z), new Vector3(0.025f, 1.55f, 1.19f), _dark, 0.025f));

                Part("LockerDoor", (new Vector3(side * 1.318f, 1.77f, z), new Vector3(0.022f, 1.49f, 1.13f), _red, 0.02f));

                Part("LockerLatch", (new Vector3(side * 1.345f, 1.12f, z), new Vector3(0.06f, 0.055f, 0.87f), _dark, 0.018f));

                foreach (var handleOffset in new[] { -0.37f, 0.37f })
                {
                    Part("LatchBracket", (new Vector3(side * 1.37f, 1.12f, z + handleOffset), new Vector3(0.08f, 0.11f, 0.065f), _chrome, 0.015f));
                }

                for (var rib = 0; rib < 11; rib++)
                {
                    Part("RollupJoint", (new Vector3(side * 1.335f, 1.24f + rib * 0.105f, z), new Vector3(0.008f, 0.007f, 1.07f), _dark, 0.001f));
                }
            }

            for (var stripe = 0; stripe < 4; stripe++)
            {
                var y = 2.37f - stripe * 0.085f;

                SideStripe(side, (new Vector2(-4.18f, y), new Vector2(-1.52f, y), 0.045f));

                SideStripe(side, (new Vector2(-1.52f, y), new Vector2(-0.6f, 1.315f - stripe * 0.085f), 0.045f));

                SideStripe(side, (new Vector2(-0.6f, 1.315f - stripe * 0.085f), new Vector2(0.13f, 1.315f - stripe * 0.085f), 0.045f));
            }

            Label("BodyUnitNumber", ("43", new Vector3(side * 1.355f, 1.62f, -3.14f), side < 0 ? 90 : -90, new Vector2(0.9f, 0.5f), 4.4f, _white.color));

            Label("BodyMotto", ("COURAGE  •  COMMITMENT  •  COMPASSION", new Vector3(side * 1.355f, 2.52f, -2.22f), side < 0 ? 90 : -90, new Vector2(3.3f, 0.13f), 0.7f, _yellow.color));

            Beam("TopRail", (new Vector3(side * 1.22f, 2.92f, 0.1f), new Vector3(side * 1.22f, 2.92f, -4.15f), 0.025f, _chrome));

            foreach (var z in new[] { 2.4f, -2.65f })
            {
                Arc("FenderArch", (new Vector3(side * 1.328f, 0.66f, z), 0.73f, 0.052f, side, _chrome));
            }
        }
    }

    /// <summary>Draws one stripe in a side-facing panel plane.</summary>
    private static void SideStripe(int side, (Vector2 Start, Vector2 End, float Width) spec)
    {
        var start = new Vector3(side * 1.354f, spec.Start.y, spec.Start.x);

        var end = new Vector3(side * 1.354f, spec.End.y, spec.End.x);

        var strip = Part("ReflectiveZStripe", ((start + end) * 0.5f, new Vector3(0.014f, spec.Width, Vector3.Distance(start, end)), _white, 0.003f));

        strip.transform.localRotation = Quaternion.FromToRotation(Vector3.forward, end - start);
    }

    /// <summary>Builds a full-height pump operator panel with readable instruments.</summary>
    private static void Pump(int side)
    {
        Section(side < 0 ? "PumpOperatorSide" : "PumpPassengerSide");

        Part("PumpFrame", (new Vector3(side * 1.27f, 1.75f, 0.44f), new Vector3(0.19f, 2.05f, 1.02f), _plate, 0.025f));

        Part("PumpPanel", (new Vector3(side * 1.38f, 1.75f, 0.44f), new Vector3(0.035f, 1.9f, 0.87f), _dark, 0.015f));

        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                var position = new Vector3(side * 1.425f, 2.4f - row * 0.25f, 0.16f + column * 0.28f);

                Cylinder("GaugeChromeBezel", (position, Vector3.right, row == 0 ? 0.095f : 0.067f, 0.035f, _chrome));

                Cylinder("GaugeFace", (position + Vector3.right * side * 0.025f, Vector3.right, row == 0 ? 0.08f : 0.054f, 0.009f, _gauge));

                var needle = Part("GaugeNeedle", (position + Vector3.right * side * 0.034f, new Vector3(0.006f, 0.007f, 0.09f), _dark, 0.002f));

                needle.transform.localRotation = Quaternion.Euler(25 + column * 27, 0, 0);

                Part("GaugeLabel", (position + new Vector3(side * 0.025f, -0.12f, 0), new Vector3(0.012f, 0.027f, 0.12f), _white, 0.002f));
            }
        }

        for (var i = 0; i < 3; i++)
        {
            var center = new Vector3(side * 1.46f, 1.28f, 0.15f + i * 0.29f);

            Cylinder("IntakeCoupling", (center, Vector3.right, 0.11f, 0.17f, _chrome));

            Cylinder("IntakeCap", (center + Vector3.right * side * 0.1f, Vector3.right, 0.085f, 0.04f, _dark));

            Beam("ValveHandle", (center + new Vector3(side * 0.18f, 0.12f, 0), center + new Vector3(side * 0.18f, 0.24f, 0), 0.025f, i == 1 ? _blue : _red));
        }

        Cylinder("LargeSuctionInlet", (new Vector3(side * 1.48f, 1.62f, 0.43f), Vector3.right, 0.16f, 0.22f, _chrome));

        Cylinder("LargeSuctionCap", (new Vector3(side * 1.61f, 1.62f, 0.43f), Vector3.right, 0.13f, 0.035f, _dark));

        Beam("CapSpanner", (new Vector3(side * 1.64f, 1.51f, 0.43f), new Vector3(side * 1.64f, 1.73f, 0.43f), 0.018f, _chrome));

        Beam("PumpGrabRail", (new Vector3(side * 1.51f, 0.93f, -0.04f), new Vector3(side * 1.51f, 2.62f, -0.04f), 0.025f, _chrome));

        Part("PumpWorklight", (new Vector3(side * 1.35f, 2.82f, 0.43f), new Vector3(0.17f, 0.17f, 0.38f), _clearLens, 0.035f));

        Label("PumpIdentification", ("PUMP OPERATIONS", new Vector3(side * 1.42f, 2.63f, 0.42f), side < 0 ? 90 : -90, new Vector2(0.75f, 0.08f), 0.47f, _white.color));
    }

    /// <summary>Builds rear warning chevrons, centre locker, hosebed latches, and cone.</summary>
    private static void Rear()
    {
        Section("RearEquipment");

        Part("RearDiamondPanel", (new Vector3(0, 1.8f, -4.344f), new Vector3(2.62f, 2.08f, 0.08f), _plate, 0.025f));

        Part("RearChevronField", (new Vector3(0, 1.53f, -4.397f), new Vector3(2.48f, 1.62f, 0.025f), _red, 0.012f));

        // Chevron strips are clipped to the rear-panel rectangle rather than protruding from it.
        for (var band = -5; band <= 6; band++)
        {
            foreach (var side in new[] { -1, 1 })
            {
                Chevron(side, (band * 0.4f, _yellow));
            }
        }

        Part("RearLockerBorder", (new Vector3(0, 1.42f, -4.43f), new Vector3(1.36f, 1.37f, 0.065f), _dark, 0.035f));

        Part("RearLockerDoor", (new Vector3(0, 1.42f, -4.47f), new Vector3(1.29f, 1.3f, 0.025f), _white, 0.025f));

        for (var rib = 0; rib < 12; rib++)
        {
            Part("RearRollupJoint", (new Vector3(0, 0.82f + rib * 0.11f, -4.488f), new Vector3(1.25f, 0.006f, 0.005f), _chrome, 0.001f));
        }

        Label("RearUnitIdentification", ("ENGINE\n<size=155%>43</size>\nHOUSTON", new Vector3(0, 1.44f, -4.51f), 0, new Vector2(1.2f, 1.1f), 2.45f, _red.color));

        foreach (var y in new[] { 0.81f, 2.03f })
        {
            Beam("RearDoorHandle", (new Vector3(-0.58f, y, -4.53f), new Vector3(0.58f, y, -4.53f), 0.022f, _dark));
        }

        Part("RearUpperHosebedCover", (new Vector3(0, 2.65f, -4.36f), new Vector3(2.45f, 0.61f, 0.06f), _red, 0.02f));

        Part("HosebedCentreSeam", (new Vector3(0, 2.65f, -4.397f), new Vector3(0.025f, 0.6f, 0.012f), _dark, 0.003f));

        Part("HosebedLowerShelf", (new Vector3(0, 2.33f, -4.43f), new Vector3(2.52f, 0.055f, 0.15f), _chrome, 0.015f));

        foreach (var side in new[] { -1, 1 })
        {
            Beam("ShelfSupport", (new Vector3(side * 1.16f, 2.14f, -4.41f), new Vector3(side * 1.16f, 2.31f, -4.5f), 0.025f, _chrome));
        }

        Part("HosebedRearLip", (new Vector3(0, 3f, -4.35f), new Vector3(2.5f, 0.13f, 0.15f), _plate, 0.02f));

        for (var i = 0; i < 8; i++)
        {
            Part("HosebedCoverLatch", (new Vector3(-1.06f + i * 0.3f, 2.99f, -4.44f), new Vector3(0.23f, 0.055f, 0.055f), _dark, 0.02f));

            Part("RearUpperMarkerLensOff", (new Vector3(-1.06f + i * 0.3f, 2.99f, -4.473f), new Vector3(0.18f, 0.03f, 0.012f), _clearLens, 0.01f));
        }

        Label("UpperRear43", ("43", new Vector3(0.94f, 2.59f, -4.405f), 0, new Vector2(0.42f, 0.42f), 3.5f, _white.color));

        foreach (var side in new[] { -1, 1 })
        {
            foreach (var y in new[] { 1.05f, 1.42f, 1.79f, 2.59f, 2.85f })
            {
                Part("RearLampBezel", (new Vector3(side * 1.12f, y, -4.44f), new Vector3(0.25f, 0.16f, 0.065f), _chrome, 0.045f));

                Part("RearLampLensOff", (new Vector3(side * 1.12f, y, -4.481f), new Vector3(0.2f, 0.105f, 0.025f), y < 1.5f ? _redLens : _clearLens, 0.032f));
            }

            Beam("RearGrabLeft", (new Vector3(side * 0.9f, 1.88f, -4.49f), new Vector3(side * 0.9f, 2.19f, -4.49f), 0.025f, _chrome));

            Beam("RearGrabRight", (new Vector3(side * 0.75f, 1.88f, -4.49f), new Vector3(side * 0.75f, 2.19f, -4.49f), 0.025f, _chrome));

            Beam("RearGrabTop", (new Vector3(side * 0.9f, 2.19f, -4.49f), new Vector3(side * 0.75f, 2.19f, -4.49f), 0.025f, _chrome));
        }

        Part("ConeBase", (new Vector3(0.92f, 0.61f, -4.68f), new Vector3(0.43f, 0.08f, 0.43f), _dark, 0.045f));

        Frustum("TrafficCone", (new Vector3(0.92f, 0.93f, -4.68f), 0.21f, 0.04f, 0.6f, _amber));

        Frustum("ConeReflectiveBand", (new Vector3(0.92f, 0.98f, -4.68f), 0.13f, 0.103f, 0.095f, _white));
    }

    /// <summary>Creates one clipped diagonal chevron on either half of the rear panel.</summary>
    private static void Chevron(int side, (float Base, Material Material) spec)
    {
        var polygon = new List<Vector2> { new Vector2(0, spec.Base), new Vector2(1.24f, spec.Base - 1.24f), new Vector2(1.24f, spec.Base - 1.24f + 0.21f), new Vector2(0, spec.Base + 0.21f) };

        polygon = Clip(polygon, (0.72f, true));

        polygon = Clip(polygon, (2.34f, false));

        if (polygon.Count < 3)
        {
            return;
        }

        var vertices = polygon.Select(p => new Vector3(side * p.x, p.y, -4.417f)).ToArray();

        var triangles = new List<int>();

        for (var i = 1; i < polygon.Count - 1; i++)
        {
            if (side > 0) { triangles.AddRange(new[] { 0, i + 1, i }); }
            else { triangles.AddRange(new[] { 0, i, i + 1 }); }
        }

        var mesh = new Mesh { name = "ClippedRearChevron", vertices = vertices, triangles = triangles.ToArray() };

        mesh.RecalculateNormals();

        TemporaryMeshes.Add(mesh);

        MeshPart("RearChevron", (mesh, Vector3.zero, spec.Material));
    }

    /// <summary>Clips a polygon against a horizontal boundary.</summary>
    private static List<Vector2> Clip(List<Vector2> input, (float Height, bool KeepAbove) boundary)
    {
        var result = new List<Vector2>();

        for (var i = 0; i < input.Count; i++)
        {
            var a = input[i];

            var b = input[(i + 1) % input.Count];

            var insideA = boundary.KeepAbove ? a.y >= boundary.Height : a.y <= boundary.Height;

            var insideB = boundary.KeepAbove ? b.y >= boundary.Height : b.y <= boundary.Height;

            if (insideA) { result.Add(a); }

            if (insideA != insideB)
            {
                result.Add(Vector2.Lerp(a, b, (boundary.Height - a.y) / (b.y - a.y)));
            }
        }

        return result;
    }

    /// <summary>Builds folded canvas hose, top deck, cover rails and a monitor.</summary>
    private static void Hosebed()
    {
        Section("HosebedAndRoofEquipment");

        Part("HosebedFloor", (new Vector3(0, 2.68f, -2.05f), new Vector3(2.3f, 0.08f, 4.37f), _plate, 0.02f));

        foreach (var side in new[] { -1, 1 })
        {
            Part("HosebedCoaming", (new Vector3(side * 1.21f, 2.87f, -2.05f), new Vector3(0.1f, 0.39f, 4.4f), _red, 0.025f));
        }

        for (var lane = 0; lane < 4; lane++)
        {
            for (var layer = 0; layer < 3; layer++)
            {
                Part("FoldedAttackHose", (new Vector3(-0.82f + lane * 0.44f, 2.77f + layer * 0.075f, -2.1f), new Vector3(0.33f, 0.075f, 3.87f - layer * 0.08f), _hose, 0.035f));
            }
        }

        foreach (var side in new[] { -1, 1 })
        {
            var cover = Part("PeakedHosebedCover", (new Vector3(side * 0.615f, 3.14f, -2.13f), new Vector3(1.26f, 0.055f, 4.5f), _plate, 0.012f));

            cover.transform.localRotation = Quaternion.Euler(0, 0, -side * 10f);
        }

        Beam("CoverCentreSeam", (new Vector3(0, 3.268f, -4.38f), new Vector3(0, 3.268f, 0.12f), 0.009f, _dark));

        foreach (var z in new[] { -4.385f, 0.125f })
        {
            var cap = new Mesh { name = "PeakedCoverEnd", vertices = new[] { new Vector3(-1.24f, 3.03f, z), new Vector3(0, 3.265f, z), new Vector3(1.24f, 3.03f, z) }, triangles = z < 0 ? new[] { 0, 1, 2 } : new[] { 2, 1, 0 }, uv = new[] { new Vector2(0, 0), new Vector2(1.24f, 0.235f), new Vector2(2.48f, 0) } };

            cap.RecalculateNormals();

            TemporaryMeshes.Add(cap);

            MeshPart("PeakedCoverEnd", (cap, Vector3.zero, _plate));
        }

        Beam("SuctionHose", (new Vector3(-1.18f, 3.12f, -3.94f), new Vector3(-1.18f, 3.12f, -0.05f), 0.072f, _dark));

        foreach (var z in new[] { -3.85f, -2.7f, -1.2f, -0.15f })
        {
            Cylinder("SuctionHoseCollar", (new Vector3(-1.18f, 3.12f, z), Vector3.forward, 0.086f, 0.095f, _chrome));
        }

        Cylinder("MonitorBase", (new Vector3(0, 2.91f, 0.4f), Vector3.up, 0.15f, 0.2f, _chrome));

        Beam("MonitorUpright", (new Vector3(0, 2.95f, 0.4f), new Vector3(0, 3.18f, 0.4f), 0.08f, _red));

        Beam("MonitorBarrel", (new Vector3(0, 3.18f, 0.4f), new Vector3(0, 3.3f, 0.95f), 0.075f, _red));

        Cylinder("MonitorNozzle", (new Vector3(0, 3.3f, 0.98f), new Vector3(0, 0.2f, 1), 0.09f, 0.18f, _dark));
    }

    /// <summary>Creates a segmented lightbar and inactive emergency lights.</summary>
    private static void Lighting()
    {
        Section("EmergencyLighting");

        foreach (var side in new[] { -1, 1 })
        {
            Part("LightbarMount", (new Vector3(side * 0.67f, 2.98f, 3.17f), new Vector3(0.12f, 0.13f, 0.18f), _chrome, 0.02f));
        }

        Part("LightbarChassis", (new Vector3(0, 3.08f, 3.17f), new Vector3(2.35f, 0.035f, 0.36f), _chrome, 0.014f));

        Part("LightbarTopCap", (new Vector3(0, 3.24f, 3.17f), new Vector3(2.35f, 0.025f, 0.36f), _chrome, 0.01f));

        for (var i = 0; i < 12; i++)
        {
            var x = -1.045f + i * 0.19f;

            Part("LightbarModuleHousing", (new Vector3(x, 3.16f, 3.17f), new Vector3(0.18f, 0.14f, 0.31f), _dark, 0.022f));

            foreach (var row in new[] { -1, 1 })
            {
                foreach (var face in new[] { -1, 1 })
                {
                    Part("TwinLEDLensOff", (new Vector3(x, 3.16f + row * 0.035f, 3.17f + face * 0.16f), new Vector3(0.156f, 0.048f, 0.022f), _clearLens, 0.014f));
                }
            }
        }

        foreach (var side in new[] { -1, 1 })
        {
            foreach (var z in new[] { -4f, -0.25f, -3.13f, -2.17f, 1.95f })
            {
                var y = z == 1.95f ? 1.56f : z == -4f || z == -0.25f ? 2.7f : 1.47f;

                Part("SideMarkerGasket", (new Vector3(side * 1.36f, y, z), new Vector3(0.035f, 0.09f, 0.23f), _dark, 0.025f));

                Part("SideMarkerLensOff", (new Vector3(side * 1.382f, y, z), new Vector3(0.018f, 0.054f, 0.18f), _clearLens, 0.018f));
            }
        }

        for (var i = 0; i < 5; i++)
        {
            Part("RoofMarkerOff", (new Vector3(-0.9f + i * 0.45f, 2.89f, 3.72f), new Vector3(0.12f, 0.08f, 0.09f), _amber, 0.03f));
        }

        foreach (var side in new[] { -1, 1 })
        {
            var light = new GameObject(side < 0 ? "EmergencyLightLeft" : "EmergencyLightRight").AddComponent<Light>();

            light.transform.SetParent(_section, false);

            light.transform.localPosition = new Vector3(side, 3.2f, 3.2f);

            light.color = new Color(1, 0.025f, 0.015f);

            light.range = 14;

            light.intensity = 0;

            light.enabled = false;
        }
    }

    /// <summary>Creates a smooth tire, rim, ventilation circle and ten lug nuts.</summary>
    private static void Wheel(int side, float z)
    {
        Section((z > 0 ? "WheelFront" : "WheelRear") + (side < 0 ? "Left" : "Right"));

        var center = new Vector3(side * 1.25f, 0.65f, z);

        var profile = new[] { new Vector2(-0.21f, 0.37f), new Vector2(-0.22f, 0.49f), new Vector2(-0.18f, 0.59f), new Vector2(-0.09f, 0.65f), new Vector2(0.09f, 0.65f), new Vector2(0.18f, 0.59f), new Vector2(0.22f, 0.49f), new Vector2(0.21f, 0.37f) };

        var mesh = Lathe(profile, true);

        MeshPart("RoundedTire", (mesh, center, _tire));

        Cylinder("WheelRim", (center, Vector3.right, 0.39f, 0.44f, _chrome));

        Cylinder("RimInset", (center + Vector3.right * side * 0.23f, Vector3.right, 0.35f, 0.025f, _chrome));

        Cylinder("HubCap", (center + Vector3.right * side * 0.26f, Vector3.right, 0.15f, 0.09f, _chrome));

        Cylinder("HubCentreRecess", (center + Vector3.right * side * 0.311f, Vector3.right, 0.047f, 0.008f, _dark));

        if (z < 0)
        {
            MeshPart("InnerDualTire", (Lathe(profile, true), center - Vector3.right * side * 0.46f, _tire));
        }

        for (var bolt = 0; bolt < 10; bolt++)
        {
            var angle = bolt * Mathf.PI * 2 / 10;

            var radial = new Vector3(0, Mathf.Cos(angle), Mathf.Sin(angle));

            Cylinder("RimVent", (center + radial * 0.29f + Vector3.right * side * 0.25f, Vector3.right, 0.042f, 0.018f, _dark));

            Cylinder("LugNut", (center + radial * 0.2f + Vector3.right * side * 0.29f, Vector3.right, 0.023f, 0.035f, _dark));
        }

        for (var tread = 0; tread < 40; tread++)
        {
            var angle = tread * Mathf.PI * 2 / 40;

            var radial = new Vector3(0, Mathf.Cos(angle), Mathf.Sin(angle));

            var block = Part("TreadBlock", (center + radial * 0.642f, new Vector3(0.17f, 0.022f, 0.055f), _dark, 0.007f));

            block.transform.localRotation = Quaternion.Euler(angle * Mathf.Rad2Deg, 0, 0);
        }

        // Pivot each wheel about its axle for later animation.
        foreach (Transform child in _section)
        {
            child.localPosition -= center;
        }

        _section.localPosition = center;
    }

    /// <summary>Builds a smooth partial circular fender rail in the wheel plane.</summary>
    private static void Arc(string name, (Vector3 Center, float Radius, float Tube, int Side, Material Material) spec)
    {
        const int segments = 64;

        const int rings = 8;

        var vertices = new List<Vector3>();

        var normals = new List<Vector3>();

        var indices = new List<int>();

        for (var segment = 0; segment <= segments; segment++)
        {
            var angle = segment * Mathf.PI / segments;

            var radial = new Vector3(0, Mathf.Sin(angle), Mathf.Cos(angle));

            for (var ring = 0; ring <= rings; ring++)
            {
                var cross = ring * Mathf.PI * 2 / rings;

                var normal = Vector3.right * Mathf.Cos(cross) + radial * Mathf.Sin(cross);

                vertices.Add(spec.Center + radial * spec.Radius + normal * spec.Tube);

                normals.Add(normal);

                if (segment < segments && ring < rings)
                {
                    var index = segment * (rings + 1) + ring;

                    indices.AddRange(new[] { index, index + rings + 1, index + 1, index + 1, index + rings + 1, index + rings + 2 });
                }
            }
        }

        var mesh = new Mesh { name = name };

        mesh.SetVertices(vertices);

        mesh.SetNormals(normals);

        mesh.SetTriangles(indices, 0);

        mesh.RecalculateBounds();

        TemporaryMeshes.Add(mesh);

        MeshPart(name, (mesh, Vector3.zero, spec.Material));
    }
    /// <summary>Creates a beveled box with physical dimensions in its mesh.</summary>
    private static GameObject Part(string name, (Vector3 Position, Vector3 Size, Material Material, float Radius) spec)
    {
        var radius = Mathf.Min(spec.Radius, Mathf.Min(spec.Size.x, Mathf.Min(spec.Size.y, spec.Size.z)) * 0.45f);

        var key = spec.Size.ToString("F4") + ":" + radius.ToString("F4");

        if (!RoundedMeshes.TryGetValue(key, out var mesh))
        {
            mesh = RoundedBox(spec.Size, radius);

            RoundedMeshes.Add(key, mesh);
        }

        return MeshPart(name, (mesh, spec.Position, spec.Material));
    }

    /// <summary>Attaches a generated mesh in the current assembly.</summary>
    private static GameObject MeshPart(string name, (Mesh Mesh, Vector3 Position, Material Material) spec)
    {
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));

        go.transform.SetParent(_section, false);

        go.transform.localPosition = spec.Position;

        go.GetComponent<MeshFilter>().sharedMesh = spec.Mesh;

        go.GetComponent<MeshRenderer>().sharedMaterial = spec.Material;

        return go;
    }

    /// <summary>Builds a rounded cuboid by projecting subdivided faces onto an inset box.</summary>
    private static Mesh RoundedBox(Vector3 size, float radius)
    {
        var vertices = new List<Vector3>();

        var normals = new List<Vector3>();

        var uvs = new List<Vector2>();

        var triangles = new List<int>();

        var half = size * 0.5f;

        var inner = half - Vector3.one * radius;

        var faces = new[] { (Vector3.right, Vector3.back, Vector3.up), (Vector3.left, Vector3.forward, Vector3.up), (Vector3.up, Vector3.right, Vector3.back), (Vector3.down, Vector3.right, Vector3.forward), (Vector3.forward, Vector3.right, Vector3.up), (Vector3.back, Vector3.left, Vector3.up) };

        foreach (var face in faces)
        {
            var normal = face.Item1;

            var u = face.Item2;

            var v = face.Item3;

            var hu = Vector3.Dot(new Vector3(Mathf.Abs(u.x), Mathf.Abs(u.y), Mathf.Abs(u.z)), half);

            var hv = Vector3.Dot(new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z)), half);

            var hn = Vector3.Dot(new Vector3(Mathf.Abs(normal.x), Mathf.Abs(normal.y), Mathf.Abs(normal.z)), half);

            var us = new[] { -hu, -hu + radius * 0.3f, -hu + radius, hu - radius, hu - radius * 0.3f, hu };

            var vs = new[] { -hv, -hv + radius * 0.3f, -hv + radius, hv - radius, hv - radius * 0.3f, hv };

            var start = vertices.Count;

            for (var y = 0; y < vs.Length; y++)
            {
                for (var x = 0; x < us.Length; x++)
                {
                    var raw = normal * hn + u * us[x] + v * vs[y];

                    var clamped = new Vector3(Mathf.Clamp(raw.x, -inner.x, inner.x), Mathf.Clamp(raw.y, -inner.y, inner.y), Mathf.Clamp(raw.z, -inner.z, inner.z));

                    var outward = (raw - clamped).normalized;

                    vertices.Add(clamped + outward * radius);

                    normals.Add(outward);

                    uvs.Add(new Vector2(us[x] + hu, vs[y] + hv));
                }
            }

            for (var y = 0; y < vs.Length - 1; y++)
            {
                for (var x = 0; x < us.Length - 1; x++)
                {
                    var a = start + y * us.Length + x;

                    triangles.AddRange(new[] { a, a + 1, a + us.Length + 1, a, a + us.Length + 1, a + us.Length });
                }
            }
        }

        var mesh = new Mesh { name = "RoundedCuboid" };

        mesh.SetVertices(vertices);

        mesh.SetNormals(normals);

        mesh.SetUVs(0, uvs);

        mesh.SetTriangles(triangles, 0);

        mesh.RecalculateBounds();

        TemporaryMeshes.Add(mesh);

        return mesh;
    }

    /// <summary>Creates a smooth axial cylinder from its centre and direction.</summary>
    private static void Cylinder(string name, (Vector3 Position, Vector3 Axis, float Radius, float Length, Material Material) spec)
    {
        var mesh = Lathe(new[] { new Vector2(-spec.Length * 0.5f, 0), new Vector2(-spec.Length * 0.5f, spec.Radius), new Vector2(spec.Length * 0.5f, spec.Radius), new Vector2(spec.Length * 0.5f, 0) }, false);

        var go = MeshPart(name, (mesh, spec.Position, spec.Material));

        go.transform.localRotation = Quaternion.FromToRotation(Vector3.right, spec.Axis);
    }

    /// <summary>Creates a tube between two points.</summary>
    private static void Beam(string name, (Vector3 Start, Vector3 End, float Radius, Material Material) spec)
    {
        Cylinder(name, ((spec.Start + spec.End) * 0.5f, spec.End - spec.Start, spec.Radius, Vector3.Distance(spec.Start, spec.End), spec.Material));
    }

    /// <summary>Builds a capped cone section on the vertical axis.</summary>
    private static void Frustum(string name, (Vector3 Position, float BottomRadius, float TopRadius, float Height, Material Material) spec)
    {
        var mesh = Lathe(new[] { new Vector2(-spec.Height * 0.5f, 0), new Vector2(-spec.Height * 0.5f, spec.BottomRadius), new Vector2(spec.Height * 0.5f, spec.TopRadius), new Vector2(spec.Height * 0.5f, 0) }, false);

        var go = MeshPart(name, (mesh, spec.Position, spec.Material));

        go.transform.localRotation = Quaternion.FromToRotation(Vector3.right, Vector3.up);
    }

    /// <summary>Revolves an axial/radial profile about the X axis.</summary>
    private static Mesh Lathe(Vector2[] profile, bool closeProfile)
    {
        const int segments = 32;

        var vertices = new List<Vector3>();

        var uv = new List<Vector2>();

        var triangles = new List<int>();

        for (var ring = 0; ring < profile.Length; ring++)
        {
            for (var i = 0; i < segments; i++)
            {
                var angle = i * Mathf.PI * 2 / segments;

                vertices.Add(new Vector3(profile[ring].x, profile[ring].y * Mathf.Cos(angle), profile[ring].y * Mathf.Sin(angle)));

                uv.Add(new Vector2(i / (float)segments, ring / (float)(profile.Length - 1)));
            }
        }

        for (var ring = 0; ring < profile.Length - (closeProfile ? 0 : 1); ring++)
        {
            var nextRing = (ring + 1) % profile.Length;

            for (var i = 0; i < segments; i++)
            {
                var next = (i + 1) % segments;

                var a = ring * segments + i;

                var b = nextRing * segments + i;

                var c = nextRing * segments + next;

                var d = ring * segments + next;

                triangles.AddRange(new[] { a, c, b, a, d, c });
            }
        }

        var mesh = new Mesh { name = "LathedHardware" };

        mesh.SetVertices(vertices);

        mesh.SetUVs(0, uv);

        mesh.SetTriangles(triangles, 0);

        mesh.RecalculateNormals();

        mesh.RecalculateBounds();

        TemporaryMeshes.Add(mesh);

        return mesh;
    }

    /// <summary>Adds sharp scalable unit lettering with correct front/side orientation.</summary>
    private static void Label(string name, (string Text, Vector3 Position, float Yaw, Vector2 Size, float FontSize, Color Color) spec)
    {
        var text = new GameObject(name).AddComponent<TextMeshPro>();

        text.transform.SetParent(_section, false);

        text.transform.localPosition = spec.Position;

        text.transform.localRotation = Quaternion.Euler(0, spec.Yaw, 0);

        text.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");

        text.text = spec.Text;

        text.fontSize = spec.FontSize;

        text.fontStyle = FontStyles.Bold;

        text.alignment = TextAlignmentOptions.Center;

        text.characterSpacing = 1.5f;

        text.color = spec.Color;

        text.rectTransform.sizeDelta = spec.Size;

        text.textWrappingMode = TextWrappingModes.NoWrap;

        text.ForceMeshUpdate();
    }

    /// <summary>Combines detail meshes by assembly/material, persists them, and retains wheel pivots and text.</summary>
    private static void CombineSections()
    {
        foreach (var section in Sections)
        {
            var filters = section.GetComponentsInChildren<MeshFilter>().Where(f => f.GetComponent<TMP_Text>() == null && f.sharedMesh != null).ToArray();

            var groups = filters.GroupBy(f => f.GetComponent<MeshRenderer>().sharedMaterial).ToArray();

            foreach (var group in groups)
            {
                var instances = group.Select(f => new CombineInstance { mesh = f.sharedMesh, transform = section.worldToLocalMatrix * f.transform.localToWorldMatrix }).ToArray();

                var mesh = new Mesh { name = section.name + "_" + group.Key.name, indexFormat = IndexFormat.UInt32 };

                mesh.CombineMeshes(instances, true, true, false);

                mesh.RecalculateBounds();

                mesh = Persist(mesh, Folder + "/Meshes/" + mesh.name + ".asset");

                var go = new GameObject(group.Key.name, typeof(MeshFilter), typeof(MeshRenderer));

                go.transform.SetParent(section, false);

                go.GetComponent<MeshFilter>().sharedMesh = mesh;

                go.GetComponent<MeshRenderer>().sharedMaterial = group.Key;
            }

            foreach (var filter in filters)
            {
                Object.DestroyImmediate(filter.gameObject);
            }
        }
    }
}





