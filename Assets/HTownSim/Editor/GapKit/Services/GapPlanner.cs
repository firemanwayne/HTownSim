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

            var alongU = side < 2;

            // The frontage is the length of the front side; the rest of the lot runs back from it.
            var frontage = alongU ? rect.Size.y : rect.Size.x;

            var back = alongU ? rect.Size.x : rect.Size.y;

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

            // The building's front stands the setback behind the lot's front edge.
            var toFront = back / 2f;

            plan.Centre = rect.Centre + (normal * (toFront - setback - (depth / 2f)));

            plan.Yaw = Mathf.Atan2(normal.x, normal.y) * Mathf.Rad2Deg;

            plan.Width = width;

            plan.Depth = depth;

            return plan;
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
