#nullable enable
using System.Collections.Generic;
using System.Linq;
using HTownSim.EditorTools.GapKit.Services;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HTownSim.EditorTools.GapKit
{
    /// <summary>
    /// Paints the Medical Center's streets the way Midtown's are: a double yellow center line (two 12 cm strips 30 cm apart) along every
    /// arterial, collector and local street of the OpenStreetMap data. The line is left out within a few metres of a junction, so
    /// intersections stay clean. One mesh for the whole area. Menu: HTownSim > Med Center. Remove takes it out.
    /// </summary>
    public static class AddMedCenterMarkings
    {
        private const string CityRoot = "MidtownReal";

        private const string GroupName = "MedCenterMarkings";

        private const string DataPath = "Assets/HTownSim/Data/Osm_MedCenter.txt";

        private const string MeshPath = "Assets/HTownSim/Generated/MedCenterMarkings.asset";

        private const string PaintMaterial = "PaintYellow";

        /// <summary>The street classes that get a center line (service roads, tracks and paths do not).</summary>
        private static readonly string[] Marked =
        {
            "primary", "primary_link", "secondary", "secondary_link", "tertiary", "tertiary_link", "unclassified", "residential",
        };

        /// <summary>The paint's height: just above the road surface (BuildRealMidtown's RoadTop is 0.06).</summary>
        private const float PaintHeight = 0.075f;

        /// <summary>Strip width, and half the gap between the two strips, metres.</summary>
        private const float StripWidth = 0.12f;

        private const float StripOffset = 0.15f;

        /// <summary>The line is cut into pieces this long, and a piece within this distance of a junction is left out.</summary>
        private const float PieceLength = 4f;

        private const float JunctionClearance = 9f;

        /// <summary>A road end this close to another road is a junction.</summary>
        private const float JunctionSnap = 3.5f;

        [MenuItem("HTownSim/Med Center/Paint Streets")]
        private static void PaintFromMenu()
        {
            Debug.Log(Apply());
        }

        [MenuItem("HTownSim/Med Center/Remove Street Paint")]
        private static void RemoveFromMenu()
        {
            Debug.Log(Revert());
        }

        /// <summary>Paints the center lines (replacing any made before) and saves the scene.</summary>
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

            Remove(root.transform);

            var roads = OsmMedCenterReader.ReadRoadLines(DataPath, Marked);

            var junctions = FindJunctions(roads);

            var builder = new GapMeshBuilder();

            var painted = 0;

            foreach (var (_, points) in roads)
            {
                painted += Paint(builder, points, junctions);
            }

            var mesh = builder.Build("MedCenterMarkings", out var keys);

            AssetDatabase.CreateAsset(mesh, MeshPath);

            var go = new GameObject(GroupName, typeof(MeshFilter), typeof(MeshRenderer));

            go.transform.SetParent(root.transform, false);

            go.GetComponent<MeshFilter>().sharedMesh = mesh;

            go.GetComponent<MeshRenderer>().sharedMaterials = keys.Select(GapMaterials.Load).ToArray();

            AssetDatabase.SaveAssets();

            GapMaterials.Reset();

            EditorSceneManager.MarkSceneDirty(root.scene);

            EditorSceneManager.SaveScene(root.scene);

            return "Med Center street paint: " + painted + " line pieces on " + roads.Count + " streets, " + junctions.Count + " junctions kept clear.";
        }

        /// <summary>Removes the street paint and saves the scene.</summary>
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

            Remove(root.transform);

            EditorSceneManager.MarkSceneDirty(root.scene);

            EditorSceneManager.SaveScene(root.scene);

            return "Med Center street paint removed.";
        }

        private static void Remove(Transform root)
        {
            var old = root.Find(GroupName);

            if (old != null)
            {
                Object.DestroyImmediate(old.gameObject);
            }

            AssetDatabase.DeleteAsset(MeshPath);
        }

        /// <summary>The points where a street begins or ends on another street (the end of a side street meeting a through street).</summary>
        private static List<Vector2> FindJunctions(List<(string Class, List<Vector2> Points)> roads)
        {
            var junctions = new List<Vector2>();

            for (var i = 0; i < roads.Count; i++)
            {
                var points = roads[i].Points;

                foreach (var end in new[] { points[0], points[points.Count - 1] })
                {
                    for (var j = 0; j < roads.Count; j++)
                    {
                        if (j != i && DistanceToLine(end, roads[j].Points) <= JunctionSnap)
                        {
                            junctions.Add(end);

                            break;
                        }
                    }
                }
            }

            return junctions;
        }

        /// <summary>Paints both strips along one street in pieces, leaving out the pieces near a junction; returns how many pieces.</summary>
        private static int Paint(GapMeshBuilder builder, List<Vector2> points, List<Vector2> junctions)
        {
            var pieces = 0;

            for (var i = 0; i + 1 < points.Count; i++)
            {
                var a = points[i];

                var b = points[i + 1];

                var length = Vector2.Distance(a, b);

                if (length < 0.5f)
                {
                    continue;
                }

                var direction = (b - a) / length;

                var side = new Vector2(-direction.y, direction.x);

                var count = Mathf.Max(1, Mathf.CeilToInt(length / PieceLength));

                for (var k = 0; k < count; k++)
                {
                    var from = Vector2.Lerp(a, b, (float)k / count);

                    var to = Vector2.Lerp(a, b, (float)(k + 1) / count);

                    var middle = (from + to) / 2f;

                    if (junctions.Any(junction => (junction - middle).sqrMagnitude < JunctionClearance * JunctionClearance))
                    {
                        continue;
                    }

                    foreach (var offset in new[] { -StripOffset, StripOffset })
                    {
                        Strip(builder, from + (side * offset), to + (side * offset), side);
                    }

                    pieces++;
                }
            }

            return pieces;
        }

        private static void Strip(GapMeshBuilder builder, Vector2 from, Vector2 to, Vector2 side)
        {
            var half = side * (StripWidth / 2f);

            builder.Quad(
                PaintMaterial,
                new[]
                {
                    new Vector3(from.x - half.x, PaintHeight, from.y - half.y),
                    new Vector3(from.x + half.x, PaintHeight, from.y + half.y),
                    new Vector3(to.x + half.x, PaintHeight, to.y + half.y),
                    new Vector3(to.x - half.x, PaintHeight, to.y - half.y),
                },
                new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up },
                Vector3.up);
        }

        private static float DistanceToLine(Vector2 point, List<Vector2> line)
        {
            var best = float.MaxValue;

            for (var i = 0; i + 1 < line.Count; i++)
            {
                var edge = line[i + 1] - line[i];

                var length = edge.sqrMagnitude;

                var t = length < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(point - line[i], edge) / length);

                best = Mathf.Min(best, Vector2.Distance(point, line[i] + (edge * t)));
            }

            return best;
        }
    }
}
