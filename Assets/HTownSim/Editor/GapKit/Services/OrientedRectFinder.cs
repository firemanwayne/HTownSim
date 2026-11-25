#nullable enable
using System.Collections.Generic;
using HTownSim.EditorTools.GapKit.Models;
using UnityEngine;

namespace HTownSim.EditorTools.GapKit.Services
{
    /// <summary>Finds the minimum-area rectangle around a lot's outline (rotating calipers over the convex hull's edges).</summary>
    internal static class OrientedRectFinder
    {
        /// <summary>The rectangle around a polygon given as flat x, z pairs; false when it has fewer than three distinct points.</summary>
        internal static bool TryFind(float[] flat, out OrientedRect rect)
        {
            rect = default;

            var points = new List<Vector2>();

            for (var i = 0; i + 1 < flat.Length; i += 2)
            {
                points.Add(new Vector2(flat[i], flat[i + 1]));
            }

            var hull = ConvexHull(points);

            if (hull.Count < 3)
            {
                return false;
            }

            var bestArea = float.MaxValue;

            for (var i = 0; i < hull.Count; i++)
            {
                var edge = hull[(i + 1) % hull.Count] - hull[i];

                if (edge.sqrMagnitude < 1e-6f)
                {
                    continue;
                }

                var angle = Mathf.Atan2(edge.y, edge.x);

                var u = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

                var v = new Vector2(-u.y, u.x);

                var minU = float.MaxValue;

                var maxU = float.MinValue;

                var minV = float.MaxValue;

                var maxV = float.MinValue;

                foreach (var point in hull)
                {
                    var pu = Vector2.Dot(point, u);

                    var pv = Vector2.Dot(point, v);

                    minU = Mathf.Min(minU, pu);

                    maxU = Mathf.Max(maxU, pu);

                    minV = Mathf.Min(minV, pv);

                    maxV = Mathf.Max(maxV, pv);
                }

                var area = (maxU - minU) * (maxV - minV);

                if (area < bestArea)
                {
                    bestArea = area;

                    var centre = (u * ((minU + maxU) / 2f)) + (v * ((minV + maxV) / 2f));

                    rect = new OrientedRect(centre, new Vector2(maxU - minU, maxV - minV), angle);
                }
            }

            return bestArea < float.MaxValue;
        }

        /// <summary>Andrew's monotone chain, counter-clockwise.</summary>
        private static List<Vector2> ConvexHull(List<Vector2> points)
        {
            points.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));

            var lower = new List<Vector2>();

            foreach (var point in points)
            {
                while (lower.Count >= 2 && Cross(lower[lower.Count - 2], lower[lower.Count - 1], point) <= 0f)
                {
                    lower.RemoveAt(lower.Count - 1);
                }

                lower.Add(point);
            }

            var upper = new List<Vector2>();

            for (var i = points.Count - 1; i >= 0; i--)
            {
                var point = points[i];

                while (upper.Count >= 2 && Cross(upper[upper.Count - 2], upper[upper.Count - 1], point) <= 0f)
                {
                    upper.RemoveAt(upper.Count - 1);
                }

                upper.Add(point);
            }

            if (lower.Count > 0)
            {
                lower.RemoveAt(lower.Count - 1);
            }

            if (upper.Count > 0)
            {
                upper.RemoveAt(upper.Count - 1);
            }

            lower.AddRange(upper);

            return lower;
        }

        private static float Cross(Vector2 a, Vector2 b, Vector2 c)
        {
            return ((b.x - a.x) * (c.y - a.y)) - ((b.y - a.y) * (c.x - a.x));
        }
    }
}
