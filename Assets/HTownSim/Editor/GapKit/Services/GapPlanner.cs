#nullable enable
using HTownSim.EditorTools.GapKit.Models;
using UnityEngine;

namespace HTownSim.EditorTools.GapKit.Services
{
    /// <summary>
    /// Decides what to build on a lot: style and height from the land-use class and floors (the same rules the neighborhood builders use),
    /// the lot's oriented rectangle, and which side is the street front (the side with no other lot just beyond it).
    /// </summary>
    internal static class GapPlanner
    {
        /// <summary>A storey's height, metres (BuildRealMidtown's FloorHeight).</summary>
        internal const float FloorHeight = 3.2f;

        /// <summary>A shop's ground storey and upper storeys, metres.</summary>
        internal const float ShopGroundHeight = 4.5f;

        internal const float ShopUpperHeight = 3.6f;

        /// <summary>Tower floors are this tall.</summary>
        internal const float TowerFloorHeight = 3.6f;

        /// <summary>Distances beyond a lot's edge probed for a neighbouring lot, in metres.</summary>
        private static readonly float[] Probes = { 4f, 9f };

        /// <summary>Lots smaller than this are left empty, and so are lots without a building.</summary>
        private const int MinimumArea = 60;

        /// <summary>The smallest building worth making, metres.</summary>
        private const float MinimumSize = 3.2f;

        /// <summary>Fitting a building inside its lot: how many times it may shrink, by how much, how far it moves toward the lot's middle, and the clearance from the outline.</summary>
        private const int FitSteps = 16;

        private const float FitShrink = 0.92f;

        private const float FitNudge = 0.12f;

        private const float FitMargin = 0.4f;

        /// <summary>The shortest lot edge worth facing, how closely it must face the same way as the rectangle's front (cosine), and how far out the street probe looks.</summary>
        private const float MinimumEdge = 4f;

        private const float EdgeAlignment = 0.7f;

        private const float StreetProbe = 6f;

        /// <summary>Space left at each side of the building and behind it.</summary>
        private const float SideMargin = 1.5f;

        private const float RearMargin = 2f;

        /// <summary>The plan for a lot, or null when it gets no building.</summary>
        internal static GapPlan? Plan(GapParcel parcel, GapParcelIndex index)
        {
            if ((parcel.Floors == 0 && parcel.MeasuredFloors == 0) || parcel.Area < MinimumArea || IsPark(parcel))
            {
                return null;
            }

            if (!OrientedRectFinder.TryFind(parcel.Polygon, out var rect))
            {
                return null;
            }

            var side = FrontSide(parcel, rect, index);

            var normal = side switch
            {
                0 => rect.AxisU,
                1 => -rect.AxisU,
                2 => rect.AxisV,
                _ => -rect.AxisV,
            };

            // Face the way the street runs: turn the building to the lot's longest street-facing edge when there is one close to the
            // rectangle's front, so a lot on a curve, a slant or at a corner is lined up with its street, not with a bounding rectangle.
            var edge = StreetEdgeNormal(parcel, index, normal);

            if (edge != null)
            {
                normal = edge.Value;
            }

            var along = new Vector2(-normal.y, normal.x);

            var extent = Project(parcel.Polygon, along, normal);

            // The frontage is how wide the lot is across the front; the rest of the lot runs back from it.
            var frontage = extent.maxU - extent.minU;

            var back = extent.maxV - extent.minV;

            var plan = Describe(parcel);

            var limits = Limits(plan.Style);

            // Small lots give up less: the margins shrink with the lot, so a narrow row-house lot still gets a building.
            var sideMargin = Mathf.Min(SideMargin, frontage * 0.1f);

            var width = Mathf.Min(frontage - (2f * sideMargin), limits.x);

            var setback = Mathf.Min(FrontSetback(plan.Style), back * 0.25f);

            var depth = Mathf.Min(back - setback - Mathf.Min(RearMargin, back * 0.1f), limits.y);

            if (width < MinimumSize || depth < MinimumSize)
            {
                return null;
            }

            // The building's front stands the setback behind the lot's front edge, in the middle of the lot's width.
            plan.Centre = (along * ((extent.minU + extent.maxU) / 2f)) + (normal * (extent.maxV - setback - (depth / 2f)));

            plan.Yaw = Mathf.Atan2(normal.x, normal.y) * Mathf.Rad2Deg;

            plan.Width = width;

            plan.Depth = depth;

            return FitInside(plan, parcel) ? plan : null;
        }

        /// <summary>
        /// Shrinks a building, nudging it toward the middle of the lot, until its corners and the middles of its sides all lie inside the lot
        /// outline with a margin; false when it cannot be made to fit. This keeps a building off the street and off its neighbours on a
        /// lot that is not a rectangle.
        /// </summary>
        private static bool FitInside(GapPlan plan, GapParcel parcel)
        {
            var centroid = Vector2.zero;

            var count = parcel.Polygon.Length / 2;

            for (var i = 0; i < count; i++)
            {
                centroid += new Vector2(parcel.Polygon[i * 2], parcel.Polygon[(i * 2) + 1]);
            }

            centroid /= Mathf.Max(1, count);

            for (var step = 0; step < FitSteps; step++)
            {
                if (AllInside(plan, parcel.Polygon))
                {
                    return true;
                }

                plan.Width *= FitShrink;

                plan.Depth *= FitShrink;

                plan.Centre = Vector2.Lerp(plan.Centre, centroid, FitNudge);

                if (plan.Width < MinimumSize || plan.Depth < MinimumSize)
                {
                    return false;
                }
            }

            return AllInside(plan, parcel.Polygon);
        }

        /// <summary>True when the building's four corners and the middles of its four sides are inside the polygon by the margin.</summary>
        private static bool AllInside(GapPlan plan, float[] polygon)
        {
            var yaw = plan.Yaw * Mathf.Deg2Rad;

            var axisX = new Vector2(Mathf.Cos(yaw), -Mathf.Sin(yaw));

            var axisZ = new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw));

            var hx = plan.Width / 2f;

            var hz = plan.Depth / 2f;

            foreach (var (x, z) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f), (0f, -1f), (0f, 1f), (-1f, 0f), (1f, 0f) })
            {
                if (!GapSidewalk.InsideBy(polygon, plan.Centre + (axisX * (x * hx)) + (axisZ * (z * hz)), FitMargin))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>The outward direction of the lot's longest edge that faces the street and roughly the way <paramref name="hint"/> does, or null.</summary>
        private static Vector2? StreetEdgeNormal(GapParcel parcel, GapParcelIndex index, Vector2 hint)
        {
            var polygon = parcel.Polygon;

            var count = polygon.Length / 2;

            Vector2? best = null;

            var bestLength = MinimumEdge;

            for (int i = 0, j = count - 1; i < count; j = i++)
            {
                var a = new Vector2(polygon[j * 2], polygon[(j * 2) + 1]);

                var b = new Vector2(polygon[i * 2], polygon[(i * 2) + 1]);

                var length = Vector2.Distance(a, b);

                if (length <= bestLength)
                {
                    continue;
                }

                var direction = (b - a) / length;

                var middle = (a + b) / 2f;

                var normal = new Vector2(direction.y, -direction.x);

                // The outward side is the one that leaves the lot.
                if (GapSidewalk.Inside(polygon, middle + (normal * 0.6f)))
                {
                    normal = -normal;
                }

                if (Vector2.Dot(normal, hint) < EdgeAlignment || index.InsideAnother(middle + (normal * StreetProbe), parcel))
                {
                    continue;
                }

                best = normal;

                bestLength = length;
            }

            return best;
        }

        /// <summary>The polygon's extent along an axis (u) and along the front direction (v).</summary>
        private static (float minU, float maxU, float minV, float maxV) Project(float[] polygon, Vector2 along, Vector2 front)
        {
            var minU = float.MaxValue;

            var maxU = float.MinValue;

            var minV = float.MaxValue;

            var maxV = float.MinValue;

            for (var i = 0; i + 1 < polygon.Length; i += 2)
            {
                var point = new Vector2(polygon[i], polygon[i + 1]);

                var u = Vector2.Dot(point, along);

                var v = Vector2.Dot(point, front);

                minU = Mathf.Min(minU, u);

                maxU = Mathf.Max(maxU, u);

                minV = Mathf.Min(minV, v);

                maxV = Mathf.Max(maxV, v);
            }

            return (minU, maxU, minV, maxV);
        }

        /// <summary>The style, height, floors and seed of a lot's building (its size and place are filled in later).</summary>
        private static GapPlan Describe(GapParcel parcel)
        {
            var floors = parcel.BuildFloors;

            var residential = parcel.LandUse.StartsWith("A", System.StringComparison.Ordinal);

            var plan = new GapPlan { Parcel = parcel, Floors = floors, Seed = Hash(parcel.Index) };

            if (!residential && floors <= 3 && !parcel.LandUse.StartsWith("B", System.StringComparison.Ordinal))
            {
                plan.Style = GapStyle.Shop;

                plan.Height = Mathf.Clamp(ShopGroundHeight + ((floors - 1) * ShopUpperHeight), ShopGroundHeight, 16f);
            }
            else if (residential && floors <= 2)
            {
                plan.Style = GapStyle.House;

                plan.Height = Mathf.Max(3.4f, floors * FloorHeight);
            }
            else if (floors >= 10)
            {
                plan.Style = GapStyle.Tower;

                plan.GlassTower = !(parcel.Year > 0 && parcel.Year < 1985);

                plan.Height = Mathf.Min(220f, floors * TowerFloorHeight);
            }
            else
            {
                plan.Style = GapStyle.Apartment;

                plan.Height = floors * FloorHeight;
            }

            return plan;
        }

        /// <summary>The biggest footprint (frontage, depth) a style gets.</summary>
        private static Vector2 Limits(GapStyle style)
        {
            return style switch
            {
                GapStyle.House => new Vector2(15f, 15f),
                GapStyle.Shop => new Vector2(45f, 40f),
                GapStyle.Tower => new Vector2(60f, 60f),
                _ => new Vector2(80f, 60f),
            };
        }

        private static float FrontSetback(GapStyle style)
        {
            return style switch
            {
                GapStyle.House => 6f,
                GapStyle.Shop => 2f,
                GapStyle.Tower => 3f,
                _ => 4f,
            };
        }

        /// <summary>The side of the rectangle that faces the street: 0 +U, 1 -U, 2 +V, 3 -V. The side with the fewest lots just past it; of equal ones, the shorter.</summary>
        private static int FrontSide(GapParcel parcel, OrientedRect rect, GapParcelIndex index)
        {
            var best = 0;

            var bestScore = int.MaxValue;

            var bestLength = float.MaxValue;

            for (var side = 0; side < 4; side++)
            {
                var alongU = side < 2;

                var normal = side switch
                {
                    0 => rect.AxisU,
                    1 => -rect.AxisU,
                    2 => rect.AxisV,
                    _ => -rect.AxisV,
                };

                var half = (alongU ? rect.Size.x : rect.Size.y) / 2f;

                var length = alongU ? rect.Size.y : rect.Size.x;

                var score = 0;

                foreach (var probe in Probes)
                {
                    score += index.InsideAnother(rect.Centre + (normal * (half + probe)), parcel) ? 1 : 0;
                }

                if (score < bestScore || (score == bestScore && length < bestLength))
                {
                    best = side;

                    bestScore = score;

                    bestLength = length;
                }
            }

            return best;
        }

        private static bool IsPark(GapParcel parcel)
        {
            return parcel.Floors == 0 && parcel.LandUse.StartsWith("X", System.StringComparison.Ordinal) && parcel.Area >= 700;
        }

        /// <summary>A well-spread positive number from a lot's index.</summary>
        internal static int Hash(int index)
        {
            unchecked
            {
                var value = (uint)index * 2654435761u;

                value ^= value >> 15;

                return (int)(value & 0x7fffffff);
            }
        }
    }
}
