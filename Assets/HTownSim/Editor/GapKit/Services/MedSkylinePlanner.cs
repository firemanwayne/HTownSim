#nullable enable
using System.Collections.Generic;
using System.Linq;
using HTownSim.EditorTools.GapKit.Models;
using UnityEngine;

namespace HTownSim.EditorTools.GapKit.Services
{
    /// <summary>
    /// Turns the Medical Center's OpenStreetMap buildings into tower plans. The floors OSM records are kept, and named hospital, research and
    /// clinic buildings that OSM leaves at a few floors are raised to the heights such buildings have. Parking structures become plain
    /// concrete garages. The big medical outlines are massed like real hospitals: a single large hospital building becomes a low podium
    /// carrying one to three towers; a whole campus (MD Anderson, St. Luke's) becomes an irregular layout of blocks and streets with a mix
    /// of heights, the tallest toward the middle, leaving the named buildings that stand inside it alone.
    /// </summary>
    internal static class MedSkylinePlanner
    {
        /// <summary>An outline over this (square metres) that belongs to a medical campus is massed as a podium with towers.</summary>
        private const float ComplexArea = 9000f;

        /// <summary>An outline over this is a whole campus, laid out as blocks and streets.</summary>
        private const float CampusArea = 30000f;

        /// <summary>The biggest block of a campus layout and the street between blocks, metres.</summary>
        private const float MinimumBlock = 55f;

        private const float MaximumBlock = 80f;

        private const float StreetMinimum = 10f;

        private const float StreetMaximum = 16f;

        /// <summary>Blocks smaller than this are not worth building.</summary>
        private const float SmallestBlock = 22f;

        /// <summary>A campus keeps this far inside its outline, and a block keeps this far from a named building, metres.</summary>
        private const float CampusEdge = 3f;

        private const float NeighbourMargin = 3f;

        /// <summary>A podium's floors; towers on it are at least this many floors above.</summary>
        private const int PodiumFloors = 4;

        /// <summary>Floors from which a block gets a tower on a podium.</summary>
        private const int TowerFrom = 13;

        /// <summary>Most floors any building gets.</summary>
        private const int MaximumFloors = 40;

        /// <summary>Space taken off each side of a building so neighbours do not touch, metres.</summary>
        private const float Shrink = 1f;

        /// <summary>The smallest building worth making, metres.</summary>
        private const float MinimumSize = 6f;

        /// <summary>How far in front of a side a road is looked for, metres.</summary>
        private const float RoadProbe = 8f;

        private static readonly string[] CampusWords =
        {
            "anderson", "luke", "medical", "hospital", "children", "methodist", "baylor", "uthealth", "hermann", "ben taub", "pavilion", "institute", "clinic",
        };

        private static readonly string[] KeepLowWords = { "museum", "church", "park", "inn", "marriott", "intercontinental", "hotel", "temple" };

        /// <summary>The plans for every building.</summary>
        internal static List<GapPlan> Plan(IReadOnlyList<MedBuilding> buildings, IReadOnlyList<(Vector2 A, Vector2 B)> roads)
        {
            var plans = new List<GapPlan>();

            foreach (var building in buildings)
            {
                var name = building.Name.ToLowerInvariant();

                var garage = name.Contains("garage") || name.Contains("parking");

                if (garage || building.Area <= ComplexArea || !Has(name, CampusWords) || Has(name, KeepLowWords))
                {
                    Add(plans, Box(building), Floors(building, plans.Count), name, roads);

                    continue;
                }

                if (building.Area >= CampusArea)
                {
                    var others = buildings.Where(other => other != building && Box(other).Overlaps(Box(building), 0f)).Select(Box).ToList();

                    Campus(building, others, plans, roads);
                }
                else
                {
                    Complex(building, plans, roads);
                }
            }

            return plans;
        }

        /// <summary>
        /// Civic buildings from LiDAR footprints (museums, schools, park buildings; no names): one building per box, two storeys and up in
        /// limestone, concrete or stucco with tall windows, one storey as a low shop. A box that overlaps a building in <paramref name="existing"/> is
        /// left out, since that building is already built from better data.
        /// </summary>
        internal static List<GapPlan> PlanCivic(IReadOnlyList<MedBuilding> boxes, IReadOnlyList<OrientedBox> existing, IReadOnlyList<(Vector2 A, Vector2 B)> roads)
        {
            var plans = new List<GapPlan>();

            foreach (var building in boxes)
            {
                var box = Box(building);

                if (existing.Any(other => box.Overlaps(other, 2f)))
                {
                    continue;
                }

                Add(plans, box, Mathf.Min(building.Floors, MaximumFloors), "civic", roads);
            }

            return plans;
        }

        /// <summary>One large hospital building: a low podium over the whole footprint with one to three towers along its long axis.</summary>
        private static void Complex(MedBuilding building, List<GapPlan> plans, IReadOnlyList<(Vector2 A, Vector2 B)> roads)
        {
            var name = building.Name.ToLowerInvariant();

            var box = Box(building);

            var seed = GapPlanner.Hash(plans.Count + 7);

            var podiumFloors = Mathf.Clamp(building.Floors, 3, 6);

            Add(plans, box, podiumFloors, name, roads);

            var longX = building.Width >= building.Depth;

            var longSide = longX ? building.Width : building.Depth;

            var shortSide = longX ? building.Depth : building.Width;

            var towers = building.Area > 15000f ? 2 + (seed % 2) : 1 + ((seed + 1) % 2) + (seed % 3 == 0 ? 1 : 0);

            // The towers' height comes from what OSM says (or a hospital's usual 10+), with the middle one tallest.
            var baseFloors = Mathf.Max(building.Floors + 6, 12);

            for (var i = 0; i < towers; i++)
            {
                var along = towers == 1 ? 0.5f : 0.2f + (0.6f * i / (towers - 1));

                var local = new Vector2((along - 0.5f) * longSide * 0.8f, (((seed >> (i + 3)) % 5) - 2f) * 0.04f * shortSide);

                var tower = new Vector2(Mathf.Clamp(shortSide * (0.45f + ((seed >> (i + 5)) % 4 * 0.05f)), 24f, 50f), Mathf.Clamp(longSide / (towers + 1.5f), 24f, 55f));

                var size = longX ? new Vector2(tower.y, tower.x) : tower;

                var local2 = longX ? local : new Vector2(local.y, local.x);

                var floors = baseFloors + ((seed >> (i + 1)) % 7) + (i == towers / 2 ? 4 : 0);

                Add(plans, new OrientedBox(box.ToWorld(local2), size, box.Yaw), Mathf.Min(floors, MaximumFloors), name, roads);
            }
        }

        /// <summary>A whole campus: an irregular layout of blocks with streets between, mid-rise with the occasional tower, avoiding named buildings.</summary>
        private static void Campus(MedBuilding building, List<OrientedBox> others, List<GapPlan> plans, IReadOnlyList<(Vector2 A, Vector2 B)> roads)
        {
            var name = building.Name.ToLowerInvariant();

            var box = Box(building);

            var random = new System.Random(GapPlanner.Hash(plans.Count + 11));

            var leaves = new List<Rect>();

            var area = Rect.MinMaxRect((-building.Width / 2f) + CampusEdge, (-building.Depth / 2f) + CampusEdge, (building.Width / 2f) - CampusEdge, (building.Depth / 2f) - CampusEdge);

            Split(area, random, 0, leaves);

            var halfDiagonal = new Vector2(building.Width, building.Depth).magnitude / 2f;

            foreach (var leaf in leaves)
            {
                if (leaf.width < SmallestBlock || leaf.height < SmallestBlock)
                {
                    continue;
                }

                // Blocks are not all the same: each is pulled in a little by a different amount.
                var inset = new Vector2((float)random.NextDouble() * 5f, (float)random.NextDouble() * 5f);

                var size = new Vector2(leaf.width - (2f * inset.x), leaf.height - (2f * inset.y));

                var block = new OrientedBox(box.ToWorld(leaf.center), size, box.Yaw);

                // Open plazas, and no building on top of a named building that stands inside the campus.
                if (random.NextDouble() < 0.12 || others.Any(other => block.Overlaps(other, NeighbourMargin)))
                {
                    continue;
                }

                var middle = 1f - Mathf.Clamp01(leaf.center.magnitude / halfDiagonal);

                // Mostly mid-rise to high-rise, the tallest toward the middle of the campus.
                var floors = 8 + Mathf.RoundToInt((float)random.NextDouble() * 8f) + Mathf.RoundToInt(middle * 8f);

                if (random.NextDouble() < 0.18)
                {
                    floors = 22 + Mathf.RoundToInt((float)random.NextDouble() * 12f);
                }

                if (floors < TowerFrom)
                {
                    Add(plans, block, floors, name, roads);

                    continue;
                }

                // A tall block is a podium with a tower standing on it, set off to one side.
                Add(plans, block, PodiumFloors, name, roads);

                var scale = 0.5f + ((float)random.NextDouble() * 0.2f);

                var slack = new Vector2(block.Size.x * (1f - scale) / 2f, block.Size.y * (1f - scale) / 2f);

                var shift = new Vector2(((float)random.NextDouble() - 0.5f) * 2f * slack.x, ((float)random.NextDouble() - 0.5f) * 2f * slack.y);

                Add(plans, new OrientedBox(block.ToWorld(shift), block.Size * scale, block.Yaw), Mathf.Min(floors, MaximumFloors), name, roads);
            }
        }

        /// <summary>Splits a rectangle into blocks by cutting across its long side again and again, leaving a street at each cut.</summary>
        private static void Split(Rect rect, System.Random random, int depth, List<Rect> leaves)
        {
            var limit = MinimumBlock + ((float)random.NextDouble() * (MaximumBlock - MinimumBlock));

            if ((rect.width <= limit && rect.height <= limit) || depth >= 6)
            {
                leaves.Add(rect);

                return;
            }

            var street = StreetMinimum + ((float)random.NextDouble() * (StreetMaximum - StreetMinimum));

            var ratio = 0.38f + ((float)random.NextDouble() * 0.24f);

            if (rect.width >= rect.height)
            {
                var cut = rect.xMin + (rect.width * ratio);

                Split(Rect.MinMaxRect(rect.xMin, rect.yMin, cut - (street / 2f), rect.yMax), random, depth + 1, leaves);

                Split(Rect.MinMaxRect(cut + (street / 2f), rect.yMin, rect.xMax, rect.yMax), random, depth + 1, leaves);
            }
            else
            {
                var cut = rect.yMin + (rect.height * ratio);

                Split(Rect.MinMaxRect(rect.xMin, rect.yMin, rect.xMax, cut - (street / 2f)), random, depth + 1, leaves);

                Split(Rect.MinMaxRect(rect.xMin, cut + (street / 2f), rect.xMax, rect.yMax), random, depth + 1, leaves);
            }
        }

        /// <summary>The floors a building gets: OSM's, raised for named buildings OSM leaves low.</summary>
        private static int Floors(MedBuilding building, int serial)
        {
            var name = building.Name.ToLowerInvariant();

            var seed = GapPlanner.Hash(serial + 1);

            var floors = building.Floors;

            if (name.Contains("garage") || name.Contains("parking"))
            {
                return Mathf.Clamp(floors, 4, 10);
            }

            if (Has(name, KeepLowWords))
            {
                return floors;
            }

            if (name.Contains("tower"))
            {
                floors = Mathf.Max(floors, 14 + (seed % 11));
            }
            else if (name.Contains("hospital") || name.Contains("medical plaza") || name.Contains("medical center"))
            {
                floors = Mathf.Max(floors, 8 + (seed % 9));
            }
            else if (Has(name, new[] { "research", "institute", "building", "pavilion", "clinic", "center", "centre", "college", "school", "faculty", "commons", "plaza" }))
            {
                floors = Mathf.Max(floors, 6 + (seed % 6));
            }

            return Mathf.Min(floors, MaximumFloors);
        }

        /// <summary>Makes one plan: turns the box so its front faces the nearest road and sets the style and height from the floors.</summary>
        private static void Add(List<GapPlan> plans, OrientedBox box, int floors, string name, IReadOnlyList<(Vector2 A, Vector2 B)> roads)
        {
            var width = box.Size.x - (2f * Shrink);

            var depth = box.Size.y - (2f * Shrink);

            if (width < MinimumSize || depth < MinimumSize)
            {
                return;
            }

            var side = NearestRoadSide(box, roads);

            var normal = side switch
            {
                0 => box.AxisX,
                1 => -box.AxisX,
                2 => box.AxisZ,
                _ => -box.AxisZ,
            };

            var alongX = side < 2;

            var id = plans.Count;

            var plan = new GapPlan
            {
                Parcel = new GapParcel { Index = id },
                Centre = box.Centre,
                Yaw = Mathf.Atan2(normal.x, normal.y) * Mathf.Rad2Deg,
                Width = alongX ? depth : width,
                Depth = alongX ? width : depth,
                Floors = floors,
                Seed = GapPlanner.Hash(id + 1),
            };

            Style(plan, name);

            plans.Add(plan);
        }

        /// <summary>The style and wall height from the floors and the name.</summary>
        private static void Style(GapPlan plan, string name)
        {
            var floors = plan.Floors;

            if (name.Contains("garage") || name.Contains("parking"))
            {
                plan.Style = GapStyle.Garage;

                plan.Height = floors * GapPlanner.FloorHeight;
            }
            else if (floors >= 10)
            {
                plan.Style = GapStyle.Tower;

                plan.GlassTower = plan.Seed % 3 == 0;

                plan.Height = floors * GapPlanner.TowerFloorHeight;
            }
            else if (name == "house" || name == "residential")
            {
                plan.Style = GapStyle.House;

                plan.Floors = Mathf.Min(floors, 2);

                plan.Height = Mathf.Max(3.4f, plan.Floors * GapPlanner.FloorHeight);
            }
            else if (floors >= 4 || name == "apartments" || (name == "civic" && floors >= 2))
            {
                plan.Style = GapStyle.Apartment;

                plan.Institutional = name != "apartments";

                plan.Height = floors * (plan.Institutional ? GapPlanner.TowerFloorHeight : GapPlanner.FloorHeight);
            }
            else
            {
                plan.Style = GapStyle.Shop;

                plan.Height = Mathf.Clamp(GapPlanner.ShopGroundHeight + ((floors - 1) * GapPlanner.ShopUpperHeight), GapPlanner.ShopGroundHeight, 16f);
            }
        }

        /// <summary>The side (0 +X, 1 -X, 2 +Z, 3 -Z of the box) with the nearest road just beyond it.</summary>
        private static int NearestRoadSide(OrientedBox box, IReadOnlyList<(Vector2 A, Vector2 B)> roads)
        {
            var best = 0;

            var bestDistance = float.MaxValue;

            for (var side = 0; side < 4; side++)
            {
                var normal = side switch
                {
                    0 => box.AxisX,
                    1 => -box.AxisX,
                    2 => box.AxisZ,
                    _ => -box.AxisZ,
                };

                var half = (side < 2 ? box.Size.x : box.Size.y) / 2f;

                var distance = DistanceToRoads(box.Centre + (normal * (half + RoadProbe)), roads);

                if (distance < bestDistance)
                {
                    bestDistance = distance;

                    best = side;
                }
            }

            return best;
        }

        private static float DistanceToRoads(Vector2 point, IReadOnlyList<(Vector2 A, Vector2 B)> roads)
        {
            var best = float.MaxValue;

            foreach (var (a, b) in roads)
            {
                var edge = b - a;

                var length = edge.sqrMagnitude;

                var t = length < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(point - a, edge) / length);

                best = Mathf.Min(best, (point - (a + (edge * t))).sqrMagnitude);
            }

            return Mathf.Sqrt(best);
        }

        private static OrientedBox Box(MedBuilding building)
        {
            return new OrientedBox(building.Centre, new Vector2(building.Width, building.Depth), building.Yaw);
        }

        private static bool Has(string name, string[] words)
        {
            foreach (var word in words)
            {
                if (name.Contains(word))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
