#nullable enable
using System.Collections.Generic;
using System.Linq;
using HTownSim.EditorTools.GapKit.Models;
using HTownSim.EditorTools.GapKit.Services;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HTownSim.EditorTools.GapKit
{
    /// <summary>
    /// Covers the stretches of the map that are neither a lot nor a street with open ground. The city's ground is one sheet of asphalt, so
    /// where the City data has no lots (a bayou bank, a rail yard, the freeway's right of way, the edge of the data) it reads as a vast
    /// empty road. A cell is open land when it is on the ground sheet, a good way from every street centre line and from every lot.
    /// Runs of cells along a row become one rectangle each; the whole thing is one mesh. Menu: HTownSim > Gap. Remove takes it out.
    /// </summary>
    public static class AddOpenLand
    {
        private const string CityRoot = "MidtownReal";

        private const string GroupName = "OpenLand";

        private const string MeshPath = "Assets/HTownSim/Generated/OpenLand.asset";

        private const string StreetData = "Assets/HTownSim/Data/CityStreets.txt";

        private const string GroundMaterial = "GroundVacant";

        /// <summary>The map window (scene metres) and the cell size.</summary>
        private const float MinX = -2250f;

        private const float MaxX = 690f;

        private const float MinZ = -4740f;

        private const float MaxZ = 1575f;

        private const float Cell = 4f;

        /// <summary>A cell is open land only when it is at least this far from a street centre line (major streets get more) and from a lot's edge.</summary>
        private const float LocalClearance = 12f;

        private const float MajorClearance = 16f;

        private const float LotClearance = 6f;

        /// <summary>The surface the ground sheet is made of, its height, and the open land's height above it (metres).</summary>
        private const string GroundSheet = "Streets";

        private const float OpenHeight = 0.03f;

        /// <summary>How many metres of ground one texture tile covers.</summary>
        private const float TileSize = 8f;

        [MenuItem("HTownSim/Gap/Fill Open Land")]
        private static void FillFromMenu()
        {
            Debug.Log(Apply());
        }

        [MenuItem("HTownSim/Gap/Remove Open Land")]
        private static void RemoveFromMenu()
        {
            Debug.Log(Revert());
        }

        /// <summary>Fills the open ground (replacing any made before) and saves the scene.</summary>
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

            Physics.SyncTransforms();

            var lots = new GapParcelIndex(AuditMapAlignment.LoadWorldParcels(root.transform));

            var streets = StreetReader.Read(StreetData);

            var major = new StreetDistanceIndex(streets.Where(line => line.IsMajor));

            var local = new StreetDistanceIndex(streets);

            var columns = Mathf.CeilToInt((MaxX - MinX) / Cell);

            var builder = new GapMeshBuilder();

            var rectangles = 0;

            var cells = 0;

            for (var row = 0; row * Cell + MinZ < MaxZ; row++)
            {
                var z = MinZ + (row * Cell) + (Cell / 2f);

                var runStart = -1;

                for (var column = 0; column <= columns; column++)
                {
                    var open = column < columns && IsOpen(new Vector2(MinX + (column * Cell) + (Cell / 2f), z), lots, major, local);

                    if (open)
                    {
                        cells++;

                        if (runStart < 0)
                        {
                            runStart = column;
                        }

                        continue;
                    }

                    if (runStart >= 0)
                    {
                        AddRun(builder, MinX + (runStart * Cell), MinX + (column * Cell), z - (Cell / 2f));

                        rectangles++;

                        runStart = -1;
                    }
                }
            }

            var mesh = builder.Build("OpenLand", out var keys);

            AssetDatabase.CreateAsset(mesh, MeshPath);

            var go = new GameObject(GroupName, typeof(MeshFilter), typeof(MeshRenderer));

            go.transform.SetParent(root.transform, false);

            go.GetComponent<MeshFilter>().sharedMesh = mesh;

            go.GetComponent<MeshRenderer>().sharedMaterials = keys.Select(GapMaterials.Load).ToArray();

            AssetDatabase.SaveAssets();

            GapMaterials.Reset();

            EditorSceneManager.MarkSceneDirty(root.scene);

            EditorSceneManager.SaveScene(root.scene);

            return "Open land: " + cells + " cells (" + (cells * Cell * Cell / 10000f).ToString("0.0") + " ha) in " + rectangles + " rectangles.";
        }

        /// <summary>Removes the open land and saves the scene.</summary>
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

            return "Open land removed.";
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

        /// <summary>True when the cell's middle and corners are on the ground sheet and clear of every street and lot.</summary>
        private static bool IsOpen(Vector2 centre, GapParcelIndex lots, StreetDistanceIndex major, StreetDistanceIndex local)
        {
            if (local.Nearest(centre) < LocalClearance || major.Nearest(centre) < MajorClearance)
            {
                return false;
            }

            if (!Physics.Raycast(new Vector3(centre.x, 60f, centre.y), Vector3.down, out var hit, 120f) || hit.collider.name != GroundSheet)
            {
                return false;
            }

            return !lots.Near(centre).Any(lot => GapSidewalk.Inside(lot.Polygon, centre) || GapSidewalk.DistanceOutside(lot.Polygon, centre) <= LotClearance + (Cell / 2f));
        }

        private static void AddRun(GapMeshBuilder builder, float x0, float x1, float z0)
        {
            var z1 = z0 + Cell;

            builder.Quad(
                GroundMaterial,
                new[]
                {
                    new Vector3(x0, OpenHeight, z0), new Vector3(x0, OpenHeight, z1), new Vector3(x1, OpenHeight, z1), new Vector3(x1, OpenHeight, z0),
                },
                new[]
                {
                    new Vector2(x0 / TileSize, z0 / TileSize), new Vector2(x0 / TileSize, z1 / TileSize), new Vector2(x1 / TileSize, z1 / TileSize), new Vector2(x1 / TileSize, z0 / TileSize),
                },
                Vector3.up);
        }
    }
}
