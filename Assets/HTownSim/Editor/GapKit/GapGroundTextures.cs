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
    /// Gives the gap regions' lot surfaces the textured ground materials the Midtown ground pass uses (Poly Haven scans through the
    /// world-space triplanar shader): paved sidewalks, lawns, parking asphalt and park grass, in place of flat colours. Only the
    /// materials change, so the slabs, their colliders and the streets stay as they were. Revert puts the flat colours back.
    /// Menu: HTownSim > Gap.
    /// </summary>
    public static class GapGroundTextures
    {
        private const string CityRoot = "MidtownReal";

        private const string SlabsPrefix = "ParcelSlabs";

        private const string MedCenterContainer = "Neighborhood_MedCenter";

        /// <summary>The flat material of a surface, and the textured one that replaces it.</summary>
        private static readonly (string Flat, string Textured)[] Surfaces =
        {
            ("Concrete", "GroundSidewalk"),
            ("LotGrass", "GroundLawn"),
            ("LotAsphalt", "GroundParking"),
            ("ParkGrass", "GroundPark"),
        };

        [MenuItem("HTownSim/Gap/Texture Gap Ground")]
        private static void TextureFromMenu()
        {
            Debug.Log(Apply(true));
        }

        [MenuItem("HTownSim/Gap/Plain Gap Ground")]
        private static void PlainFromMenu()
        {
            Debug.Log(Apply(false));
        }

        /// <summary>Swaps the lot surfaces of every gap to the textured materials (or back to flat colours) and saves the scene.</summary>
        public static string Apply(bool textured)
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

            var changed = 0;

            // The gaps' containers, and the Medical Center neighborhood (its campus lots are flat tan concrete too).
            var containers = GapArea.All.Select(area => area.Container).Append(MedCenterContainer);

            foreach (var containerName in containers)
            {
                var container = root.transform.Find(containerName);

                if (container == null)
                {
                    continue;
                }

                foreach (var renderer in SurfaceRenderers(container))
                {
                    var current = renderer.sharedMaterial;

                    if (current == null)
                    {
                        continue;
                    }

                    foreach (var (flat, listed) in Surfaces)
                    {
                        // The Medical Center's wide campus plazas are paved, not sidewalk.
                        var texturedName = containerName == MedCenterContainer && listed == "GroundSidewalk" ? "GroundPaving" : listed;

                        var from = textured ? flat : texturedName;

                        var to = textured ? texturedName : flat;

                        if (current.name == from)
                        {
                            renderer.sharedMaterial = GapMaterials.Load(to);

                            changed++;

                            break;
                        }
                    }
                }
            }

            EditorSceneManager.MarkSceneDirty(root.scene);

            EditorSceneManager.SaveScene(root.scene);

            return "Gap ground: " + changed + " surfaces now " + (textured ? "textured" : "flat") + ".";
        }

        /// <summary>The lot slabs and the park ground of a gap container.</summary>
        private static IEnumerable<Renderer> SurfaceRenderers(Transform container)
        {
            foreach (Transform section in container)
            {
                if (section.name.StartsWith(SlabsPrefix))
                {
                    foreach (var renderer in section.GetComponentsInChildren<Renderer>(true))
                    {
                        yield return renderer;
                    }
                }
                else if (section.name.StartsWith("Ground"))
                {
                    foreach (Transform piece in section)
                    {
                        if (piece.name == "CorridorPark" && piece.TryGetComponent<Renderer>(out var park))
                        {
                            yield return park;
                        }
                    }
                }
            }
        }
    }
}
