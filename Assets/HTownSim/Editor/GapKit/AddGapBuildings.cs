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
    /// Replaces the plain placeholder boxes in the gap regions with real buildings: houses, shops, apartments and towers with textured
    /// walls, window facades and roofs, one mesh per building on the lot's own angle and facing its street. The boxes are hidden, not
    /// deleted, so Revert brings them back. Menu: HTownSim > Gap. Refuses to run in Play Mode and saves the scene when it is done.
    /// </summary>
    public static class AddGapBuildings
    {
        /// <summary>The root of the generated city in the scene.</summary>
        private const string CityRoot = "MidtownReal";

        /// <summary>The group the buildings go in, inside each gap's container.</summary>
        private const string BuildingsName = "GapBuildings";

        /// <summary>Where the generated meshes are kept (one asset per gap).</summary>
        private const string MeshFolder = "Assets/HTownSim/Generated";

        /// <summary>A placeholder counts as a lot's when it is this near the lot's centre, in metres.</summary>
        private const float MatchDistance = 3f;

        [MenuItem("HTownSim/Gap/Build Gap Buildings")]
        private static void BuildFromMenu()
        {
            Debug.Log(Apply());
        }

        [MenuItem("HTownSim/Gap/Remove Gap Buildings")]
        private static void RemoveFromMenu()
        {
            Debug.Log(Revert());
        }

        /// <summary>Builds the buildings of every gap (replacing any made before) and saves the scene.</summary>
        public static string Apply()
        {
            return Apply(GapArea.All.Select(area => area.Name).ToArray(), int.MaxValue);
        }

        /// <summary>Builds the named gaps, at most <paramref name="limit"/> buildings each (for a quick look), and saves the scene.</summary>
        public static string Apply(string[] names, int limit)
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

            var report = new StringBuilder();

            foreach (var area in GapArea.All.Where(area => names.Contains(area.Name)))
            {
                report.AppendLine(BuildArea(new AreaRequest(area, root.transform, limit)));
            }

            GapMaterials.Reset();

            EditorSceneManager.MarkSceneDirty(root.scene);

            EditorSceneManager.SaveScene(root.scene);

            return report.ToString();
        }

        /// <summary>Removes the buildings of every gap and shows the placeholder boxes again.</summary>
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

                if (container == null)
                {
                    continue;
                }

                var old = container.Find(BuildingsName);

                if (old != null)
                {
                    Object.DestroyImmediate(old.gameObject);
                }

                container.Find(area.Placeholders)?.gameObject.SetActive(true);

                AssetDatabase.DeleteAsset(MeshPath(area));
            }

            EditorSceneManager.MarkSceneDirty(root.scene);

            EditorSceneManager.SaveScene(root.scene);

            return "Gap buildings removed; the placeholder boxes are back.";
        }

        /// <summary>Counts why lots that had a placeholder box get no building (for tuning the planner).</summary>
        public static string Diagnose()
        {
            var root = GameObject.Find(CityRoot);

            var area = GapArea.Southwest;

            var placeholders = root.transform.Find(area.Container).Find(area.Placeholders);

            var anchors = PlaceholderCentres(placeholders);

            var parcels = GapParcelReader.Read(area.DataPath);

            var index = new GapParcelIndex(parcels);

            var withBox = 0;

            var built = 0;

            var small = new List<int>();

            foreach (var parcel in parcels)
            {
                if (!anchors.Near(parcel, MatchDistance))
                {
                    continue;
                }

                withBox++;

                if (GapPlanner.Plan(parcel, index) is null)
                {
                    small.Add(parcel.Area);
                }
                else
                {
                    built++;
                }
            }

            small.Sort();

            return "lots with a box: " + withBox + ", built: " + built + ", unbuilt: " + small.Count + " (area median " + (small.Count > 0 ? small[small.Count / 2] : 0) + " m2, max " + (small.Count > 0 ? small[small.Count - 1] : 0) + ")";
        }

        private static string BuildArea(AreaRequest request)
        {
            var area = request.Area;

            var container = request.Root.Find(area.Container);

            if (container == null)
            {
                return area.Name + ": no " + area.Container + " in the scene (run the gap placeholder pass first).";
            }

            var placeholders = container.Find(area.Placeholders);

            if (placeholders == null)
            {
                return area.Name + ": no " + area.Placeholders + " in the scene.";
            }

            var old = container.Find(BuildingsName);

            if (old != null)
            {
                Object.DestroyImmediate(old.gameObject);
            }

            var anchors = PlaceholderCentres(placeholders);

            var parcels = GapParcelReader.Read(area.DataPath);

            var index = new GapParcelIndex(parcels);

            var group = new GameObject(BuildingsName).transform;

            group.SetParent(container, false);

            AssetDatabase.DeleteAsset(MeshPath(area));

            var counts = new Dictionary<GapStyle, int>();

            var made = 0;

            Object? meshAsset = null;

            foreach (var parcel in parcels)
            {
                if (made >= request.Limit)
                {
                    break;
                }

                var plan = GapPlanner.Plan(parcel, index);

                if (plan is null || !anchors.Near(plan.Parcel, MatchDistance))
                {
                    continue;
                }

                var mesh = GapBuildingMesher.Build(plan, out var keys);

                if (meshAsset == null)
                {
                    AssetDatabase.CreateAsset(mesh, MeshPath(area));

                    meshAsset = mesh;
                }
                else
                {
                    AssetDatabase.AddObjectToAsset(mesh, meshAsset);
                }

                GapBuildingPlacer.Place(group, plan, mesh, keys);

                counts[plan.Style] = counts.TryGetValue(plan.Style, out var n) ? n + 1 : 1;

                made++;
            }

            // Hidden, not deleted: Revert shows them again.
            placeholders.gameObject.SetActive(false);

            AssetDatabase.SaveAssets();

            return area.Name + ": " + made + " buildings from " + parcels.Count + " lots (" + string.Join(", ", counts.Select(pair => pair.Value + " " + pair.Key.ToString().ToLowerInvariant())) + "); " + placeholders.childCount + " placeholder boxes hidden.";
        }

        private static string MeshPath(GapArea area)
        {
            return MeshFolder + "/GapBuildings_" + area.Name + ".asset";
        }

        /// <summary>The (x, z) centres of the placeholder boxes, to tell which lots the earlier pass built on.</summary>
        private static Anchors PlaceholderCentres(Transform placeholders)
        {
            var points = new List<Vector2>();

            foreach (Transform box in placeholders)
            {
                points.Add(new Vector2(box.position.x, box.position.z));
            }

            return new Anchors(points);
        }

        private readonly struct AreaRequest
        {
            internal AreaRequest(GapArea area, Transform root, int limit)
            {
                Area = area;

                Root = root;

                Limit = limit;
            }

            internal GapArea Area { get; }

            internal Transform Root { get; }

            internal int Limit { get; }
        }

        /// <summary>A grid over the placeholder centres, to find one near a point.</summary>
        private sealed class Anchors
        {
            private const float Cell = 20f;

            private readonly Dictionary<long, List<Vector2>> _cells = new Dictionary<long, List<Vector2>>();

            internal Anchors(IEnumerable<Vector2> points)
            {
                foreach (var point in points)
                {
                    var key = Key(Mathf.FloorToInt(point.x / Cell), Mathf.FloorToInt(point.y / Cell));

                    if (!_cells.TryGetValue(key, out var list))
                    {
                        list = new List<Vector2>();

                        _cells[key] = list;
                    }

                    list.Add(point);
                }
            }

            /// <summary>True when a placeholder box stands within <paramref name="distance"/> of the lot's centre.</summary>
            internal bool Near(GapParcel parcel, float distance)
            {
                if (!OrientedRectFinder.TryFind(parcel.Polygon, out var rect))
                {
                    return false;
                }

                var cx = Mathf.FloorToInt(rect.Centre.x / Cell);

                var cz = Mathf.FloorToInt(rect.Centre.y / Cell);

                for (var x = cx - 1; x <= cx + 1; x++)
                {
                    for (var z = cz - 1; z <= cz + 1; z++)
                    {
                        if (_cells.TryGetValue(Key(x, z), out var list) && list.Any(point => (point - rect.Centre).sqrMagnitude <= distance * distance))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            private static long Key(int x, int z)
            {
                return ((long)x << 32) ^ (uint)z;
            }
        }
    }
}
