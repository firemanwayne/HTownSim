#nullable enable
using HTownSim.EditorTools.GapKit.Models;
using UnityEngine;

namespace HTownSim.EditorTools.GapKit.Services
{
    /// <summary>
    /// Builds one building's mesh from its plan, in the building's own frame (origin on the ground at its centre, street front at +Z):
    /// walls, window overlays tiled in metres from the facade textures, and a roof (gable or hip for a house, flat with a parapet and
    /// rooftop unit for the rest). One mesh, one submesh per material.
    /// </summary>
    internal static class GapBuildingMesher
    {
        /// <summary>How far the window overlays stand off the wall, metres.</summary>
        private const float OverlayOut = 0.05f;

        /// <summary>Parapet height and thickness on a flat roof.</summary>
        private const float ParapetHeight = 0.6f;

        private const float ParapetThickness = 0.25f;

        /// <summary>How far a pitched roof reaches past the walls.</summary>
        private const float EaveOverhang = 0.4f;

        private static readonly string[] HouseWalls =
        {
            "SidingWhite", "SidingCream", "SidingYellow", "SidingBlue", "SidingSage", "SidingGrey", "BrickBuff", "Brick", "Stucco", "StuccoWhite",
        };

        private static readonly string[] ShopWalls = { "Brick", "BrickBuff", "BrickDark", "Stucco", "StuccoWhite", "Concrete", "SageStucco" };

        private static readonly string[] ApartmentWalls = { "Brick", "BrickDark", "Stucco", "StuccoTerracotta", "SageStucco", "BrickBuff" };

        private static readonly string[] TowerWalls = { "Limestone", "Concrete", "Granite" };

        private static readonly string[] InstitutionalWalls = { "Limestone", "Concrete", "Stucco", "BrickBuff", "SageStucco", "StuccoWhite" };

        private const string GarageWall = "Concrete";

        private static readonly string[] PitchedRoofs = { "RoofShingle", "ShingleBrown", "ShingleBlack", "ShingleTan" };

        private static readonly string[] FlatRoofs = { "RoofTar", "RoofWhite", "RoofMetal", "RoofTar" };

        private const string RooftopUnit = "Steel";

        private const string ParapetCap = "Limestone";

        private const string Penthouse = "Concrete";

        /// <summary>One wall: its outward direction, its centre on the ground and its length.</summary>
        private readonly struct Side
        {
            internal Side(Vector3 normal, Vector3 centre, float length)
            {
                Normal = normal;

                Centre = centre;

                Length = length;
            }

            internal Vector3 Normal { get; }

            internal Vector3 Centre { get; }

            internal float Length { get; }

            /// <summary>The direction to the viewer's right when looking at the wall from outside.</summary>
            internal Vector3 Right => Vector3.Cross(Normal, Vector3.up);

            internal Vector3 Left(float y)
            {
                return Centre - (Right * (Length / 2f)) + (Vector3.up * y);
            }

            internal Vector3 RightEnd(float y)
            {
                return Centre + (Right * (Length / 2f)) + (Vector3.up * y);
            }
        }

        /// <summary>Builds the mesh; <paramref name="materials"/> names the material of each submesh.</summary>
        internal static Mesh Build(GapPlan plan, out string[] materials)
        {
            var builder = new GapMeshBuilder();

            var halfWidth = plan.Width / 2f;

            var halfDepth = plan.Depth / 2f;

            var sides = new[]
            {
                new Side(Vector3.forward, new Vector3(0f, 0f, halfDepth), plan.Width),
                new Side(Vector3.right, new Vector3(halfWidth, 0f, 0f), plan.Depth),
                new Side(Vector3.back, new Vector3(0f, 0f, -halfDepth), plan.Width),
                new Side(Vector3.left, new Vector3(-halfWidth, 0f, 0f), plan.Depth),
            };

            var wall = WallMaterial(plan);

            var glass = plan.Style == GapStyle.Tower && plan.GlassTower;

            var house = plan.Style == GapStyle.House;

            var wallTop = house ? plan.Height : plan.Height + ParapetHeight;

            foreach (var side in sides)
            {
                Wall(builder, side, wall, new Vector2(0f, wallTop), glass ? GapFacade.CurtainWall : null);
            }

            if (house)
            {
                PitchedRoof(builder, plan, wall);
            }
            else
            {
                FlatRoof(builder, plan, sides);
            }

            Windows(builder, plan, sides);

            return builder.Build("Gap_" + plan.Parcel.Index, out materials);
        }

        /// <summary>A plain wall (triplanar materials ignore UVs); a curtain wall gets UVs in tile units.</summary>
        private static void Wall(GapMeshBuilder builder, Side side, string material, Vector2 range, GapFacade? tiled)
        {
            var a = side.Left(range.x);

            var b = side.RightEnd(range.x);

            var c = side.RightEnd(range.y);

            var d = side.Left(range.y);

            var u = tiled is null ? 0f : side.Length / tiled.TileWidth;

            var v0 = tiled is null ? 0f : range.x / tiled.TileHeight;

            var v1 = tiled is null ? 0f : range.y / tiled.TileHeight;

            builder.Quad(material, new[] { a, b, c, d }, new[] { new Vector2(0f, v0), new Vector2(u, v0), new Vector2(u, v1), new Vector2(0f, v1) }, side.Normal);
        }

        /// <summary>Overlays a facade texture on a wall between two heights, a whole number of bays wide and centred.</summary>
        private static void Overlay(GapMeshBuilder builder, Side side, GapFacade facade, Vector2 range)
        {
            var bays = Mathf.FloorToInt(side.Length / facade.BayWidth);

            if (bays < 1 || range.y - range.x < 1f)
            {
                return;
            }

            var used = bays * facade.BayWidth;

            var start = (side.Length - used) / 2f;

            var offset = (side.Normal * OverlayOut) - (side.Right * (side.Length / 2f)) + (side.Right * start);

            var a = side.Centre + offset + (Vector3.up * range.x);

            var b = a + (side.Right * used);

            var c = b + (Vector3.up * (range.y - range.x));

            var d = a + (Vector3.up * (range.y - range.x));

            var u1 = used / facade.TileWidth;

            var v1 = (range.y - range.x) / facade.TileHeight;

            builder.Quad(facade.Material, new[] { a, b, c, d }, new[] { new Vector2(0f, 0f), new Vector2(u1, 0f), new Vector2(u1, v1), new Vector2(0f, v1) }, side.Normal);
        }

        /// <summary>The window overlays for a style: which texture on which wall, between which heights.</summary>
        private static void Windows(GapMeshBuilder builder, GapPlan plan, Side[] sides)
        {
            switch (plan.Style)
            {
                case GapStyle.House:
                    for (var i = 0; i < sides.Length; i++)
                    {
                        Overlay(builder, sides[i], i == 0 ? GapFacade.HouseFront : GapFacade.HouseSide, new Vector2(0f, plan.Floors * GapPlanner.FloorHeight));
                    }

                    break;

                case GapStyle.Shop:
                    Overlay(builder, sides[0], GapFacade.Storefront, new Vector2(0f, GapPlanner.ShopGroundHeight));

                    for (var i = 0; i < sides.Length; i++)
                    {
                        // The upper floors have windows all round; the ground floor is only a storefront on the front.
                        Overlay(builder, sides[i], GapFacade.Shop, new Vector2(GapPlanner.ShopGroundHeight, plan.Height));
                    }

                    break;

                case GapStyle.Tower:
                    if (!plan.GlassTower)
                    {
                        foreach (var side in sides)
                        {
                            Overlay(builder, side, GapFacade.TowerWindows, new Vector2(0f, plan.Floors * GapPlanner.TowerFloorHeight));
                        }
                    }

                    break;

                case GapStyle.Garage:
                    break;

                default:
                    foreach (var side in sides)
                    {
                        if (plan.Institutional)
                        {
                            Overlay(builder, side, GapFacade.TowerWindows, new Vector2(0f, plan.Floors * GapPlanner.TowerFloorHeight));
                        }
                        else
                        {
                            Overlay(builder, side, GapFacade.Residential, new Vector2(0f, plan.Floors * GapPlanner.FloorHeight));
                        }
                    }

                    break;
            }
        }

        /// <summary>A flat roof surface, a parapet with its inner faces and cap, and a rooftop unit.</summary>
        private static void FlatRoof(GapMeshBuilder builder, GapPlan plan, Side[] sides)
        {
            var roof = Pick(FlatRoofs, plan.Seed / 3);

            var halfWidth = plan.Width / 2f;

            var halfDepth = plan.Depth / 2f;

            var y = plan.Height;

            builder.Quad(
                roof,
                new[] { new Vector3(-halfWidth, y, -halfDepth), new Vector3(-halfWidth, y, halfDepth), new Vector3(halfWidth, y, halfDepth), new Vector3(halfWidth, y, -halfDepth) },
                new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right },
                Vector3.up);

            foreach (var side in sides)
            {
                // Inner face, facing the roof.
                var inner = new Side(-side.Normal, side.Centre - (side.Normal * ParapetThickness), side.Length - (2f * ParapetThickness));

                builder.Quad(
                    WallMaterial(plan),
                    new[] { inner.Left(y), inner.RightEnd(y), inner.RightEnd(y + ParapetHeight), inner.Left(y + ParapetHeight) },
                    new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up },
                    -side.Normal);

                // The cap: a strip across the top of the parapet.
                var top = y + ParapetHeight;

                var outerLeft = side.Left(top);

                var outerRight = side.RightEnd(top);

                var inwards = -side.Normal * ParapetThickness;

                builder.Quad(
                    ParapetCap,
                    new[] { outerLeft, outerRight, outerRight + inwards, outerLeft + inwards },
                    new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up },
                    Vector3.up);
            }

            if (plan.Style != GapStyle.Tower && plan.Width * plan.Depth > 60f && plan.Seed % 3 != 0)
            {
                var x = ((plan.Seed % 7) - 3f) / 3f * Mathf.Max(0f, (plan.Width / 2f) - 2.5f);

                var z = (((plan.Seed / 7) % 7) - 3f) / 3f * Mathf.Max(0f, (plan.Depth / 2f) - 2.5f);

                builder.Box(RooftopUnit, new Vector3(x - 0.9f, y, z - 0.6f), new Vector3(x + 0.9f, y + 1.3f, z + 0.6f));
            }

            if (plan.Style == GapStyle.Tower)
            {
                // A mechanical penthouse: smaller than the roof and grey, so the roof does not read as one big pale slab.
                var crown = Mathf.Min(plan.Width, plan.Depth) * 0.18f;

                builder.Box(Penthouse, new Vector3(-crown, y, -crown), new Vector3(crown, y + 3.5f, crown));
            }
        }

        /// <summary>A gable or hip roof on the building's long axis, with its gable ends filled in the wall material.</summary>
        private static void PitchedRoof(GapMeshBuilder builder, GapPlan plan, string wall)
        {
            var shingle = Pick(PitchedRoofs, plan.Seed / 5);

            var hip = plan.Seed % 3 == 0;

            var pitch = Mathf.Tan((24f + ((plan.Seed % 5) * 2.5f)) * Mathf.Deg2Rad);

            var alongX = plan.Width >= plan.Depth;

            // A is the long direction (the ridge runs along it), B the short one.
            var halfA = (alongX ? plan.Width : plan.Depth) / 2f;

            var halfB = (alongX ? plan.Depth : plan.Width) / 2f;

            var ridge = plan.Height + (halfB * pitch);

            var eave = plan.Height - (EaveOverhang * pitch);

            var reach = halfB + EaveOverhang;

            Vector3 P(float a, float b, float y)
            {
                return alongX ? new Vector3(a, y, b) : new Vector3(b, y, a);
            }

            Vector3 N(float a, float b)
            {
                return alongX ? new Vector3(a, 0f, b) : new Vector3(b, 0f, a);
            }

            var slopeUp = new Vector3(0f, 1f, 0f);

            if (!hip)
            {
                var end = halfA + 0.2f;

                foreach (var sign in new[] { -1f, 1f })
                {
                    builder.Quad(
                        shingle,
                        new[] { P(-end, sign * reach, eave), P(end, sign * reach, eave), P(end, 0f, ridge), P(-end, 0f, ridge) },
                        new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up },
                        N(0f, sign) + slopeUp);

                    builder.Triangle(wall, new[] { P(sign * halfA, -halfB, plan.Height), P(sign * halfA, halfB, plan.Height), P(sign * halfA, 0f, ridge) }, N(sign, 0f));
                }

                return;
            }

            var ridgeHalf = Mathf.Max(0.3f, halfA - halfB);

            var endReach = halfA + EaveOverhang;

            foreach (var sign in new[] { -1f, 1f })
            {
                builder.Quad(
                    shingle,
                    new[] { P(-endReach, sign * reach, eave), P(endReach, sign * reach, eave), P(ridgeHalf, 0f, ridge), P(-ridgeHalf, 0f, ridge) },
                    new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up },
                    N(0f, sign) + slopeUp);

                builder.Triangle(shingle, new[] { P(sign * endReach, -reach, eave), P(sign * endReach, reach, eave), P(sign * ridgeHalf, 0f, ridge) }, N(sign, 0f) + slopeUp);
            }
        }

        private static string WallMaterial(GapPlan plan)
        {
            return plan.Style switch
            {
                GapStyle.House => Pick(HouseWalls, plan.Seed),
                GapStyle.Shop => Pick(ShopWalls, plan.Seed),
                GapStyle.Tower => plan.GlassTower ? GapFacade.CurtainWall.Material : Pick(TowerWalls, plan.Seed),
                GapStyle.Garage => GarageWall,
                _ => plan.Institutional ? Pick(InstitutionalWalls, plan.Seed) : Pick(ApartmentWalls, plan.Seed),
            };
        }

        private static string Pick(string[] choices, int seed)
        {
            return choices[Mathf.Abs(seed) % choices.Length];
        }
    }
}
