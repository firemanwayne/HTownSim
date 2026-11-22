using System;
using System.Collections.Generic;
using TMPro;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// Remodels the station into Houston Fire Station 7 (three bays: Ladder 7, Engine 7, Medic 7 as seen from the
/// street, left to right) and rebuilds Engine 7 with reference-based detail. Ladder 7 and Medic 7 are parked in
/// their bays; Engine 7 is the player's truck and starts in the middle bay.
/// </summary>
public static class BuildApparatus
{
    private const string MaterialFolder = "Assets/HTownSim/Generated/";

    /// <summary>World offset the station root was given by BuildRealMidtown.</summary>
    private static readonly Vector3 StationOffset = new Vector3(11.3f, 0f, 50.2f);

    /// <summary>Bay centres in the station's own (old) coordinates, left to right as seen from the street.</summary>
    private const float LadderBayX = 9f;

    private const float EngineBayX = 1f;

    private const float MedicBayX = -7f;

    private const float HallCentreZ = -71f;

    private static Material _flashRed, _flashWhite, _flashAmber, _glowRed, _glowWhite, _glowAmber, _tireGrey, _plateWhite, _offRed, _offWhite, _offAmber;

    private static readonly List<(Renderer Lens, Material On, Material Off, int Group)> Flashers = new List<(Renderer, Material, Material, int)>();

    private static Material _red, _white, _chrome, _plate, _black, _glass, _yellow, _hose, _alu, _gold, _blue, _amber, _lensRed, _lensGreen, _headlight, _steel, _cream, _dark;

    private static Transform _parent = null!;

    private readonly struct Spec
    {
        public Spec(string name, Vector3 position, Vector3 scale, Material material, PrimitiveType shape = PrimitiveType.Cube, Quaternion? rotation = null)
        {
            Name = name;

            Position = position;

            Scale = scale;

            Material = material;

            Shape = shape;

            Rotation = rotation ?? Quaternion.identity;
        }

        public string Name { get; }

        public Vector3 Position { get; }

        public Vector3 Scale { get; }

        public Material Material { get; }

        public PrimitiveType Shape { get; }

        public Quaternion Rotation { get; }
    }

    private readonly struct LabelSpec
    {
        public LabelSpec(string text, Vector3 position, float yaw, Vector2 box, float fontSize, Color colour)
        {
            Text = text;

            Position = position;

            Yaw = yaw;

            Box = box;

            FontSize = fontSize;

            Colour = colour;
        }

        public string Text { get; }

        public Vector3 Position { get; }

        public float Yaw { get; }

        public Vector2 Box { get; }

        public float FontSize { get; }

        public Color Colour { get; }
    }

    /// <summary>Runs the whole remodel and returns a summary.</summary>
    public static string Build()
    {
        if (EditorApplication.isPlaying)
        {
            return "Exit Play Mode first.";
        }

        LoadMaterials();

        var station = GameObject.Find("Station07") ?? GameObject.Find("Station08");

        var engine = GameObject.Find("Engine07") ?? GameObject.Find("Engine08");

        if (station is null || engine is null)
        {
            return "Station or engine not found.";
        }

        station.name = "Station07";

        engine.name = "Engine07";

        foreach (var old in new[] { "Ladder07", "Medic07" })
        {
            var existing = GameObject.Find(old);

            if (existing != null)
            {
                Object.DestroyImmediate(existing);
            }
        }

        RemodelStation(station.transform);

        BuildEngine(engine);

        var ladder = NewVehicle("Ladder07", LadderBayX);

        BuildLadder(ladder);

        var medic = NewVehicle("Medic07", MedicBayX);

        BuildMedic(medic);

        PlaceEngine(engine);

        ConfigureCamera(engine);

        UpdateHudText();

        AssetDatabase.SaveAssets();

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        return "Station 7 remodelled; Ladder 7, Engine 7 and Medic 7 built. Engine 7 at " + engine.transform.position;
    }

    private static void LoadMaterials()
    {
        _red = Mat("ApparatusRed", "B52E32", 0.45f);

        _white = Mat("PaintWhite", "DAD8CC", 0.4f);

        _chrome = Mat("Chrome", "C8D0D4", 0.9f);

        _plate = Mat("DiamondPlate", "8C979C", 0.7f);

        _black = Mat("TruckBlack", "14191C", 0.3f);

        _glass = Mat("TruckGlass", "1B2F3B", 0.95f);

        _yellow = Mat("ChevronYellow", "E5E32F", 0.4f);

        _hose = Mat("HoseTan", "C3AE7C", 0.2f);

        _alu = Mat("LadderAlu", "D8DEE0", 0.75f);

        _gold = Mat("ShieldGold", "D4A83A", 0.8f);

        _blue = Mat("ShieldBlue", "2B4C8C", 0.5f);

        _amber = Mat("Amber", "FFAF52", 0.5f);

        _lensRed = Mat("LensRed", "E03A3A", 0.6f);

        _lensGreen = Mat("SignalGreen", "3AD17A", 0.6f);

        _headlight = Mat("Headlight", "F4F1E2", 0.9f);

        _steel = Mat("Steel", "71858C", 0.6f);

        _cream = Mat("Limestone", "E6D7B7", 0.3f);

        _dark = Mat("Rubber", "18282F", 0.2f);

        Brighten();
    }

    /// <summary>Gives the apparatus a brighter paint colour and makes lenses glow so they read at dusk.</summary>
    private static void Brighten()
    {
        _red.color = Hex("C7302E");

        _red.SetFloat("_Smoothness", 0.6f);

        EditorUtility.SetDirty(_red);

        _glowRed = Glow("GlowRed", "FF2A2A", 2.2f);

        _glowWhite = Glow("GlowWhite", "FFF6E0", 2.5f);

        _glowAmber = Glow("GlowAmber", "FFA640", 2.0f);

        _headlight = Glow("Headlight", "FFF6E0", 2.2f);

        _tireGrey = Mat("TireGrey", "2B3439", 0.25f);

        _plateWhite = Mat("PlateWhite", "ECEBE4", 0.3f);

        _flashRed = Glow("FlashRedOn", "FF2A2A", 10f);

        _flashWhite = Glow("FlashWhiteOn", "FFF6E0", 12f);

        _flashAmber = Glow("FlashAmberOn", "FFA640", 8f);

        _offRed = Mat("FlasherRedOff", "7A2222", 0.6f);

        _offWhite = Mat("FlasherWhiteOff", "C9C6BC", 0.6f);

        _offAmber = Mat("FlasherAmberOff", "8A5A1E", 0.6f);
    }

    private static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out var colour);

        return colour;
    }

    private static Material Glow(string name, string hex, float intensity)
    {
        var material = Mat(name, hex, 0.8f);

        material.color = Hex(hex);

        material.EnableKeyword("_EMISSION");

        material.SetColor("_EmissionColor", Hex(hex) * intensity);

        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;

        EditorUtility.SetDirty(material);

        return material;
    }

    private static Material Mat(string name, string hex, float smoothness)
    {
        var path = MaterialFolder + name + ".mat";

        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (existing != null)
        {
            return existing;
        }

        ColorUtility.TryParseHtmlString("#" + hex, out var colour);

        var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name, color = colour };

        material.SetFloat("_Smoothness", smoothness);

        AssetDatabase.CreateAsset(material, path);

        return material;
    }

    /// <summary>Opens the office into a third bay, adds bay signage and lights, and shortens the apron.</summary>
    private static void RemodelStation(Transform station)
    {
        var remove = new List<GameObject>();

        Transform? doorTemplate = null;

        Transform? pillarTemplate = null;

        Transform? fascia = null;

        Transform? stationName = null;

        foreach (Transform child in station)
        {
            switch (child.name)
            {
                case "OfficeWindow":
                case "OfficeRoof":
                    remove.Add(child.gameObject);

                    break;

                case "LeftWall":
                    remove.Add(child.gameObject);

                    break;

                case "OpenBayDoor":
                    doorTemplate = child;

                    break;

                case "CenterPillar":
                    pillarTemplate = child;

                    break;

                case "Fascia":
                    fascia = child;

                    break;

                case "StationName":
                    stationName = child;

                    break;
            }
        }

        foreach (var go in remove)
        {
            Object.DestroyImmediate(go);
        }

        _parent = station;

        var fresh = station.Find("BayPillarWest") is null;

        if (pillarTemplate != null && fresh)
        {
            var pillar = Object.Instantiate(pillarTemplate.gameObject, station);

            pillar.name = "BayPillarWest";

            pillar.transform.localPosition = new Vector3(-3f, pillarTemplate.localPosition.y, pillarTemplate.localPosition.z);
        }

        if (doorTemplate != null && fresh)
        {
            var header = Object.Instantiate(doorTemplate.gameObject, station);

            header.name = "BayHeaderMedic";

            header.transform.localPosition = new Vector3(MedicBayX, doorTemplate.localPosition.y, doorTemplate.localPosition.z);
        }

        var floor = station.Find("BayFloor");

        if (floor != null)
        {
            floor.localPosition = new Vector3(1f, floor.localPosition.y, floor.localPosition.z);

            floor.localScale = new Vector3(24f, floor.localScale.y, floor.localScale.z);
        }

        foreach (Transform child in station)
        {
            if (child.name == "ApronGuide")
            {
                child.GetComponent<Renderer>().sharedMaterial = _yellow;
            }
        }

        foreach (var x in fresh ? new[] { MedicBayX } : new float[0])
        {
            foreach (var side in new[] { -2.4f, 2.4f })
            {
                var guide = Object.Instantiate(station.Find("ApronGuide").gameObject, station);

                guide.name = "ApronGuide";

                var p = guide.transform.localPosition;

                guide.transform.localPosition = new Vector3(x + side, p.y, p.z);
            }
        }

        foreach (var bay in fresh ? new[] { (LadderBayX, "LADDER 7"), (EngineBayX, "ENGINE 7"), (MedicBayX, "MEDIC 7") } : new (float, string)[0])
        {
            Label(new LabelSpec(bay.Item2, new Vector3(bay.Item1, 5.3f, -63.6f), 180f, new Vector2(7f, 0.55f), 4.5f, _cream.color));
        }

        if (stationName != null)
        {
            var text = stationName.GetComponent<TMP_Text>();

            text.text = "CITY OF HOUSTON   FIRE STATION NO. 7";
        }

        foreach (Transform child in station)
        {
            if (child.name == "BayLight")
            {
                var existing = child.GetComponent<Light>();

                existing.intensity = 14f;

                existing.range = 18f;
            }
        }

        foreach (var x in fresh ? new[] { MedicBayX, EngineBayX, LadderBayX } : new float[0])
        {
            var light = new GameObject("BayLight").AddComponent<Light>();

            light.transform.SetParent(station, false);

            light.transform.localPosition = new Vector3(x, 9f, HallCentreZ);

            light.type = LightType.Point;

            light.range = 18f;

            light.intensity = 14f;

            light.color = new Color(1f, 0.93f, 0.8f);

            light.shadows = LightShadows.None;
        }

        _ = fascia;
    }

    /// <summary>Creates a vehicle root in its bay (world position from the station offset).</summary>
    private static Transform NewVehicle(string name, float bayX)
    {
        var root = new GameObject(name).transform;

        root.position = new Vector3(bayX, 0.2f, HallCentreZ) + StationOffset;

        _parent = root;

        return root;
    }

    private static void PlaceEngine(GameObject engine)
    {
        var controller = engine.GetComponent<CharacterController>();

        controller.enabled = false;

        engine.transform.SetPositionAndRotation(new Vector3(EngineBayX, 0.2f, HallCentreZ) + StationOffset, Quaternion.identity);

        controller.enabled = true;
    }

    private static void ConfigureCamera(GameObject engine)
    {
        engine.tag = "Player";

        var camera = GameObject.Find("VehicleCamera");

        if (camera is null || camera.GetComponent<CinemachineDeoccluder>() != null)
        {
            return;
        }

        var deoccluder = camera.AddComponent<CinemachineDeoccluder>();

        deoccluder.CollideAgainst = 1;

        deoccluder.IgnoreTag = "Player";

        deoccluder.MinimumDistanceFromTarget = 1.5f;
    }

    private static void UpdateHudText()
    {
        foreach (var text in Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include))
        {
            if (text.text.Contains("ENGINE 08"))
            {
                text.text = text.text.Replace("ENGINE 08", "ENGINE 7");
            }
        }
    }

    // ------------------------------------------------------------------ Engine 7

    private static void BuildEngine(GameObject engine)
    {
        var lights = new List<Light>(engine.GetComponentsInChildren<Light>());

        foreach (var light in lights)
        {
            light.transform.SetParent(null, true);
        }

        for (var i = engine.transform.childCount - 1; i >= 0; i--)
        {
            Object.DestroyImmediate(engine.transform.GetChild(i).gameObject);
        }

        _parent = engine.transform;

        Flashers.Clear();

        Cab(new CabSpec(0f, 2.1f, 2.8f, 2.6f, 7f));

        Part("Chassis", new Vector3(0f, 0.8f, -0.2f), new Vector3(2.2f, 0.4f, 7.6f), _black);

        Part("EquipmentBody", new Vector3(0f, 1.65f, -1.6f), new Vector3(2.6f, 2f, 4.6f), _red);

        Part("RaisedCabRoof", new Vector3(0f, 2.93f, 1.55f), new Vector3(2.6f, 0.55f, 1.7f), _red);

        // Full-width lightbar across the front of the cab roof, as on the real engine.
        Part("LightbarBase", new Vector3(0f, 2.74f, 3.2f), new Vector3(2.5f, 0.1f, 0.42f), _black);

        for (var i = 0; i < 6; i++)
        {
            var x = -1.0f + i * 0.4f;

            Lens("Lightbar", new Vector3(x, 2.84f, 3.2f), new Vector3(0.37f, 0.16f, 0.36f), i == 0 || i == 5 ? 0 : 1, x < 0f ? 0 : 1);
        }

        Part("LightbarCap", new Vector3(0f, 2.94f, 3.2f), new Vector3(2.5f, 0.04f, 0.42f), _black);

        foreach (var side in new[] { -1f, 1f })
        {
            var beacon = lights.Find(l => Mathf.Sign(l.transform.position.x - engine.transform.position.x) == side) ?? lights[0];

            beacon.transform.SetParent(engine.transform, false);

            beacon.transform.localPosition = new Vector3(side * 0.8f, 3.1f, 3.45f);

            // Forward-facing spot, pitched down so the flashing light sweeps the road ahead of the engine.
            beacon.transform.localRotation = Quaternion.Euler(16f, side * 12f, 0f);

            beacon.type = LightType.Spot;

            beacon.spotAngle = 105f;

            beacon.innerSpotAngle = 45f;

            beacon.range = 34f;

            beacon.color = side < 0f ? Hex("FF2A22") : Hex("FFF1E0");

            beacon.shadows = LightShadows.None;

            lights.Remove(beacon);
        }

        foreach (var side in new[] { -1f, 1f })
        {
            // Pump panel with gauges and valves.
            Part("PumpPanel", new Vector3(side * 1.31f, 1.7f, -0.1f), new Vector3(0.06f, 1.3f, 0.85f), _black);

            foreach (var gz in new[] { -0.3f, 0.1f })
            {
                Part("Gauge", new Vector3(side * 1.35f, 2.15f, gz - 0.1f), new Vector3(0.24f, 0.02f, 0.24f), _white, PrimitiveType.Cylinder, Quaternion.Euler(0f, 0f, 90f));
            }

            foreach (var vz in new[] { -0.35f, -0.1f, 0.15f })
            {
                Part("Valve", new Vector3(side * 1.4f, 1.5f, vz - 0.05f), new Vector3(0.16f, 0.18f, 0.16f), _chrome, PrimitiveType.Cylinder, Quaternion.Euler(0f, 0f, 90f));
            }

            // Roll-up compartment doors, two rows.
            foreach (var z in new[] { -1.3f, -2.45f, -3.5f })
            {
                RollUp(side, 1.2f, z, 1.0f);

                RollUp(side, 2.15f, z, 1.0f);
            }

            // White stripe band, white Z, door seams, windows, handles, steps, mirrors, shields.
            Part("StripeBand", new Vector3(side * 1.31f, 1.0f, -0.2f), new Vector3(0.04f, 0.2f, 7.6f), _white);

            Part("ZTop", new Vector3(side * 1.31f, 2.42f, -2.3f), new Vector3(0.04f, 0.14f, 2.8f), _white);

            Part("ZDiagonal", new Vector3(side * 1.31f, 1.75f, -0.45f), new Vector3(0.04f, 0.14f, 1.58f), _white, PrimitiveType.Cube, Quaternion.Euler(55f, 0f, 0f));

            Part("DoorSeam", new Vector3(side * 1.31f, 1.65f, 1.95f), new Vector3(0.04f, 1.9f, 0.04f), _black);

            Part("CabWindow", new Vector3(side * 1.31f, 2.2f, 2.55f), new Vector3(0.05f, 0.8f, 1.0f), _glass);

            Part("CrewWindow", new Vector3(side * 1.31f, 2.2f, 1.3f), new Vector3(0.05f, 0.8f, 0.85f), _glass);

            Part("DoorHandle", new Vector3(side * 1.33f, 1.7f, 2.2f), new Vector3(0.06f, 0.05f, 0.22f), _chrome);

            Part("DoorHandle", new Vector3(side * 1.33f, 1.7f, 1.6f), new Vector3(0.06f, 0.05f, 0.22f), _chrome);

            Part("Step", new Vector3(side * 1.45f, 0.55f, 2.1f), new Vector3(0.3f, 0.06f, 1.4f), _plate);

            Part("MirrorArm", new Vector3(side * 1.55f, 2.55f, 3.3f), new Vector3(0.5f, 0.05f, 0.05f), _chrome);

            Part("Mirror", new Vector3(side * 1.78f, 2.55f, 3.3f), new Vector3(0.1f, 0.46f, 0.26f), _black);

            Part("MirrorGlass", new Vector3(side * 1.78f, 2.55f, 3.3f - 0.135f), new Vector3(0.08f, 0.4f, 0.02f), _chrome);

            Part("ShieldGold", new Vector3(side * 1.32f, 1.65f, 2.05f), new Vector3(0.46f, 0.01f, 0.46f), _gold, PrimitiveType.Cylinder, Quaternion.Euler(0f, 0f, 90f));

            Part("ShieldBlue", new Vector3(side * 1.325f, 1.65f, 2.05f), new Vector3(0.36f, 0.01f, 0.36f), _blue, PrimitiveType.Cylinder, Quaternion.Euler(0f, 0f, 90f));

            Label(new LabelSpec("7", new Vector3(side * 1.34f, 1.45f, -3.1f), side < 0f ? 90f : 270f, new Vector2(0.8f, 0.8f), 7f, _white.color));
        }

        // Hose bed.
        foreach (var side in new[] { -1f, 1f })
        {
            Part("HoseBedWall", new Vector3(side * 1.25f, 2.9f, -1.7f), new Vector3(0.1f, 0.5f, 4.4f), _red);
        }

        // Hose bed is covered by a diamond-plate cover that splits down the middle into two hinged halves.
        foreach (var side in new[] { -1f, 1f })
        {
            Part("HoseBedCover", new Vector3(side * 0.6f, 3.06f, -1.7f), new Vector3(1.16f, 0.07f, 4.3f), _plate, PrimitiveType.Cube, Quaternion.Euler(0f, 0f, -side * 10f));

            Part("CoverRim", new Vector3(side * 1.16f, 2.98f, -1.7f), new Vector3(0.06f, 0.05f, 4.3f), _chrome);

            for (var i = 0; i < 2; i++)
            {
                Part("CoverHinge", new Vector3(side * 1.18f, 2.95f, -3.2f + i * 2.9f), new Vector3(0.1f, 0.06f, 0.35f), _black);
            }

            Part("CoverHandle", new Vector3(side * 0.22f, 3.14f, -1.7f), new Vector3(0.05f, 0.05f, 0.5f), _chrome);
        }

        Part("CoverSeam", new Vector3(0f, 3.1f, -1.7f), new Vector3(0.04f, 0.05f, 4.3f), _black);

        foreach (var z in new[] { -3.6f, 0.2f })
        {
            Part("CoverEnd", new Vector3(0f, 2.95f, z), new Vector3(2.3f, 0.12f, 0.08f), _red);
        }

        // Wheels.
        Wheels(new[] { 2.5f, -2.3f }, 0.62f, 0.4f, 1.2f);

        // Rear: tailboard, chevron panel, lights, lettering.
        Part("Tailboard", new Vector3(0f, 0.85f, -4.15f), new Vector3(2.4f, 0.08f, 0.5f), _plate);

        Part("RearChevronBase", new Vector3(0f, 1.5f, -3.92f), new Vector3(2.4f, 0.55f, 0.04f), _yellow);

        for (var i = 0; i < 5; i++)
        {
            Part("RearChevron", new Vector3(-0.95f + i * 0.475f, 1.5f, -3.95f), new Vector3(0.2f, 0.6f, 0.04f), _lensRed, PrimitiveType.Cube, Quaternion.Euler(0f, 0f, i % 2 == 0 ? 35f : -35f));
        }

        foreach (var side in new[] { -1f, 1f })
        {
            Part("RearLight", new Vector3(side * 1.1f, 1.95f, -3.93f), new Vector3(0.22f, 0.22f, 0.05f), _glowRed);
        }

        // Rear details from the reference: red number panel, stacked tail lights, amber row and red corner flashers.
        foreach (var side in new[] { -1f, 1f })
        {
            Part("RearTurn", new Vector3(side * 1.1f, 1.7f, -3.93f), new Vector3(0.22f, 0.14f, 0.05f), _glowAmber);

            Part("RearStop", new Vector3(side * 1.1f, 1.5f, -3.93f), new Vector3(0.22f, 0.14f, 0.05f), _glowRed);

            Part("RearBackup", new Vector3(side * 1.1f, 1.3f, -3.93f), new Vector3(0.22f, 0.14f, 0.05f), _white);

            Lens("RearFlasher", new Vector3(side * 1.18f, 2.86f, -3.95f), new Vector3(0.2f, 0.16f, 0.06f), 2, side < 0f ? 0 : 1);
        }

        // Eight dark amber light bars along the top rear edge, as on the reference.
        for (var i = 0; i < 8; i++)
        {
            Part("RearBar", new Vector3(-1.0f + i * 0.2857f, 3.02f, -4.0f), new Vector3(0.22f, 0.07f, 0.05f), _black);
        }

        // Two red flaps drape down the back from the hose bed cover and split in the middle.
        // Seen from behind the left flap is blank and the right flap (truck +X) reads 07.
        foreach (var side in new[] { -1f, 1f })
        {
            Part("RearFlap", new Vector3(side * 0.57f, 2.48f, -3.99f), new Vector3(1.1f, 1.05f, 0.03f), _red);

            Part("FlapHem", new Vector3(side * 0.57f, 1.97f, -3.995f), new Vector3(1.1f, 0.04f, 0.035f), _white);

            Part("FlapSnap", new Vector3(side * 1.0f, 2.1f, -4.01f), new Vector3(0.07f, 0.07f, 0.03f), _chrome, PrimitiveType.Cylinder, Quaternion.Euler(90f, 0f, 0f));
        }

        Part("FlapSplit", new Vector3(0f, 2.48f, -3.995f), new Vector3(0.03f, 1.05f, 0.035f), _black);

        Part("NumberPlate", new Vector3(0.88f, 2.8f, -4.005f), new Vector3(0.44f, 0.4f, 0.03f), Mat("PlateBlue", "7D8FB5", 0.3f));

        Label(new LabelSpec("07", new Vector3(0.88f, 2.4f, -4.012f), 0f, new Vector2(0.5f, 0.5f), 6f, _white.color));

        // Texas flag and Houston badge on the cab door.
        foreach (var side in new[] { -1f, 1f })
        {
            Part("FlagWhite", new Vector3(side * 1.33f, 2.25f, 1.55f), new Vector3(0.02f, 0.22f, 0.3f), _white);

            Part("FlagBlue", new Vector3(side * 1.335f, 2.25f, 1.65f), new Vector3(0.02f, 0.22f, 0.1f), _blue);

            Part("FlagRed", new Vector3(side * 1.335f, 2.14f, 1.5f), new Vector3(0.02f, 0.1f, 0.2f), _lensRed);
        }

        EngineExtras();

        AttachFlashers(engine);
    }

    private readonly struct CabSpec
    {
        public CabSpec(float x, float z, float length, float width, float frontZ)
        {
            X = x;

            Z = z;

            Length = length;

            Width = width;

            FrontZ = frontZ;
        }

        public float X { get; }

        public float Z { get; }

        public float Length { get; }

        public float Width { get; }

        public float FrontZ { get; }
    }

    /// <summary>Builds the engine cab: shell, split windshield, grille, headlights and bumper.</summary>
    private static void Cab(CabSpec cab)
    {
        Part("Cab", new Vector3(cab.X, 1.65f, cab.Z), new Vector3(cab.Width, 2f, cab.Length), _red);

        var front = cab.Z + cab.Length / 2f;

        foreach (var side in new[] { -1f, 1f })
        {
            Part("Windshield", new Vector3(side * 0.62f, 2.2f, front + 0.02f), new Vector3(1.1f, 0.9f, 0.06f), _glass);

            Part("HeadlightHousing", new Vector3(side * 0.95f, 1.2f, front + 0.03f), new Vector3(0.55f, 0.5f, 0.07f), _chrome);

            Part("Headlight", new Vector3(side * 0.95f, 1.28f, front + 0.06f), new Vector3(0.4f, 0.3f, 0.08f), _headlight);

            Lens("WarnLight", new Vector3(side * 0.95f, 1.0f, front + 0.06f), new Vector3(0.4f, 0.14f, 0.08f), 0, side < 0f ? 0 : 1);

            Part("TurnLight", new Vector3(side * 0.95f, 0.84f, front + 0.06f), new Vector3(0.4f, 0.07f, 0.08f), _glowAmber);
        }

        Part("WindshieldDivider", new Vector3(0f, 2.2f, front + 0.03f), new Vector3(0.1f, 0.9f, 0.08f), _red);

        Part("GrilleSurround", new Vector3(0f, 1.25f, front + 0.02f), new Vector3(1.7f, 1.0f, 0.06f), _chrome);

        Part("Grille", new Vector3(0f, 1.25f, front + 0.05f), new Vector3(1.5f, 0.85f, 0.08f), _black);

        foreach (var y in new[] { 1.05f, 1.25f, 1.45f })
        {
            Part("GrilleBar", new Vector3(0f, y, front + 0.1f), new Vector3(1.5f, 0.05f, 0.04f), _chrome);
        }

        for (var i = 0; i < 9; i++)
        {
            Part("GrilleMesh", new Vector3(-0.64f + i * 0.16f, 1.25f, front + 0.09f), new Vector3(0.03f, 0.85f, 0.04f), _chrome);
        }

        foreach (var x in new[] { -1.0f, -0.5f, 0f, 0.5f, 1.0f })
        {
            Part("RoofMarker", new Vector3(x, 2.6f, front + 0.04f), new Vector3(0.14f, 0.08f, 0.05f), _glowAmber);
        }

        Part("Bumper", new Vector3(0f, 0.78f, front + 0.35f), new Vector3(2.7f, 0.42f, 0.62f), _red);

        Part("BumperPlate", new Vector3(0f, 1.01f, front + 0.35f), new Vector3(2.5f, 0.04f, 0.5f), _plate);

        foreach (var side in new[] { -1f, 1f })
        {
            Part("BumperMarker", new Vector3(side * 0.88f, 0.8f, front + 0.67f), new Vector3(0.3f, 0.14f, 0.04f), _white);

            Part("FogLight", new Vector3(side * 0.5f, 0.8f, front + 0.67f), new Vector3(0.2f, 0.05f, 0.2f), _chrome, PrimitiveType.Cylinder, Quaternion.Euler(90f, 0f, 0f));

            Label(new LabelSpec("7", new Vector3(side * 1.2f, 0.8f, front + 0.68f), 180f, new Vector2(0.4f, 0.4f), 4.5f, _white.color));
        }
    }

    // ------------------------------------------------------------------ Ladder 7

    private static void BuildLadder(Transform root)
    {
        Part("Chassis", new Vector3(0f, 0.8f, 0f), new Vector3(2.2f, 0.4f, 10f), _black);

        Part("Body", new Vector3(0f, 1.6f, -1.2f), new Vector3(2.6f, 1.9f, 7.6f), _red);

        Cab(new CabSpec(0f, 3.9f, 2.6f, 2.6f, 5.2f));

        Part("RaisedCabRoof", new Vector3(0f, 2.9f, 3.6f), new Vector3(2.6f, 0.5f, 2f), _red);

        Part("LightbarBase", new Vector3(0f, 3.18f, 4.7f), new Vector3(2.5f, 0.12f, 0.42f), _black);

        for (var i = 0; i < 6; i++)
        {
            var x = -1.0f + i * 0.4f;

            Lens("Lightbar", new Vector3(x, 3.3f, 4.7f), new Vector3(0.37f, 0.16f, 0.36f), i == 0 || i == 5 ? 0 : 1, x < 0f ? 0 : 1);
        }

        foreach (var side in new[] { -1f, 1f })
        {
            for (var i = 0; i < 5; i++)
            {
                var z = -3.9f + i * 1.3f;

                RollUp(side, 1.2f, z, 1.1f);

                RollUp(side, 2.1f, z, 1.1f);
            }

            Part("StripeBand", new Vector3(side * 1.31f, 1.0f, -0.2f), new Vector3(0.04f, 0.2f, 9.4f), _white);

            Part("ZTop", new Vector3(side * 1.31f, 2.5f, -2.3f), new Vector3(0.04f, 0.14f, 3.4f), _white);

            Part("ZDiagonal", new Vector3(side * 1.31f, 1.9f, -0.05f), new Vector3(0.04f, 0.14f, 1.5f), _white, PrimitiveType.Cube, Quaternion.Euler(50f, 0f, 0f));

            Part("CabWindow", new Vector3(side * 1.31f, 2.2f, 4.2f), new Vector3(0.05f, 0.8f, 1.0f), _glass);

            Part("DoorHandle", new Vector3(side * 1.33f, 1.7f, 3.9f), new Vector3(0.06f, 0.05f, 0.22f), _chrome);

            Part("Step", new Vector3(side * 1.45f, 0.55f, 3.9f), new Vector3(0.3f, 0.06f, 1.4f), _plate);

            Part("MirrorArm", new Vector3(side * 1.55f, 2.55f, 5.0f), new Vector3(0.5f, 0.05f, 0.05f), _chrome);

            Part("Mirror", new Vector3(side * 1.78f, 2.55f, 5.0f), new Vector3(0.1f, 0.46f, 0.26f), _black);

            Part("MirrorGlass", new Vector3(side * 1.78f, 2.55f, 5.0f - 0.135f), new Vector3(0.08f, 0.4f, 0.02f), _chrome);

            Part("ShieldGold", new Vector3(side * 1.32f, 1.65f, 3.7f), new Vector3(0.46f, 0.01f, 0.46f), _gold, PrimitiveType.Cylinder, Quaternion.Euler(0f, 0f, 90f));

            Part("ShieldBlue", new Vector3(side * 1.325f, 1.65f, 3.7f), new Vector3(0.36f, 0.01f, 0.36f), _blue, PrimitiveType.Cylinder, Quaternion.Euler(0f, 0f, 90f));

            Part("Outrigger", new Vector3(side * 1.45f, 0.9f, -1.9f), new Vector3(0.35f, 0.45f, 0.6f), _steel);

            Part("OutriggerPad", new Vector3(side * 1.45f, 0.62f, -1.9f), new Vector3(0.5f, 0.08f, 0.5f), _plate);

            Part("LadderRail", new Vector3(side * 0.5f, 3.35f, -0.1f), new Vector3(0.12f, 0.3f, 9.6f), _alu);

            Part("LadderTopChord", new Vector3(side * 0.5f, 3.62f, -0.1f), new Vector3(0.06f, 0.06f, 9.6f), _alu);

            Part("LadderPlate", new Vector3(side * 0.58f, 3.4f, -0.5f), new Vector3(0.04f, 0.6f, 4.6f), _white);

            Label(new LabelSpec("HOUSTON LADDER 7", new Vector3(side * 0.61f, 3.4f, -0.5f), side < 0f ? 90f : 270f, new Vector2(4.4f, 0.5f), 3.1f, _red.color));

            Label(new LabelSpec("7", new Vector3(side * 1.34f, 1.45f, -4.2f), side < 0f ? 90f : 270f, new Vector2(0.8f, 0.8f), 7f, _white.color));
        }

        for (var i = 0; i < 16; i++)
        {
            Part("LadderRung", new Vector3(0f, 3.4f, -4.6f + i * 0.58f), new Vector3(1f, 0.07f, 0.07f), _alu);
        }

        Part("Turntable", new Vector3(0f, 2.95f, -3.7f), new Vector3(1.8f, 0.7f, 1.6f), _red);

        Part("TurntableBand", new Vector3(0f, 2.78f, -3.7f), new Vector3(1.84f, 0.08f, 1.64f), _white);

        Part("TurntableBase", new Vector3(0f, 2.55f, -3.7f), new Vector3(1.9f, 0.1f, 1.7f), _black);

        Part("TurntableCab", new Vector3(0f, 3.3f, -3.7f), new Vector3(1.3f, 0.5f, 1.2f), _red);

        Part("LadderCradle", new Vector3(0f, 3.22f, 4.0f), new Vector3(1.3f, 0.25f, 0.35f), _steel);

        Part("TurntableLight", new Vector3(0f, 3.7f, -3.7f), new Vector3(0.3f, 0.2f, 0.3f), _amber);

        Wheels(new[] { 3.6f }, 0.62f, 0.4f, 1.2f);

        Wheels(new[] { -2.7f, -4.2f }, 0.6f, 0.4f, 1.2f);

        Part("Tailboard", new Vector3(0f, 0.85f, -5.45f), new Vector3(2.4f, 0.08f, 0.5f), _plate);

        Part("RearChevronBase", new Vector3(0f, 1.5f, -4.74f), new Vector3(2.4f, 0.6f, 0.04f), _yellow);

        for (var i = 0; i < 5; i++)
        {
            Part("RearChevron", new Vector3(-0.95f + i * 0.475f, 1.5f, -4.77f), new Vector3(0.2f, 0.65f, 0.04f), _lensRed, PrimitiveType.Cube, Quaternion.Euler(0f, 0f, i % 2 == 0 ? 35f : -35f));
        }

        foreach (var side in new[] { -1f, 1f })
        {
            Part("RearLight", new Vector3(side * 1.1f, 2.0f, -4.75f), new Vector3(0.22f, 0.22f, 0.05f), _glowRed);
        }

        Label(new LabelSpec("LADDER 7", new Vector3(0f, 2.4f, -4.78f), 0f, new Vector2(2.2f, 0.55f), 4.4f, _white.color));

        LadderExtras();

        var collider = root.gameObject.AddComponent<BoxCollider>();

        collider.center = new Vector3(0f, 1.75f, 0f);

        collider.size = new Vector3(2.7f, 3.5f, 10.4f);
    }

    // ------------------------------------------------------------------ Medic 7

    private static void BuildMedic(Transform root)
    {
        Part("Chassis", new Vector3(0f, 0.65f, 0f), new Vector3(2f, 0.35f, 6.4f), _black);

        Part("BoxBody", new Vector3(0f, 1.85f, -1.75f), new Vector3(2.3f, 2f, 3.9f), _red);

        Part("Cab", new Vector3(0f, 1.45f, 1.1f), new Vector3(2.2f, 1.3f, 1.8f), _red);

        Part("CabRoof", new Vector3(0f, 2.15f, 1.1f), new Vector3(2.2f, 0.1f, 1.8f), _red);

        Part("Hood", new Vector3(0f, 1.15f, 2.75f), new Vector3(2.1f, 0.7f, 1.3f), _red);

        Part("Windshield", new Vector3(0f, 1.75f, 2.02f), new Vector3(1.9f, 0.75f, 0.06f), _glass);

        Part("GrilleSurround", new Vector3(0f, 1.1f, 3.42f), new Vector3(1.5f, 0.5f, 0.06f), _chrome);

        Part("Grille", new Vector3(0f, 1.1f, 3.45f), new Vector3(1.3f, 0.38f, 0.08f), _black);

        Part("PushBumper", new Vector3(0f, 0.8f, 3.55f), new Vector3(2.1f, 0.5f, 0.15f), _chrome);

        foreach (var x in new[] { -0.7f, 0f, 0.7f })
        {
            Part("PushBarPost", new Vector3(x, 1.12f, 3.64f), new Vector3(0.08f, 0.85f, 0.08f), _black);
        }

        Part("BumperBar", new Vector3(0f, 1.25f, 3.6f), new Vector3(1.8f, 0.08f, 0.08f), _black);

        foreach (var side in new[] { -1f, 1f })
        {
            Part("Headlight", new Vector3(side * 0.8f, 1.2f, 3.42f), new Vector3(0.35f, 0.25f, 0.06f), _headlight);

            Part("CabWindow", new Vector3(side * 1.12f, 1.75f, 1.1f), new Vector3(0.05f, 0.65f, 1.1f), _glass);

            Part("DoorSeam", new Vector3(side * 1.12f, 1.45f, 0.45f), new Vector3(0.04f, 1.2f, 0.04f), _black);

            Part("DoorHandle", new Vector3(side * 1.14f, 1.35f, 0.7f), new Vector3(0.06f, 0.05f, 0.2f), _chrome);

            Part("Mirror", new Vector3(side * 1.4f, 2.0f, 1.85f), new Vector3(0.1f, 0.5f, 0.3f), _black);

            Part("MirrorGlass", new Vector3(side * 1.4f, 2.0f, 1.85f - 0.16f), new Vector3(0.08f, 0.44f, 0.02f), _chrome);

            Part("MirrorArm", new Vector3(side * 1.25f, 1.95f, 1.9f), new Vector3(0.3f, 0.05f, 0.05f), _black);

            Part("StripeBand", new Vector3(side * 1.16f, 1.2f, -0.6f), new Vector3(0.04f, 0.3f, 5.9f), _white);

            Part("PatientDoor", new Vector3(side * 1.16f, 1.85f, -1.2f), new Vector3(0.03f, 1.5f, 0.04f), _black);

            Part("PatientWindow", new Vector3(side * 1.165f, 2.3f, -1.5f), new Vector3(0.04f, 0.5f, 0.45f), _glass);

            Part("CompartmentDoor", new Vector3(side * 1.16f, 1.2f, -2.9f), new Vector3(0.04f, 0.8f, 0.9f), _plate);

            Part("NamePlate", new Vector3(side * 1.165f, 2.2f, -2.6f), new Vector3(0.03f, 0.55f, 1.9f), _white);

            Part("EmsPlate", new Vector3(side * 1.165f, 1.55f, -2.55f), new Vector3(0.03f, 0.32f, 2.3f), _white);

            Part("StarOfLife", new Vector3(side * 1.17f, 2.0f, -0.55f), new Vector3(0.02f, 0.45f, 0.45f), _blue, PrimitiveType.Cylinder, Quaternion.Euler(0f, 0f, 90f));

            Label(new LabelSpec("MEDIC 7", new Vector3(side * 1.19f, 2.2f, -2.6f), side < 0f ? 90f : 270f, new Vector2(1.8f, 0.5f), 4.2f, _blue.color));

            Label(new LabelSpec("HOUSTON FIRE EMS", new Vector3(side * 1.19f, 1.55f, -2.55f), side < 0f ? 90f : 270f, new Vector2(2.2f, 0.3f), 2.1f, _blue.color));

            Part("MirrorSignal", new Vector3(side * 1.1f, 0.85f, -3.75f), new Vector3(0.2f, 0.2f, 0.05f), _glowRed);
        }

        for (var i = 0; i < 5; i++)
        {
            var x = -1f + i * 0.5f;

            Lens("RoofStrobe", new Vector3(x, 2.92f, 0.1f), new Vector3(0.3f, 0.15f, 0.15f), i % 2 == 0 ? 0 : 1, i % 2);

            Lens("RoofStrobe", new Vector3(x, 2.92f, -3.6f), new Vector3(0.3f, 0.15f, 0.15f), i % 2 == 0 ? 1 : 0, (i + 1) % 2);
        }

        Part("RearDoorSeam", new Vector3(0f, 1.85f, -3.72f), new Vector3(0.04f, 1.9f, 0.04f), _black);

        foreach (var side in new[] { -1f, 1f })
        {
            Part("RearWindow", new Vector3(side * 0.45f, 2.3f, -3.72f), new Vector3(0.45f, 0.65f, 0.04f), _glass);
        }

        Part("RearChevronBase", new Vector3(0f, 1.35f, -3.72f), new Vector3(2.2f, 1.1f, 0.04f), _yellow);

        for (var i = 0; i < 6; i++)
        {
            Part("RearChevron", new Vector3(-0.9f + i * 0.36f, 1.35f, -3.75f), new Vector3(0.18f, 1.15f, 0.04f), _lensRed, PrimitiveType.Cube, Quaternion.Euler(0f, 0f, i % 2 == 0 ? 30f : -30f));
        }

        Part("RearBumper", new Vector3(0f, 0.72f, -3.85f), new Vector3(2.2f, 0.3f, 0.4f), _steel);

        Label(new LabelSpec("MEDIC 7", new Vector3(0f, 2.74f, -3.76f), 0f, new Vector2(1.8f, 0.3f), 2.6f, _white.color));

        Wheels(new[] { 2.3f }, 0.45f, 0.3f, 1.0f);

        Wheels(new[] { -1.5f }, 0.45f, 0.45f, 1.0f);

        MedicExtras();

        var collider = root.gameObject.AddComponent<BoxCollider>();

        collider.center = new Vector3(0f, 1.5f, -0.15f);

        collider.size = new Vector3(2.3f, 3f, 7.1f);
    }

    // ------------------------------------------------------------------ polish

    /// <summary>Front hardware shared by the cab apparatus: plate, siren, marker lights, wipers and grab handles.</summary>
    private static void CabHardware(float frontZ, float cabRearZ, float markerTop)
    {
        Part("FrontPlate", new Vector3(0f, 0.78f, frontZ + 0.68f), new Vector3(0.5f, 0.24f, 0.03f), _plateWhite);

        foreach (var wipe in new[] { -0.9f, 0.3f })
        {
            Part("Wiper", new Vector3(wipe, 1.85f, frontZ + 0.07f), new Vector3(0.7f, 0.03f, 0.03f), _black, PrimitiveType.Cube, Quaternion.Euler(0f, 0f, 12f));
        }

        foreach (var side in new[] { -1f, 1f })
        {
            Part("GrabHandle", new Vector3(side * 1.34f, 1.5f, cabRearZ), new Vector3(0.04f, 0.75f, 0.04f), _chrome);

            Part("CabMarker", new Vector3(side * 1.3f, markerTop, frontZ - 0.1f), new Vector3(0.06f, 0.08f, 0.2f), _glowAmber);
        }
    }

    private static void EngineExtras()
    {
        CabHardware(3.5f, 0.7f, 2.68f);

        foreach (var side in new[] { -1f, 1f })
        {
            for (var i = 0; i < 4; i++)
            {
                Part("SideMarker", new Vector3(side * 1.31f, 0.78f, -3.4f + i * 1.9f), new Vector3(0.05f, 0.07f, 0.16f), _glowAmber);
            }

            Part("HoseBedRail", new Vector3(side * 1.25f, 3.17f, -1.7f), new Vector3(0.04f, 0.04f, 4.2f), _chrome);

            Part("RearHandrail", new Vector3(side * 1.12f, 1.6f, -4.0f), new Vector3(0.04f, 1.3f, 0.04f), _chrome);

            Part("DoorShieldRear", new Vector3(side * 1.33f, 2.45f, -0.55f), new Vector3(0.05f, 0.5f, 0.5f), _white);
        }

        Part("RearPlate", new Vector3(0f, 0.95f, -4.18f), new Vector3(0.5f, 0.24f, 0.03f), _plateWhite);

        Part("ExhaustStack", new Vector3(1.16f, 2.2f, 0.45f), new Vector3(0.14f, 1.6f, 0.14f), _chrome, PrimitiveType.Cylinder);

        Part("AirHornLeft", new Vector3(-0.5f, 2.72f, 2.4f), new Vector3(0.1f, 0.1f, 0.5f), _chrome, PrimitiveType.Cylinder, Quaternion.Euler(90f, 0f, 0f));

        Part("AirHornRight", new Vector3(0.5f, 2.72f, 2.4f), new Vector3(0.1f, 0.1f, 0.5f), _chrome, PrimitiveType.Cylinder, Quaternion.Euler(90f, 0f, 0f));
    }

    private static void LadderExtras()
    {
        CabHardware(5.2f, 2.6f, 2.68f);

        // Lattice: vertical posts and diagonals between the ladder rails.
        for (var i = 0; i < 16; i++)
        {
            var z = -4.6f + i * 0.58f;

            foreach (var side in new[] { -1f, 1f })
            {
                Part("LadderPost", new Vector3(side * 0.5f, 3.5f, z), new Vector3(0.04f, 0.28f, 0.04f), _alu);

                Part("LadderDiagonal", new Vector3(side * 0.5f, 3.5f, z + 0.29f), new Vector3(0.03f, 0.03f, 0.66f), _alu, PrimitiveType.Cube, Quaternion.Euler(i % 2 == 0 ? 25f : -25f, 0f, 0f));
            }
        }

        foreach (var side in new[] { -1f, 1f })
        {
            Part("LadderSearchlight", new Vector3(side * 0.55f, 3.75f, 4.55f), new Vector3(0.3f, 0.2f, 0.3f), _glowWhite);

            Part("RailPost", new Vector3(side * 0.85f, 3.45f, -4.35f), new Vector3(0.04f, 0.7f, 0.04f), _chrome);

            Part("RailPost", new Vector3(side * 0.85f, 3.45f, -3.05f), new Vector3(0.04f, 0.7f, 0.04f), _chrome);

            Part("RailSide", new Vector3(side * 0.85f, 3.78f, -3.7f), new Vector3(0.04f, 0.04f, 1.3f), _chrome);

            for (var i = 0; i < 4; i++)
            {
                Part("SideMarker", new Vector3(side * 1.31f, 0.78f, -4.3f + i * 2.9f), new Vector3(0.05f, 0.07f, 0.16f), _glowAmber);
            }
        }

        Part("RailBack", new Vector3(0f, 3.78f, -4.35f), new Vector3(1.7f, 0.04f, 0.04f), _chrome);

        Part("RailFront", new Vector3(0f, 3.78f, -3.05f), new Vector3(1.7f, 0.04f, 0.04f), _chrome);

        Part("Nozzle", new Vector3(0f, 3.55f, 4.7f), new Vector3(0.18f, 0.7f, 0.18f), _red, PrimitiveType.Cylinder, Quaternion.Euler(90f, 0f, 0f));

        Part("PivotPost", new Vector3(0f, 3.6f, -3.7f), new Vector3(0.7f, 0.6f, 0.7f), _steel, PrimitiveType.Cylinder);

        for (var i = 0; i < 3; i++)
        {
            Part("RearStep", new Vector3(-1.0f, 0.55f + i * 0.5f, -5.05f), new Vector3(0.5f, 0.06f, 0.3f), _plate);
        }

        Part("RearGrab", new Vector3(-1.2f, 1.1f, -4.95f), new Vector3(0.04f, 1.4f, 0.04f), _chrome);

        Part("RearPlate", new Vector3(0.4f, 0.95f, -5.0f), new Vector3(0.5f, 0.24f, 0.03f), _plateWhite);
    }

    private static void MedicExtras()
    {
        Part("FrontPlate", new Vector3(0f, 0.68f, 3.64f), new Vector3(0.5f, 0.24f, 0.03f), _plateWhite);

        for (var i = 0; i < 5; i++)
        {
            Lens("BoxLight", new Vector3(-0.8f + i * 0.4f, 2.4f, 0.24f), new Vector3(0.28f, 0.16f, 0.06f), i % 2 == 0 ? 0 : 1, i % 2);
        }

        foreach (var side in new[] { -1f, 1f })
        {
            Lens("BoxCornerLight", new Vector3(side * 1.0f, 2.66f, 0.24f), new Vector3(0.3f, 0.22f, 0.08f), 1, side < 0f ? 0 : 1);
        }

        Label(new LabelSpec("A", new Vector3(0.38f, 2.68f, 0.23f), 180f, new Vector2(0.4f, 0.4f), 5f, _white.color));

        Label(new LabelSpec("7", new Vector3(-0.38f, 2.68f, 0.23f), 180f, new Vector2(0.4f, 0.4f), 5f, _white.color));

        Lens("GrilleLight", new Vector3(-0.45f, 1.1f, 3.51f), new Vector3(0.2f, 0.1f, 0.05f), 0, 0);

        foreach (var wipe in new[] { -0.55f, 0.35f })
        {
            Part("Wiper", new Vector3(wipe, 1.5f, 2.07f), new Vector3(0.6f, 0.03f, 0.03f), _black, PrimitiveType.Cube, Quaternion.Euler(0f, 0f, 10f));
        }

        Part("BumperLight", new Vector3(-0.9f, 0.82f, 3.64f), new Vector3(0.25f, 0.12f, 0.04f), _glowAmber);

        Part("BumperLight", new Vector3(0.9f, 0.82f, 3.64f), new Vector3(0.25f, 0.12f, 0.04f), _glowAmber);

        Part("RoofVent", new Vector3(0.45f, 3.0f, -1.5f), new Vector3(0.7f, 0.18f, 0.7f), _plate);

        Part("Antenna", new Vector3(-0.9f, 3.45f, -0.4f), new Vector3(0.04f, 1.2f, 0.04f), _black, PrimitiveType.Cylinder);

        foreach (var side in new[] { -1f, 1f })
        {
            Part("BoxHandle", new Vector3(side * 1.18f, 1.7f, -0.35f), new Vector3(0.06f, 0.05f, 0.18f), _chrome);

            Part("FuelDoor", new Vector3(side * 1.12f, 1.0f, 0.55f), new Vector3(0.03f, 0.2f, 0.16f), _chrome);

            Part("StarCrossV", new Vector3(side * 1.185f, 2.0f, -0.55f), new Vector3(0.02f, 0.34f, 0.09f), _white);

            Part("StarCrossH", new Vector3(side * 1.185f, 2.0f, -0.55f), new Vector3(0.02f, 0.09f, 0.34f), _white);

            Part("RearGrab", new Vector3(side * 0.95f, 1.6f, -3.8f), new Vector3(0.04f, 1.0f, 0.04f), _chrome);

            for (var i = 0; i < 3; i++)
            {
                Part("SideMarker", new Vector3(side * 1.16f, 0.9f, -3.3f + i * 1.4f), new Vector3(0.05f, 0.07f, 0.16f), _glowAmber);
            }
        }

        Part("RearPlate", new Vector3(0f, 0.95f, -3.9f), new Vector3(0.5f, 0.22f, 0.03f), _plateWhite);
    }

    /// <summary>Creates a warning-light lens that is dull until the emergency lights flash it. Colour: 0 red, 1 white, 2 amber.</summary>
    private static void Lens(string name, Vector3 position, Vector3 scale, int colour, int group)
    {
        var on = colour == 0 ? _flashRed : colour == 1 ? _flashWhite : _flashAmber;

        var off = colour == 0 ? _offRed : colour == 1 ? _offWhite : _offAmber;

        var lens = Part(name, position, scale, off).GetComponent<Renderer>();

        Flashers.Add((lens, on, off, group));
    }

    /// <summary>A roll-up compartment door: dark reveal, plate, horizontal slats and a pull bar.</summary>
    private static void RollUp(float side, float y, float z, float length)
    {
        Part("DoorFrame", new Vector3(side * 1.30f, y, z), new Vector3(0.04f, 0.82f, length + 0.1f), _black);

        Part("RollUpDoor", new Vector3(side * 1.31f, y, z), new Vector3(0.05f, 0.75f, length), _plate);

        for (var i = 1; i <= 3; i++)
        {
            Part("Slat", new Vector3(side * 1.337f, y - 0.375f + i * 0.1875f, z), new Vector3(0.01f, 0.02f, length - 0.06f), _black);
        }

        Part("DoorBar", new Vector3(side * 1.345f, y - 0.3f, z), new Vector3(0.02f, 0.04f, 0.3f), _chrome);
    }

    /// <summary>Gives the engine a component that flashes its lenses with the player's emergency lights.</summary>
    private static void AttachFlashers(GameObject engine)
    {
        Type? type = null;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            type ??= assembly.GetType("HTownSim.Runtime.ApparatusLights");
        }

        var response = engine.GetComponent("FireResponse");

        if (type == null || response == null)
        {
            return;
        }

        var component = engine.GetComponent(type) ?? engine.AddComponent(type);

        var serialized = new SerializedObject(component);

        serialized.FindProperty("_response").objectReferenceValue = response;

        var lenses = serialized.FindProperty("_lenses");

        var on = serialized.FindProperty("_on");

        var off = serialized.FindProperty("_off");

        var group = serialized.FindProperty("_group");

        lenses.arraySize = on.arraySize = off.arraySize = group.arraySize = Flashers.Count;

        for (var i = 0; i < Flashers.Count; i++)
        {
            lenses.GetArrayElementAtIndex(i).objectReferenceValue = Flashers[i].Lens;

            on.GetArrayElementAtIndex(i).objectReferenceValue = Flashers[i].On;

            off.GetArrayElementAtIndex(i).objectReferenceValue = Flashers[i].Off;

            group.GetArrayElementAtIndex(i).intValue = Flashers[i].Group;
        }

        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Adds a left and right wheel at each of the given axle Z positions.</summary>
    private static void Wheels(float[] axles, float radius, float width, float x)
    {
        foreach (var z in axles)
        {
            foreach (var side in new[] { -1f, 1f })
            {
                var centre = new Vector3(side * x, radius, z);

                Part("Tire", centre, new Vector3(radius * 2f, width, radius * 2f), _black, PrimitiveType.Cylinder, Quaternion.Euler(0f, 0f, 90f));

                Part("Hub", centre + new Vector3(side * (width / 2f + 0.01f), 0f, 0f), new Vector3(radius * 1.2f, 0.02f, radius * 1.2f), _chrome, PrimitiveType.Cylinder, Quaternion.Euler(0f, 0f, 90f));

                Part("HubCap", centre + new Vector3(side * (width / 2f + 0.02f), 0f, 0f), new Vector3(radius * 0.4f, 0.02f, radius * 0.4f), _black, PrimitiveType.Cylinder, Quaternion.Euler(0f, 0f, 90f));

                Part("WheelWell", new Vector3(side * (x + width / 2f - 0.06f), radius, z), new Vector3(radius * 2.36f, 0.05f, radius * 2.36f), _black, PrimitiveType.Cylinder, Quaternion.Euler(0f, 0f, 90f));

                Part("Sidewall", centre + new Vector3(side * (width / 2f + 0.004f), 0f, 0f), new Vector3(radius * 1.7f, 0.01f, radius * 1.7f), _tireGrey, PrimitiveType.Cylinder, Quaternion.Euler(0f, 0f, 90f));

                Part("MudFlap", new Vector3(side * (x + width / 2f - 0.1f), radius * 0.5f, z - radius * 1.2f), new Vector3(0.03f, radius * 0.7f, radius * 0.55f), _black);
            }
        }
    }

    private static GameObject Part(string name, Vector3 localPosition, Vector3 scale, Material material, PrimitiveType shape = PrimitiveType.Cube, Quaternion? rotation = null)
    {
        var go = GameObject.CreatePrimitive(shape);

        go.name = name;

        go.transform.SetParent(_parent, false);

        go.transform.localPosition = localPosition;

        go.transform.localRotation = rotation ?? Quaternion.identity;

        go.transform.localScale = shape == PrimitiveType.Cylinder ? new Vector3(scale.x, scale.y * 0.5f, scale.z) : scale;

        go.GetComponent<Renderer>().sharedMaterial = material;

        Object.DestroyImmediate(go.GetComponent<Collider>());

        return go;
    }

    /// <summary>Adds world text. <paramref name="yaw"/> is the direction a viewer looks: 0 reads from behind (looking +Z), 180 from the front.</summary>
    private static void Label(LabelSpec spec)
    {
        var text = new GameObject("Label " + spec.Text).AddComponent<TextMeshPro>();

        text.transform.SetParent(_parent, false);

        text.transform.localPosition = spec.Position;

        text.transform.localRotation = Quaternion.Euler(0f, spec.Yaw, 0f);

        text.text = spec.Text;

        text.fontSize = spec.FontSize;

        text.fontStyle = FontStyles.Bold;

        text.characterSpacing = 2f;

        text.alignment = TextAlignmentOptions.Center;

        text.textWrappingMode = TextWrappingModes.NoWrap;

        text.rectTransform.sizeDelta = spec.Box;

        text.color = spec.Colour;
    }
}
