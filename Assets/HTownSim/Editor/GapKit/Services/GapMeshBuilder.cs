#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace HTownSim.EditorTools.GapKit.Services
{
    /// <summary>
    /// Collects triangles by material into a mesh with one submesh per material. Faces are flat (each quad has its own vertices), and
    /// every face is wound to face the given outward direction, so callers need not care about vertex order.
    /// </summary>
    internal sealed class GapMeshBuilder
    {
        private readonly List<string> _order = new List<string>();

        private readonly Dictionary<string, Part> _parts = new Dictionary<string, Part>();

        /// <summary>Adds a quad (corners in order around it) facing <paramref name="outward"/>, with a UV for each corner.</summary>
        internal void Quad(string material, Vector3[] corners, Vector2[] uvs, Vector3 outward)
        {
            var part = PartFor(material);

            var start = part.Vertices.Count;

            for (var i = 0; i < 4; i++)
            {
                part.Vertices.Add(corners[i]);

                part.Uvs.Add(uvs[i]);
            }

            var normal = Vector3.Cross(corners[1] - corners[0], corners[2] - corners[0]);

            if (Vector3.Dot(normal, outward) >= 0f)
            {
                part.Triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
            }
            else
            {
                part.Triangles.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
            }
        }

        /// <summary>Adds a triangle facing <paramref name="outward"/>.</summary>
        internal void Triangle(string material, Vector3[] corners, Vector3 outward)
        {
            var part = PartFor(material);

            var start = part.Vertices.Count;

            foreach (var corner in corners)
            {
                part.Vertices.Add(corner);

                part.Uvs.Add(new Vector2(corner.x + corner.z, corner.y));
            }

            var normal = Vector3.Cross(corners[1] - corners[0], corners[2] - corners[0]);

            if (Vector3.Dot(normal, outward) >= 0f)
            {
                part.Triangles.AddRange(new[] { start, start + 1, start + 2 });
            }
            else
            {
                part.Triangles.AddRange(new[] { start, start + 2, start + 1 });
            }
        }

        /// <summary>An axis-aligned box (all six faces, facing out) from its minimum and maximum corners.</summary>
        internal void Box(string material, Vector3 min, Vector3 max)
        {
            var c = new[]
            {
                new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z), new Vector3(max.x, min.y, max.z), new Vector3(min.x, min.y, max.z),
                new Vector3(min.x, max.y, min.z), new Vector3(max.x, max.y, min.z), new Vector3(max.x, max.y, max.z), new Vector3(min.x, max.y, max.z),
            };

            var flat = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };

            Quad(material, new[] { c[4], c[5], c[6], c[7] }, flat, Vector3.up);

            Quad(material, new[] { c[0], c[1], c[5], c[4] }, flat, Vector3.back);

            Quad(material, new[] { c[1], c[2], c[6], c[5] }, flat, Vector3.right);

            Quad(material, new[] { c[2], c[3], c[7], c[6] }, flat, Vector3.forward);

            Quad(material, new[] { c[3], c[0], c[4], c[7] }, flat, Vector3.left);
        }

        /// <summary>The finished mesh and the material of each of its submeshes, in order.</summary>
        internal Mesh Build(string name, out string[] materials)
        {
            var vertices = new List<Vector3>();

            var uvs = new List<Vector2>();

            var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };

            var triangles = new List<int[]>();

            foreach (var key in _order)
            {
                var part = _parts[key];

                var offset = vertices.Count;

                vertices.AddRange(part.Vertices);

                uvs.AddRange(part.Uvs);

                var shifted = new int[part.Triangles.Count];

                for (var i = 0; i < shifted.Length; i++)
                {
                    shifted[i] = part.Triangles[i] + offset;
                }

                triangles.Add(shifted);
            }

            mesh.SetVertices(vertices);

            mesh.SetUVs(0, uvs);

            mesh.subMeshCount = triangles.Count;

            for (var i = 0; i < triangles.Count; i++)
            {
                mesh.SetTriangles(triangles[i], i);
            }

            mesh.RecalculateNormals();

            mesh.RecalculateBounds();

            materials = _order.ToArray();

            return mesh;
        }

        private Part PartFor(string material)
        {
            if (!_parts.TryGetValue(material, out var part))
            {
                part = new Part();

                _parts[material] = part;

                _order.Add(material);
            }

            return part;
        }

        private sealed class Part
        {
            internal List<Vector3> Vertices { get; } = new List<Vector3>();

            internal List<Vector2> Uvs { get; } = new List<Vector2>();

            internal List<int> Triangles { get; } = new List<int>();
        }
    }
}
