#nullable enable
using UnityEditor;
using UnityEngine;

namespace HTownSim.EditorTools.GapKit.Services
{
    /// <summary>The street lamp: one small mesh (post, arm and lamp head, local +Z toward the street), shared by every lamp so the GPU draws them instanced.</summary>
    internal static class GapLampMesh
    {
        private const string AssetPath = "Assets/HTownSim/Generated/GapLamp.asset";

        /// <summary>The material of each submesh: the steel post and arm, then the amber lamp.</summary>
        internal static readonly string[] Materials = { "Steel", "Amber" };

        /// <summary>The shared lamp mesh, created on first use.</summary>
        internal static Mesh Get()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(AssetPath);

            if (existing != null)
            {
                return existing;
            }

            var builder = new GapMeshBuilder();

            builder.Box(Materials[0], new Vector3(-0.125f, 0f, -0.125f), new Vector3(0.125f, 7f, 0.125f));

            builder.Box(Materials[0], new Vector3(-0.075f, 6.925f, 0f), new Vector3(0.075f, 7.075f, 2.2f));

            builder.Box(Materials[1], new Vector3(-0.35f, 6.8f, 1.75f), new Vector3(0.35f, 7f, 2.45f));

            var mesh = builder.Build("GapLamp", out var keys);

            if (keys.Length != Materials.Length || keys[0] != Materials[0] || keys[1] != Materials[1])
            {
                Debug.LogWarning("Gap kit: the lamp mesh's submeshes are not in the expected order.");
            }

            AssetDatabase.CreateAsset(mesh, AssetPath);

            return mesh;
        }
    }
}
