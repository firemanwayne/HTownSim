#nullable enable
using HTownSim.EditorTools.GapKit.Models;
using UnityEngine;

namespace HTownSim.EditorTools.GapKit.Services
{
    /// <summary>Tells whether a point is on a lot or its sidewalk (the lot outline pushed out by the sidewalk width), where trees and props may stand.</summary>
    internal static class GapSidewalk
    {
        /// <summary>The sidewalk's width around every lot, metres (BuildRealMidtown's SidewalkWidth).</summary>
        internal const float Width = 2.4f;

        /// <summary>True when the point is inside the lot or within the sidewalk width of its outline.</summary>
        internal static bool OnLotOrSidewalk(GapParcel parcel, Vector2 point)
        {
            return Inside(parcel.Polygon, point) || DistanceToOutline(parcel.Polygon, point) <= Width;
        }

        /// <summary>True when the point is inside the polygon (flat x, z pairs).</summary>
        internal static bool Inside(float[] polygon, Vector2 point)
        {
            var inside = false;

            var count = polygon.Length / 2;

            for (int i = 0, j = count - 1; i < count; j = i++)
            {
                var xi = polygon[i * 2];

                var zi = polygon[(i * 2) + 1];

                var xj = polygon[j * 2];

                var zj = polygon[(j * 2) + 1];

                if (((zi > point.y) != (zj > point.y)) && (point.x < ((xj - xi) * (point.y - zi) / (zj - zi)) + xi))
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        /// <summary>The shortest distance from a point to the polygon's outline.</summary>
        private static float DistanceToOutline(float[] polygon, Vector2 point)
        {
            var best = float.MaxValue;

            var count = polygon.Length / 2;

            for (int i = 0, j = count - 1; i < count; j = i++)
            {
                var a = new Vector2(polygon[j * 2], polygon[(j * 2) + 1]);

                var b = new Vector2(polygon[i * 2], polygon[(i * 2) + 1]);

                var edge = b - a;

                var length = edge.sqrMagnitude;

                var t = length < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(point - a, edge) / length);

                best = Mathf.Min(best, Vector2.Distance(point, a + (edge * t)));
            }

            return best;
        }
    }
}
