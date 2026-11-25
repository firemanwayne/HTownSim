#nullable enable
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace HTownSim.EditorTools.GapKit.Services
{
    /// <summary>Finds the project's wall, roof, facade and ground materials by name (they live in Generated, Generated/Facades and Generated/Ground).</summary>
    internal static class GapMaterials
    {
        private static readonly string[] Folders =
        {
            "Assets/HTownSim/Generated/Facades",
            "Assets/HTownSim/Generated",
            "Assets/HTownSim/Generated/Ground",
        };

        /// <summary>The material used when a name is not found anywhere.</summary>
        private const string Fallback = "Stucco";

        private static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();

        /// <summary>The material with a name, or the fallback with a warning.</summary>
        internal static Material Load(string name)
        {
            if (Cache.TryGetValue(name, out var cached) && cached != null)
            {
                return cached;
            }

            foreach (var folder in Folders)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(folder + "/" + name + ".mat");

                if (material != null)
                {
                    Cache[name] = material;

                    return material;
                }
            }

            Debug.LogWarning("Gap kit: material '" + name + "' not found; using " + Fallback + ".");

            var fallback = name == Fallback ? new Material(Shader.Find("Universal Render Pipeline/Lit")) : Load(Fallback);

            Cache[name] = fallback;

            return fallback;
        }

        /// <summary>Forgets what was loaded (after the assets changed).</summary>
        internal static void Reset()
        {
            Cache.Clear();
        }
    }
}
