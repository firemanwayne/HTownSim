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
    /// Builds the Texas Medical Center skyline from the OpenStreetMap buildings (Assets/HTownSim/Data/Osm_MedCenter.txt): towers and
    /// mid-rises with window facades, concrete garages, campuses split into several buildings. It replaces the procedural apartments,
    /// shops, glass and striped towers the Medical Center neighborhood had inside the OSM window (they are hidden, not deleted) and hides
    /// the street trees and props that would stand inside the new buildings. Menu: HTownSim > Med Center. Remove brings the old
    /// buildings back.
    /// </summary>
    public static class AddMedCenterSkyline
    {
        private const string CityRoot = "MidtownReal";

        private const string Neighborhood = "Neighborhood_MedCenter";

        private const string SkylineName = "MedCenterSkyline";

        private const string DataPath = "Assets/HTownSim/Data/Osm_MedCenter.txt";

        private const string MeshPath = "Assets/HTownSim/Generated/MedCenterSkyline.asset";

        /// <summary>The window the OSM data covers (Midtown's frame); procedural buildings inside it are replaced.</summary>
        private static readonly Rect Window = Rect.MinMaxRect(-450f, -4700f, 650f, -2400f);

        /// <summary>The procedural building kinds (the name before the first underscore) that the skyline replaces.</summary>
        private static readonly string[] ReplacedKinds = { "Apartment", "Striped", "Glass", "Shop", "House" };

        /// <summary>Trees and props this close to a new building's walls (metres) are hidden.</summary>
        private const float TreeClearance = 2.5f;

        [MenuItem("HTownSim/Med Center/Build Skyline")]
        private static void BuildFromMenu()
        {
            Debug.Log(Apply());
        }

        [MenuItem("HTownSim/Med Center/Remove Skyline")]
        private static void RemoveFromMenu()
        {
            Debug.Log(Revert());
        }

        /// <summary>Builds the skyline (replacing any made before), hides what it replaces, and saves the scene.</summary>
        public static string Apply()
        {
            if (EditorApplication.isPlaying)
            {
                return "Stop Play Mode first.";
            }

            var root = GameObject.Find(CityRoot);

            var med = root == null ? null : root.transform.Find(Neighborhood);

            if (root == null || med == null)
            {
                return "No " + CityRoot + "/" + Neighborhood + " in the open scene.";
            }

            Remove(root.transform);

            Restore(med);

            var plans = MedSkylinePlanner.Plan(OsmMedCenterReader.ReadBuildings(DataPath), OsmMedCenterReader.ReadRoadSegments(DataPath));

            var group = new GameObject(SkylineName).transform;

            group.SetParent(root.transform, false);

            Object? meshAsset = null;

            var counts = new Dictionary<string, int>();

            foreach (var plan in plans)
            {
                var mesh = GapBuildingMesher.Build(plan, out var keys);

                mesh.name = "Med_" + plan.Parcel.Index;

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

                var key = plan.Style == GapStyle.Tower ? (plan.Floors >= 20 ? "tower 20+ floors" : "tower 10-19 floors") : plan.Style.ToString().ToLowerInvariant();

                counts[key] = counts.TryGetValue(key, out var n) ? n + 1 : 1;
            }

            var hidden = HideReplaced(med);

            var trees = GapBuildingPlacer.HideInside(med.Find("StreetTrees_MedCenter"), plans, TreeClearance) + GapBuildingPlacer.HideInside(med.Find("StreetProps_MedCenter"), plans, TreeClearance);

            AssetDatabase.SaveAssets();

            GapMaterials.Reset();

            EditorSceneManager.MarkSceneDirty(root.scene);

            EditorSceneManager.SaveScene(root.scene);

            return "Med Center skyline: " + plans.Count + " buildings (" + string.Join(", ", counts.OrderBy(pair => pair.Key).Select(pair => pair.Value + " " + pair.Key)) + "); "
                + hidden + " procedural buildings hidden, " + trees + " trees and props hidden.";
        }

        /// <summary>Removes the skyline and shows everything it hid.</summary>
        public static string Revert()
        {
            if (EditorApplication.isPlaying)
            {
                return "Stop Play Mode first.";
            }

            var root = GameObject.Find(CityRoot);

            var med = root == null ? null : root.transform.Find(Neighborhood);

            if (root == null || med == null)
            {
                return "No " + CityRoot + "/" + Neighborhood + " in the open scene.";
            }

            Remove(root.transform);

            Restore(med);

            EditorSceneManager.MarkSceneDirty(root.scene);

            EditorSceneManager.SaveScene(root.scene);

            return "Med Center skyline removed; the procedural buildings are back.";
        }

        private static void Remove(Transform root)
        {
            var old = root.Find(SkylineName);

            if (old != null)
            {
                Object.DestroyImmediate(old.gameObject);
            }

            AssetDatabase.DeleteAsset(MeshPath);
        }

        /// <summary>Shows again every building, roof, tree and prop of the neighborhood (they are all active when first built).</summary>
        private static void Restore(Transform med)
        {
            foreach (var section in new[] { "Buildings_MedCenter", "Roofs_MedCenter", "StreetTrees_MedCenter", "StreetProps_MedCenter" })
            {
                var parent = med.Find(section);

                if (parent == null)
                {
                    continue;
                }

                foreach (Transform child in parent)
                {
                    child.gameObject.SetActive(true);
                }
            }
        }

        /// <summary>Hides the procedural buildings inside the OSM window, and the combined pitched roofs that belong to them.</summary>
        private static int HideReplaced(Transform med)
        {
            var hidden = 0;

            var buildings = med.Find("Buildings_MedCenter");

            if (buildings != null)
            {
                foreach (Transform building in buildings)
                {
                    var kind = building.name.Contains("_") ? building.name.Substring(0, building.name.IndexOf('_')) : building.name;

                    if (!ReplacedKinds.Contains(kind))
                    {
                        continue;
                    }

                    var renderers = building.GetComponentsInChildren<Renderer>();

                    if (renderers.Length == 0)
                    {
                        continue;
                    }

                    var bounds = renderers[0].bounds;

                    foreach (var renderer in renderers)
                    {
                        bounds.Encapsulate(renderer.bounds);
                    }

                    if (Window.Contains(new Vector2(bounds.center.x, bounds.center.z)))
                    {
                        building.gameObject.SetActive(false);

                        hidden++;
                    }
                }
            }

            // The pitched roofs of houses and some shops are combined into a few meshes that cannot be hidden per building.
            var roofs = med.Find("Roofs_MedCenter");

            if (roofs != null)
            {
                foreach (Transform roof in roofs)
                {
                    roof.gameObject.SetActive(false);
                }
            }

            return hidden;
        }
    }
}
