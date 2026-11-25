#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HTownSim.EditorTools.GapKit.Models;
using HTownSim.EditorTools.GapKit.Services;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HTownSim.EditorTools.GapKit
{
    /// <summary>
    /// Gives the gap regions the street furniture the neighborhoods have: a street tree in front of every second building (picked from the
    /// same weighted prefab list), a street lamp and a prop (hydrant, bin, bench, parking meter ...) in front of one building in six, live
    /// oaks in parks, and stall lines with parked cars on vacant parking lots. Runs after <see cref="AddGapBuildings"/>, reading the
    /// buildings it left in the scene. Menu: HTownSim > Gap. Replaces its own earlier output.
    /// </summary>
    public static class AddGapStreetDetail
    {
        private const string CityRoot = "MidtownReal";

        private const string TreesName = "GapStreetTrees";

        private const string PropsName = "GapStreetProps";

        private const string LampsName = "GapStreetLamps";

        private const string ParkedName = "GapParkedCars";

        private const string StallsName = "GapStallLines";

        private const string TreeFolder = "Assets/SimplePoly City - Low Poly Assets/Prefab/Natures/";

        private const string PropFolder = "Assets/POLYGON city pack/Prefabs/Props/";

        private static readonly string[] TreeNames = { "Natures_Big Tree", "Natures_Cube Tree", "Natures_Fir Tree" };

        /// <summary>How many of each street tree the original city has, so the gaps get the same mix.</summary>
        private static readonly float[] TreeWeights = { 1463f, 538f, 295f };

        private static readonly string[] PropNames = { "Bin prefab", "Hydrant prefab", "Mail_box prefab", "parking meter prefab", "phone booth prefab", "Bench 2 prefab", "Bus stop prefab" };

        private static readonly float[] PropWeights = { 44f, 56f, 15f, 38f, 6f, 22f, 8f };

        /// <summary>Heights, metres: the lot top, the sidewalk top and a parked car's base.</summary>
        private const float LotTop = 0.215f;

        private const float CurbTop = 0.2f;

        private const float CarBase = 0.22f;

        /// <summary>The trees' spacing in a park, metres, and the most park trees one gap gets.</summary>
        private const float ParkSpacing = 11f;

        /// <summary>How far a park tree may be moved from its grid spot, metres.</summary>
        private const float ParkJitter = 4f;

        private const int MaximumParkTrees = 3000;

        [MenuItem("HTownSim/Gap/Add Street Detail")]
        private static void AddFromMenu()
        {
            Debug.Log(Apply());
        }

        [MenuItem("HTownSim/Gap/Remove Street Detail")]
        private static void RemoveFromMenu()
        {
            Debug.Log(Revert());
        }

        /// <summary>Adds the street furniture to every gap (replacing any made before) and saves the scene.</summary>
        public static string Apply()
        {
            if (EditorApplication.isPlaying)
            {
                return "Stop Play Mode first.";
            }

            var root = GameObject.Find(CityRoot);

            if (root == null)
            {
                return "No " + CityRoot + " in the open scene.";
            }

            Physics.SyncTransforms();

            var report = new StringBuilder();

            foreach (var area in GapArea.All)
            {
                var container = root.transform.Find(area.Container);

                report.AppendLine(container == null ? area.Name + ": no " + area.Container + " in the scene." : Detail(area, container));
            }

            EditorSceneManager.MarkSceneDirty(root.scene);

            EditorSceneManager.SaveScene(root.scene);

            return report.ToString();
        }

        /// <summary>Removes everything Apply made and saves the scene.</summary>
        public static string Revert()
        {
            if (EditorApplication.isPlaying)
            {
                return "Stop Play Mode first.";
            }

            var root = GameObject.Find(CityRoot);

            if (root == null)
            {
                return "No " + CityRoot + " in the open scene.";
            }

            foreach (var area in GapArea.All)
            {
                var container = root.transform.Find(area.Container);

                if (container != null)
                {
                    RemoveOld(container);
                }

                AssetDatabase.DeleteAsset(StallPath(area));
            }

            EditorSceneManager.MarkSceneDirty(root.scene);

            EditorSceneManager.SaveScene(root.scene);

            return "Gap street detail removed.";
        }

        private static string Detail(GapArea area, Transform container)
        {
            var buildings = container.Find("GapBuildings");

            if (buildings == null)
            {
                return area.Name + ": no GapBuildings (run Build Gap Buildings first).";
            }

            RemoveOld(container);

            var parcels = GapParcelReader.Read(area.DataPath).ToDictionary(parcel => parcel.Index);

            var scene = new SceneRequest(container, parcels);

            var roots = new Roots(container);

            var assets = new Assets();

            var counts = new Counts();

            foreach (Transform building in buildings)
            {
                Streetscape(building, scene, roots, assets, counts);
            }

            VacantLots(area, scene, roots, assets, counts);

            if (area == GapArea.South)
            {
                CorridorPark(roots, assets, counts);
            }

            AssetDatabase.SaveAssets();

            return area.Name + ": " + counts.Trees + " street trees, " + counts.Lamps + " lamps, " + counts.Props + " props, " + counts.ParkTrees + " park trees, "
                + counts.Cars + " parked cars on " + counts.Lots + " vacant lots.";
        }

        /// <summary>The trees, lamps and props in front of one building (the same rule BuildRealMidtown's Streetscape uses).</summary>
        private static void Streetscape(Transform building, SceneRequest scene, Roots roots, Assets assets, Counts counts)
        {
            if (!int.TryParse(building.name.Substring(1), out var index) || !scene.Parcels.TryGetValue(index, out var parcel))
            {
                return;
            }

            var box = building.GetComponent<BoxCollider>();

            if (box == null)
            {
                return;
            }

            var front = building.forward;

            var facing = new Vector2(front.x, front.z).normalized;

            var centre = new Vector2(building.position.x, building.position.z);

            var edge = centre + (facing * ((box.size.z / 2f) + 1.8f + (GapSidewalk.Width * 0.6f)));

            var random = new System.Random(index);

            if (index % 2 == 0 && box.size.x >= 8f)
            {
                if (GapSidewalk.OnLotOrSidewalk(parcel, edge))
                {
                    PlaceTree(roots.Trees, new TreeRequest(edge, random, assets));

                    counts.Trees++;
                }
            }
            else if (index % 6 == 1)
            {
                if (GapSidewalk.OnLotOrSidewalk(parcel, edge))
                {
                    PlaceLamp(roots.Lamps, edge, facing, assets);

                    counts.Lamps++;
                }

                var along = new Vector2(facing.y, facing.x);

                var propAt = edge + (along * 2.5f);

                if (GapSidewalk.OnLotOrSidewalk(parcel, propAt) && PlaceProp(roots.Props, propAt, random, assets))
                {
                    counts.Props++;
                }
            }
        }

        /// <summary>Park trees and stall lines with parked cars, for the gap's own lots that have no building.</summary>
        private static void VacantLots(GapArea area, SceneRequest scene, Roots roots, Assets assets, Counts counts)
        {
            Object? stallAsset = null;

            var parkPoints = new List<Vector2>();

            foreach (var parcel in scene.Parcels.Values)
            {
                if (parcel.Floors != 0 || parcel.MeasuredFloors != 0)
                {
                    continue;
                }

                var centre = new Vector2(parcel.Centre[0], parcel.Centre[1]);

                if (!InThisGap(centre, scene.Container))
                {
                    continue;
                }

                if (parcel.LandUse.StartsWith("X", System.StringComparison.Ordinal) && parcel.Area >= 700)
                {
                    ParkCandidates(parcel, parkPoints);

                    continue;
                }

                if (parcel.Area < 250 || parcel.LandUse.StartsWith("A", System.StringComparison.Ordinal) || parcel.LandUse.StartsWith("C", System.StringComparison.Ordinal))
                {
                    continue;
                }

                if (ParkingLot(parcel, roots, assets, counts, out var mesh) && mesh != null)
                {
                    if (stallAsset == null)
                    {
                        AssetDatabase.CreateAsset(mesh, StallPath(area));

                        stallAsset = mesh;
                    }
                    else
                    {
                        AssetDatabase.AddObjectToAsset(mesh, stallAsset);
                    }

                    counts.Lots++;
                }
            }

            PlaceParkTrees(parkPoints, roots, assets, counts);
        }

        /// <summary>The Hermann Park corridor's ground (the south gap's park slab, Midtown's frame) and the grid and density of the trees scattered on it.</summary>
        private static readonly Rect CorridorParkRect = new Rect(-450f, -3300f, 1140f, 890f);

        private const float CorridorSpacing = 15f;

        /// <summary>Trees are planted where this noise exceeds the threshold, so they come in groves with open meadow between.</summary>
        private const float GroveScale = 0.006f;

        private const float GroveThreshold = 0.48f;

        /// <summary>Trees keep this far from a street (metres) and from a building (the radius checked).</summary>
        private const float RoadClearance = 11f;

        private const float BuildingClearance = 4f;

        private const string RoadsPath = "Assets/HTownSim/Data/SouthRoads.txt";

        private const string OsmPath = "Assets/HTownSim/Data/Osm_MedCenter.txt";

        /// <summary>Scatters trees in groves over the Hermann Park meadow, clear of streets and buildings.</summary>
        private static void CorridorPark(Roots roots, Assets assets, Counts counts)
        {
            var roads = OsmMedCenterReader.ReadRoadSegments(OsmPath);

            roads.AddRange(OsmMedCenterReader.ReadCorridorRoads(RoadsPath));

            var random = new System.Random(4242);

            for (var x = CorridorParkRect.xMin; x < CorridorParkRect.xMax; x += CorridorSpacing)
            {
                for (var z = CorridorParkRect.yMin; z < CorridorParkRect.yMax; z += CorridorSpacing)
                {
                    var point = new Vector2(x + (((float)random.NextDouble() - 0.5f) * CorridorSpacing), z + (((float)random.NextDouble() - 0.5f) * CorridorSpacing));

                    if (Mathf.PerlinNoise(point.x * GroveScale, point.y * GroveScale) < GroveThreshold || counts.ParkTrees >= 2 * MaximumParkTrees)
                    {
                        continue;
                    }

                    if (NearRoad(point, roads) || Physics.CheckSphere(new Vector3(point.x, 7f, point.y), BuildingClearance + 1f, ~0, QueryTriggerInteraction.Ignore))
                    {
                        continue;
                    }

                    PlaceTree(roots.Trees, new TreeRequest(point, random, assets));

                    counts.ParkTrees++;
                }
            }
        }

        private static bool NearRoad(Vector2 point, List<(Vector2 A, Vector2 B)> roads)
        {
            foreach (var (a, b) in roads)
            {
                var edge = b - a;

                var length = edge.sqrMagnitude;

                var t = length < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(point - a, edge) / length);

                if ((point - (a + (edge * t))).sqrMagnitude < RoadClearance * RoadClearance)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>True when the topmost ground at a point belongs to this gap's container (so the lot is the gap's own, not Midtown's or a neighborhood's).</summary>
        private static bool InThisGap(Vector2 point, Transform container)
        {
            var hits = Physics.RaycastAll(new Vector3(point.x, 2f, point.y), Vector3.down, 4f, ~0, QueryTriggerInteraction.Ignore);

            if (hits.Length == 0)
            {
                return false;
            }

            var top = hits.OrderByDescending(hit => hit.point.y).First();

            return top.collider.transform.IsChildOf(container);
        }

        /// <summary>Candidate tree spots in a park: a loose grid, each spot nudged so the trees do not stand in rows.</summary>
        private static void ParkCandidates(GapParcel parcel, List<Vector2> points)
        {
            var bounds = parcel.Bounds;

            var random = new System.Random(parcel.Index);

            for (var x = bounds[0] + 4f; x <= bounds[2] - 3f; x += ParkSpacing)
            {
                for (var z = bounds[1] + 4f; z <= bounds[3] - 3f; z += ParkSpacing)
                {
                    var spot = new Vector2(x + (((float)random.NextDouble() - 0.5f) * 2f * ParkJitter), z + (((float)random.NextDouble() - 0.5f) * 2f * ParkJitter));

                    if (GapSidewalk.Inside(parcel.Polygon, spot))
                    {
                        points.Add(spot);
                    }
                }
            }
        }

        /// <summary>Plants the park trees, thinned evenly over every park when there are more spots than the cap, so no park is left bare.</summary>
        private static void PlaceParkTrees(List<Vector2> points, Roots roots, Assets assets, Counts counts)
        {
            var keep = Mathf.Min(1f, MaximumParkTrees / (float)Mathf.Max(1, points.Count));

            var random = new System.Random(points.Count);

            foreach (var point in points)
            {
                if (random.NextDouble() <= keep)
                {
                    PlaceTree(roots.Trees, new TreeRequest(point, random, assets));

                    counts.ParkTrees++;
                }
            }
        }

        /// <summary>Stall lines (one mesh for the lot) and parked cars on a vacant parking lot; false when the lot is not rectangular or too small.</summary>
        private static bool ParkingLot(GapParcel parcel, Roots roots, Assets assets, Counts counts, out Mesh? mesh)
        {
            mesh = null;

            var b = parcel.Bounds;

            var width = b[2] - b[0] - 2f;

            var depth = b[3] - b[1] - 2f;

            var boundsArea = (b[2] - b[0]) * (b[3] - b[1]);

            if (width < 8f || depth < 8f || parcel.Area < boundsArea * 0.75f)
            {
                return false;
            }

            var random = new System.Random(parcel.Index);

            var rows = depth >= 16f ? new[] { b[1] + 4.5f, b[3] - 4.5f } : new[] { parcel.Centre[1] };

            var builder = new GapMeshBuilder();

            var line = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };

            foreach (var z in rows)
            {
                for (var x = b[0] + 2.4f; x < b[2] - 1.2f; x += 2.8f)
                {
                    var left = x - 1.4f;

                    builder.Quad(
                        "PaintWhite",
                        new[] { new Vector3(left, LotTop + 0.01f, z - 2.5f), new Vector3(left + 0.1f, LotTop + 0.01f, z - 2.5f), new Vector3(left + 0.1f, LotTop + 0.01f, z + 2.5f), new Vector3(left, LotTop + 0.01f, z + 2.5f) },
                        line,
                        Vector3.up);

                    if (random.NextDouble() < 0.55 && assets.Cars.Count > 0)
                    {
                        PlaceCar(roots.Cars, new Vector2(x, z), random, assets);

                        counts.Cars++;
                    }
                }
            }

            mesh = builder.Build("Stalls_" + parcel.Index, out var keys);

            var go = new GameObject("Stalls" + parcel.Index, typeof(MeshFilter), typeof(MeshRenderer));

            go.transform.SetParent(roots.Stalls, false);

            go.GetComponent<MeshFilter>().sharedMesh = mesh;

            go.GetComponent<MeshRenderer>().sharedMaterials = keys.Select(GapMaterials.Load).ToArray();

            return true;
        }

        private static void PlaceTree(Transform parent, TreeRequest request)
        {
            var prefab = request.Assets.Trees[Pick(TreeWeights, request.Random)];

            if (prefab == null)
            {
                return;
            }

            var tree = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);

            tree.transform.localPosition = new Vector3(request.Point.x, LotTop + 0.01f, request.Point.y);

            tree.transform.localRotation = Quaternion.Euler(0f, (float)request.Random.NextDouble() * 360f, 0f);

            tree.transform.localScale = Vector3.one * (1.15f + ((float)request.Random.NextDouble() * 0.22f));
        }

        private static bool PlaceProp(Transform parent, Vector2 point, System.Random random, Assets assets)
        {
            var prefab = assets.Props[Pick(PropWeights, random)];

            if (prefab == null)
            {
                return false;
            }

            var prop = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);

            prop.transform.localPosition = new Vector3(point.x, CurbTop, point.y);

            prop.transform.localRotation = Quaternion.Euler(0f, 270f, 0f);

            return true;
        }

        private static void PlaceLamp(Transform parent, Vector2 point, Vector2 facing, Assets assets)
        {
            var lamp = new GameObject("Lamp", typeof(MeshFilter), typeof(MeshRenderer));

            lamp.transform.SetParent(parent, false);

            lamp.transform.localPosition = new Vector3(point.x, CurbTop, point.y);

            lamp.transform.localRotation = Quaternion.LookRotation(new Vector3(facing.x, 0f, facing.y));

            lamp.GetComponent<MeshFilter>().sharedMesh = assets.Lamp;

            lamp.GetComponent<MeshRenderer>().sharedMaterials = GapLampMesh.Materials.Select(GapMaterials.Load).ToArray();
        }

        private static void PlaceCar(Transform parent, Vector2 point, System.Random random, Assets assets)
        {
            var template = assets.Cars[random.Next(assets.Cars.Count)];

            var car = Object.Instantiate(template, parent);

            car.name = "ParkedCar";

            var yaw = (random.NextDouble() < 0.5 ? 0f : 180f) + (((float)random.NextDouble() - 0.5f) * 3f);

            car.transform.localPosition = new Vector3(point.x, CarBase, point.y);

            car.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        }

        private static int Pick(float[] weights, System.Random random)
        {
            var roll = (float)random.NextDouble() * weights.Sum();

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

        private static void RemoveOld(Transform container)
        {
            foreach (var name in new[] { TreesName, PropsName, LampsName, ParkedName, StallsName })
            {
                var old = container.Find(name);

                if (old != null)
                {
                    Object.DestroyImmediate(old.gameObject);
                }
            }
        }

        private static string StallPath(GapArea area)
        {
            return "Assets/HTownSim/Generated/GapStalls_" + area.Name + ".asset";
        }

        private sealed class Counts
        {
            internal int Trees { get; set; }

            internal int Lamps { get; set; }

            internal int Props { get; set; }

            internal int ParkTrees { get; set; }

            internal int Cars { get; set; }

            internal int Lots { get; set; }
        }

        private readonly struct SceneRequest
        {
            internal SceneRequest(Transform container, Dictionary<int, GapParcel> parcels)
            {
                Container = container;

                Parcels = parcels;
            }

            internal Transform Container { get; }

            internal Dictionary<int, GapParcel> Parcels { get; }
        }

        private readonly struct TreeRequest
        {
            internal TreeRequest(Vector2 point, System.Random random, Assets assets)
            {
                Point = point;

                Random = random;

                Assets = assets;
            }

            internal Vector2 Point { get; }

            internal System.Random Random { get; }

            internal Assets Assets { get; }
        }

        /// <summary>The groups the furniture goes in, under the gap's container.</summary>
        private sealed class Roots
        {
            internal Roots(Transform container)
            {
                Trees = Group(container, TreesName);

                Props = Group(container, PropsName);

                Lamps = Group(container, LampsName);

                Cars = Group(container, ParkedName);

                Stalls = Group(container, StallsName);
            }

            internal Transform Trees { get; }

            internal Transform Props { get; }

            internal Transform Lamps { get; }

            internal Transform Cars { get; }

            internal Transform Stalls { get; }

            private static Transform Group(Transform container, string name)
            {
                var group = new GameObject(name).transform;

                group.SetParent(container, false);

                return group;
            }
        }

        /// <summary>The prefabs, the lamp mesh and the parked-car models, loaded once per area.</summary>
        private sealed class Assets
        {
            internal Assets()
            {
                Trees = TreeNames.Select(name => AssetDatabase.LoadAssetAtPath<GameObject>(TreeFolder + name + ".prefab")).ToArray();

                Props = PropNames.Select(name => AssetDatabase.LoadAssetAtPath<GameObject>(PropFolder + name + ".prefab")).ToArray();

                Lamp = GapLampMesh.Get();

                Cars = FindCarTemplates();
            }

            internal GameObject?[] Trees { get; }

            internal GameObject?[] Props { get; }

            internal Mesh Lamp { get; }

            /// <summary>One example of each parked-car model already in the city (Midtown's ParkedCars group).</summary>
            internal List<GameObject> Cars { get; }

            private static List<GameObject> FindCarTemplates()
            {
                var templates = new List<GameObject>();

                var parked = GameObject.Find("ParkedCars");

                if (parked == null)
                {
                    return templates;
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

                    if (seen.Add(source != null ? AssetDatabase.GetAssetPath(source) : model.name))
                    {
                        templates.Add(car.gameObject);
                    }
                }

                return templates;
            }
        }
    }
}
