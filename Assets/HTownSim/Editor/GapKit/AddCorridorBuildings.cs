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
    /// Replaces the plain cream boxes of the Hermann Park and Museum District corridor (the 2018 LiDAR footprints, scene section
    /// Footprints_Corridor) with civic buildings from the same kit: limestone, concrete or stucco walls with tall windows and flat roofs,
    /// facing the nearest corridor or Medical Center street. A box that overlaps a building the Medical Center skyline already makes from the
    /// OpenStreetMap data is left out, so nothing is built twice. The boxes are hidden, not deleted. Menu: HTownSim > Med Center.
    /// </summary>
    public static class AddCorridorBuildings
    {
        private const string CityRoot = "MidtownReal";

        private const string BoxesName = "Footprints_Corridor";

        private const string GroupName = "CorridorBuildings";

        private const string FootprintsPath = "Assets/HTownSim/Data/Corridor_Footprints.txt";

        private const string OsmPath = "Assets/HTownSim/Data/Osm_MedCenter.txt";

        private const string RoadsPath = "Assets/HTownSim/Data/SouthRoads.txt";

        private const string MeshPath = "Assets/HTownSim/Generated/CorridorBuildings.asset";

        /// <summary>Trees and props this close to a new building's walls (metres) are hidden.</summary>
        private const float Clearance = 2.5f;

        /// <summary>The groups whose trees and props are checked against the new buildings.</summary>
        private static readonly string[] TreeGroups = { "GapStreetTrees", "GapStreetProps" };

        [MenuItem("HTownSim/Med Center/Build Corridor Buildings")]
        private static void BuildFromMenu()
        {
            Debug.Log(Apply());
        }

        [MenuItem("HTownSim/Med Center/Remove Corridor Buildings")]
        private static void RemoveFromMenu()
        {
            Debug.Log(Revert());
        }

        /// <summary>Builds the corridor buildings (replacing any made before), hides the cream boxes, and saves the scene.</summary>
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

            var boxes = OsmMedCenterReader.ReadBuildings(FootprintsPath);

            var existing = OsmMedCenterReader.ReadBuildings(OsmPath).Select(Box).ToList();

            var roads = OsmMedCenterReader.ReadRoadSegments(OsmPath);

            roads.AddRange(OsmMedCenterReader.ReadCorridorRoads(RoadsPath));

            var plans = MedSkylinePlanner.PlanCivic(boxes, existing, roads);

            var group = new GameObject(GroupName).transform;

            group.SetParent(root.transform, false);

            Object? meshAsset = null;

            foreach (var plan in plans)
            {
                var mesh = GapBuildingMesher.Build(plan, out var keys);

                mesh.name = "Corridor_" + plan.Parcel.Index;

                if (meshAsset == null)
                {
                    AssetDatabase.CreateAsset(mesh, MeshPath);

                    meshAsset = mesh;
                }
                else
                {
                    AssetDatabase.AddObjectToAsset(mesh, meshAsset);
                }

                GapBuildingPlacer.Place(group, plan, mesh, keys);
            }

            var hiddenBoxes = HideBoxes(root.transform);

            var trees = 0;

            foreach (var container in new[] { "Gap_South", "Gap_Placeholders" })
            {
                var gap = root.transform.Find(container);

                foreach (var name in TreeGroups)
                {
                    trees += GapBuildingPlacer.HideInside(gap == null ? null : gap.Find(name), plans, Clearance);
                }
            }

            AssetDatabase.SaveAssets();

            GapMaterials.Reset();

            EditorSceneManager.MarkSceneDirty(root.scene);

            EditorSceneManager.SaveScene(root.scene);

            return "Corridor: " + plans.Count + " buildings from " + boxes.Count + " footprints (" + (boxes.Count - plans.Count) + " already built from OpenStreetMap or too small); "
                + hiddenBoxes + " cream boxes hidden, " + trees + " trees and props hidden.";
        }

        /// <summary>Removes the corridor buildings and shows the cream boxes again.</summary>
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

            var boxes = root.transform.Find(BoxesName);

            if (boxes != null)
            {
                foreach (Transform section in boxes)
                {
                    section.gameObject.SetActive(true);
                }
            }

            EditorSceneManager.MarkSceneDirty(root.scene);

            EditorSceneManager.SaveScene(root.scene);

            return "Corridor buildings removed; the cream boxes are back.";
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

        /// <summary>Hides the corridor's box buildings (the "Buildings" section inside Footprints_Corridor).</summary>
        private static int HideBoxes(Transform root)
        {
            var boxes = root.Find(BoxesName);

            var hidden = 0;

            if (boxes == null)
            {
                return 0;
            }

            foreach (Transform section in boxes)
            {
                hidden += section.childCount;

                section.gameObject.SetActive(false);
            }

            return hidden;
        }

        private static OrientedBox Box(MedBuilding building)
        {
            return new OrientedBox(building.Centre, new Vector2(building.Width, building.Depth), building.Yaw);
        }
    }
}
