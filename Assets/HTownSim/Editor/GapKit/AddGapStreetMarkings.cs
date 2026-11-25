#nullable enable
using System.Collections.Generic;
using System.Linq;
using HTownSim.EditorTools.GapKit.Models;
using HTownSim.EditorTools.GapKit.Services;
using HTownSim.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HTownSim.EditorTools.GapKit
{
    /// <summary>
    /// Paints the gap streets (the south-west gap and the south gap) the way Midtown's are marked: center lines, zebra crosswalks and stop
    /// bars at junctions, and traffic signals where major streets meet, all along the City's street centre lines. Only streets that
    /// belong to the gap are touched; the parts of the map that have their own marking pass (Midtown, South Midtown, Station 16) are left
    /// alone. The paint and the signal structure are one mesh each, and the lenses are two meshes (one per phase) that
    /// <see cref="GapSignalCycle"/> switches. Menu: HTownSim > Gap. Remove takes it all out.
    /// </summary>
    public static class AddGapStreetMarkings
    {
        private const string CityRoot = "MidtownReal";

        private const string GroupName = "GapStreetMarkings";

        private const string DataPath = "Assets/HTownSim/Data/CityStreets.txt";

        private const string MeshFolder = "Assets/HTownSim/Generated/";

        /// <summary>Source lot files of the parts of the map that mark their own streets, and of the gap.</summary>
        private static readonly string[] MarkedElsewhere = { "MidtownExpanded.txt", "Neighborhood_SouthMidtown.txt", "Neighborhood_Station16.txt" };

        private static readonly string[] GapFiles = { "Gap_Southwest.txt", "Gap_South.txt" };

        /// <summary>A street point counts as gap when a gap lot is this close, and as marked elsewhere when one of those lots is this close (metres).</summary>
        private const float GapReach = 20f;

        private const float ElsewhereReach = 15f;

        /// <summary>The gap streets' surface height, metres (1.8 cm).</summary>
        private const float StreetHeight = 0.018f;

        [MenuItem("HTownSim/Gap/Paint Gap Streets")]
        private static void PaintFromMenu()
        {
            Debug.Log(Apply());
        }

        [MenuItem("HTownSim/Gap/Remove Gap Street Paint")]
        private static void RemoveFromMenu()
        {
            Debug.Log(Revert());
        }

        /// <summary>Marks the gap streets (replacing any made before) and saves the scene.</summary>
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

            var lines = GapLines(root.transform);

            var junctions = StreetJunctionFinder.Find(lines);

            var paint = new GapMeshBuilder();

            var painter = new GapStreetPainter(paint, junctions);

            var pieces = lines.Sum(line => painter.Line(line));

            var crossings = junctions.Sum(junction => painter.Crossings(junction));

            var structure = new GapMeshBuilder();

            var phaseA = new GapMeshBuilder();

            var phaseB = new GapMeshBuilder();

            var mesher = new GapSignalMesher(structure, phaseA, phaseB, StreetHeight);

            var signalised = junctions.Where(junction => junction.Signalised).ToList();

            var masts = signalised.Sum(junction => mesher.Add(junction));

            var group = new GameObject(GroupName);

            group.transform.SetParent(root.transform, false);

            Add(group, "Paint", paint);

            if (signalised.Count > 0)
            {
                Add(group, "Signals", structure);

                var a = Add(group, "LensesA", phaseA, "Off");

                var b = Add(group, "LensesB", phaseB, "Off");

                WireCycle(group, a, b);
            }

            AssetDatabase.SaveAssets();

            GapMaterials.Reset();

            EditorSceneManager.MarkSceneDirty(root.scene);

            EditorSceneManager.SaveScene(root.scene);

            return "Gap streets: " + lines.Count + " street pieces, " + pieces + " center-line pieces, " + junctions.Count + " junctions, "
                + crossings + " crosswalk bars and stop bars, " + signalised.Count + " signalised junctions with " + masts + " masts.";
        }

        /// <summary>Removes the gap street paint and signals and saves the scene.</summary>
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

            return "Gap street paint removed.";
        }

        private static void Remove(Transform root)
        {
            var old = root.Find(GroupName);

            if (old != null)
            {
                Object.DestroyImmediate(old.gameObject);
            }

            foreach (var name in new[] { "Paint", "Signals", "LensesA", "LensesB" })
            {
                AssetDatabase.DeleteAsset(MeshFolder + GroupName + "_" + name + ".asset");
            }
        }

        /// <summary>The street pieces that lie in the gap, cut where they leave it.</summary>
        private static List<StreetLine> GapLines(Transform root)
        {
            var gap = new GapParcelIndex(AuditMapAlignment.LoadWorldParcels(root, file => GapFiles.Contains(file)));

            var elsewhere = new GapParcelIndex(AuditMapAlignment.LoadWorldParcels(root, file => MarkedElsewhere.Contains(file)));

            bool InGap(Vector2 point)
            {
                return gap.Near(point).Any(lot => GapSidewalk.DistanceOutside(lot.Polygon, point) <= GapReach)
                    && !elsewhere.Near(point).Any(lot => GapSidewalk.DistanceOutside(lot.Polygon, point) <= ElsewhereReach);
            }

            var kept = new List<StreetLine>();

            foreach (var line in StreetReader.Read(DataPath))
            {
                var run = new List<Vector2>();

                foreach (var point in line.Points)
                {
                    if (InGap(point))
                    {
                        run.Add(point);

                        continue;
                    }

                    Flush(line, run, kept);

                    run = new List<Vector2>();
                }

                Flush(line, run, kept);
            }

            return kept;
        }

        private static void Flush(StreetLine line, List<Vector2> run, List<StreetLine> kept)
        {
            if (run.Count >= 2)
            {
                kept.Add(new StreetLine { Class = line.Class, Name = line.Name, Points = run });
            }
        }

        /// <summary>Saves a mesh as an asset and puts it in the scene as a child of the group; the lens submeshes take their unlit material.</summary>
        private static MeshRenderer Add(GameObject group, string name, GapMeshBuilder builder, string suffix = "")
        {
            var mesh = builder.Build(GroupName + "_" + name, out var keys);

            AssetDatabase.CreateAsset(mesh, MeshFolder + GroupName + "_" + name + ".asset");

            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));

            go.transform.SetParent(group.transform, false);

            go.GetComponent<MeshFilter>().sharedMesh = mesh;

            var renderer = go.GetComponent<MeshRenderer>();

            renderer.sharedMaterials = keys.Select(key => GapMaterials.Load(key + suffix)).ToArray();

            return renderer;
        }

        /// <summary>Adds the signal cycle to the group and gives it the two lens renderers and the lit and unlit lens materials.</summary>
        private static void WireCycle(GameObject group, MeshRenderer a, MeshRenderer b)
        {
            var cycle = group.AddComponent<GapSignalCycle>();

            var serialized = new SerializedObject(cycle);

            serialized.FindProperty("_phaseA").objectReferenceValue = a;

            serialized.FindProperty("_phaseB").objectReferenceValue = b;

            SetMaterials(serialized.FindProperty("_lit"), "On");

            SetMaterials(serialized.FindProperty("_unlit"), "Off");

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetMaterials(SerializedProperty property, string suffix)
        {
            property.arraySize = GapSignalMesher.LensOrder.Length;

            for (var i = 0; i < GapSignalMesher.LensOrder.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = GapMaterials.Load(GapSignalMesher.LensOrder[i] + suffix);
            }
        }
    }
}
