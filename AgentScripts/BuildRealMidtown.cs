using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// Builds the city from real City of Houston GIS data (see PrepareMidtownData.js). Real parcel polygons become
/// lots and sidewalks, the gaps between them become the streets, and each parcel gets a building whose style
/// and height come from its land-use class and appraised-value density. The earlier hand-placed city is kept,
/// disabled, under <c>LegacyCanalBlock</c>. Everything new lives under <c>MidtownReal</c>.
/// </summary>
public static class BuildRealMidtown
{
    private const string DataPath = "Assets/HTownSim/Data/MidtownReal.txt";

    private const string RootName = "MidtownReal";

    private const string LegacyName = "LegacyCanalBlock";

    private const string MaterialFolder = "Assets/HTownSim/Generated/";

    /// <summary>Playable window in the scene frame (metres, station geocode at the origin).</summary>
    private const float MinX = -520f;

    private const float MaxX = 560f;

    private const float MinZ = -450f;

    private const float MaxZ = 620f;

    private const float GroundMargin = 80f;

    private const float SidewalkWidth = 2.4f;

    private const float CurbTop = 0.2f;

    private const float LotTop = 0.215f;

    private const float StationYardTop = 0.14f;

    private const float RoadTop = 0.06f;

    private const float MarkingY = 0.075f;

    private const float PodiumHeight = 3.5f;

    private const float FloorHeight = 3.2f;

    private const float FreewayX = 610f;

    private static readonly Vector2 StationParcelCentre = new Vector2(12.3f, -26.6f);

    private static readonly string[] KeepNames =
    {
        "CanalMarketSign", "MarketSign", "CityMapCamera", "DuskGrade", "DuskSun", "Engine08", "Engine07", "FireIncident", "Flames",
        "MainCamera", "ResponseHud", "Smoke", "Station08", "Station07", "VehicleCamera", "WaterMonitor",
        "Ladder07", "Medic07", "EnvironmentWeather",
    };

    private static Material _asphalt, _concrete, _limestone, _glass, _steel, _dark, _leaf, _amber, _brick, _red;

    private static Material _towerGlass, _granite, _yellow, _white, _signalRed, _signalGreen, _wood, _park, _stucco, _sage;

    private static Material _lawn, _lotAsphalt, _roof;

    private static Material _roofBrown, _roofBlack, _roofTan, _roofMetal, _roofTar, _roofWhite;

    private static Material _lensRedOn, _lensRedOff, _lensAmberOn, _lensAmberOff, _lensGreenOn, _lensGreenOff, _glowWhite, _glowRed, _pants;

    private static Material[] _shirtMaterials = new Material[0];

    private static Material[] _skinMaterials = new Material[0];

    private static readonly List<Renderer> EwRed = new List<Renderer>();

    private static readonly List<Renderer> EwAmber = new List<Renderer>();

    private static readonly List<Renderer> EwGreen = new List<Renderer>();

    private static readonly List<Renderer> NsRed = new List<Renderer>();

    private static readonly List<Renderer> NsAmber = new List<Renderer>();

    private static readonly List<Renderer> NsGreen = new List<Renderer>();

    private static readonly Dictionary<Material, SlabMesh> RoofSlabs = new Dictionary<Material, SlabMesh>();

    private static Material[] _carMaterials;

    private static Material[] _wallMaterials;

    private static Transform _root;

    private static Transform _parent;

    private static int _wallIndex;

    private static RealData _data = null!;

    private enum BuildingStyle
    {
        Glass,
        Striped,
        Garage,
        Shop,
        Apartment,
        House,
    }

    public sealed class GridAxis
    {
        public float c;

        public float w;

        public string name = string.Empty;
    }

    public sealed class GridData
    {
        public GridAxis[] ns = new GridAxis[0];

        public GridAxis[] ew = new GridAxis[0];
    }

    public sealed class StationData
    {
        public float x;

        public float z;

        public string address = string.Empty;
    }

    public sealed class Parcel
    {
        public float[] c = new float[0];

        public float[] b = new float[0];

        public float[] p = new float[0];

        public int a;

        public int f;

        /// <summary>Measured floors (gross floor area / footprint area); 0 when unavailable.</summary>
        public int m;

        /// <summary>Real building footprint bounds (minX, minZ, maxX, maxZ), or empty.</summary>
        public float[] fb = new float[0];

        /// <summary>Gross building area in square metres.</summary>
        public int ba;

        public string k = string.Empty;

        public int y;

        public int u;

        public string s = string.Empty;

        public string n = string.Empty;
    }

    public sealed class PathData
    {
        public float[] p = new float[0];
    }

    public sealed class RealData
    {
        public float rotationDegrees;

        public StationData station = new StationData();

        public GridData grid = new GridData();

        public Parcel[] parcels = new Parcel[0];

        public PathData[] rail = new PathData[0];
    }

    private readonly struct Spec
    {
        public Spec(string name, Vector3 position, Vector3 scale, Material material, PrimitiveType shape = PrimitiveType.Cube, bool collide = false)
        {
            Name = name;

            Position = position;

            Scale = scale;

            Material = material;

            Shape = shape;

            Collide = collide;
        }

        public string Name { get; }

        public Vector3 Position { get; }

        public Vector3 Scale { get; }

        public Material Material { get; }

        public PrimitiveType Shape { get; }

        public bool Collide { get; }
    }

    private readonly struct BuildingSpec
    {
        public BuildingSpec(string name, Vector2 centre, Vector3 size, BuildingStyle style, Vector3 facing)
        {
            Name = name;

            Centre = centre;

            Size = size;

            Style = style;

            Facing = facing;
        }

        public string Name { get; }

        public Vector2 Centre { get; }

        public Vector3 Size { get; }

        public BuildingStyle Style { get; }

        public Vector3 Facing { get; }
    }

    /// <summary>Collects polygons into one combined mesh per material.</summary>
    private sealed class SlabMesh
    {
        public readonly List<Vector3> Vertices = new List<Vector3>();

        public readonly List<int> Triangles = new List<int>();

        /// <summary>Adds an upward-facing polygon at height <paramref name="y"/>.</summary>
        public void AddTop(List<Vector2> polygon, float y)
        {
            var start = Vertices.Count;

            foreach (var point in polygon)
            {
                Vertices.Add(new Vector3(point.x, y, point.y));
            }

            var triangles = Triangulate(polygon);

            for (var i = 0; i < triangles.Count; i += 3)
            {
                Triangles.Add(start + triangles[i]);

                Triangles.Add(start + triangles[i + 2]);

                Triangles.Add(start + triangles[i + 1]);
            }
        }

        /// <summary>Adds a convex planar face, winding it so its normal points towards <paramref name="outward"/>.</summary>
        public void AddFace(Vector3 outward, params Vector3[] points)
        {
            var start = Vertices.Count;

            Vertices.AddRange(points);

            for (var i = 1; i + 1 < points.Length; i++)
            {
                var normal = Vector3.Cross(points[i] - points[0], points[i + 1] - points[0]);

                if (Vector3.Dot(normal, outward) >= 0f)
                {
                    Triangles.Add(start);

                    Triangles.Add(start + i);

                    Triangles.Add(start + i + 1);
                }
                else
                {
                    Triangles.Add(start);

                    Triangles.Add(start + i + 1);

                    Triangles.Add(start + i);
                }
            }
        }

        /// <summary>Adds outward-facing vertical walls around a counter-clockwise polygon.</summary>
        public void AddWalls(List<Vector2> polygon, float bottom, float top)
        {
            for (var i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i];

                var b = polygon[(i + 1) % polygon.Count];

                var start = Vertices.Count;

                Vertices.Add(new Vector3(a.x, bottom, a.y));

                Vertices.Add(new Vector3(b.x, bottom, b.y));

                Vertices.Add(new Vector3(b.x, top, b.y));

                Vertices.Add(new Vector3(a.x, top, a.y));

                Triangles.Add(start);

                Triangles.Add(start + 2);

                Triangles.Add(start + 1);

                Triangles.Add(start);

                Triangles.Add(start + 3);

                Triangles.Add(start + 2);
            }
        }
    }

    /// <summary>Builds the real-data city and returns a summary.</summary>
    public static string Build()
    {
        if (EditorApplication.isPlaying)
        {
            return "Exit Play Mode first.";
        }

        if (!File.Exists(DataPath))
        {
            return "Missing " + DataPath + ". Run: node AgentScripts/PrepareMidtownData.js";
        }

        _data = Parse(File.ReadAllLines(DataPath));

        var existing = GameObject.Find(RootName);

        if (existing != null)
        {
            Object.DestroyImmediate(existing);
        }

        MoveLegacyAside();

        _root = new GameObject(RootName).transform;

        _wallIndex = 0;

        RoofSlabs.Clear();

        EwRed.Clear();

        EwAmber.Clear();

        EwGreen.Clear();

        NsRed.Clear();

        NsAmber.Clear();

        NsGreen.Clear();

        LoadMaterials();

        BuildGround();

        var window = new List<Parcel>();

        foreach (var parcel in _data.parcels)
        {
            if (parcel.c[0] >= MinX && parcel.c[0] <= MaxX && parcel.c[1] >= MinZ && parcel.c[1] <= MaxZ)
            {
                window.Add(parcel);
            }
        }

        BuildParcelSlabs(window);

        var incident = BuildBuildings(window);

        FlushRoofs();

        BuildMarkings();

        BuildCrossings();

        BuildSignals();

        BuildRail();

        BuildFreeway();

        BuildSkyline();

        RelocateStation();

        RelocateIncident(incident);

        BuildBounds();

        WireDispatch(BuildIncidentSites(window, incident));

        BuildTraffic();

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        var measured = 0;

        Parcel? tallest = null;

        foreach (var parcel in window)
        {
            if (parcel.m > 0)
            {
                measured++;

                if (tallest is null || parcel.m > tallest.m)
                {
                    tallest = parcel;
                }
            }
        }

        var detail = " measured floors on " + measured + "/" + window.Count + (tallest is null ? string.Empty : "; tallest " + tallest.m + " floors at " + tallest.n + " " + tallest.s) + ".";

        return "Built " + window.Count + " parcels (" + detail + " " + _root.GetComponentsInChildren<Transform>().Length + " objects). Incident: " + incident.n + " " + incident.s;
    }


    /// <summary>Parses the tab-separated file written by PrepareMidtownData.js.</summary>
    private static RealData Parse(string[] lines)
    {
        var data = new RealData();

        var ns = new List<GridAxis>();

        var ew = new List<GridAxis>();

        var parcels = new List<Parcel>();

        var rail = new List<PathData>();

        var culture = System.Globalization.CultureInfo.InvariantCulture;

        float F(string text)
        {
            return float.Parse(text, culture);
        }

        foreach (var line in lines)
        {
            var f = line.Split('\t');

            switch (f[0])
            {
                case "R":
                    data.rotationDegrees = F(f[1]);

                    break;

                case "S":
                    data.station = new StationData { x = F(f[1]), z = F(f[2]), address = f[3] };

                    break;

                case "G":
                    var axis = new GridAxis { c = F(f[2]), w = F(f[3]), name = f[4] };

                    (f[1] == "ns" ? ns : ew).Add(axis);

                    break;

                case "P":
                    parcels.Add(new Parcel
                    {
                        c = new[] { F(f[1]), F(f[2]) },
                        b = new[] { F(f[3]), F(f[4]), F(f[5]), F(f[6]) },
                        a = int.Parse(f[7], culture),
                        f = int.Parse(f[8], culture),
                        k = f[9],
                        y = int.Parse(f[10], culture),
                        u = int.Parse(f[11], culture),
                        s = f[12],
                        n = f[13],
                        p = ParsePolygon(f[14], culture),
                        m = f.Length > 15 ? int.Parse(f[15], culture) : 0,
                        fb = f.Length > 16 ? ParseList(f[16], culture) : new float[0],
                        ba = f.Length > 17 ? int.Parse(f[17], culture) : 0,
                    });

                    break;

                case "L":
                    rail.Add(new PathData { p = ParsePolygon(f[1], culture) });

                    break;
            }
        }

        data.grid = new GridData { ns = ns.ToArray(), ew = ew.ToArray() };

        data.parcels = parcels.ToArray();

        data.rail = rail.ToArray();

        return data;
    }

    private static float[] ParseList(string text, System.Globalization.CultureInfo culture)
    {
        var values = new List<float>();

        foreach (var part in text.Split(','))
        {
            if (part.Length > 0)
            {
                values.Add(float.Parse(part, culture));
            }
        }

        return values.ToArray();
    }

    private static float[] ParsePolygon(string text, System.Globalization.CultureInfo culture)
    {
        var values = new List<float>();

        foreach (var pair in text.Split(';'))
        {
            var xz = pair.Split(',');

            if (xz.Length == 2)
            {
                values.Add(float.Parse(xz[0], culture));

                values.Add(float.Parse(xz[1], culture));
            }
        }

        return values.ToArray();
    }

    /// <summary>Moves every old root object that the sim does not need into a disabled group.</summary>
    private static void MoveLegacyAside()
    {
        var legacy = GameObject.Find(LegacyName);

        if (legacy == null)
        {
            legacy = new GameObject(LegacyName);
        }

        var keep = new HashSet<string>(KeepNames) { RootName, LegacyName };

        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (!keep.Contains(root.name))
            {
                root.transform.SetParent(legacy.transform, true);
            }
        }

        legacy.SetActive(false);
    }

    private static void LoadMaterials()
    {
        _asphalt = Mat("Asphalt", "303D43");

        _concrete = Mat("Concrete", "AAA697");

        _limestone = Mat("Limestone", "E6D7B7");

        _glass = Mat("Glazing", "284B59");

        _steel = Mat("Steel", "71858C");

        _dark = Mat("Rubber", "18282F");

        _leaf = Mat("LiveOak", "536D50");

        _amber = Mat("Amber", "FFAF52");

        _brick = Mat("Brick", "875946");

        _red = Mat("ApparatusRed", "B52E32");

        _towerGlass = Mat("TowerGlass", "6C93A8");

        _granite = Mat("Granite", "6E625B");

        _yellow = Mat("PaintYellow", "E0B83A");

        _white = Mat("PaintWhite", "DAD8CC");

        _signalRed = Mat("SignalRed", "E03A3A");

        _signalGreen = Mat("SignalGreen", "3AD17A");

        _wood = Mat("UtilityWood", "5A4636");

        _park = Mat("ParkGrass", "6A8F58");

        _stucco = Mat("Stucco", "C9B79C");

        _sage = Mat("SageStucco", "A9B49A");

        _lawn = Mat("LotGrass", "7A9560");

        _lotAsphalt = Mat("LotAsphalt", "465259");

        _roof = Mat("RoofShingle", "4A4F52");

        _roofBrown = Mat("ShingleBrown", "5B4A3E");

        _roofBlack = Mat("ShingleBlack", "2E3033");

        _roofTan = Mat("ShingleTan", "7A6A58");

        _roofMetal = Mat("RoofMetal", "9AA5AB");

        _roofTar = Mat("RoofTar", "5E6265");

        _roofWhite = Mat("RoofWhite", "C9CDCB");

        foreach (var roofMaterial in new[] { _roof, _roofBrown, _roofBlack, _roofTan, _roofTar, _roofWhite })
        {
            roofMaterial.SetFloat("_Smoothness", 0.06f);

            EditorUtility.SetDirty(roofMaterial);
        }

        _roofMetal.SetFloat("_Smoothness", 0.4f);

        EditorUtility.SetDirty(_roofMetal);

        _carMaterials = new[] { Mat("CarWhite", "E9E9E6"), Mat("CarBlack", "1E2226"), Mat("CarSilver", "9AA3A8"), Mat("CarBlue", "2F4A6E"), Mat("CarRed", "9A2F2F"), Mat("CarTan", "B3A184") };

        _wallMaterials = new[] { _brick, _limestone, _concrete, _stucco, _sage };

        _carMaterials = new[] { _carMaterials[0], _carMaterials[1], _carMaterials[2], _carMaterials[3], _carMaterials[4], _carMaterials[5], Mat("CarGreen", "3E6B4F"), Mat("CarYellow", "D9B63A") };

        _lensRedOn = Glow("LensRedOn", "FF3030", 3f);

        _lensRedOff = Mat("LensRedOff", "3A1212");

        _lensAmberOn = Glow("LensAmberOn", "FFB030", 3f);

        _lensAmberOff = Mat("LensAmberOff", "3A2A10");

        _lensGreenOn = Glow("LensGreenOn", "30FF80", 3f);

        _lensGreenOff = Mat("LensGreenOff", "103A22");

        _glowWhite = Glow("GlowWhite", "FFF6E0", 2.5f);

        _glowRed = Glow("GlowRed", "FF2A2A", 2.2f);

        _pants = Mat("PantsDark", "2A3138");

        var shirtColours = new[] { "C84B4B", "3F6FB0", "E0B93A", "4FA06B", "8E5BB0", "E07A3A", "E8E4D8", "2E3A4A" };

        _shirtMaterials = new Material[shirtColours.Length];

        for (var i = 0; i < shirtColours.Length; i++)
        {
            _shirtMaterials[i] = Mat("Shirt" + i, shirtColours[i]);
        }

        var skinColours = new[] { "F0C8A0", "D9A877", "A9744F", "6B4630" };

        _skinMaterials = new Material[skinColours.Length];

        for (var i = 0; i < skinColours.Length; i++)
        {
            _skinMaterials[i] = Mat("Skin" + i, skinColours[i]);
        }
    }

    private static Material Glow(string name, string hex, float intensity)
    {
        var material = Mat(name, hex);

        ColorUtility.TryParseHtmlString("#" + hex, out var colour);

        material.color = colour;

        material.EnableKeyword("_EMISSION");

        material.SetColor("_EmissionColor", colour * intensity);

        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;

        EditorUtility.SetDirty(material);

        return material;
    }

    private static Material Mat(string name, string hex)
    {
        var path = MaterialFolder + name + ".mat";

        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (existing != null)
        {
            return existing;
        }

        ColorUtility.TryParseHtmlString("#" + hex, out var colour);

        var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name, color = colour };

        AssetDatabase.CreateAsset(material, path);

        return material;
    }

    /// <summary>Creates the grass base and the asphalt that shows wherever no parcel covers the ground.</summary>
    private static void BuildGround()
    {
        Section("Ground", _root);

        var width = MaxX - MinX;

        var depth = MaxZ - MinZ;

        var centre = new Vector3((MinX + MaxX) / 2f, 0f, (MinZ + MaxZ) / 2f);

        Make(new Spec("Ground", centre + new Vector3(0f, -0.3f, 0f), new Vector3(width + GroundMargin * 2f, 0.5f, depth + GroundMargin * 2f), _leaf, PrimitiveType.Cube, true));

        Make(new Spec("Streets", centre, new Vector3(width, RoadTop * 2f, depth), _asphalt, PrimitiveType.Cube, true));
    }

    /// <summary>Builds sidewalks and lot surfaces from the real parcel polygons as a few combined meshes.</summary>
    private static void BuildParcelSlabs(List<Parcel> parcels)
    {
        Section("ParcelSlabs", _root);

        var slabs = new Dictionary<Material, SlabMesh>();

        SlabMesh For(Material material)
        {
            if (!slabs.TryGetValue(material, out var mesh))
            {
                mesh = new SlabMesh();

                slabs[material] = mesh;
            }

            return mesh;
        }

        foreach (var parcel in parcels)
        {
            var polygon = ToCounterClockwise(ToPolygon(parcel.p));

            if (polygon.Count < 3)
            {
                continue;
            }

            var outer = Offset(polygon, SidewalkWidth);

            var sidewalk = For(_concrete);

            if (Vector2.Distance(new Vector2(parcel.c[0], parcel.c[1]), StationParcelCentre) < 4f)
            {
                // The station lot is a paved yard just below curb height so the bay floor and apron sit on top of it.
                sidewalk.AddTop(outer, StationYardTop);

                sidewalk.AddWalls(outer, RoadTop, StationYardTop);

                continue;
            }


            sidewalk.AddTop(outer, CurbTop);

            sidewalk.AddWalls(outer, RoadTop, CurbTop);

            For(LotSurface(parcel)).AddTop(polygon, LotTop);
        }

        foreach (var pair in slabs)
        {
            var mesh = new Mesh { name = "ParcelSlab_" + pair.Key.name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };

            mesh.SetVertices(pair.Value.Vertices);

            mesh.SetTriangles(pair.Value.Triangles, 0);

            mesh.RecalculateNormals();

            mesh.RecalculateBounds();

            var go = new GameObject("Slab_" + pair.Key.name, typeof(MeshFilter), typeof(MeshRenderer));

            go.transform.SetParent(_parent, false);

            go.GetComponent<MeshFilter>().sharedMesh = mesh;

            go.GetComponent<MeshRenderer>().sharedMaterial = pair.Key;

            if (pair.Key == _concrete)
            {
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
            }

            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
        }
    }

    /// <summary>Chooses the ground cover of a lot from its land-use class.</summary>
    private static Material LotSurface(Parcel parcel)
    {
        if (IsPark(parcel))
        {
            return _park;
        }

        var residential = parcel.k.StartsWith("A", StringComparison.Ordinal) || parcel.k.StartsWith("B", StringComparison.Ordinal) || parcel.k.StartsWith("C", StringComparison.Ordinal) || parcel.k.StartsWith("O", StringComparison.Ordinal);

        if (residential)
        {
            return _lawn;
        }

        return parcel.f == 0 ? _lotAsphalt : _concrete;
    }

    private static bool IsPark(Parcel parcel)
    {
        return parcel.f == 0 && parcel.k.StartsWith("X", StringComparison.Ordinal) && parcel.a >= 700;
    }

    /// <summary>Creates a building, lot detail or park for every parcel and returns the parcel chosen for the fire.</summary>
    private static Parcel BuildBuildings(List<Parcel> parcels)
    {
        Section("Buildings", _root);

        var buildings = _parent;

        var incident = ChooseIncidentParcel(parcels);

        var index = 0;

        foreach (var parcel in parcels)
        {
            index++;

            var centre = new Vector2(parcel.c[0], parcel.c[1]);

            if (Vector2.Distance(centre, StationParcelCentre) < 4f)
            {
                continue;
            }

            if (IsPark(parcel))
            {
                Section("Park", buildings);

                ParkTrees(parcel);

                continue;
            }

            if (parcel.f == 0 && parcel.m == 0)
            {
                if (parcel.a >= 250 && !parcel.k.StartsWith("A", StringComparison.Ordinal) && !parcel.k.StartsWith("C", StringComparison.Ordinal))
                {
                    Section("Lot", buildings);

                    LotDetails(parcel, index);
                }

                continue;
            }

            foreach (var spec in Describe(parcel, parcel == incident, index))
            {
                if (spec.Size.x < 4f || spec.Size.z < 4f)
                {
                    continue;
                }

                Section(spec.Name, buildings);

                BuildOne(spec);

                Streetscape(spec, index);
            }
        }

        return incident;
    }

    /// <summary>One building per real footprint (largest first); parcels without footprints get one building sized from the lot.</summary>
    private static List<BuildingSpec> Describe(Parcel parcel, bool forceShop, int index)
    {
        var specs = new List<BuildingSpec>();

        var floors = Floors(parcel);

        var count = parcel.fb.Length / 4;

        if (count == 0)
        {
            specs.Add(DescribeOne(parcel, forceShop, index, null, floors));

            return specs;
        }

        for (var i = 0; i < count; i++)
        {
            var box = new[] { parcel.fb[i * 4], parcel.fb[i * 4 + 1], parcel.fb[i * 4 + 2], parcel.fb[i * 4 + 3] };

            var area = (box[2] - box[0]) * (box[3] - box[1]);

            if (i > 0 && area < 40f)
            {
                continue;
            }

            var own = i == 0 ? floors : Mathf.Min(floors, 2);

            specs.Add(DescribeOne(parcel, forceShop && i == 0, index * 20 + i, box, own));
        }

        return specs;
    }

    /// <summary>Floors for a parcel: measured from gross floor area when available, otherwise estimated from value density.</summary>
    private static int Floors(Parcel parcel)
    {
        return parcel.m > 0 ? parcel.m : Mathf.Max(1, parcel.f);
    }

    /// <summary>Turns a parcel into a building description using its real footprint and floor count.</summary>
    private static BuildingSpec DescribeOne(Parcel parcel, bool forceShop, int index, float[]? box, int floors)
    {
        var hasFootprint = box != null && box[2] - box[0] >= 3f && box[3] - box[1] >= 3f;

        float width;

        float depth;

        Vector2 centre;

        if (hasFootprint)
        {
            width = box![2] - box[0];

            depth = box[3] - box[1];

            centre = new Vector2((box[0] + box[2]) / 2f, (box[1] + box[3]) / 2f);
        }
        else
        {
            width = Mathf.Max(0f, parcel.b[2] - parcel.b[0] - 3.6f);

            depth = Mathf.Max(0f, parcel.b[3] - parcel.b[1] - 3.6f);

            centre = new Vector2(parcel.c[0], parcel.c[1]);
        }

        var residential = parcel.k.StartsWith("A", StringComparison.Ordinal);

        var style = BuildingStyle.Apartment;

        var height = floors * FloorHeight;

        if (forceShop || (!residential && floors <= 3 && !parcel.k.StartsWith("B", StringComparison.Ordinal)))
        {
            style = BuildingStyle.Shop;

            height = Mathf.Clamp(4.5f + (floors - 1) * 3.6f, 4.5f, 16f);
        }
        else if (residential && floors <= 2)
        {
            style = BuildingStyle.House;

            height = Mathf.Max(3.4f, floors * FloorHeight);
        }
        else if (floors >= 10)
        {
            style = parcel.y > 0 && parcel.y < 1985 ? BuildingStyle.Striped : BuildingStyle.Glass;

            height = Mathf.Min(220f, floors * 3.6f);

            width = Mathf.Min(width, hasFootprint ? 90f : 34f);

            depth = Mathf.Min(depth, hasFootprint ? 90f : 34f);
        }
        else
        {
            width = Mathf.Min(width, 100f);

            depth = Mathf.Min(depth, 100f);
        }

        if (style == BuildingStyle.Glass || style == BuildingStyle.Striped)
        {
            width = Mathf.Max(0f, width - 2f);

            depth = Mathf.Max(0f, depth - 2f);
        }

        var name = style + "_" + floors + "fl_" + parcel.n + "_" + parcel.s.Replace(' ', '_') + "_" + index;

        return new BuildingSpec(name, centre, new Vector3(width, height, depth), style, FacingStreet(centre, width, depth));
    }

    /// <summary>Returns the axis direction from a building towards the nearest street of the real grid.</summary>
    private static Vector3 FacingStreet(Vector2 centre, float width, float depth)
    {
        var bestX = float.MaxValue;

        var signX = 1f;

        foreach (var axis in _data.grid.ns)
        {
            var gap = Mathf.Abs(centre.x - axis.c) - axis.w / 2f - width / 2f;

            if (gap < bestX)
            {
                bestX = gap;

                signX = axis.c >= centre.x ? 1f : -1f;
            }
        }

        var bestZ = float.MaxValue;

        var signZ = 1f;

        foreach (var axis in _data.grid.ew)
        {
            var gap = Mathf.Abs(centre.y - axis.c) - axis.w / 2f - depth / 2f;

            if (gap < bestZ)
            {
                bestZ = gap;

                signZ = axis.c >= centre.y ? 1f : -1f;
            }
        }

        return bestX < bestZ ? new Vector3(signX, 0f, 0f) : new Vector3(0f, 0f, signZ);
    }

    /// <summary>Picks a real low-rise commercial parcel roughly 110 m from the station to host the fire.</summary>
    private static Parcel ChooseIncidentParcel(List<Parcel> parcels)
    {
        Parcel? best = null;

        var bestScore = float.MaxValue;

        foreach (var parcel in parcels)
        {
            if (parcel.k != "F1" || parcel.fb.Length < 4 || Floors(parcel) < 1 || Floors(parcel) > 3 || parcel.f == 0 || parcel.a < 200 || parcel.a > 900)
            {
                continue;
            }

            var distance = Vector2.Distance(new Vector2(parcel.c[0], parcel.c[1]), StationParcelCentre);

            var score = Mathf.Abs(distance - 110f);

            if (distance >= 70f && score < bestScore)
            {
                best = parcel;

                bestScore = score;
            }
        }

        return best ?? parcels[0];
    }

    private static void BuildOne(BuildingSpec spec)
    {
        switch (spec.Style)
        {
            case BuildingStyle.Shop:
                BuildShop(spec);

                break;

            case BuildingStyle.Apartment:
                BuildApartment(spec);

                break;

            case BuildingStyle.House:
                BuildHouse(spec);

                break;

            default:
                BuildTower(spec);

                break;
        }
    }

    /// <summary>Adds a street tree and an occasional lamp on the sidewalk in front of a building.</summary>
    private static void Streetscape(BuildingSpec spec, int index)
    {
        var half = Mathf.Abs(spec.Facing.x) > 0.5f ? spec.Size.x / 2f : spec.Size.z / 2f;

        var edge = new Vector3(spec.Centre.x, 0f, spec.Centre.y) + spec.Facing * (half + 1.8f + SidewalkWidth * 0.6f);

        var lateral = Mathf.Abs(spec.Facing.x) > 0.5f ? spec.Size.z : spec.Size.x;

        if (index % 2 == 0 && lateral >= 8f)
        {
            Oak(edge);
        }
        else if (index % 6 == 1)
        {
            Lamp(edge, spec.Facing);
        }
    }

    private static Vector3 FacePoint(BuildingSpec spec, float y, float outward)
    {
        var half = Mathf.Abs(spec.Facing.x) > 0.5f ? spec.Size.x / 2f : spec.Size.z / 2f;

        return new Vector3(spec.Centre.x, y, spec.Centre.y) + spec.Facing * (half + outward);
    }

    private static Vector3 FaceScale(BuildingSpec spec, float wallFraction, float height, float thickness)
    {
        return Mathf.Abs(spec.Facing.x) > 0.5f
            ? new Vector3(thickness, height, spec.Size.z * wallFraction)
            : new Vector3(spec.Size.x * wallFraction, height, thickness);
    }

    /// <summary>Builds a low shop with a storefront and awning; some get a low-pitch metal gable, the rest a flat roof with rooftop units.</summary>
    private static void BuildShop(BuildingSpec spec)
    {
        var hash = StableHash(spec.Name);

        var height = spec.Size.y;

        var body = _wallMaterials[_wallIndex % _wallMaterials.Length];

        _wallIndex++;

        Make(new Spec("Body", new Vector3(spec.Centre.x, height / 2f, spec.Centre.y), spec.Size, body, PrimitiveType.Cube, true));

        var pitched = height <= 6f && spec.Size.x * spec.Size.z < 500f && hash % 4 == 0;

        if (pitched)
        {
            AddPitchedRoof(new RoofSpec(spec.Centre, new Vector2(spec.Size.x, spec.Size.z), height, 14f, 0.4f, true, _roofMetal));
        }
        else
        {
            FlatRoof(spec, hash, height);
        }

        Make(new Spec("Storefront", FacePoint(spec, 2f, 0.06f), FaceScale(spec, 0.8f, 2.4f, 0.12f), _glass));

        var awning = _wallIndex % 2 == 0 ? _red : _amber;

        var awningScale = Mathf.Abs(spec.Facing.x) > 0.5f ? new Vector3(1.8f, 0.15f, spec.Size.z * 0.8f) : new Vector3(spec.Size.x * 0.8f, 0.15f, 1.8f);

        Make(new Spec("Awning", FacePoint(spec, 3.5f, 0.9f), awningScale, awning));
    }

    /// <summary>Builds a mid-rise apartment or townhome with window strips, balconies and a flat roof with rooftop details.</summary>
    private static void BuildApartment(BuildingSpec spec)
    {
        var hash = StableHash(spec.Name);

        var height = spec.Size.y;

        var body = _wallIndex % 2 == 0 ? _brick : _stucco;

        _wallIndex++;

        Make(new Spec("Body", new Vector3(spec.Centre.x, height / 2f, spec.Centre.y), spec.Size, body, PrimitiveType.Cube, true));

        var floors = Mathf.FloorToInt(height / FloorHeight);

        var balconyScale = Mathf.Abs(spec.Facing.x) > 0.5f ? new Vector3(1.2f, 0.22f, spec.Size.z * 0.8f) : new Vector3(spec.Size.x * 0.8f, 0.22f, 1.2f);

        for (var floor = 0; floor < floors; floor++)
        {
            Make(new Spec("Windows", FacePoint(spec, floor * FloorHeight + 1.9f, 0.06f), FaceScale(spec, 0.8f, 1.5f, 0.12f), _glass));

            if (floor > 0)
            {
                Make(new Spec("Balcony", FacePoint(spec, floor * FloorHeight + 0.6f, 0.6f), balconyScale, _concrete));
            }
        }

        FlatRoof(spec, hash, height);
    }

    /// <summary>Builds a one- or two-storey house with a gable or hip roof, a shingle colour and sometimes a chimney.</summary>
    private static void BuildHouse(BuildingSpec spec)
    {
        var hash = StableHash(spec.Name);

        var height = spec.Size.y;

        var body = _wallMaterials[_wallIndex % _wallMaterials.Length];

        _wallIndex++;

        Make(new Spec("Body", new Vector3(spec.Centre.x, height / 2f, spec.Centre.y), spec.Size, body, PrimitiveType.Cube, true));

        var pitch = 24f + (hash % 5) * 2.5f;

        var gable = hash % 3 != 0;

        AddPitchedRoof(new RoofSpec(spec.Centre, new Vector2(spec.Size.x, spec.Size.z), height, pitch, 0.35f, gable, ShingleFor(hash / 7)));

        if (hash % 5 < 2)
        {
            var ridgeAlongZ = spec.Size.z >= spec.Size.x;

            var rise = Mathf.Min(spec.Size.x, spec.Size.z) / 2f * Mathf.Tan(pitch * Mathf.Deg2Rad);

            var offset = (hash % 2 == 0 ? 1f : -1f) * Mathf.Max(spec.Size.x, spec.Size.z) * 0.2f;

            var centreY = height + 0.3f + (rise + 0.6f) / 2f;

            var position = ridgeAlongZ ? new Vector3(spec.Centre.x, centreY, spec.Centre.y + offset) : new Vector3(spec.Centre.x + offset, centreY, spec.Centre.y);

            Make(new Spec("Chimney", position, new Vector3(0.7f, rise + 0.6f, 0.7f), _brick));
        }

        Make(new Spec("Windows", FacePoint(spec, 1.7f, 0.06f), FaceScale(spec, 0.6f, 1.1f, 0.12f), _glass));

        if (height >= 6f)
        {
            Make(new Spec("Windows", FacePoint(spec, 4.9f, 0.06f), FaceScale(spec, 0.6f, 1.1f, 0.12f), _glass));
        }
    }

    // ---------------------------------------------------------------- roofs

    private static int StableHash(string text)
    {
        unchecked
        {
            var hash = 17;

            foreach (var c in text)
            {
                hash = hash * 31 + c;
            }

            return hash & 0x7fffffff;
        }
    }

    private static Material ShingleFor(int hash)
    {
        switch (hash % 6)
        {
            case 1:
            case 5:
                return _roofBrown;

            case 2:
                return _roofBlack;

            case 3:
                return _roofTan;

            default:
                return _roof;
        }
    }

    private readonly struct RoofSpec
    {
        public RoofSpec(Vector2 centre, Vector2 size, float wallTop, float pitch, float overhang, bool gable, Material material)
        {
            Centre = centre;

            Size = size;

            WallTop = wallTop;

            Pitch = pitch;

            Overhang = overhang;

            Gable = gable;

            Material = material;
        }

        public Vector2 Centre { get; }

        public Vector2 Size { get; }

        public float WallTop { get; }

        public float Pitch { get; }

        public float Overhang { get; }

        public bool Gable { get; }

        public Material Material { get; }
    }

    private static SlabMesh RoofSlab(Material material)
    {
        if (!RoofSlabs.TryGetValue(material, out var mesh))
        {
            mesh = new SlabMesh();

            RoofSlabs[material] = mesh;
        }

        return mesh;
    }

    /// <summary>Adds a gable or hip roof over a rectangular footprint, with the ridge along the longer side.</summary>
    private static void AddPitchedRoof(RoofSpec roof)
    {
        var mesh = RoofSlab(roof.Material);

        var alongZ = roof.Size.y >= roof.Size.x;

        var length = alongZ ? roof.Size.y : roof.Size.x;

        var span = alongZ ? roof.Size.x : roof.Size.y;

        var tan = Mathf.Tan(roof.Pitch * Mathf.Deg2Rad);

        var halfSpan = span / 2f;

        var halfLength = length / 2f;

        var overhang = roof.Overhang;

        var ridgeY = roof.WallTop + halfSpan * tan;

        var eaveY = roof.WallTop - overhang * tan;

        var centre3 = new Vector3(roof.Centre.x, roof.WallTop, roof.Centre.y);

        Vector3 P(float u, float v, float y)
        {
            return alongZ ? new Vector3(roof.Centre.x + u, y, roof.Centre.y + v) : new Vector3(roof.Centre.x + v, y, roof.Centre.y + u);
        }

        void Face(params Vector3[] points)
        {
            var centroid = Vector3.zero;

            foreach (var point in points)
            {
                centroid += point;
            }

            centroid /= points.Length;

            mesh.AddFace(centroid - centre3 + Vector3.up * 0.01f, points);
        }

        var edge = halfSpan + overhang;

        if (roof.Gable)
        {
            var end = halfLength + overhang;

            Face(P(-edge, -end, eaveY), P(-edge, end, eaveY), P(0f, end, ridgeY), P(0f, -end, ridgeY));

            Face(P(edge, end, eaveY), P(edge, -end, eaveY), P(0f, -end, ridgeY), P(0f, end, ridgeY));

            foreach (var sign in new[] { -1f, 1f })
            {
                Face(P(-halfSpan, sign * halfLength, roof.WallTop), P(halfSpan, sign * halfLength, roof.WallTop), P(0f, sign * halfLength, ridgeY));
            }

            return;
        }

        var ridgeHalf = Mathf.Max(0f, halfLength - halfSpan);

        var eave = halfLength + overhang;

        Face(P(-edge, -eave, eaveY), P(-edge, eave, eaveY), P(0f, ridgeHalf, ridgeY), P(0f, -ridgeHalf, ridgeY));

        Face(P(edge, eave, eaveY), P(edge, -eave, eaveY), P(0f, -ridgeHalf, ridgeY), P(0f, ridgeHalf, ridgeY));

        Face(P(-edge, eave, eaveY), P(edge, eave, eaveY), P(0f, ridgeHalf, ridgeY));

        Face(P(edge, -eave, eaveY), P(-edge, -eave, eaveY), P(0f, -ridgeHalf, ridgeY));
    }

    /// <summary>Flat roof: parapet, a membrane surface, then rooftop units, a stair penthouse and the occasional water tank.</summary>
    private static void FlatRoof(BuildingSpec spec, int hash, float height)
    {
        Make(new Spec("Parapet", new Vector3(spec.Centre.x, height + 0.25f, spec.Centre.y), new Vector3(spec.Size.x + 0.3f, 0.5f, spec.Size.z + 0.3f), _limestone));

        Make(new Spec("RoofSurface", new Vector3(spec.Centre.x, height + 0.52f, spec.Centre.y), new Vector3(spec.Size.x - 0.2f, 0.06f, spec.Size.z - 0.2f), hash % 3 == 0 ? _roofWhite : _roofTar));

        var random = new System.Random(hash);

        var area = spec.Size.x * spec.Size.z;

        var units = Mathf.Clamp(Mathf.RoundToInt(area / 140f), 1, 8);

        for (var i = 0; i < units; i++)
        {
            var w = 1.4f + (float)random.NextDouble() * 1.2f;

            var d = 1.0f + (float)random.NextDouble() * 0.8f;

            var h = 0.8f + (float)random.NextDouble() * 0.5f;

            var x = spec.Centre.x + ((float)random.NextDouble() - 0.5f) * Mathf.Max(0f, spec.Size.x - w - 1.5f);

            var z = spec.Centre.y + ((float)random.NextDouble() - 0.5f) * Mathf.Max(0f, spec.Size.z - d - 1.5f);

            Make(new Spec("RooftopUnit", new Vector3(x, height + 0.55f + h / 2f, z), new Vector3(w, h, d), _steel));
        }

        if (spec.Size.y >= 9f && spec.Size.x >= 4f && spec.Size.z >= 4f)
        {
            var sx = hash % 2 == 0 ? 1f : -1f;

            var sz = hash % 4 < 2 ? 1f : -1f;

            var px = spec.Centre.x + sx * Mathf.Max(0f, spec.Size.x / 2f - 1.8f);

            var pz = spec.Centre.y + sz * Mathf.Max(0f, spec.Size.z / 2f - 1.8f);

            Make(new Spec("Penthouse", new Vector3(px, height + 0.55f + 1.3f, pz), new Vector3(2.4f, 2.6f, 2.6f), _stucco));
        }

        if (hash % 7 == 0 && area > 200f)
        {
            Make(new Spec("WaterTank", new Vector3(spec.Centre.x, height + 0.55f + 1.4f, spec.Centre.y), new Vector3(2.2f, 2.8f, 2.2f), _wood, PrimitiveType.Cylinder));
        }
    }

    /// <summary>Gives a tower a setback crown and sometimes an antenna with a beacon.</summary>
    private static void Crown(BuildingSpec spec, float top)
    {
        var random = new System.Random(StableHash(spec.Name));

        var x = spec.Centre.x;

        var z = spec.Centre.y;

        if (random.NextDouble() < 0.6)
        {
            Make(new Spec("Crown", new Vector3(x, top + 2.2f, z), new Vector3(spec.Size.x * 0.62f, 4.4f, spec.Size.z * 0.62f), _limestone));

            top += 4.4f;
        }

        if (random.NextDouble() < 0.5)
        {
            Make(new Spec("Antenna", new Vector3(x, top + 7f, z), new Vector3(0.25f, 14f, 0.25f), _steel, PrimitiveType.Cylinder));

            Make(new Spec("Beacon", new Vector3(x, top + 14.2f, z), new Vector3(0.5f, 0.5f, 0.5f), _signalRed));
        }
    }

    /// <summary>Turns the collected roof triangles into a few combined meshes.</summary>
    private static void FlushRoofs()
    {
        Section("Roofs", _root);

        foreach (var pair in RoofSlabs)
        {
            var mesh = new Mesh { name = "Roof_" + pair.Key.name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };

            mesh.SetVertices(pair.Value.Vertices);

            mesh.SetTriangles(pair.Value.Triangles, 0);

            mesh.RecalculateNormals();

            mesh.RecalculateBounds();

            var go = new GameObject("Roof_" + pair.Key.name, typeof(MeshFilter), typeof(MeshRenderer));

            go.transform.SetParent(_parent, false);

            go.GetComponent<MeshFilter>().sharedMesh = mesh;

            go.GetComponent<MeshRenderer>().sharedMaterial = pair.Key;

            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
        }

        RoofSlabs.Clear();
    }

    private static void BuildTower(BuildingSpec spec)
    {
        var x = spec.Centre.x;

        var z = spec.Centre.y;

        var width = spec.Size.x;

        var height = spec.Size.y;

        var depth = spec.Size.z;

        Make(new Spec("Podium", new Vector3(x, PodiumHeight / 2f, z), new Vector3(width + 2f, PodiumHeight, depth + 2f), _limestone, PrimitiveType.Cube, true));

        var bodyMaterial = spec.Style == BuildingStyle.Glass ? _towerGlass : _limestone;

        Make(new Spec("Body", new Vector3(x, PodiumHeight + height / 2f, z), spec.Size, bodyMaterial, PrimitiveType.Cube, true));

        if (spec.Style == BuildingStyle.Glass)
        {
            for (var y = PodiumHeight + 3.6f; y < PodiumHeight + height; y += 3.6f)
            {
                Make(new Spec("Floor", new Vector3(x, y, z), new Vector3(width + 0.4f, 0.3f, depth + 0.4f), _limestone));
            }
        }
        else
        {
            AddFins(spec);
        }

        var top = PodiumHeight + height;

        Make(new Spec("Parapet", new Vector3(x, top + 0.3f, z), new Vector3(width + 0.4f, 0.6f, depth + 0.4f), _limestone));

        Make(new Spec("RoofPlant", new Vector3(x, top + 1.8f, z), new Vector3(width * 0.4f, 3f, depth * 0.4f), _steel));

        Crown(spec, top + 0.6f);
    }

    private static void AddFins(BuildingSpec spec)
    {
        const float Spacing = 2.2f;

        var centreY = PodiumHeight + spec.Size.y / 2f;

        var halfWidth = spec.Size.x / 2f;

        var halfDepth = spec.Size.z / 2f;

        for (var offset = -halfWidth + 1f; offset <= halfWidth - 0.9f; offset += Spacing)
        {
            foreach (var side in new[] { -1f, 1f })
            {
                Make(new Spec("Fin", new Vector3(spec.Centre.x + offset, centreY, spec.Centre.y + side * (halfDepth + 0.15f)), new Vector3(0.6f, spec.Size.y, 0.5f), _granite));
            }
        }

        for (var offset = -halfDepth + 1f; offset <= halfDepth - 0.9f; offset += Spacing)
        {
            foreach (var side in new[] { -1f, 1f })
            {
                Make(new Spec("Fin", new Vector3(spec.Centre.x + side * (halfWidth + 0.15f), centreY, spec.Centre.y + offset), new Vector3(0.5f, spec.Size.y, 0.6f), _granite));
            }
        }
    }

    /// <summary>Paints stall lines and parks cars on a paved lot.</summary>
    private static void LotDetails(Parcel parcel, int seed)
    {
        var width = parcel.b[2] - parcel.b[0] - 2f;

        var depth = parcel.b[3] - parcel.b[1] - 2f;

        var boundsArea = (parcel.b[2] - parcel.b[0]) * (parcel.b[3] - parcel.b[1]);

        if (width < 8f || depth < 8f || parcel.a < boundsArea * 0.75f)
        {
            return;
        }

        var random = new System.Random(seed);

        var rows = depth >= 16f ? new[] { parcel.b[1] + 4.5f, parcel.b[3] - 4.5f } : new[] { parcel.c[1] };

        foreach (var z in rows)
        {
            for (var x = parcel.b[0] + 2.4f; x < parcel.b[2] - 1.2f; x += 2.8f)
            {
                Make(new Spec("StallLine", new Vector3(x - 1.4f, LotTop + 0.01f, z), new Vector3(0.1f, 0.02f, 5f), _white));

                if (random.NextDouble() < 0.55)
                {
                    Car(new Vector3(x, LotTop, z), _carMaterials[random.Next(_carMaterials.Length)]);
                }
            }
        }
    }

    private static void Car(Vector3 position, Material paint)
    {
        Make(new Spec("Car", position + new Vector3(0f, 0.65f, 0f), new Vector3(1.8f, 0.75f, 4.2f), paint, PrimitiveType.Cube, true));

        Make(new Spec("CarCabin", position + new Vector3(0f, 1.3f, -0.1f), new Vector3(1.6f, 0.6f, 2.2f), _glass));
    }

    /// <summary>Plants live oaks inside a park parcel.</summary>
    private static void ParkTrees(Parcel parcel)
    {
        var polygon = ToPolygon(parcel.p);

        for (var x = parcel.b[0] + 4f; x <= parcel.b[2] - 3f; x += 11f)
        {
            for (var z = parcel.b[1] + 4f; z <= parcel.b[3] - 3f; z += 11f)
            {
                if (PointInPolygon(polygon, new Vector2(x, z)))
                {
                    Oak(new Vector3(x, LotTop, z));
                }
            }
        }
    }

    /// <summary>Paints centre lines on every street of the detected grid, leaving gaps at the intersections.</summary>
    private static void BuildMarkings()
    {
        Section("Markings", _root);

        foreach (var axis in _data.grid.ns)
        {
            if (axis.c < MinX + 5f || axis.c > MaxX - 5f)
            {
                continue;
            }

            var oneWay = axis.name == "AUSTIN ST" || axis.name == "LA BRANCH ST";

            foreach (var span in Spans(MinZ, MaxZ, _data.grid.ew))
            {
                MarkStreet(axis, span, true, oneWay);
            }
        }

        foreach (var axis in _data.grid.ew)
        {
            if (axis.c < MinZ + 5f || axis.c > MaxZ - 5f)
            {
                continue;
            }

            foreach (var span in Spans(MinX, MaxX, _data.grid.ns))
            {
                MarkStreet(axis, span, false, false);
            }
        }
    }

    /// <summary>Returns the stretches between the crossing streets' road edges.</summary>
    private static List<Vector2> Spans(float from, float to, GridAxis[] crossings)
    {
        var spans = new List<Vector2>();

        var start = from;

        foreach (var crossing in crossings)
        {
            var edge = crossing.c - crossing.w / 2f;

            if (edge > start && crossing.c <= to)
            {
                spans.Add(new Vector2(start, edge));
            }

            start = Mathf.Max(start, crossing.c + crossing.w / 2f);
        }

        if (to > start)
        {
            spans.Add(new Vector2(start, to));
        }

        return spans;
    }

    private static void MarkStreet(GridAxis axis, Vector2 span, bool alongZ, bool oneWay)
    {
        var length = span.y - span.x;

        var centre = span.x + length / 2f;

        if (length < 6f)
        {
            return;
        }

        if (oneWay)
        {
            for (var along = span.x + 2f; along < span.y - 2f; along += 8f)
            {
                Make(new Spec("LaneDash", At(axis.c, along, alongZ, MarkingY), Along(0.14f, 3f, alongZ), _white));
            }

            for (var along = span.x + 14f; along < span.y - 6f; along += 40f)
            {
                Arrow(axis.c, along, alongZ);
            }

            return;
        }

        foreach (var offset in new[] { -0.15f, 0.15f })
        {
            Make(new Spec("CenterLine", At(axis.c + offset, centre, alongZ, MarkingY), Along(0.12f, length, alongZ), _yellow));
        }
    }

    private static void Arrow(float axis, float along, bool alongZ)
    {
        Make(new Spec("ArrowShaft", At(axis, along, alongZ, MarkingY), Along(0.4f, 2.6f, alongZ), _white));

        foreach (var side in new[] { -1f, 1f })
        {
            var head = Make(new Spec("ArrowHead", At(axis + side * 0.49f, along + 0.81f, alongZ, MarkingY), Along(0.4f, 1.4f, alongZ), _white));

            head.transform.rotation = Quaternion.Euler(0f, -side * 45f, 0f);
        }
    }

    /// <summary>Position on a street: <paramref name="across"/> is the axis coordinate, <paramref name="along"/> runs down the street.</summary>
    private static Vector3 At(float across, float along, bool alongZ, float y)
    {
        return alongZ ? new Vector3(across, y, along) : new Vector3(along, y, across);
    }

    private static Vector3 Along(float width, float length, bool alongZ)
    {
        return alongZ ? new Vector3(width, 0.02f, length) : new Vector3(length, 0.02f, width);
    }

    /// <summary>Paints zebra crossings on all four legs of every Elgin Street intersection.</summary>
    private static void BuildCrossings()
    {
        Section("Crosswalks", _root);

        var elgin = FindAxis(_data.grid.ew, "ELGIN ST");

        if (elgin is null)
        {
            return;
        }

        foreach (var ns in _data.grid.ns)
        {
            if (ns.c < MinX + 20f || ns.c > MaxX - 20f)
            {
                continue;
            }

            foreach (var side in new[] { -1f, 1f })
            {
                var xLeg = ns.c + side * (ns.w / 2f + 1.8f);

                for (var z = elgin.c - (elgin.w / 2f - SidewalkWidth) + 0.8f; z <= elgin.c + (elgin.w / 2f - SidewalkWidth) - 0.6f; z += 1.6f)
                {
                    Make(new Spec("Crosswalk", new Vector3(xLeg, MarkingY, z), new Vector3(3f, 0.02f, 0.8f), _white));
                }

                var zLeg = elgin.c + side * (elgin.w / 2f + 1.8f);

                for (var x = ns.c - (ns.w / 2f - SidewalkWidth) + 0.8f; x <= ns.c + (ns.w / 2f - SidewalkWidth) - 0.6f; x += 1.6f)
                {
                    Make(new Spec("Crosswalk", new Vector3(x, MarkingY, zLeg), new Vector3(0.8f, 0.02f, 3f), _white));
                }
            }
        }
    }

    private static GridAxis? FindAxis(GridAxis[] axes, string name)
    {
        foreach (var axis in axes)
        {
            if (axis.name == name)
            {
                return axis;
            }
        }

        return null;
    }

    /// <summary>Builds mast-arm signals at every Elgin Street intersection.</summary>
    private static void BuildSignals()
    {
        Section("Signals", _root);

        var elgin = FindAxis(_data.grid.ew, "ELGIN ST");

        if (elgin is null)
        {
            return;
        }

        foreach (var ns in _data.grid.ns)
        {
            if (ns.c < MinX + 20f || ns.c > MaxX - 20f)
            {
                continue;
            }

            var hx = ns.w / 2f - 1.2f;

            var hz = elgin.w / 2f - 1.2f;

            Mast(new Vector3(ns.c - hx, 0f, elgin.c - hz), new Vector3(0f, 0f, hz * 2f));

            Mast(new Vector3(ns.c + hx, 0f, elgin.c + hz), new Vector3(0f, 0f, -hz * 2f));

            Mast(new Vector3(ns.c + hx, 0f, elgin.c - hz), new Vector3(-hx * 2f, 0f, 0f));
        }
    }

    private static void Mast(Vector3 position, Vector3 arm)
    {
        const float ArmY = 7.6f;

        Make(new Spec("MastPole", position + Vector3.up * 4f, new Vector3(0.35f, 8f, 0.35f), _steel, PrimitiveType.Cylinder, true));

        var alongX = Mathf.Abs(arm.x) > Mathf.Abs(arm.z);

        Make(new Spec("MastArm", position + arm / 2f + Vector3.up * ArmY, new Vector3(Mathf.Abs(arm.x) + 0.25f, 0.25f, Mathf.Abs(arm.z) + 0.25f), _steel));

        var faceOffset = alongX ? new Vector3(0f, 0f, 0.27f) : new Vector3(0.27f, 0f, 0f);

        var lensScale = alongX ? new Vector3(0.28f, 0.28f, 0.1f) : new Vector3(0.1f, 0.28f, 0.28f);

        foreach (var fraction in new[] { 0.45f, 0.8f })
        {
            var head = position + arm * fraction + Vector3.up * (ArmY - 0.9f);

            Make(new Spec("SignalHead", head, new Vector3(0.5f, 1.4f, 0.5f), _dark));

            foreach (var sign in new[] { -1f, 1f })
            {
                (alongX ? NsRed : EwRed).Add(Make(new Spec("SignalRed", head + Vector3.up * 0.45f + faceOffset * sign, lensScale, _lensRedOff)).GetComponent<Renderer>());

                (alongX ? NsAmber : EwAmber).Add(Make(new Spec("SignalAmber", head + faceOffset * sign, lensScale, _lensAmberOff)).GetComponent<Renderer>());

                (alongX ? NsGreen : EwGreen).Add(Make(new Spec("SignalGreen", head + Vector3.down * 0.45f + faceOffset * sign, lensScale, _lensGreenOff)).GetComponent<Renderer>());
            }
        }
    }

    /// <summary>Draws any real METRORail track that passes through the window.</summary>
    private static void BuildRail()
    {
        Section("Rail", _root);

        foreach (var path in _data.rail)
        {
            for (var i = 0; i + 3 < path.p.Length; i += 2)
            {
                var a = new Vector3(path.p[i], MarkingY, path.p[i + 1]);

                var b = new Vector3(path.p[i + 2], MarkingY, path.p[i + 3]);

                if (a.x < MinX || a.x > MaxX || a.z < MinZ || a.z > MaxZ || Vector3.Distance(a, b) < 0.5f)
                {
                    continue;
                }

                var piece = Make(new Spec("RailBed", (a + b) / 2f, new Vector3(4.5f, 0.04f, Vector3.Distance(a, b)), _concrete));

                piece.transform.rotation = Quaternion.LookRotation(b - a);

                foreach (var side in new[] { -0.72f, 0.72f })
                {
                    var rail = Make(new Spec("Rail", (a + b) / 2f + Vector3.Cross(Vector3.up, (b - a).normalized) * side + Vector3.up * 0.05f, new Vector3(0.12f, 0.06f, Vector3.Distance(a, b)), _steel));

                    rail.transform.rotation = Quaternion.LookRotation(b - a);
                }
            }
        }
    }

    /// <summary>Builds a distant elevated freeway along the south-east edge, standing in for US 59 / I-45.</summary>
    private static void BuildFreeway()
    {
        Section("Freeway", _root);

        const float DeckY = 9f;

        const float DeckWidth = 34f;

        var south = MinZ - GroundMargin;

        var north = MaxZ + GroundMargin;

        var length = north - south;

        var centreZ = south + length / 2f;

        Make(new Spec("FreewayDeck", new Vector3(FreewayX, DeckY, centreZ), new Vector3(DeckWidth, 1.2f, length), _concrete, PrimitiveType.Cube, true));

        foreach (var side in new[] { -1f, 1f })
        {
            Make(new Spec("FreewayBarrier", new Vector3(FreewayX + side * (DeckWidth / 2f - 0.4f), DeckY + 1.15f, centreZ), new Vector3(0.8f, 1.1f, length), _concrete, PrimitiveType.Cube, true));
        }

        Make(new Spec("FreewayAsphalt", new Vector3(FreewayX, DeckY + 0.62f, centreZ), new Vector3(DeckWidth - 2f, 0.04f, length), _asphalt));

        foreach (var offset in new[] { -8.5f, 0f, 8.5f })
        {
            for (var z = south + 4f; z < north; z += 9f)
            {
                Make(new Spec("FreewayPaint", new Vector3(FreewayX + offset, DeckY + 0.66f, z), new Vector3(0.18f, 0.02f, 4f), Mathf.Approximately(offset, 0f) ? _yellow : _white));
            }
        }

        for (var z = south + 8f; z < north; z += 24f)
        {
            foreach (var side in new[] { -1f, 1f })
            {
                Make(new Spec("FreewayColumn", new Vector3(FreewayX + side * 8f, DeckY / 2f - 0.3f, z), new Vector3(2.2f, DeckY - 0.6f, 2.2f), _concrete, PrimitiveType.Cylinder, true));
            }

            Make(new Spec("FreewayCapBeam", new Vector3(FreewayX, DeckY - 0.9f, z), new Vector3(24f, 1.4f, 2.6f), _concrete));
        }
    }

    /// <summary>Adds a distant downtown skyline beyond the north-east edge, where downtown lies on the map.</summary>
    private static void BuildSkyline()
    {
        Section("DowntownSkyline", _root);

        var random = new System.Random(4242);

        for (var x = MinX - 40f; x <= MaxX + 60f; x += 16f)
        {
            var height = 30f + (float)random.NextDouble() * 80f;

            var width = 14f + (float)random.NextDouble() * 8f;

            Make(new Spec("Skyline", new Vector3(x, height / 2f, MaxZ + GroundMargin + 40f + (float)random.NextDouble() * 50f), new Vector3(width, height, width), random.NextDouble() < 0.6 ? _towerGlass : _granite));
        }
    }

    /// <summary>Moves the station onto its real lot on Elgin Street and shrinks its apron to a driveway.</summary>
    private static void RelocateStation()
    {
        var station = GameObject.Find("Station07") ?? GameObject.Find("Station08");

        if (station is null)
        {
            return;
        }

        station.transform.position = new Vector3(11.3f, 0f, 50.2f);

        var apron = station.transform.Find("StationApron");

        if (apron != null)
        {
            apron.localPosition = new Vector3(4f, 0.08f, -60.5f);

            apron.localScale = new Vector3(32f, 0.15f, 7f);
        }

        foreach (Transform child in station.transform)
        {
            if (child.name == "ApronGuide")
            {
                child.localPosition = new Vector3(child.localPosition.x, child.localPosition.y, -60.5f);

                child.localScale = new Vector3(child.localScale.x, child.localScale.y, 6f);
            }
        }

        var engine = GameObject.Find("Engine07") ?? GameObject.Find("Engine08");

        if (engine != null)
        {
            var controller = engine.GetComponent<CharacterController>();

            controller.enabled = false;

            engine.transform.SetPositionAndRotation(new Vector3(12.3f, 0.2f, -9f), Quaternion.identity);

            controller.enabled = true;
        }
    }

    /// <summary>Puts the fire, its smoke and the shop sign on the chosen parcel's street-facing wall.</summary>
    private static void RelocateIncident(Parcel parcel)
    {
        var spec = Describe(parcel, true, 0)[0];

        var facing = spec.Facing;

        var fire = FacePoint(spec, 3f, 0.25f);

        Place("FireIncident", fire);

        Place("Flames", fire);

        Place("Smoke", fire + new Vector3(0.6f, 6f, 0f));

        var sign = GameObject.Find("MarketSign") ?? GameObject.Find("CanalMarketSign");

        if (sign != null)
        {
            sign.transform.SetPositionAndRotation(FacePoint(spec, 5.2f, 0.12f), Quaternion.LookRotation(-facing));
        }
    }

    /// <summary>Invisible walls around the playable window. They sit on the Ignore Raycast layer so the follow camera is not pulled in by them.</summary>
    private static void BuildBounds()
    {
        Section("WorldBounds", _root);

        const float Thickness = 6f;

        const float Height = 40f;

        const float Overshoot = 12f;

        var width = MaxX - MinX;

        var depth = MaxZ - MinZ;

        var walls = new[]
        {
            ("WallWest", new Vector3(MinX - Thickness / 2f, Height / 2f, (MinZ + MaxZ) / 2f), new Vector3(Thickness, Height, depth + Overshoot * 2f)),
            ("WallEast", new Vector3(MaxX + Thickness / 2f, Height / 2f, (MinZ + MaxZ) / 2f), new Vector3(Thickness, Height, depth + Overshoot * 2f)),
            ("WallSouth", new Vector3((MinX + MaxX) / 2f, Height / 2f, MinZ - Thickness / 2f), new Vector3(width + Overshoot * 2f, Height, Thickness)),
            ("WallNorth", new Vector3((MinX + MaxX) / 2f, Height / 2f, MaxZ + Thickness / 2f), new Vector3(width + Overshoot * 2f, Height, Thickness)),
        };

        foreach (var wall in walls)
        {
            var go = new GameObject(wall.Item1) { layer = 2 };

            go.transform.SetParent(_parent, false);

            go.transform.position = wall.Item2;

            go.AddComponent<BoxCollider>().size = wall.Item3;
        }
    }

    /// <summary>Marks real storefronts as possible fire sites for free-roam calls. Site 0 is the first call.</summary>
    private static List<(Transform Site, string Street)> BuildIncidentSites(List<Parcel> parcels, Parcel first)
    {
        Section("IncidentSites", _root);

        var sites = new List<(Transform, string)>();

        var chosen = new List<Vector2>();

        void Add(Parcel parcel)
        {
            var spec = Describe(parcel, true, 0)[0];

            var go = new GameObject("Site " + parcel.n + " " + parcel.s);

            go.transform.SetParent(_parent, false);

            go.transform.SetPositionAndRotation(FacePoint(spec, 3f, 0.25f), Quaternion.LookRotation(-spec.Facing));

            sites.Add((go.transform, parcel.n + " " + parcel.s));

            chosen.Add(new Vector2(parcel.c[0], parcel.c[1]));
        }

        Add(first);

        var random = new System.Random(77);

        var candidates = new List<Parcel>();

        foreach (var parcel in parcels)
        {
            var inset = parcel.c[0] > MinX + 60f && parcel.c[0] < MaxX - 60f && parcel.c[1] > MinZ + 60f && parcel.c[1] < MaxZ - 60f;

            if (parcel != first && inset && (parcel.k == "F1" || parcel.k == "F2") && parcel.fb.Length >= 4 && parcel.f > 0 && Floors(parcel) <= 3 && parcel.a >= 150 && parcel.a <= 1600 && Vector2.Distance(new Vector2(parcel.c[0], parcel.c[1]), StationParcelCentre) >= 100f)
            {
                candidates.Add(parcel);
            }
        }

        for (var i = candidates.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);

            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }

        foreach (var parcel in candidates)
        {
            if (sites.Count >= 24)
            {
                break;
            }

            var here = new Vector2(parcel.c[0], parcel.c[1]);

            var clear = true;

            foreach (var other in chosen)
            {
                if (Vector2.Distance(here, other) < 55f)
                {
                    clear = false;

                    break;
                }
            }

            if (clear)
            {
                Add(parcel);
            }
        }

        var result = new List<(Transform Site, string Street)>();

        foreach (var site in sites)
        {
            result.Add((site.Item1, site.Item2));
        }

        return result;
    }

    /// <summary>Hands the incident sites, the sign and the playable bounds to the engine's FireResponse.</summary>
    private static void WireDispatch(List<(Transform Site, string Street)> sites)
    {
        var engine = GameObject.Find("Engine07") ?? GameObject.Find("Engine08");

        var response = engine == null ? null : engine.GetComponent("FireResponse");

        if (response == null)
        {
            return;
        }

        var serialized = new SerializedObject(response);

        var siteProperty = serialized.FindProperty("_incidentSites");

        var streetProperty = serialized.FindProperty("_incidentStreets");

        siteProperty.arraySize = sites.Count;

        streetProperty.arraySize = sites.Count;

        for (var i = 0; i < sites.Count; i++)
        {
            siteProperty.GetArrayElementAtIndex(i).objectReferenceValue = sites[i].Site;

            streetProperty.GetArrayElementAtIndex(i).stringValue = sites[i].Street;
        }

        var sign = GameObject.Find("MarketSign") ?? GameObject.Find("CanalMarketSign");

        serialized.FindProperty("_sign").objectReferenceValue = sign == null ? null : sign.transform;

        serialized.FindProperty("_signText").objectReferenceValue = sign == null ? null : sign.GetComponentInChildren<TMP_Text>(true);

        serialized.FindProperty("_boundsMin").vector2Value = new Vector2(MinX, MinZ);

        serialized.FindProperty("_boundsMax").vector2Value = new Vector2(MaxX, MaxZ);

        serialized.FindProperty("_incidentLocation").stringValue = sites.Count > 0 ? sites[0].Street : "CANAL ST";

        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>Authors the car and pedestrian templates and the TrafficSystem that drives them.</summary>
    private static void BuildTraffic()
    {
        var engine = GameObject.Find("Engine07") ?? GameObject.Find("Engine08");

        var response = engine == null ? null : engine.GetComponent("FireResponse");

        Type? trafficType = null;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            trafficType ??= assembly.GetType("HTownSim.Runtime.TrafficSystem");
        }

        if (engine == null || response == null || trafficType == null)
        {
            return;
        }

        Section("TrafficTemplates", _root);

        var templates = _parent;

        var car = BuildCarTemplate(templates);

        var pedestrian = BuildPedestrianTemplate(templates);

        var traffic = new GameObject("Traffic");

        traffic.transform.SetParent(_root, false);

        var system = traffic.AddComponent(trafficType);

        var serialized = new SerializedObject(system);

        serialized.FindProperty("_player").objectReferenceValue = engine.transform;

        serialized.FindProperty("_response").objectReferenceValue = response;

        serialized.FindProperty("_carTemplate").objectReferenceValue = car;

        serialized.FindProperty("_pedestrianTemplate").objectReferenceValue = pedestrian;

        SetObjects(serialized, "_carPaints", _carMaterials);

        SetObjects(serialized, "_shirtPaints", _shirtMaterials);

        SetObjects(serialized, "_skinPaints", _skinMaterials);

        var nsCentre = new List<float>();

        var nsWidth = new List<float>();

        var nsOneWay = new List<bool>();

        foreach (var axis in _data.grid.ns)
        {
            if (axis.c > MinX + 25f && axis.c < MaxX - 25f)
            {
                nsCentre.Add(axis.c);

                nsWidth.Add(axis.w);

                nsOneWay.Add(axis.name == "AUSTIN ST" || axis.name == "LA BRANCH ST");
            }
        }

        var ewCentre = new List<float>();

        var ewWidth = new List<float>();

        var ewOneWay = new List<bool>();

        foreach (var axis in _data.grid.ew)
        {
            if (axis.c > MinZ + 25f && axis.c < MaxZ - 25f)
            {
                ewCentre.Add(axis.c);

                ewWidth.Add(axis.w);

                ewOneWay.Add(false);
            }
        }

        SetFloats(serialized, "_nsCentre", nsCentre);

        SetFloats(serialized, "_nsWidth", nsWidth);

        SetBools(serialized, "_nsOneWay", nsOneWay);

        SetFloats(serialized, "_ewCentre", ewCentre);

        SetFloats(serialized, "_ewWidth", ewWidth);

        SetBools(serialized, "_ewOneWay", ewOneWay);

        serialized.FindProperty("_boundsMin").vector2Value = new Vector2(MinX, MinZ);

        serialized.FindProperty("_boundsMax").vector2Value = new Vector2(MaxX, MaxZ);

        var elgin = FindAxis(_data.grid.ew, "ELGIN ST");

        serialized.FindProperty("_signalZ").floatValue = elgin == null ? 0f : elgin.c;

        SetObjects(serialized, "_ewRed", EwRed.ToArray());

        SetObjects(serialized, "_ewAmber", EwAmber.ToArray());

        SetObjects(serialized, "_ewGreen", EwGreen.ToArray());

        SetObjects(serialized, "_nsRed", NsRed.ToArray());

        SetObjects(serialized, "_nsAmber", NsAmber.ToArray());

        SetObjects(serialized, "_nsGreen", NsGreen.ToArray());

        serialized.FindProperty("_redOn").objectReferenceValue = _lensRedOn;

        serialized.FindProperty("_redOff").objectReferenceValue = _lensRedOff;

        serialized.FindProperty("_amberOn").objectReferenceValue = _lensAmberOn;

        serialized.FindProperty("_amberOff").objectReferenceValue = _lensAmberOff;

        serialized.FindProperty("_greenOn").objectReferenceValue = _lensGreenOn;

        serialized.FindProperty("_greenOff").objectReferenceValue = _lensGreenOff;

        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetObjects(SerializedObject serialized, string name, Object[] items)
    {
        var property = serialized.FindProperty(name);

        property.arraySize = items.Length;

        for (var i = 0; i < items.Length; i++)
        {
            property.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
        }
    }

    private static void SetFloats(SerializedObject serialized, string name, List<float> items)
    {
        var property = serialized.FindProperty(name);

        property.arraySize = items.Count;

        for (var i = 0; i < items.Count; i++)
        {
            property.GetArrayElementAtIndex(i).floatValue = items[i];
        }
    }

    private static void SetBools(SerializedObject serialized, string name, List<bool> items)
    {
        var property = serialized.FindProperty(name);

        property.arraySize = items.Count;

        for (var i = 0; i < items.Count; i++)
        {
            property.GetArrayElementAtIndex(i).boolValue = items[i];
        }
    }

    /// <summary>Removes static batching from every part of a dynamic template.</summary>
    private static void MakeDynamic(GameObject root)
    {
        foreach (var child in root.GetComponentsInChildren<Transform>(true))
        {
            GameObjectUtility.SetStaticEditorFlags(child.gameObject, 0);
        }
    }

    private static GameObject BuildCarTemplate(Transform templates)
    {
        var root = new GameObject("CarTemplate");

        root.transform.SetParent(templates, false);

        _parent = root.transform;

        Make(new Spec("Body", new Vector3(0f, 0.65f, 0f), new Vector3(1.8f, 0.75f, 4.2f), _carMaterials[0]));

        Make(new Spec("Cabin", new Vector3(0f, 1.3f, -0.1f), new Vector3(1.6f, 0.6f, 2.2f), _glass));

        foreach (var side in new[] { -1f, 1f })
        {
            foreach (var z in new[] { -1.35f, 1.35f })
            {
                Make(new Spec("Wheel", new Vector3(side * 0.85f, 0.35f, z), new Vector3(0.7f, 0.25f, 0.7f), _dark, PrimitiveType.Cylinder)).transform.rotation = Quaternion.Euler(0f, 0f, 90f);
            }

            Make(new Spec("Headlight", new Vector3(side * 0.6f, 0.7f, 2.12f), new Vector3(0.35f, 0.16f, 0.06f), _glowWhite));

            Make(new Spec("Taillight", new Vector3(side * 0.6f, 0.8f, -2.12f), new Vector3(0.35f, 0.16f, 0.06f), _glowRed));
        }

        var collider = root.AddComponent<BoxCollider>();

        collider.center = new Vector3(0f, 0.85f, 0f);

        collider.size = new Vector3(1.9f, 1.6f, 4.3f);

        var body = root.AddComponent<Rigidbody>();

        body.isKinematic = true;

        body.useGravity = false;

        MakeDynamic(root);

        return root;
    }

    private static GameObject BuildPedestrianTemplate(Transform templates)
    {
        var root = new GameObject("PedestrianTemplate");

        root.transform.SetParent(templates, false);

        _parent = root.transform;

        Make(new Spec("Torso", new Vector3(0f, 1.16f, 0f), new Vector3(0.46f, 0.62f, 0.28f), _shirtMaterials[0]));

        Make(new Spec("Head", new Vector3(0f, 1.62f, 0f), Vector3.one * 0.26f, _skinMaterials[0], PrimitiveType.Sphere));

        foreach (var side in new[] { -1f, 1f })
        {
            var leg = new GameObject(side < 0f ? "LegL" : "LegR").transform;

            leg.SetParent(root.transform, false);

            leg.localPosition = new Vector3(side * 0.12f, 0.85f, 0f);

            var legMesh = Make(new Spec("Leg", Vector3.zero, new Vector3(0.17f, 0.85f, 0.2f), _pants));

            legMesh.transform.SetParent(leg, false);

            legMesh.transform.localPosition = new Vector3(0f, -0.425f, 0f);

            var arm = new GameObject(side < 0f ? "ArmL" : "ArmR").transform;

            arm.SetParent(root.transform, false);

            arm.localPosition = new Vector3(side * 0.3f, 1.42f, 0f);

            var armMesh = Make(new Spec("Arm", Vector3.zero, new Vector3(0.12f, 0.6f, 0.14f), _skinMaterials[0]));

            armMesh.transform.SetParent(arm, false);

            armMesh.transform.localPosition = new Vector3(0f, -0.3f, 0f);
        }

        MakeDynamic(root);

        return root;
    }

    private static void Place(string name, Vector3 position)
    {
        var go = GameObject.Find(name);

        if (go != null)
        {
            go.transform.position = position;
        }
    }

    private static void Oak(Vector3 position)
    {
        Make(new Spec("OakTrunk", position + Vector3.up * 2f, new Vector3(0.6f, 4f, 0.6f), _wood, PrimitiveType.Cylinder, true));

        Make(new Spec("OakCanopy", position + new Vector3(-1.2f, 4.6f, 0f), Vector3.one * 4.6f, _leaf, PrimitiveType.Sphere));

        Make(new Spec("OakCanopy", position + new Vector3(0f, 5.6f, 0f), Vector3.one * 5f, _leaf, PrimitiveType.Sphere));

        Make(new Spec("OakCanopy", position + new Vector3(1.2f, 4.6f, 0f), Vector3.one * 4.6f, _leaf, PrimitiveType.Sphere));
    }

    private static void Lamp(Vector3 position, Vector3 direction)
    {
        Make(new Spec("LampPost", position + Vector3.up * 3.5f, new Vector3(0.25f, 7f, 0.25f), _steel, PrimitiveType.Cylinder, true));

        var armScale = new Vector3(Mathf.Abs(direction.x) * 2.2f + 0.15f, 0.15f, Mathf.Abs(direction.z) * 2.2f + 0.15f);

        Make(new Spec("LampArm", position + direction * 1.1f + Vector3.up * 7f, armScale, _steel));

        Make(new Spec("Lamp", position + direction * 2.1f + Vector3.up * 6.9f, new Vector3(0.7f, 0.2f, 0.7f), _amber));
    }

    private static void Section(string name, Transform parent)
    {
        var go = new GameObject(name);

        go.transform.SetParent(parent, false);

        _parent = go.transform;
    }

    private static GameObject Make(Spec spec)
    {
        var go = GameObject.CreatePrimitive(spec.Shape);

        go.name = spec.Name;

        go.transform.SetParent(_parent, false);

        go.transform.position = spec.Position;

        var scale = spec.Scale;

        if (spec.Shape == PrimitiveType.Cylinder)
        {
            scale = new Vector3(scale.x, scale.y * 0.5f, scale.z);
        }

        go.transform.localScale = scale;

        go.GetComponent<Renderer>().sharedMaterial = spec.Material;

        if (!spec.Collide)
        {
            Object.DestroyImmediate(go.GetComponent<Collider>());
        }

        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);

        return go;
    }

    private static List<Vector2> ToPolygon(float[] flat)
    {
        var polygon = new List<Vector2>(flat.Length / 2);

        for (var i = 0; i + 1 < flat.Length; i += 2)
        {
            polygon.Add(new Vector2(flat[i], flat[i + 1]));
        }

        return polygon;
    }

    private static float SignedArea(List<Vector2> polygon)
    {
        var area = 0f;

        for (var i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i];

            var b = polygon[(i + 1) % polygon.Count];

            area += a.x * b.y - b.x * a.y;
        }

        return area / 2f;
    }

    private static List<Vector2> ToCounterClockwise(List<Vector2> polygon)
    {
        if (SignedArea(polygon) < 0f)
        {
            polygon.Reverse();
        }

        return polygon;
    }

    /// <summary>Offsets a counter-clockwise polygon outward by <paramref name="distance"/> using mitred corners.</summary>
    private static List<Vector2> Offset(List<Vector2> polygon, float distance)
    {
        var result = new List<Vector2>(polygon.Count);

        for (var i = 0; i < polygon.Count; i++)
        {
            var previous = polygon[(i + polygon.Count - 1) % polygon.Count];

            var current = polygon[i];

            var next = polygon[(i + 1) % polygon.Count];

            var e1 = (current - previous).normalized;

            var e2 = (next - current).normalized;

            var n1 = new Vector2(e1.y, -e1.x);

            var n2 = new Vector2(e2.y, -e2.x);

            var shift = (n1 + n2) * (distance / Mathf.Max(0.35f, 1f + Vector2.Dot(n1, n2)));

            result.Add(current + Vector2.ClampMagnitude(shift, distance * 3f));
        }

        return result;
    }

    private static bool PointInPolygon(List<Vector2> polygon, Vector2 point)
    {
        var inside = false;

        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var a = polygon[i];

            var b = polygon[j];

            if (a.y > point.y != b.y > point.y && point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    /// <summary>Ear-clipping triangulation of a counter-clockwise polygon; falls back to a fan if clipping stalls.</summary>
    private static List<int> Triangulate(List<Vector2> polygon)
    {
        var triangles = new List<int>();

        var remaining = new List<int>();

        for (var i = 0; i < polygon.Count; i++)
        {
            remaining.Add(i);
        }

        var guard = polygon.Count * polygon.Count;

        while (remaining.Count > 3 && guard-- > 0)
        {
            var clipped = false;

            for (var i = 0; i < remaining.Count; i++)
            {
                var a = remaining[(i + remaining.Count - 1) % remaining.Count];

                var b = remaining[i];

                var c = remaining[(i + 1) % remaining.Count];

                var cross = Cross(polygon[b] - polygon[a], polygon[c] - polygon[b]);

                if (cross <= 1e-5f || ContainsAny(polygon, remaining, (a, b, c)))
                {
                    continue;
                }

                triangles.Add(a);

                triangles.Add(b);

                triangles.Add(c);

                remaining.RemoveAt(i);

                clipped = true;

                break;
            }

            if (!clipped)
            {
                break;
            }
        }

        for (var i = 1; i + 1 < remaining.Count; i++)
        {
            triangles.Add(remaining[0]);

            triangles.Add(remaining[i]);

            triangles.Add(remaining[i + 1]);
        }

        return triangles;
    }

    private static float Cross(Vector2 u, Vector2 v)
    {
        return u.x * v.y - u.y * v.x;
    }

    private static bool ContainsAny(List<Vector2> polygon, List<int> remaining, (int A, int B, int C) triangle)
    {
        var a = polygon[triangle.A];

        var b = polygon[triangle.B];

        var c = polygon[triangle.C];

        foreach (var index in remaining)
        {
            if (index == triangle.A || index == triangle.B || index == triangle.C)
            {
                continue;
            }

            var p = polygon[index];

            if (Cross(b - a, p - a) >= 0f && Cross(c - b, p - b) >= 0f && Cross(a - c, p - c) >= 0f)
            {
                return true;
            }
        }

        return false;
    }
}
