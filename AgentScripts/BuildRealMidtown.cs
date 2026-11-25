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

    private const string ExpandedDataPath = "Assets/HTownSim/Data/MidtownExpanded.txt";

    /// <summary>The original playable window in the scene frame (metres, station geocode at the origin).</summary>
    private const float OldMinX = -520f;

    private const float OldMaxX = 560f;

    private const float OldMinZ = -450f;

    private const float OldMaxZ = 620f;

    /// <summary>The expanded window: one block west of Louisiana St, four blocks north of Fire Station 8 (past Bell St). Fire Station 16 lies outside it and is built as its own neighborhood (ExpandNeighborhood).</summary>
    private const float NewMinX = -845f;

    private const float NewMaxX = 560f;

    private const float NewMinZ = -450f;

    private const float NewMaxZ = 1575f;

    /// <summary>The window currently being built; <see cref="Build"/> uses the old one and <see cref="Expand"/> the new one.</summary>
    private static float MinX = OldMinX;

    private static float MaxX = OldMaxX;

    private static float MinZ = OldMinZ;

    private static float MaxZ = OldMaxZ;

    private const float GroundMargin = 80f;

    /// <summary>How far a building footprint may reach past its lot's bounds, in metres.</summary>
    private const float FootprintSlack = 0.5f;

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

    /// <summary>True while <see cref="Expand"/> adds to the existing city instead of rebuilding it.</summary>
    private static bool _expanding;

    /// <summary>Suffix added to the sections created by an expansion so a rerun can remove them.</summary>
    private static string _suffix = string.Empty;

    /// <summary>Yaw of the street grid's own frame relative to the scene axes (radians); zero for the original city.</summary>
    private static float _frameYaw;

    private static Quaternion _yawRot = Quaternion.identity;

    /// <summary>When set, <see cref="Make"/> turns each piece by the frame yaw (markings, crosswalks and signals follow the real streets).</summary>
    private static bool _framed;

    /// <summary>Added to building indexes during an expansion so names and random seeds do not repeat the original city's.</summary>
    private static int _indexBase;

    /// <summary>The current parcel's lot plus sidewalk outline, used to keep trees and lamps off the roadway.</summary>
    private static List<Vector2>? _currentOuter;

    /// <summary>The parcel that holds Fire Station 8, if the data has one.</summary>
    private static Parcel? _station8Parcel;

    /// <summary>The parcel that holds Fire Station 16, if the data has one.</summary>
    private static Parcel? _station16Parcel;

    /// <summary>True while a neighborhood (a district with its own street-grid angle) is built: the Midtown window checks do not apply.</summary>
    private static bool _neighborhood;

    /// <summary>The street whose intersections get crosswalks and signals (Elgin St in Midtown, Richmond Ave in the Station 16 neighborhood).</summary>
    private static string _signalStreet = "ELGIN ST";

    /// <summary>While a neighborhood's own TrafficSystem is written: that component, the yaw its root adds to the street frame and the world rectangle its cars stay in.</summary>
    private static Component? _trafficOverride;

    private static float _trafficExtraYaw;

    private static Rect _trafficWorldRect;

    private static Transform? _treeRoot;

    private static Transform? _propRoot;

    private static Transform? _parkedRoot;

    private static readonly System.Random PlaceRandom = new System.Random(8008);

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

        /// <summary>Street centre in the grid's own frame (NaN when the data has none, then <see cref="c"/> applies).</summary>
        public float cl = float.NaN;

        /// <summary>First and last coordinate along the street where it is paved and continuous (NaN when unknown).</summary>
        public float e0 = float.NaN;

        public float e1 = float.NaN;
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

        public StationData? station8;

        public StationData? station16;

        public float frameYaw;

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

        ResetState();

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

        var incident = BuildBuildings(window)!;

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


    // ---------------------------------------------------------------- expansion

    /// <summary>Compiles the script and reports the expansion window without changing anything.</summary>
    public static string Check()
    {
        return "BuildRealMidtown compiled; expansion window x " + NewMinX + ".." + NewMaxX + ", z " + NewMinZ + ".." + NewMaxZ;
    }

    private const string ExpansionSuffix = "_Exp";

    private const string TreeFolder = "Assets/SimplePoly City - Low Poly Assets/Prefab/Natures/";

    private const string PropFolder = "Assets/POLYGON city pack/Prefabs/Props/";

    private static readonly string[] TreeNames = { "Natures_Big Tree", "Natures_Cube Tree", "Natures_Fir Tree" };

    /// <summary>How many of each street tree the original city has, so the new blocks get the same mix.</summary>
    private static readonly float[] TreeWeights = { 1463f, 538f, 295f };

    private static readonly string[] PropNames = { "Bin prefab", "Hydrant prefab", "Mail_box prefab", "parking meter prefab", "phone booth prefab", "Bench 2 prefab", "Bus stop prefab" };

    private static readonly float[] PropWeights = { 44f, 56f, 15f, 38f, 6f, 22f, 8f };

    private static GameObject?[] _treePrefabs = new GameObject?[0];

    private static GameObject?[] _propPrefabs = new GameObject?[0];

    private static List<GameObject> _parkedTemplates = new List<GameObject>();

    /// <summary>Footprint centre of the Station 7 model relative to its root, used to drop a copy onto Station 8's lot.</summary>
    private static readonly Vector3 StationModelCentre = new Vector3(4f, 0f, -67.63f);

    private static void ResetState()
    {
        MinX = OldMinX;

        MaxX = OldMaxX;

        MinZ = OldMinZ;

        MaxZ = OldMaxZ;

        _expanding = false;

        _suffix = string.Empty;

        _frameYaw = 0f;

        _yawRot = Quaternion.identity;

        _framed = false;

        _indexBase = 0;

        _currentOuter = null;

        _station8Parcel = null;

        _station16Parcel = null;

        _neighborhood = false;

        _signalStreet = "ELGIN ST";

        _trafficOverride = null;

        _trafficExtraYaw = 0f;

        _treeRoot = null;

        _propRoot = null;

        _parkedRoot = null;
    }

    private static bool IsStationYard(Parcel parcel)
    {
        if ((_station8Parcel != null && parcel == _station8Parcel) || (_station16Parcel != null && parcel == _station16Parcel))
        {
            return true;
        }

        return Vector2.Distance(new Vector2(parcel.c[0], parcel.c[1]), StationParcelCentre) < 4f;
    }

    private static bool InOldWindow(float x, float z)
    {
        if (_neighborhood)
        {
            return false;
        }

        return x >= OldMinX && x <= OldMaxX && z >= OldMinZ && z <= OldMaxZ;
    }

    /// <summary>True when the point lies on the current parcel's lot or sidewalk, so a tree or lamp there is not in the roadway.</summary>
    private static bool OnSidewalk(Vector3 position)
    {
        return _currentOuter != null && PointInPolygon(_currentOuter, new Vector2(position.x, position.z));
    }

    private static int Pick(float[] weights)
    {
        var total = 0f;

        foreach (var weight in weights)
        {
            total += weight;
        }

        var roll = (float)PlaceRandom.NextDouble() * total;

        for (var i = 0; i < weights.Length; i++)
        {
            roll -= weights[i];

            if (roll <= 0f)
            {
                return i;
            }
        }

        return weights.Length - 1;
    }

    private static GameObject?[] LoadPrefabs(string folder, string[] names)
    {
        var result = new GameObject?[names.Length];

        for (var i = 0; i < names.Length; i++)
        {
            result[i] = AssetDatabase.LoadAssetAtPath<GameObject>(folder + names[i] + ".prefab");
        }

        return result;
    }

    /// <summary>Loads the tree and prop prefabs and finds one example of every parked-car model in the existing city.</summary>
    private static void LoadPlacementAssets()
    {
        _treePrefabs = LoadPrefabs(TreeFolder, TreeNames);

        _propPrefabs = LoadPrefabs(PropFolder, PropNames);

        _parkedTemplates = new List<GameObject>();

        var parked = GameObject.Find("ParkedCars");

        if (parked == null)
        {
            return;
        }

        var seen = new HashSet<string>();

        foreach (Transform car in parked.transform)
        {
            if (car.childCount == 0)
            {
                continue;
            }

            var model = car.GetChild(0).gameObject;

            var source = PrefabUtility.GetCorrespondingObjectFromSource(model);

            var key = source != null ? AssetDatabase.GetAssetPath(source) : model.name;

            if (seen.Add(key))
            {
                _parkedTemplates.Add(car.gameObject);
            }
        }
    }

    private static void PlaceTree(Vector3 position)
    {
        if (_treeRoot == null || !OnSidewalk(position))
        {
            return;
        }

        var prefab = _treePrefabs[Pick(TreeWeights)];

        if (prefab == null)
        {
            return;
        }

        var tree = (GameObject)PrefabUtility.InstantiatePrefab(prefab, _treeRoot);

        tree.transform.SetPositionAndRotation(new Vector3(position.x, CurbTop + 0.01f, position.z), Quaternion.Euler(0f, (float)PlaceRandom.NextDouble() * 360f, 0f));

        tree.transform.localScale = Vector3.one * (1.15f + (float)PlaceRandom.NextDouble() * 0.22f);
    }

    private static void PlaceProp(Vector3 edge, Vector3 facing)
    {
        if (_propRoot == null)
        {
            return;
        }

        var prefab = _propPrefabs[Pick(PropWeights)];

        var along = new Vector3(facing.z, 0f, facing.x);

        var position = edge + along * 2.5f;

        if (prefab == null || !OnSidewalk(position))
        {
            return;
        }

        var prop = (GameObject)PrefabUtility.InstantiatePrefab(prefab, _propRoot);

        prop.transform.SetPositionAndRotation(new Vector3(position.x, CurbTop, position.z), Quaternion.Euler(0f, 270f, 0f));
    }

    private static void PlaceParkedCar(Vector3 position)
    {
        if (_parkedRoot == null || _parkedTemplates.Count == 0)
        {
            return;
        }

        var template = _parkedTemplates[PlaceRandom.Next(_parkedTemplates.Count)];

        var car = Object.Instantiate(template, _parkedRoot);

        car.name = "ParkedCar";

        var yaw = (PlaceRandom.NextDouble() < 0.5 ? 0f : 180f) + ((float)PlaceRandom.NextDouble() - 0.5f) * 3f;

        car.transform.SetPositionAndRotation(new Vector3(position.x, 0.22f, position.z), Quaternion.Euler(0f, yaw, 0f));
    }

    /// <summary>
    /// Adds the blocks west and north of the original window (one block west of Louisiana St, and four blocks north of Fire
    /// Station 8) to the existing city without rebuilding it. New ground, lots, buildings, trees, props and parked cars go
    /// into sections ending in _Exp; the street markings, crosswalks and signals are rebuilt for the whole window along the
    /// real street frame; the bounds, dispatch sites and traffic are updated in place. Safe to run more than once.
    /// </summary>
    public static string Expand()
    {
        if (EditorApplication.isPlaying)
        {
            return "Exit Play Mode first.";
        }

        if (!File.Exists(ExpandedDataPath))
        {
            return "Missing " + ExpandedDataPath + ". Run: EXPANDED=1 node AgentScripts/PrepareMidtownData.js";
        }

        var root = GameObject.Find(RootName);

        if (root == null)
        {
            return "Build the city first (BuildRealMidtown.Build).";
        }

        try
        {
            return ExpandCity(root);
        }
        finally
        {
            ResetState();
        }
    }

    private static string ExpandCity(GameObject root)
    {
        _data = Parse(File.ReadAllLines(ExpandedDataPath));

        ResetState();

        MinX = NewMinX;

        MaxX = NewMaxX;

        MinZ = NewMinZ;

        MaxZ = NewMaxZ;

        _expanding = true;

        _suffix = ExpansionSuffix;

        _frameYaw = _data.frameYaw * Mathf.Deg2Rad;

        _yawRot = Quaternion.Euler(0f, _data.frameYaw, 0f);

        _indexBase = 100000;

        _root = root.transform;

        RemoveExpansion(root.transform);

        _wallIndex = 1000;

        RoofSlabs.Clear();

        EwRed.Clear();

        EwAmber.Clear();

        EwGreen.Clear();

        NsRed.Clear();

        NsAmber.Clear();

        NsGreen.Clear();

        LoadMaterials();

        var added = new List<Parcel>();

        foreach (var parcel in _data.parcels)
        {
            var x = parcel.c[0];

            var z = parcel.c[1];

            if (x >= MinX && x <= MaxX && z >= MinZ && z <= MaxZ && !InOldWindow(x, z))
            {
                added.Add(parcel);
            }
        }

        _station8Parcel = FindStation8Parcel(added);

        _station16Parcel = FindStationParcel(added, _data.station16);

        BuildExpansionGround();

        BuildParcelSlabs(added);

        _treeRoot = new GameObject("StreetTrees" + _suffix).transform;

        _propRoot = new GameObject("StreetProps" + _suffix).transform;

        _parkedRoot = new GameObject("ParkedCars" + _suffix).transform;

        LoadPlacementAssets();

        BuildBuildings(added, false);

        FlushRoofs();

        BuildRail();

        BuildFreeway(OldMaxZ + GroundMargin);

        // The markings, crosswalks, signals and skyline are rebuilt for the whole window, replacing the originals.
        foreach (var name in new[] { "Markings", "Crosswalks", "Signals", "DowntownSkyline" })
        {
            var old = root.transform.Find(name);

            if (old != null)
            {
                Object.DestroyImmediate(old.gameObject);
            }
        }

        var keepSuffix = _suffix;

        _suffix = string.Empty;

        BuildMarkings();

        BuildCrossings();

        BuildSignals();

        BuildSkyline();

        _suffix = keepSuffix;

        PlaceStation8();

        PlaceStation16();

        UpdateBounds();

        var sites = BuildExpansionSites(added);

        UpdateDispatch(sites);

        var axes = UpdateTraffic();

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());

        AssetDatabase.SaveAssets();

        var station = _station8Parcel == null ? "no Station 8 lot found" : "Station 8 at " + _station8Parcel.n + " " + _station8Parcel.s;

        return "Expanded to x " + MinX + ".." + MaxX + ", z " + MinZ + ".." + MaxZ + ": " + added.Count + " new parcels, " + _parkedRoot.childCount + " parked cars, " + _treeRoot.childCount + " trees, " + _propRoot.childCount + " props, " + sites.Count + " new fire sites, " + axes + "; " + station + ".";
    }

    /// <summary>Removes everything a previous expansion added so the pass can run again.</summary>
    private static void RemoveExpansion(Transform root)
    {
        var doomed = new List<GameObject>();

        foreach (Transform child in root)
        {
            if (child.name.EndsWith(ExpansionSuffix, StringComparison.Ordinal))
            {
                doomed.Add(child.gameObject);
            }
        }

        foreach (var name in new[] { "StreetTrees", "StreetProps", "ParkedCars" })
        {
            var extra = GameObject.Find(name + ExpansionSuffix);

            if (extra != null)
            {
                doomed.Add(extra);
            }
        }

        foreach (var stationName in new[] { "FireStation8", "FireStation16" })
        {
            var station = GameObject.Find(stationName);

            if (station != null)
            {
                doomed.Add(station);
            }
        }

        foreach (var go in doomed)
        {
            Object.DestroyImmediate(go);
        }
    }

    /// <summary>Finds the lot that holds Fire Station 8: by street address first, then the nearest lot to the geocode.</summary>
    private static Parcel? FindStation8Parcel(List<Parcel> parcels)
    {
        return FindStationParcel(parcels, _data.station8);
    }

    /// <summary>Finds the lot that holds a fire station copy (Station 16): by street address first, then the nearest lot to the geocode.</summary>
    private static Parcel? FindStationParcel(List<Parcel> parcels, StationData? station)
    {

        if (station == null)
        {
            return null;
        }

        var space = station.address.IndexOf(' ');

        var number = space > 0 ? station.address.Substring(0, space) : station.address;

        var street = space > 0 ? station.address.Substring(space + 1) : string.Empty;

        Parcel? best = null;

        foreach (var parcel in parcels)
        {
            if (parcel.n == number && string.Equals(parcel.s, street, StringComparison.OrdinalIgnoreCase) && (best == null || parcel.a > best.a))
            {
                best = parcel;
            }
        }

        if (best != null)
        {
            return best;
        }

        var bestDistance = 60f;

        foreach (var parcel in parcels)
        {
            var distance = Vector2.Distance(new Vector2(parcel.c[0], parcel.c[1]), new Vector2(station.x, station.z));

            if (distance < bestDistance)
            {
                bestDistance = distance;

                best = parcel;
            }
        }

        return best;
    }

    /// <summary>Grass base and asphalt for the new area: the asphalt covers only the west, north and south strips so it never overlaps the existing roads.</summary>
    private static void BuildExpansionGround()
    {
        Section("Ground", _root);

        var width = MaxX - MinX;

        var depth = MaxZ - MinZ;

        var centre = new Vector3((MinX + MaxX) / 2f, 0f, (MinZ + MaxZ) / 2f);

        // Just below the original base so the two never fight for the same pixels.
        Make(new Spec("Ground", centre + new Vector3(0f, -0.32f, 0f), new Vector3(width + GroundMargin * 2f, 0.5f, depth + GroundMargin * 2f), _leaf, PrimitiveType.Cube, true));

        var strips = new[]
        {
            new Rect(MinX, MinZ, OldMinX - MinX, MaxZ - MinZ),
            new Rect(OldMinX, OldMaxZ, MaxX - OldMinX, MaxZ - OldMaxZ),
            new Rect(OldMinX, MinZ, MaxX - OldMinX, OldMinZ - MinZ),
        };

        foreach (var strip in strips)
        {
            if (strip.width < 0.1f || strip.height < 0.1f)
            {
                continue;
            }

            Make(new Spec("Streets", new Vector3(strip.center.x, 0f, strip.center.y), new Vector3(strip.width, RoadTop * 2f, strip.height), _asphalt, PrimitiveType.Cube, true));
        }
    }

    /// <summary>Copies the Station 7 model onto Station 8's lot, turned to face the nearest street, and relabels it.</summary>
    private static void PlaceStation8()
    {
        PlaceStationCopy(_station8Parcel, "FireStation8", "8");
    }

    /// <summary>Copies the Station 7 model onto Station 16's lot, turned to face the nearest street, and relabels it.</summary>
    private static void PlaceStation16()
    {
        PlaceStationCopy(_station16Parcel, "FireStation16", "16");
    }

    /// <summary>Copies the Station 7 model onto a station lot, turned to face the nearest street, with its lights removed and its 7s relabelled to the station number.</summary>
    private static void PlaceStationCopy(Parcel? parcel, string name, string number)
    {
        var source = GameObject.Find("Station07");

        if (parcel == null || source == null)
        {
            return;
        }

        var clone = Object.Instantiate(source);

        clone.name = name;

        if (_neighborhood)
        {
            clone.transform.SetParent(_root, true);
        }

        var centre = new Vector2(parcel.c[0], parcel.c[1]);

        var facing = FacingStreet(centre, 32f, 21f);

        var rotation = Quaternion.Euler(0f, Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg, 0f);

        clone.transform.SetPositionAndRotation(new Vector3(centre.x, 0f, centre.y) - rotation * StationModelCentre, rotation);

        foreach (var light in clone.GetComponentsInChildren<Light>(true))
        {
            Object.DestroyImmediate(light.gameObject);
        }

        foreach (var text in clone.GetComponentsInChildren<TMP_Text>(true))
        {
            text.text = text.text.Replace("7", number);
        }
    }

    /// <summary>Moves the invisible walls out to the expanded window.</summary>
    private static void UpdateBounds()
    {
        var bounds = _root.Find("WorldBounds");

        if (bounds == null)
        {
            return;
        }

        const float Thickness = 6f;

        const float Height = 40f;

        const float Overshoot = 12f;

        var width = MaxX - MinX;

        var depth = MaxZ - MinZ;

        var walls = new Dictionary<string, (Vector3 Position, Vector3 Size)>
        {
            { "WallWest", (new Vector3(MinX - Thickness / 2f, Height / 2f, (MinZ + MaxZ) / 2f), new Vector3(Thickness, Height, depth + Overshoot * 2f)) },
            { "WallEast", (new Vector3(MaxX + Thickness / 2f, Height / 2f, (MinZ + MaxZ) / 2f), new Vector3(Thickness, Height, depth + Overshoot * 2f)) },
            { "WallSouth", (new Vector3((MinX + MaxX) / 2f, Height / 2f, MinZ - Thickness / 2f), new Vector3(width + Overshoot * 2f, Height, Thickness)) },
            { "WallNorth", (new Vector3((MinX + MaxX) / 2f, Height / 2f, MaxZ + Thickness / 2f), new Vector3(width + Overshoot * 2f, Height, Thickness)) },
        };

        foreach (var pair in walls)
        {
            var wall = bounds.Find(pair.Key);

            if (wall == null)
            {
                continue;
            }

            wall.position = pair.Value.Position;

            var box = wall.GetComponent<BoxCollider>();

            if (box != null)
            {
                box.size = pair.Value.Size;
            }
        }
    }

    /// <summary>Marks storefronts in the new blocks as extra fire sites for free-roam calls.</summary>
    private static List<(Transform Site, string Street)> BuildExpansionSites(List<Parcel> parcels)
    {
        Section("IncidentSites", _root);

        var sites = new List<(Transform, string)>();

        var chosen = new List<Vector2>();

        var random = new System.Random(78);

        var candidates = new List<Parcel>();

        foreach (var parcel in parcels)
        {
            var inset = parcel.c[0] > MinX + 60f && parcel.c[0] < MaxX - 60f && parcel.c[1] > MinZ + 60f && parcel.c[1] < MaxZ - 60f;

            if (inset && !IsStationYard(parcel) && (parcel.k == "F1" || parcel.k == "F2") && parcel.fb.Length >= 4 && parcel.f > 0 && Floors(parcel) <= 3 && parcel.a >= 150 && parcel.a <= 1600)
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

            if (!clear)
            {
                continue;
            }

            var spec = Describe(parcel, true, 0)[0];

            var go = new GameObject("Site " + parcel.n + " " + parcel.s);

            go.transform.SetParent(_parent, false);

            go.transform.SetPositionAndRotation(FacePoint(spec, 3f, 0.25f), Quaternion.LookRotation(-spec.Facing));

            sites.Add((go.transform, parcel.n + " " + parcel.s));

            chosen.Add(here);
        }

        var result = new List<(Transform Site, string Street)>();

        foreach (var site in sites)
        {
            result.Add((site.Item1, site.Item2));
        }

        return result;
    }

    /// <summary>Appends the new fire sites to the engine's FireResponse and widens its playable bounds.</summary>
    private static void UpdateDispatch(List<(Transform Site, string Street)> added)
    {
        var engine = GameObject.Find("Engine07");

        var response = engine == null ? null : engine.GetComponent("FireResponse");

        if (response == null)
        {
            return;
        }

        var serialized = new SerializedObject(response);

        var siteProperty = serialized.FindProperty("_incidentSites");

        var streetProperty = serialized.FindProperty("_incidentStreets");

        var sites = new List<Object>();

        var streets = new List<string>();

        for (var i = 0; i < siteProperty.arraySize; i++)
        {
            var site = siteProperty.GetArrayElementAtIndex(i).objectReferenceValue;

            if (site != null)
            {
                sites.Add(site);

                streets.Add(i < streetProperty.arraySize ? streetProperty.GetArrayElementAtIndex(i).stringValue : string.Empty);
            }
        }

        foreach (var site in added)
        {
            sites.Add(site.Site);

            streets.Add(site.Street);
        }

        siteProperty.arraySize = sites.Count;

        streetProperty.arraySize = sites.Count;

        for (var i = 0; i < sites.Count; i++)
        {
            siteProperty.GetArrayElementAtIndex(i).objectReferenceValue = sites[i];

            streetProperty.GetArrayElementAtIndex(i).stringValue = streets[i];
        }

        serialized.FindProperty("_boundsMin").vector2Value = new Vector2(MinX, MinZ);

        serialized.FindProperty("_boundsMax").vector2Value = new Vector2(MaxX, MaxZ);

        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>Rewires the traffic system to the expanded street grid (centres and extents in the street frame) and the rebuilt signals.</summary>
    private static string UpdateTraffic()
    {
        var traffic = _trafficOverride == null ? _root.Find("Traffic") : null;

        Component? system = _trafficOverride;

        if (traffic != null)
        {
            foreach (var component in traffic.GetComponents<MonoBehaviour>())
            {
                if (component != null && component.GetType().Name == "TrafficSystem")
                {
                    system = component;
                }
            }
        }

        if (system == null)
        {
            return "no TrafficSystem found";
        }

        var serialized = new SerializedObject(system);

        var nsCentre = new List<float>();

        var nsWidth = new List<float>();

        var nsOneWay = new List<bool>();

        var nsMin = new List<float>();

        var nsMax = new List<float>();

        foreach (var axis in _data.grid.ns)
        {
            var c = AxisC(axis);

            if (c > MinX + 25f && c < MaxX - 25f)
            {
                nsCentre.Add(c);

                nsWidth.Add(axis.w);

                nsOneWay.Add(IsOneWay(axis));

                nsMin.Add(AxisStart(axis, MinZ) + 20f);

                nsMax.Add(AxisEnd(axis, MaxZ) - 20f);
            }
        }

        var ewCentre = new List<float>();

        var ewWidth = new List<float>();

        var ewOneWay = new List<bool>();

        var ewMin = new List<float>();

        var ewMax = new List<float>();

        foreach (var axis in _data.grid.ew)
        {
            var c = AxisC(axis);

            if (c > MinZ + 25f && c < MaxZ - 25f)
            {
                ewCentre.Add(c);

                ewWidth.Add(axis.w);

                ewOneWay.Add(false);

                ewMin.Add(AxisStart(axis, MinX) + 20f);

                ewMax.Add(AxisEnd(axis, MaxX) - 20f);
            }
        }

        SetFloats(serialized, "_nsCentre", nsCentre);

        SetFloats(serialized, "_nsWidth", nsWidth);

        SetBools(serialized, "_nsOneWay", nsOneWay);

        SetFloats(serialized, "_nsMin", nsMin);

        SetFloats(serialized, "_nsMax", nsMax);

        SetFloats(serialized, "_ewCentre", ewCentre);

        SetFloats(serialized, "_ewWidth", ewWidth);

        SetBools(serialized, "_ewOneWay", ewOneWay);

        SetFloats(serialized, "_ewMin", ewMin);

        SetFloats(serialized, "_ewMax", ewMax);

        serialized.FindProperty("_frameYawDegrees").floatValue = _data.frameYaw + _trafficExtraYaw;

        serialized.FindProperty("_boundsMin").vector2Value = _trafficOverride == null ? new Vector2(MinX, MinZ) : _trafficWorldRect.min;

        serialized.FindProperty("_boundsMax").vector2Value = _trafficOverride == null ? new Vector2(MaxX, MaxZ) : _trafficWorldRect.max;

        var elgin = FindAxis(_data.grid.ew, _signalStreet);

        serialized.FindProperty("_signalZ").floatValue = elgin == null ? 0f : AxisC(elgin);

        SetObjects(serialized, "_ewRed", EwRed.ToArray());

        SetObjects(serialized, "_ewAmber", EwAmber.ToArray());

        SetObjects(serialized, "_ewGreen", EwGreen.ToArray());

        SetObjects(serialized, "_nsRed", NsRed.ToArray());

        SetObjects(serialized, "_nsAmber", NsAmber.ToArray());

        SetObjects(serialized, "_nsGreen", NsGreen.ToArray());

        serialized.ApplyModifiedPropertiesWithoutUndo();

        return nsCentre.Count + " north-south and " + ewCentre.Count + " east-west traffic streets";
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

                case "H":
                    if (f[1] == "16")
                    {
                        data.station16 = new StationData { x = F(f[2]), z = F(f[3]), address = f[4] };
                    }
                    else
                    {
                        data.station8 = new StationData { x = F(f[2]), z = F(f[3]), address = f[4] };
                    }

                    break;

                case "Y":
                    data.frameYaw = F(f[1]);

                    break;

                case "G":
                    var axis = new GridAxis { c = F(f[2]), w = F(f[3]), name = f[4] };

                    if (f.Length > 7)
                    {
                        axis.cl = F(f[5]);

                        axis.e0 = F(f[6]);

                        axis.e1 = F(f[7]);
                    }

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

            if (IsStationYard(parcel))
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
    private static Parcel? BuildBuildings(List<Parcel> parcels, bool chooseIncident = true)
    {
        Section("Buildings", _root);

        var buildings = _parent;

        if (_expanding)
        {
            ResolveFootprintOverlaps(parcels);
        }

        var incident = chooseIncident ? ChooseIncidentParcel(parcels) : null;

        var index = _indexBase;

        foreach (var parcel in parcels)
        {
            index++;

            var centre = new Vector2(parcel.c[0], parcel.c[1]);

            if (IsStationYard(parcel))
            {
                continue;
            }

            if (_expanding)
            {
                _currentOuter = Offset(ToCounterClockwise(ToPolygon(parcel.p)), SidewalkWidth);
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

            if (CampusParcels.Contains(parcel) && i > 0)
            {
                // Campus blocks step down from the tower: 60%, 40%, a three-storey wing, 75% of the lot's floors, and so on.
                var steps = new[] { 0.6f, 0.4f, 0.2f, 0.75f };

                own = Mathf.Clamp(Mathf.RoundToInt(floors * steps[i % steps.Length]), 3, floors);
            }

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

        float width = 0f;

        float depth = 0f;

        Vector2 centre = new Vector2(parcel.c[0], parcel.c[1]);

        var placed = false;

        if (hasFootprint)
        {
            // The footprint box can reach well past the lot (the 2018 footprints and the tax lots do not line up, and merged condo lots
            // hold only one unit's outline). Keep it inside the lot: clamp it to the lot's bounds, then pull in any corner that is
            // still outside the lot's outline, so a building never spills into the street or its neighbours.
            var x0 = Mathf.Max(box![0], parcel.b[0] - FootprintSlack);

            var z0 = Mathf.Max(box[1], parcel.b[1] - FootprintSlack);

            var x1 = Mathf.Min(box[2], parcel.b[2] + FootprintSlack);

            var z1 = Mathf.Min(box[3], parcel.b[3] + FootprintSlack);

            if (x1 - x0 < 3f || z1 - z0 < 3f)
            {
                x0 = box[0];

                z0 = box[1];

                x1 = box[2];

                z1 = box[3];
            }

            if (ShrinkIntoLot(ref x0, ref z0, ref x1, ref z1, parcel))
            {
                width = x1 - x0;

                depth = z1 - z0;

                centre = new Vector2((x0 + x1) / 2f, (z0 + z1) / 2f);

                placed = true;
            }
        }

        if (!placed)
        {
            // No usable footprint: a building sized from the lot (1.8 m in from each edge), pulled inside the outline the same way.
            hasFootprint = false;

            var lx0 = parcel.b[0] + 1.8f;

            var lz0 = parcel.b[1] + 1.8f;

            var lx1 = parcel.b[2] - 1.8f;

            var lz1 = parcel.b[3] - 1.8f;

            if (lx1 - lx0 >= 3f && lz1 - lz0 >= 3f && ShrinkIntoLot(ref lx0, ref lz0, ref lx1, ref lz1, parcel))
            {
                width = lx1 - lx0;

                depth = lz1 - lz0;

                centre = new Vector2((lx0 + lx1) / 2f, (lz0 + lz1) / 2f);
            }
            else
            {
                width = 0f;

                depth = 0f;
            }
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
        if (_frameYaw != 0f)
        {
            centre = ToFrame(centre.x, centre.y);
        }

        var bestX = float.MaxValue;

        var signX = 1f;

        foreach (var axis in _data.grid.ns)
        {
            var gap = Mathf.Abs(centre.x - AxisC(axis)) - axis.w / 2f - width / 2f;

            if (gap < bestX)
            {
                bestX = gap;

                signX = AxisC(axis) >= centre.x ? 1f : -1f;
            }
        }

        var bestZ = float.MaxValue;

        var signZ = 1f;

        foreach (var axis in _data.grid.ew)
        {
            var gap = Mathf.Abs(centre.y - AxisC(axis)) - axis.w / 2f - depth / 2f;

            if (gap < bestZ)
            {
                bestZ = gap;

                signZ = AxisC(axis) >= centre.y ? 1f : -1f;
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

            if (_expanding)
            {
                PlaceProp(edge, spec.Facing);
            }
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
        if (_expanding)
        {
            PlaceParkedCar(position);

            return;
        }

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

    // ---------------------------------------------------------------- street frame

    /// <summary>Street centre in the grid frame (the original data has no separate frame value).</summary>
    private static float AxisC(GridAxis axis)
    {
        return float.IsNaN(axis.cl) ? axis.c : axis.cl;
    }

    /// <summary>First coordinate along a street where it is paved and continuous, limited to the window.</summary>
    private static float AxisStart(GridAxis axis, float windowMin)
    {
        return float.IsNaN(axis.e0) ? windowMin : Mathf.Max(axis.e0, windowMin);
    }

    private static float AxisEnd(GridAxis axis, float windowMax)
    {
        return float.IsNaN(axis.e1) ? windowMax : Mathf.Min(axis.e1, windowMax);
    }

    private static bool IsOneWay(GridAxis axis)
    {
        return string.Equals(axis.name, "AUSTIN ST", StringComparison.OrdinalIgnoreCase) || string.Equals(axis.name, "LA BRANCH ST", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Turns a position in the street grid frame (u across the north-south streets, v along them) into a scene position.</summary>
    private static Vector3 FromFrame(float u, float v, float y)
    {
        var sin = Mathf.Sin(_frameYaw);

        var cos = Mathf.Cos(_frameYaw);

        return new Vector3(u * cos + v * sin, y, -u * sin + v * cos);
    }

    /// <summary>The inverse of <see cref="FromFrame"/>.</summary>
    private static Vector2 ToFrame(float x, float z)
    {
        var sin = Mathf.Sin(_frameYaw);

        var cos = Mathf.Cos(_frameYaw);

        return new Vector2(x * cos - z * sin, x * sin + z * cos);
    }

    /// <summary>The cross streets that really cross <paramref name="axis"/> (their paved run covers it).</summary>
    private static GridAxis[] CrossingsOf(GridAxis axis, GridAxis[] others, float windowMin, float windowMax)
    {
        var list = new List<GridAxis>();

        var c = AxisC(axis);

        foreach (var other in others)
        {
            var oc = AxisC(other);

            if (oc > windowMin && oc < windowMax && c >= AxisStart(other, float.MinValue) - 1f && c <= AxisEnd(other, float.MaxValue) + 1f)
            {
                list.Add(other);
            }
        }

        return list.ToArray();
    }

    /// <summary>Paints centre lines on every street of the detected grid, leaving gaps at the intersections. Lines follow each street frame.</summary>
    private static void BuildMarkings()
    {
        Section("Markings", _root);

        _framed = true;

        foreach (var axis in _data.grid.ns)
        {
            var c = AxisC(axis);

            if (c < MinX + 5f || c > MaxX - 5f)
            {
                continue;
            }

            var crossings = CrossingsOf(axis, _data.grid.ew, MinZ, MaxZ);

            foreach (var span in Spans(AxisStart(axis, MinZ), AxisEnd(axis, MaxZ), crossings))
            {
                MarkStreet(c, span, true, IsOneWay(axis));
            }
        }

        foreach (var axis in _data.grid.ew)
        {
            var c = AxisC(axis);

            if (c < MinZ + 5f || c > MaxZ - 5f)
            {
                continue;
            }

            var crossings = CrossingsOf(axis, _data.grid.ns, MinX, MaxX);

            foreach (var span in Spans(AxisStart(axis, MinX), AxisEnd(axis, MaxX), crossings))
            {
                MarkStreet(c, span, false, false);
            }
        }

        _framed = false;
    }

    /// <summary>Returns the stretches between the crossing streets' road edges.</summary>
    private static List<Vector2> Spans(float from, float to, GridAxis[] crossings)
    {
        var spans = new List<Vector2>();

        var start = from;

        var ordered = new List<GridAxis>(crossings);

        ordered.Sort((p, q) => AxisC(p).CompareTo(AxisC(q)));

        foreach (var crossing in ordered)
        {
            var edge = AxisC(crossing) - crossing.w / 2f;

            if (edge > start && AxisC(crossing) <= to)
            {
                spans.Add(new Vector2(start, edge));
            }

            start = Mathf.Max(start, AxisC(crossing) + crossing.w / 2f);
        }

        if (to > start)
        {
            spans.Add(new Vector2(start, to));
        }

        return spans;
    }

    private static void MarkStreet(float axisC, Vector2 span, bool alongZ, bool oneWay)
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
                Make(new Spec("LaneDash", At(axisC, along, alongZ, MarkingY), Along(0.14f, 3f, alongZ), _white));
            }

            for (var along = span.x + 14f; along < span.y - 6f; along += 40f)
            {
                Arrow(axisC, along, alongZ);
            }

            return;
        }

        foreach (var offset in new[] { -0.15f, 0.15f })
        {
            Make(new Spec("CenterLine", At(axisC + offset, centre, alongZ, MarkingY), Along(0.12f, length, alongZ), _yellow));
        }
    }

    private static void Arrow(float axis, float along, bool alongZ)
    {
        Make(new Spec("ArrowShaft", At(axis, along, alongZ, MarkingY), Along(0.4f, 2.6f, alongZ), _white));

        foreach (var side in new[] { -1f, 1f })
        {
            var head = Make(new Spec("ArrowHead", At(axis + side * 0.49f, along + 0.81f, alongZ, MarkingY), Along(0.4f, 1.4f, alongZ), _white));

            head.transform.rotation = (_framed ? _yawRot : Quaternion.identity) * Quaternion.Euler(0f, -side * 45f, 0f);
        }
    }

    /// <summary>Position on a street: <paramref name="across"/> is the axis coordinate, <paramref name="along"/> runs down the street (grid frame).</summary>
    private static Vector3 At(float across, float along, bool alongZ, float y)
    {
        return alongZ ? FromFrame(across, along, y) : FromFrame(along, across, y);
    }

    private static Vector3 Along(float width, float length, bool alongZ)
    {
        return alongZ ? new Vector3(width, 0.02f, length) : new Vector3(length, 0.02f, width);
    }

    /// <summary>Paints zebra crossings on all four legs of every Elgin Street intersection.</summary>
    private static void BuildCrossings()
    {
        Section("Crosswalks", _root);

        var elgin = FindAxis(_data.grid.ew, _signalStreet);

        if (elgin is null)
        {
            return;
        }

        _framed = true;

        var ec = AxisC(elgin);

        foreach (var ns in _data.grid.ns)
        {
            var nc = AxisC(ns);

            if (nc < MinX + 20f || nc > MaxX - 20f || nc < AxisStart(elgin, float.MinValue) || nc > AxisEnd(elgin, float.MaxValue))
            {
                continue;
            }

            foreach (var side in new[] { -1f, 1f })
            {
                var xLeg = nc + side * (ns.w / 2f + 1.8f);

                for (var v = ec - (elgin.w / 2f - SidewalkWidth) + 0.8f; v <= ec + (elgin.w / 2f - SidewalkWidth) - 0.6f; v += 1.6f)
                {
                    Make(new Spec("Crosswalk", FromFrame(xLeg, v, MarkingY), new Vector3(3f, 0.02f, 0.8f), _white));
                }

                var zLeg = ec + side * (elgin.w / 2f + 1.8f);

                for (var u = nc - (ns.w / 2f - SidewalkWidth) + 0.8f; u <= nc + (ns.w / 2f - SidewalkWidth) - 0.6f; u += 1.6f)
                {
                    Make(new Spec("Crosswalk", FromFrame(u, zLeg, MarkingY), new Vector3(0.8f, 0.02f, 3f), _white));
                }
            }
        }

        _framed = false;
    }

    private static GridAxis? FindAxis(GridAxis[] axes, string name)
    {
        foreach (var axis in axes)
        {
            if (string.Equals(axis.name, name, StringComparison.OrdinalIgnoreCase))
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

        var elgin = FindAxis(_data.grid.ew, _signalStreet);

        if (elgin is null)
        {
            return;
        }

        _framed = true;

        var ec = AxisC(elgin);

        foreach (var ns in _data.grid.ns)
        {
            var nc = AxisC(ns);

            if (nc < MinX + 20f || nc > MaxX - 20f || nc < AxisStart(elgin, float.MinValue) || nc > AxisEnd(elgin, float.MaxValue))
            {
                continue;
            }

            var hx = ns.w / 2f - 1.2f;

            var hz = elgin.w / 2f - 1.2f;

            Mast(new Vector3(nc - hx, 0f, ec - hz), new Vector3(0f, 0f, hz * 2f));

            Mast(new Vector3(nc + hx, 0f, ec + hz), new Vector3(0f, 0f, -hz * 2f));

            Mast(new Vector3(nc + hx, 0f, ec - hz), new Vector3(-hx * 2f, 0f, 0f));
        }

        _framed = false;
    }

    /// <summary>Builds one mast arm. <paramref name="position"/> and <paramref name="arm"/> are in the street grid frame.</summary>
    private static void Mast(Vector3 position, Vector3 arm)
    {
        const float ArmY = 7.6f;

        Vector3 W(Vector3 local)
        {
            return FromFrame(local.x, local.z, local.y);
        }

        Make(new Spec("MastPole", W(position + Vector3.up * 4f), new Vector3(0.35f, 8f, 0.35f), _steel, PrimitiveType.Cylinder, true));

        var alongX = Mathf.Abs(arm.x) > Mathf.Abs(arm.z);

        Make(new Spec("MastArm", W(position + arm / 2f + Vector3.up * ArmY), new Vector3(Mathf.Abs(arm.x) + 0.25f, 0.25f, Mathf.Abs(arm.z) + 0.25f), _steel));

        var faceOffset = alongX ? new Vector3(0f, 0f, 0.27f) : new Vector3(0.27f, 0f, 0f);

        var lensScale = alongX ? new Vector3(0.28f, 0.28f, 0.1f) : new Vector3(0.1f, 0.28f, 0.28f);

        foreach (var fraction in new[] { 0.45f, 0.8f })
        {
            var head = position + arm * fraction + Vector3.up * (ArmY - 0.9f);

            Make(new Spec("SignalHead", W(head), new Vector3(0.5f, 1.4f, 0.5f), _dark));

            foreach (var sign in new[] { -1f, 1f })
            {
                (alongX ? NsRed : EwRed).Add(Make(new Spec("SignalRed", W(head + Vector3.up * 0.45f + faceOffset * sign), lensScale, _lensRedOff)).GetComponent<Renderer>());

                (alongX ? NsAmber : EwAmber).Add(Make(new Spec("SignalAmber", W(head + faceOffset * sign), lensScale, _lensAmberOff)).GetComponent<Renderer>());

                (alongX ? NsGreen : EwGreen).Add(Make(new Spec("SignalGreen", W(head + Vector3.down * 0.45f + faceOffset * sign), lensScale, _lensGreenOff)).GetComponent<Renderer>());
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

                if (a.x < MinX || a.x > MaxX || a.z < MinZ || a.z > MaxZ || Vector3.Distance(a, b) < 0.5f || (_expanding && InOldWindow(a.x, a.z)))
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
    private static void BuildFreeway(float? southOverride = null)
    {
        Section("Freeway", _root);

        const float DeckY = 9f;

        const float DeckWidth = 34f;

        var south = southOverride ?? MinZ - GroundMargin;

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
        if (_expanding)
        {
            PlaceTree(position);

            return;
        }

        Make(new Spec("OakTrunk", position + Vector3.up * 2f, new Vector3(0.6f, 4f, 0.6f), _wood, PrimitiveType.Cylinder, true));

        Make(new Spec("OakCanopy", position + new Vector3(-1.2f, 4.6f, 0f), Vector3.one * 4.6f, _leaf, PrimitiveType.Sphere));

        Make(new Spec("OakCanopy", position + new Vector3(0f, 5.6f, 0f), Vector3.one * 5f, _leaf, PrimitiveType.Sphere));

        Make(new Spec("OakCanopy", position + new Vector3(1.2f, 4.6f, 0f), Vector3.one * 4.6f, _leaf, PrimitiveType.Sphere));
    }

    private static void Lamp(Vector3 position, Vector3 direction)
    {
        if (_expanding && !OnSidewalk(position))
        {
            return;
        }

        Make(new Spec("LampPost", position + Vector3.up * 3.5f, new Vector3(0.25f, 7f, 0.25f), _steel, PrimitiveType.Cylinder, true));

        var armScale = new Vector3(Mathf.Abs(direction.x) * 2.2f + 0.15f, 0.15f, Mathf.Abs(direction.z) * 2.2f + 0.15f);

        Make(new Spec("LampArm", position + direction * 1.1f + Vector3.up * 7f, armScale, _steel));

        Make(new Spec("Lamp", position + direction * 2.1f + Vector3.up * 6.9f, new Vector3(0.7f, 0.2f, 0.7f), _amber));
    }

    private static void Section(string name, Transform parent)
    {
        var go = new GameObject(parent == _root ? name + _suffix : name);

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

        if (_framed)
        {
            go.transform.rotation = _yawRot * go.transform.rotation;
        }

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

    // ------------------------------------------------------------------ neighborhoods

    /// <summary>The street grid of Midtown, in degrees: every other neighborhood root is rotated by the difference to its own grid angle.</summary>
    private const float MidtownGridDegrees = 35.06f;

    /// <summary>A district with its own street-grid angle. It is built in its own frame (so the axis-aligned builders and traffic code work unchanged) and its root is then rotated into the city.</summary>
    private sealed class NeighborhoodDef
    {
        public string Name = string.Empty;

        public string DataPath = string.Empty;

        public float MinX;

        public float MaxX;

        public float MinZ;

        public float MaxZ;

        public string SignalStreet = "ELGIN ST";

        /// <summary>The neighborhood's own street-grid angle in degrees (Midtown's is 35.06); its root is turned by the difference.</summary>
        public float GridDegrees = MidtownGridDegrees;

        /// <summary>The fire station this neighborhood places (16), or 0 for none.</summary>
        public int StationNumber;
    }

    /// <summary>Fire Station 16 (1700 Richmond Ave) and its blocks: Montrose and Hyde Park, whose streets run about 35 degrees off Midtown's.</summary>
    private static readonly NeighborhoodDef Station16Neighborhood = new NeighborhoodDef
    {
        Name = "Station16",
        DataPath = "Assets/HTownSim/Data/Neighborhood_Station16.txt",
        MinX = -3421f,
        MaxX = -2121f,
        MinZ = -1225f,
        MaxZ = 75f,
        SignalStreet = "RICHMOND AVE",
        GridDegrees = 0.1f,
        StationNumber = 16,
    };

    /// <summary>The blocks south of the Midtown window: the same grid as Midtown, so its root is not turned.</summary>
    private static readonly NeighborhoodDef SouthMidtownNeighborhood = new NeighborhoodDef
    {
        Name = "SouthMidtown",
        DataPath = "Assets/HTownSim/Data/Neighborhood_SouthMidtown.txt",
        MinX = -845f,
        MaxX = 560f,
        MinZ = -1450f,
        MaxZ = -450f,
        SignalStreet = "WHEELER ST",
        GridDegrees = 35.06f,
    };

    /// <summary>The Texas Medical Center (Fannin, Holcombe and Bertner), south of the city on Midtown's own grid: the hospitals the ambulances deliver to.</summary>
    private static readonly NeighborhoodDef MedCenterNeighborhood = new NeighborhoodDef
    {
        Name = "MedCenter",
        DataPath = "Assets/HTownSim/Data/Neighborhood_MedCenter.txt",
        MinX = -450f,
        MaxX = 650f,
        MinZ = -4700f,
        MaxZ = -3300f,
        SignalStreet = "FANNIN ST",
        GridDegrees = 35.06f,
    };

    /// <summary>Every neighborhood built so far; the gap pass leaves their lots out and the playable area covers them all.</summary>
    private static readonly NeighborhoodDef[] AllNeighborhoods = { Station16Neighborhood, SouthMidtownNeighborhood, MedCenterNeighborhood };

    /// <summary>Builds the South Midtown neighborhood; can be run again.</summary>
    public static string ExpandSouthMidtown()
    {
        try
        {
            return ExpandNeighborhood(SouthMidtownNeighborhood);
        }
        finally
        {
            ResetState();
        }
    }

    /// <summary>Builds the Texas Medical Center neighborhood; can be run again.</summary>
    public static string ExpandMedCenter()
    {
        try
        {
            return ExpandNeighborhood(MedCenterNeighborhood);
        }
        finally
        {
            ResetState();
        }
    }

    /// <summary>Builds the Station 16 neighborhood (see <see cref="NeighborhoodDef"/>); can be run again, it replaces its own earlier output.</summary>
    public static string ExpandStation16()
    {
        try
        {
            return ExpandNeighborhood(Station16Neighborhood);
        }
        finally
        {
            ResetState();
        }
    }

    private static string ExpandNeighborhood(NeighborhoodDef def)
    {
        if (EditorApplication.isPlaying)
        {
            return "Exit Play Mode first.";
        }

        if (!File.Exists(def.DataPath))
        {
            return "Missing " + def.DataPath + ". Run: NEIGHBORHOOD=station16 node AgentScripts/PrepareMidtownData.js";
        }

        var cityRoot = GameObject.Find(RootName);

        if (cityRoot == null)
        {
            return "Build the city first (BuildRealMidtown.Build).";
        }

        _data = Parse(File.ReadAllLines(def.DataPath));

        ResetState();

        _neighborhood = true;

        _signalStreet = def.SignalStreet;

        MinX = def.MinX;

        MaxX = def.MaxX;

        MinZ = def.MinZ;

        MaxZ = def.MaxZ;

        _expanding = true;

        _suffix = "_" + def.Name;

        _frameYaw = _data.frameYaw * Mathf.Deg2Rad;

        _yawRot = Quaternion.Euler(0f, _data.frameYaw, 0f);

        _indexBase = 200000;

        foreach (var oldName in new[] { "Neighborhood_" + def.Name, "Traffic_" + def.Name, "NeighborhoodFill" })
        {
            var old = cityRoot.transform.Find(oldName);

            if (old != null)
            {
                Object.DestroyImmediate(old.gameObject);
            }
        }

        var container = new GameObject("Neighborhood_" + def.Name).transform;

        container.SetParent(cityRoot.transform, false);

        _root = container;

        RoofSlabs.Clear();

        EwRed.Clear();

        EwAmber.Clear();

        EwGreen.Clear();

        NsRed.Clear();

        NsAmber.Clear();

        NsGreen.Clear();

        LoadMaterials();

        var parcels = new List<Parcel>(_data.parcels);

        if (def.Name == "MedCenter")
        {
            AddHospitalCampuses(parcels);
        }

        _station16Parcel = def.StationNumber == 16 ? FindStationParcel(parcels, _data.station16) : null;

        BuildNeighborhoodGround();

        BuildParcelSlabs(parcels);

        _treeRoot = new GameObject("StreetTrees" + _suffix).transform;

        _treeRoot.SetParent(container, false);

        _propRoot = new GameObject("StreetProps" + _suffix).transform;

        _propRoot.SetParent(container, false);

        _parkedRoot = new GameObject("ParkedCars" + _suffix).transform;

        _parkedRoot.SetParent(container, false);

        LoadPlacementAssets();

        BuildBuildings(parcels, false);

        FlushRoofs();

        BuildMarkings();

        BuildCrossings();

        BuildSignals();

        if (def.StationNumber == 16)
        {
            PlaceStation16();
        }

        var sites = BuildExpansionSites(parcels);

        // Traffic: a copy of the Midtown TrafficSystem that knows this neighborhood's streets, in its own frame plus the root's yaw.
        var psi = -(MidtownGridDegrees - _data.rotationDegrees);

        var worldRect = NeighborhoodWorldRect(def, psi);

        var midtownTraffic = cityRoot.transform.Find("Traffic");

        var trafficNote = "no Midtown TrafficSystem to copy";

        if (midtownTraffic != null)
        {
            var copy = Object.Instantiate(midtownTraffic.gameObject, cityRoot.transform);

            copy.name = "Traffic_" + def.Name;

            foreach (var component in copy.GetComponents<MonoBehaviour>())
            {
                if (component != null && component.GetType().Name == "TrafficSystem")
                {
                    _trafficOverride = component;
                }
            }

            _trafficExtraYaw = psi;

            _trafficWorldRect = worldRect;

            trafficNote = UpdateTraffic();

            _trafficOverride = null;
        }

        // Rotate the finished neighborhood into the city.
        container.rotation = Quaternion.Euler(0f, psi, 0f);

        // The playable area is now the Midtown window plus this neighborhood, with plain ground in the gap between them.
        var union = Rect.MinMaxRect(NewMinX, NewMinZ, NewMaxX, NewMaxZ);

        foreach (var other in AllNeighborhoods)
        {
            var otherRect = NeighborhoodWorldRect(other, -(MidtownGridDegrees - other.GridDegrees));

            union = Rect.MinMaxRect(
                Mathf.Min(union.xMin, otherRect.xMin - 40f),
                Mathf.Min(union.yMin, otherRect.yMin - 40f),
                Mathf.Max(union.xMax, otherRect.xMax + 40f),
                Mathf.Max(union.yMax, otherRect.yMax + 40f));
        }

        _root = cityRoot.transform;

        _suffix = string.Empty;

        Section("NeighborhoodFill", _root);

        Make(new Spec("NeighborhoodFill", new Vector3(union.center.x, -0.35f, union.center.y), new Vector3(union.width + GroundMargin * 2f, 0.5f, union.height + GroundMargin * 2f), _leaf, PrimitiveType.Cube, true));

        MinX = union.xMin;

        MaxX = union.xMax;

        MinZ = union.yMin;

        MaxZ = union.yMax;

        UpdateBounds();

        var siteList = BuildSiteList(sites);

        UpdateDispatch(siteList);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());

        AssetDatabase.SaveAssets();

        return "Neighborhood " + def.Name + ": " + parcels.Count + " parcels, root rotated " + psi.ToString("0.00") + " degrees; world x " + union.xMin.ToString("0") + ".." + union.xMax.ToString("0") + ", z " + union.yMin.ToString("0") + ".." + union.yMax.ToString("0") + "; traffic: " + trafficNote;
    }

    /// <summary>The campus lots of the Medical Center, so their buildings get varied heights (see <see cref="Describe"/>).</summary>
    private static readonly HashSet<Parcel> CampusParcels = new HashSet<Parcel>();

    /// <summary>
    /// The City's tax data carries no floor count for most of the Medical Center's exempt campuses, so they would come out as park, and each
    /// campus is one lot of tens of thousands of square metres. A large lot on a campus street gets tall buildings: floors from the lot area, and a
    /// campus of building blocks (a grid of wings and towers about 70 x 55 m with 20 m between them for drives and plazas), kept inside the lot's
    /// outline and clear of any real footprint the lot already has.
    /// </summary>
    private static void AddHospitalCampuses(List<Parcel> parcels)
    {
        CampusParcels.Clear();

        foreach (var parcel in parcels)
        {
            // Exempt lots of 8,000 to 120,000 m2 in the hospital part of the window (south of z -3500; the zoo and the golf course are bigger, and the
            // streets named for the park are park lots).
            var park = parcel.s.Contains("MACGREGOR") || parcel.s.Contains("HERMANN") || parcel.s.Contains("HERAMANN");

            if (!parcel.k.StartsWith("X", StringComparison.Ordinal) || parcel.a < 8000 || parcel.a > 120000 || parcel.c[1] > -3500f || park)
            {
                continue;
            }

            if (parcel.f == 0)
            {
                parcel.f = Mathf.Clamp(Mathf.RoundToInt(parcel.a / 5000f) + 6, 8, 18);
            }

            var boxes = new List<float[]>();

            for (var i = 0; i + 3 < parcel.fb.Length; i += 4)
            {
                boxes.Add(new[] { parcel.fb[i], parcel.fb[i + 1], parcel.fb[i + 2], parcel.fb[i + 3] });
            }

            var outline = ToPolygon(parcel.p);

            const float CellWidth = 70f;

            const float CellDepth = 55f;

            const float Gap = 20f;

            var generated = 0;

            for (var x = parcel.b[0] + 8f; x + CellWidth <= parcel.b[2] - 8f + 0.01f; x += CellWidth)
            {
                for (var z = parcel.b[1] + 8f; z + CellDepth <= parcel.b[3] - 8f + 0.01f; z += CellDepth)
                {
                    var hash = Mathf.Abs(Mathf.Sin((x * 12.9898f) + (z * 78.233f)) * 43758.5453f) % 1f;

                    if (hash < 0.18f)
                    {
                        continue;
                    }

                    var box = new[] { x + (Gap / 2f), z + (Gap / 2f), x + CellWidth - (Gap / 2f), z + CellDepth - (Gap / 2f) };

                    if (!PointInPolygon(outline, new Vector2((box[0] + box[2]) / 2f, (box[1] + box[3]) / 2f)))
                    {
                        continue;
                    }

                    var clear = true;

                    foreach (var existing in boxes)
                    {
                        if (box[0] < existing[2] + 4f && box[2] > existing[0] - 4f && box[1] < existing[3] + 4f && box[3] > existing[1] - 4f)
                        {
                            clear = false;

                            break;
                        }
                    }

                    if (!clear)
                    {
                        continue;
                    }

                    boxes.Add(box);

                    generated++;
                }
            }

            if (boxes.Count == 0)
            {
                var inset = Mathf.Min(12f, Mathf.Min(parcel.b[2] - parcel.b[0], parcel.b[3] - parcel.b[1]) / 4f);

                boxes.Add(new[] { parcel.b[0] + inset, parcel.b[1] + inset, parcel.b[2] - inset, parcel.b[3] - inset });
            }

            // Largest first: the first box carries the lot's full floor count.
            boxes.Sort((a, b) => ((b[2] - b[0]) * (b[3] - b[1])).CompareTo((a[2] - a[0]) * (a[3] - a[1])));

            var flat = new float[boxes.Count * 4];

            for (var i = 0; i < boxes.Count; i++)
            {
                Array.Copy(boxes[i], 0, flat, i * 4, 4);
            }

            parcel.fb = flat;

            CampusParcels.Add(parcel);
        }
    }

    /// <summary>Sites for FireResponse from the neighborhood's incident site transforms (already rotated into the city).</summary>
    private static List<(Transform Site, string Street)> BuildSiteList(List<(Transform Site, string Street)> sites)
    {
        return sites;
    }

    /// <summary>Grass base and asphalt for the whole neighborhood window; the lot slabs and buildings go on top, so the gaps between lots read as streets.</summary>
    private static void BuildNeighborhoodGround()
    {
        Section("Ground", _root);

        var width = MaxX - MinX;

        var depth = MaxZ - MinZ;

        var centre = new Vector3((MinX + MaxX) / 2f, 0f, (MinZ + MaxZ) / 2f);

        Make(new Spec("Ground", centre + new Vector3(0f, -0.32f, 0f), new Vector3(width + GroundMargin * 2f, 0.5f, depth + GroundMargin * 2f), _leaf, PrimitiveType.Cube, true));

        Make(new Spec("Streets", centre, new Vector3(width, RoadTop * 2f, depth), _asphalt, PrimitiveType.Cube, true));
    }

    /// <summary>The world rectangle (x, z) that covers the neighborhood window after its root is rotated by <paramref name="psi"/> degrees.</summary>
    private static Rect NeighborhoodWorldRect(NeighborhoodDef def, float psi)
    {
        var rotation = Quaternion.Euler(0f, psi, 0f);

        var min = new Vector2(float.MaxValue, float.MaxValue);

        var max = new Vector2(float.MinValue, float.MinValue);

        foreach (var corner in new[] { new Vector2(def.MinX, def.MinZ), new Vector2(def.MaxX, def.MinZ), new Vector2(def.MaxX, def.MaxZ), new Vector2(def.MinX, def.MaxZ) })
        {
            var world = rotation * new Vector3(corner.x, 0f, corner.y);

            min = Vector2.Min(min, new Vector2(world.x, world.z));

            max = Vector2.Max(max, new Vector2(world.x, world.z));
        }

        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    // ------------------------------------------------------------------ gap placeholders

    private const string GapDataPath = "Assets/HTownSim/Data/Gap_Southwest.txt";

    /// <summary>The part of the data window (Midtown's frame) that holds the gap: west of the Midtown window and south of it.</summary>
    private static readonly Rect[] GapGroundRects =
    {
        new Rect(-2310f, -2410f, 1465f, 4110f),
        new Rect(-845f, -2410f, 1405f, 1960f),
    };

    /// <summary>Roads, lots and placeholder building squares for the rectangle between Midtown and the Station 16 neighborhood. Every lot gets its sidewalk and surface, and every built lot a plain box on its own lot angle, to be replaced neighborhood by neighborhood.</summary>
    public static string ExpandGap()
    {
        try
        {
            return ExpandGapCore(GapDataPath, "Gap_Placeholders", "_Gap", 400000, GapGroundRects);
        }
        finally
        {
            ResetState();
        }
    }

    /// <summary>How far the gap placeholder layers are lowered under the built neighborhoods (metres), against z-fighting.</summary>
    private const float GapLayerDrop = 0.012f;

    private const string GapSouthDataPath = "Assets/HTownSim/Data/Gap_South.txt";

    /// <summary>The ground of the south gap (Midtown's frame): everything between the Station 16 and Medical Center neighborhoods and the corridor.</summary>
    private static readonly Rect[] GapSouthGroundRects =
    {
        new Rect(-2310f, -4710f, 1860f, 2310f),
    };

    /// <summary>Hermann Park and the Museum District corridor: grass ground rather than asphalt (the streets are laid over it by <see cref="AddSouthRoads"/>).</summary>
    private static readonly Rect GapSouthParkRect = new Rect(-450f, -3300f, 1140f, 890f);

    /// <summary>The same placeholder pass as <see cref="ExpandGap"/> for the area south of it, down to the Medical Center: lots, sidewalks and building squares from the City's parcels. Can be run again.</summary>
    public static string ExpandGapSouth()
    {
        try
        {
            return ExpandGapCore(GapSouthDataPath, "Gap_South", "_GapS", 500000, GapSouthGroundRects);
        }
        finally
        {
            ResetState();
        }
    }

    private static string ExpandGapCore(string dataPath, string containerName, string suffix, int indexBase, Rect[] groundRects)
    {
        if (EditorApplication.isPlaying)
        {
            return "Exit Play Mode first.";
        }

        if (!File.Exists(dataPath))
        {
            return "Missing " + dataPath + ". Run: NEIGHBORHOOD=gap (or gapsouth) node AgentScripts/PrepareMidtownData.js";
        }

        var cityRoot = GameObject.Find(RootName);

        if (cityRoot == null)
        {
            return "Build the city first (BuildRealMidtown.Build).";
        }

        _data = Parse(File.ReadAllLines(dataPath));

        ResetState();

        _neighborhood = true;

        _expanding = true;

        _suffix = suffix;

        _indexBase = indexBase;

        var old = cityRoot.transform.Find(containerName);

        if (old != null)
        {
            Object.DestroyImmediate(old.gameObject);
        }

        var container = new GameObject(containerName).transform;

        container.SetParent(cityRoot.transform, false);

        // Placeholders sit 1.2 cm below everything else, so where they meet a neighborhood or the upgraded ground the better surface
        // always wins instead of the two flickering (they share the same slab height).
        container.localPosition = new Vector3(0f, -GapLayerDrop, 0f);

        _root = container;

        LoadMaterials();

        // Lots already built elsewhere are left out: the Midtown window, and the Station 16 neighborhood (checked in its own rotated frame).
        var parcels = new List<Parcel>();

        foreach (var parcel in _data.parcels)
        {
            var x = parcel.c[0];

            var z = parcel.c[1];

            if (x >= NewMinX && x <= NewMaxX && z >= NewMinZ && z <= NewMaxZ)
            {
                continue;
            }

            var inNeighborhood = false;

            foreach (var built in AllNeighborhoods)
            {
                var local = Quaternion.Euler(0f, MidtownGridDegrees - built.GridDegrees, 0f) * new Vector3(x, 0f, z);

                if (local.x >= built.MinX && local.x <= built.MaxX && local.z >= built.MinZ && local.z <= built.MaxZ)
                {
                    inNeighborhood = true;

                    break;
                }
            }

            if (!inNeighborhood)
            {
                parcels.Add(parcel);
            }
        }

        Section("Ground", _root);

        foreach (var rect in groundRects)
        {
            // Just under the Midtown and neighborhood asphalt, so where they overlap theirs wins.
            Make(new Spec("Streets", new Vector3(rect.center.x, -0.03f, rect.center.y), new Vector3(rect.width, RoadTop * 2f, rect.height), _asphalt, PrimitiveType.Cube, true));
        }

        if (containerName == "Gap_South")
        {
            Make(new Spec("CorridorPark", new Vector3(GapSouthParkRect.center.x, -0.03f, GapSouthParkRect.center.y), new Vector3(GapSouthParkRect.width, RoadTop * 2f, GapSouthParkRect.height), _park, PrimitiveType.Cube, true));
        }

        BuildParcelSlabs(parcels);

        var boxes = BuildPlaceholderBuildings(parcels);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());

        AssetDatabase.SaveAssets();

        return "Gap: " + parcels.Count + " lots (of " + _data.parcels.Length + " in the data) with sidewalks and surfaces, " + boxes + " placeholder building squares.";
    }

    /// <summary>One plain box per built lot, on the lot's own angle and pulled in from its edges; returns how many.</summary>
    private static int BuildPlaceholderBuildings(List<Parcel> parcels)
    {
        Section("PlaceholderBuildings", _root);

        var material = Mat("GapBuilding", "8E9296");

        var count = 0;

        foreach (var parcel in parcels)
        {
            if ((parcel.f == 0 && parcel.m == 0) || parcel.a < 60 || IsPark(parcel))
            {
                continue;
            }

            if (!OrientedRect(parcel.p, out var centre, out var width, out var depth, out var yaw))
            {
                continue;
            }

            width = Mathf.Clamp(width - 6f, 4f, 120f);

            depth = Mathf.Clamp(depth - 8f, 4f, 120f);

            var height = Mathf.Clamp(Floors(parcel) * FloorHeight, 3.4f, 80f);

            var go = Make(new Spec("Block", new Vector3(centre.x, height / 2f, centre.y), new Vector3(width, height, depth), material, PrimitiveType.Cube, true));

            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            count++;
        }

        return count;
    }

    /// <summary>The smallest rectangle that holds a polygon (flat x, z pairs): its centre, its size along its first axis (width) and across it (depth), and the yaw in degrees that turns a cube's X axis onto that first axis.</summary>
    private static bool OrientedRect(float[] flat, out Vector2 centre, out float width, out float depth, out float yawDegrees)
    {
        centre = Vector2.zero;

        width = 0f;

        depth = 0f;

        yawDegrees = 0f;

        var points = new List<Vector2>();

        for (var i = 0; i + 1 < flat.Length; i += 2)
        {
            points.Add(new Vector2(flat[i], flat[i + 1]));
        }

        if (points.Count < 3)
        {
            return false;
        }

        points.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));

        var lower = new List<Vector2>();

        foreach (var p in points)
        {
            while (lower.Count >= 2 && HullCross(lower[lower.Count - 2], lower[lower.Count - 1], p) <= 0f)
            {
                lower.RemoveAt(lower.Count - 1);
            }

            lower.Add(p);
        }

        var upper = new List<Vector2>();

        for (var i = points.Count - 1; i >= 0; i--)
        {
            var p = points[i];

            while (upper.Count >= 2 && HullCross(upper[upper.Count - 2], upper[upper.Count - 1], p) <= 0f)
            {
                upper.RemoveAt(upper.Count - 1);
            }

            upper.Add(p);
        }

        lower.RemoveAt(lower.Count - 1);

        upper.RemoveAt(upper.Count - 1);

        var hull = new List<Vector2>(lower);

        hull.AddRange(upper);

        if (hull.Count < 3)
        {
            return false;
        }

        var bestArea = float.MaxValue;

        for (var i = 0; i < hull.Count; i++)
        {
            var a = hull[i];

            var b = hull[(i + 1) % hull.Count];

            var angle = Mathf.Atan2(b.y - a.y, b.x - a.x);

            var c = Mathf.Cos(angle);

            var s = Mathf.Sin(angle);

            var minU = float.MaxValue;

            var maxU = float.MinValue;

            var minV = float.MaxValue;

            var maxV = float.MinValue;

            foreach (var q in hull)
            {
                var u = q.x * c + q.y * s;

                var v = -q.x * s + q.y * c;

                minU = Mathf.Min(minU, u);

                maxU = Mathf.Max(maxU, u);

                minV = Mathf.Min(minV, v);

                maxV = Mathf.Max(maxV, v);
            }

            var w = maxU - minU;

            var d = maxV - minV;

            if (w * d < bestArea)
            {
                bestArea = w * d;

                width = w;

                depth = d;

                var cu = (minU + maxU) / 2f;

                var cv = (minV + maxV) / 2f;

                centre = new Vector2(cu * c - cv * s, cu * s + cv * c);

                yawDegrees = -angle * Mathf.Rad2Deg;
            }
        }

        return true;
    }

    private static float HullCross(Vector2 o, Vector2 a, Vector2 b)
    {
        return (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
    }

    // ------------------------------------------------------------------ US 59

    private const string Us59DataPath = "Assets/HTownSim/Data/US59.txt";

    /// <summary>
    /// Adds the south-western run of US 59 (the Southwest Freeway) as raised carriageways along the City's road centre lines, in the
    /// style of the Midtown-edge freeway: a concrete deck with barriers, asphalt, lane paint and columns. Each carriageway line is
    /// one deck, so the two directions stand side by side as they do on the ground. It is scenery (there are no ramps) and the streets
    /// pass underneath. Data: <c>node AgentScripts/ExtractFreeway.js</c>. Can be run again.
    /// </summary>
    public static string AddUs59Freeway()
    {
        try
        {
            return AddUs59FreewayCore(Us59DataPath, "Freeway_US59", "US 59");
        }
        finally
        {
            ResetState();
        }
    }

    private const string I45DataPath = "Assets/HTownSim/Data/I45.txt";

    /// <summary>The placeholder deck that runs straight north from Midtown's edge; the real run replaces it.</summary>
    private const string PlaceholderFreeway = "Freeway_Exp";

    /// <summary>
    /// Adds US 59 from where the Midtown-edge deck stops to its interchange with I-45, then I-45 both ways and US 59 north, each to the
    /// edge of the map, along the City's road centre lines (same decks as <see cref="AddUs59Freeway"/>). The straight placeholder deck
    /// north of Midtown is hidden, since the real run does not follow it. Data: <c>node AgentScripts/ExtractFreeway.js</c>. Can be run again.
    /// </summary>
    public static string AddI45Freeway()
    {
        try
        {
            var result = AddUs59FreewayCore(I45DataPath, "Freeway_I45", "US 59 / I-45");

            var placeholder = GameObject.Find(RootName)?.transform.Find(PlaceholderFreeway);

            if (placeholder != null && placeholder.gameObject.activeSelf)
            {
                placeholder.gameObject.SetActive(false);

                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

                EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            }

            return result;
        }
        finally
        {
            ResetState();
        }
    }

    private static string AddUs59FreewayCore(string dataPath, string containerName, string label)
    {
        if (EditorApplication.isPlaying)
        {
            return "Exit Play Mode first.";
        }

        if (!File.Exists(dataPath))
        {
            return "Missing " + dataPath + ". Run: node AgentScripts/ExtractFreeway.js";
        }

        var cityRoot = GameObject.Find(RootName);

        if (cityRoot == null)
        {
            return "Build the city first (BuildRealMidtown.Build).";
        }

        ResetState();

        var old = cityRoot.transform.Find(containerName);

        if (old != null)
        {
            Object.DestroyImmediate(old.gameObject);
        }

        var container = new GameObject(containerName).transform;

        container.SetParent(cityRoot.transform, false);

        _root = container;

        LoadMaterials();

        Section("Carriageways", _root);

        var segments = 0;

        var lengthTotal = 0f;

        foreach (var line in File.ReadAllLines(dataPath))
        {
            var fields = line.Split('\t');

            if (fields.Length < 3 || fields[0] != "F")
            {
                continue;
            }

            var points = ParsePolygon(fields[2], System.Globalization.CultureInfo.InvariantCulture);

            for (var i = 0; i + 3 < points.Length; i += 2)
            {
                var a = new Vector2(points[i], points[i + 1]);

                var b = new Vector2(points[i + 2], points[i + 3]);

                if (Vector2.Distance(a, b) < 1f)
                {
                    continue;
                }

                BuildFreewaySegment(a, b);

                segments++;

                lengthTotal += Vector2.Distance(a, b);
            }
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());

        AssetDatabase.SaveAssets();

        return label + ": " + segments + " deck segments, " + (lengthTotal / 1000f).ToString("0.0") + " km of carriageway.";
    }

    /// <summary>One straight stretch of carriageway between two points (scene x, z): deck, barriers, asphalt, lane paint and columns.</summary>
    private static void BuildFreewaySegment(Vector2 a, Vector2 b)
    {
        const float DeckY = 9f;

        const float DeckWidth = 16f;

        const float Overlap = 1.5f;

        var delta = b - a;

        var length = delta.magnitude;

        var direction = delta / length;

        var yaw = Mathf.Atan2(direction.x, direction.y) * Mathf.Rad2Deg;

        var rotation = Quaternion.Euler(0f, yaw, 0f);

        // Local +X, in scene x, z.
        var lateral = new Vector2(direction.y, -direction.x);

        GameObject Piece(string name, Vector3 size, float side, float along, float y, Material material, PrimitiveType shape = PrimitiveType.Cube, bool collide = false)
        {
            var position = a + direction * along + lateral * side;

            var go = Make(new Spec(name, new Vector3(position.x, y, position.y), size, material, shape, collide));

            go.transform.rotation = rotation;

            return go;
        }

        var middle = length / 2f;

        Piece("FreewayDeck", new Vector3(DeckWidth, 1.2f, length + Overlap), 0f, middle, DeckY, _concrete, PrimitiveType.Cube, true);

        foreach (var side in new[] { -1f, 1f })
        {
            Piece("FreewayBarrier", new Vector3(0.8f, 1.1f, length + Overlap), side * (DeckWidth / 2f - 0.4f), middle, DeckY + 1.15f, _concrete, PrimitiveType.Cube, true);
        }

        Piece("FreewayAsphalt", new Vector3(DeckWidth - 2f, 0.04f, length + Overlap), 0f, middle, DeckY + 0.62f, _asphalt);

        foreach (var side in new[] { -2.7f, 2.7f })
        {
            for (var along = 4f; along < length; along += 9f)
            {
                Piece("FreewayPaint", new Vector3(0.18f, 0.02f, 4f), side, along, DeckY + 0.66f, _white);
            }
        }

        for (var along = 8f; along < length; along += 24f)
        {
            Piece("FreewayColumn", new Vector3(2.2f, DeckY - 0.6f, 2.2f), 0f, along, DeckY / 2f - 0.3f, _concrete, PrimitiveType.Cylinder, true);

            Piece("FreewayCapBeam", new Vector3(11f, 1.4f, 2.6f), 0f, along, DeckY - 0.9f, _concrete);
        }
    }

    /// <summary>
    /// Where the footprint boxes of two different lots overlap (boxes of rotated or L-shaped buildings, party walls), moves their shared
    /// edge to the middle of the overlap along the axis with the smaller overlap, so neighbours touch instead of passing through each other.
    /// Works on the parcels' footprint bounds in memory; a box is never made smaller than 3 m.
    /// </summary>
    private static void ResolveFootprintOverlaps(List<Parcel> parcels)
    {
        const float MinSize = 3f;

        const float Tolerance = 0.3f;

        var entries = new List<(Parcel Parcel, int Slot)>();

        foreach (var parcel in parcels)
        {
            for (var i = 0; i + 3 < parcel.fb.Length; i += 4)
            {
                entries.Add((parcel, i));
            }
        }

        entries.Sort((a, b) => a.Parcel.fb[a.Slot].CompareTo(b.Parcel.fb[b.Slot]));

        for (var i = 0; i < entries.Count; i++)
        {
            var a = entries[i];

            var af = a.Parcel.fb;

            for (var j = i + 1; j < entries.Count; j++)
            {
                var b = entries[j];

                var bf = b.Parcel.fb;

                if (bf[b.Slot] >= af[a.Slot + 2])
                {
                    break;
                }

                if (a.Parcel == b.Parcel)
                {
                    continue;
                }

                var width = Mathf.Min(af[a.Slot + 2], bf[b.Slot + 2]) - Mathf.Max(af[a.Slot], bf[b.Slot]);

                var depth = Mathf.Min(af[a.Slot + 3], bf[b.Slot + 3]) - Mathf.Max(af[a.Slot + 1], bf[b.Slot + 1]);

                if (width <= Tolerance || depth <= Tolerance)
                {
                    continue;
                }

                // Split along x (indices 0 and 2) when that overlap is the smaller one, else along z (1 and 3).
                var low = width <= depth ? 0 : 1;

                var high = low + 2;

                var mid = (Mathf.Max(af[a.Slot + low], bf[b.Slot + low]) + Mathf.Min(af[a.Slot + high], bf[b.Slot + high])) / 2f;

                var aFirst = af[a.Slot + low] + af[a.Slot + high] <= bf[b.Slot + low] + bf[b.Slot + high];

                var first = aFirst ? af : bf;

                var firstSlot = aFirst ? a.Slot : b.Slot;

                var second = aFirst ? bf : af;

                var secondSlot = aFirst ? b.Slot : a.Slot;

                if (mid - first[firstSlot + low] < MinSize || second[secondSlot + high] - mid < MinSize)
                {
                    continue;
                }

                first[firstSlot + high] = mid;

                second[secondSlot + low] = mid;
            }
        }
    }

    /// <summary>
    /// Pulls a building box in, 0.5 m at a time, until all four of its corners (nudged 0.15 m inward) lie inside the lot's outline.
    /// Returns false when it would have to get smaller than 3 m, in which case the building is not placed from this box.
    /// </summary>
    private static bool ShrinkIntoLot(ref float x0, ref float z0, ref float x1, ref float z1, Parcel parcel)
    {
        var outline = ToPolygon(parcel.p);

        if (outline.Count < 3)
        {
            return true;
        }

        for (var step = 0; step < 80; step++)
        {
            var middleX = (x0 + x1) / 2f;

            var middleZ = (z0 + z1) / 2f;

            var moved = false;

            for (var corner = 0; corner < 4; corner++)
            {
                var west = corner == 0 || corner == 3;

                var south = corner == 0 || corner == 1;

                var cx = west ? x0 : x1;

                var cz = south ? z0 : z1;

                var probe = new Vector2(cx + (cx < middleX ? 0.15f : -0.15f), cz + (cz < middleZ ? 0.15f : -0.15f));

                if (PointInPolygon(outline, probe))
                {
                    continue;
                }

                moved = true;

                if (west)
                {
                    x0 += 0.5f;
                }
                else
                {
                    x1 -= 0.5f;
                }

                if (south)
                {
                    z0 += 0.5f;
                }
                else
                {
                    z1 -= 0.5f;
                }
            }

            if (!moved)
            {
                return true;
            }

            if (x1 - x0 < 3f || z1 - z0 < 3f)
            {
                return false;
            }
        }

        return false;
    }

    // ------------------------------------------------------------------ south roads

    private const string SouthRoadsPath = "Assets/HTownSim/Data/SouthRoads.txt";

    /// <summary>
    /// Streets over the plain ground south of the gap (z below -2410, outside the Medical Center window): asphalt ribbons along the City's
    /// road centre lines, 14 m for major streets and 9 m for local ones. They are flat, with no markings or traffic. Data: <c>node AgentScripts/ExtractSouthRoads.js</c>. Can be run again.
    /// </summary>
    public static string AddSouthRoads()
    {
        try
        {
            return AddSouthRoadsCore();
        }
        finally
        {
            ResetState();
        }
    }

    private static string AddSouthRoadsCore()
    {
        if (EditorApplication.isPlaying)
        {
            return "Exit Play Mode first.";
        }

        var cityRoot = GameObject.Find(RootName);

        if (cityRoot == null || !File.Exists(SouthRoadsPath))
        {
            return "Build the city and run node AgentScripts/ExtractSouthRoads.js first.";
        }

        ResetState();

        var old = cityRoot.transform.Find("Roads_South");

        if (old != null)
        {
            Object.DestroyImmediate(old.gameObject);
        }

        _root = new GameObject("Roads_South").transform;

        _root.SetParent(cityRoot.transform, false);

        LoadMaterials();

        Section("Streets", _root);

        var med = NeighborhoodWorldRect(MedCenterNeighborhood, 0f);

        var segments = 0;

        foreach (var line in File.ReadAllLines(SouthRoadsPath))
        {
            var fields = line.Split('	');

            if (fields.Length < 3 || fields[0] != "R")
            {
                continue;
            }

            var width = fields[1] == "MAJOR" ? 14f : 9f;

            var points = ParsePolygon(fields[2], System.Globalization.CultureInfo.InvariantCulture);

            for (var i = 0; i + 3 < points.Length; i += 2)
            {
                var start = new Vector2(points[i], points[i + 1]);

                var end = new Vector2(points[i + 2], points[i + 3]);

                // Cut the segment into pieces of about 25 m so the Medical Center window can be left out piece by piece.
                var pieces = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(start, end) / 25f));

                for (var j = 0; j < pieces; j++)
                {
                    var a = Vector2.Lerp(start, end, j / (float)pieces);

                    var b = Vector2.Lerp(start, end, (j + 1) / (float)pieces);

                    var middle = (a + b) / 2f;

                    if (middle.y > -2410f || middle.y < -4700f || middle.x < -2250f || middle.x > 690f || med.Contains(middle) || Vector2.Distance(a, b) < 0.5f)
                    {
                        continue;
                    }

                    var direction = (b - a).normalized;

                    var piece = Make(new Spec("Street", new Vector3(middle.x, 0.08f, middle.y), new Vector3(width, 0.06f, Vector2.Distance(a, b) + 0.6f), _asphalt, PrimitiveType.Cube, false));

                    piece.transform.rotation = Quaternion.Euler(0f, Mathf.Atan2(direction.x, direction.y) * Mathf.Rad2Deg, 0f);

                    segments++;
                }
            }
        }

        // The City's centre lines stop in Hermann Park, so the last stretch of Main St to the Medical Center is a straight 16 m road along the
        // Main St axis of the Medical Center window (x -392), so there is a way to drive there.
        var spine = Make(new Spec("MainStSpine", new Vector3(-392f, 0.08f, (-2700f + MedCenterNeighborhood.MaxZ) / 2f), new Vector3(16f, 0.06f, Mathf.Abs(-2700f - MedCenterNeighborhood.MaxZ)), _asphalt, PrimitiveType.Cube, false));

        spine.name = "MainStSpine";

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());

        return "South roads: " + segments + " street segments.";
    }

    // ------------------------------------------------------------------ corridor footprints

    private const string CorridorFootprintsPath = "Assets/HTownSim/Data/Corridor_Footprints.txt";

    /// <summary>
    /// Plain boxes on the 2018 LiDAR footprints of the Hermann Park corridor (Museum District, the zoo, the golf course clubhouse), which lie between the city
    /// and the Medical Center and have no lots of their own. Floors come from the footprint area. Data: <c>node AgentScripts/ExtractCorridorFootprints.js</c>. Can be run again.
    /// </summary>
    public static string AddCorridorFootprints()
    {
        try
        {
            return AddCorridorFootprintsCore();
        }
        finally
        {
            ResetState();
        }
    }

    private static string AddCorridorFootprintsCore()
    {
        if (EditorApplication.isPlaying)
        {
            return "Exit Play Mode first.";
        }

        var cityRoot = GameObject.Find(RootName);

        if (cityRoot == null || !File.Exists(CorridorFootprintsPath))
        {
            return "Build the city and run node AgentScripts/ExtractCorridorFootprints.js first.";
        }

        ResetState();

        var old = cityRoot.transform.Find("Footprints_Corridor");

        if (old != null)
        {
            Object.DestroyImmediate(old.gameObject);
        }

        _root = new GameObject("Footprints_Corridor").transform;

        _root.SetParent(cityRoot.transform, false);

        LoadMaterials();

        Section("Buildings", _root);

        var count = 0;

        foreach (var line in File.ReadAllLines(CorridorFootprintsPath))
        {
            var fields = line.Split('	');

            if (fields.Length < 7 || fields[0] != "B")
            {
                continue;
            }

            var culture = System.Globalization.CultureInfo.InvariantCulture;

            var height = int.Parse(fields[6], culture) * 3.8f;

            var box = Make(new Spec("Footprint", new Vector3(float.Parse(fields[1], culture), height / 2f, float.Parse(fields[2], culture)), new Vector3(float.Parse(fields[3], culture), height, float.Parse(fields[4], culture)), _limestone, PrimitiveType.Cube, true));

            box.transform.rotation = Quaternion.Euler(0f, -float.Parse(fields[5], culture), 0f);

            count++;
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());

        return "Corridor footprints: " + count + " buildings.";
    }
}
