#nullable enable
using System.Collections.Generic;
using System.Linq;
using HTownSim.EditorTools.GapKit.Models;
using UnityEngine;

namespace HTownSim.EditorTools.GapKit.Services
{
    /// <summary>
    /// Paints a street network's markings into a mesh: a double yellow center line on major streets and a dashed yellow one on local
    /// streets, both left out around junctions; zebra crosswalks and stop bars at the junctions that have a major street.
    /// </summary>
    internal sealed class GapStreetPainter
    {
        /// <summary>Material names of the yellow and the white paint.</summary>
        private const string Yellow = "PaintYellow";

        private const string White = "PaintWhite";

        /// <summary>Paint sits this high (metres): the gap streets' surface is 1.8 cm, the paint is a few cm over it.</summary>
        private const float PaintHeight = 0.05f;

        /// <summary>Strip width and half the distance between the two strips of a double line, metres.</summary>
        private const float StripWidth = 0.12f;

        private const float StripOffset = 0.15f;

        /// <summary>A line is cut into pieces this long; a dashed line paints every other piece.</summary>
        private const float PieceLength = 4f;

        /// <summary>A piece within this much past a junction's half size is left out.</summary>
        private const float JunctionClearance = 5f;

        /// <summary>Crosswalk: the bar size along the street and across it, the spacing, and the gap from the junction's edge, metres.</summary>
        private const float BarLength = 3f;

        private const float BarWidth = 0.7f;

        private const float BarSpacing = 1.5f;

        private const float CrosswalkGap = 1.2f;

        /// <summary>The sidewalk on each side of a street, which the crosswalk does not cover (metres).</summary>
        private const float Sidewalk = 2.4f;

        /// <summary>Stop bar: its width and its distance before the crosswalk.</summary>
        private const float StopBarWidth = 0.45f;

        private const float StopBarGap = 1.2f;

        private readonly GapMeshBuilder _builder;

        private readonly IReadOnlyList<StreetJunction> _junctions;

        /// <summary>Paints into <paramref name="builder"/>, keeping lines clear of <paramref name="junctions"/>.</summary>
        internal GapStreetPainter(GapMeshBuilder builder, IReadOnlyList<StreetJunction> junctions)
        {
            _builder = builder;

            _junctions = junctions;
        }

        /// <summary>Paints the center line along one street; returns how many pieces were painted.</summary>
        internal int Line(StreetLine line)
        {
            var pieces = 0;

            var dashed = !line.IsMajor;

            var points = line.Points;

            for (var i = 0; i + 1 < points.Count; i++)
            {
                var length = Vector2.Distance(points[i], points[i + 1]);

                if (length < 0.5f)
                {
                    continue;
                }

                var direction = (points[i + 1] - points[i]) / length;

                var count = Mathf.Max(1, Mathf.CeilToInt(length / PieceLength));

                for (var k = 0; k < count; k++)
                {
                    if (dashed && (k & 1) == 1)
                    {
                        continue;
                    }

                    var from = Vector2.Lerp(points[i], points[i + 1], (float)k / count);

                    var to = Vector2.Lerp(points[i], points[i + 1], (float)(k + 1) / count);

                    var middle = (from + to) / 2f;

                    if (NearJunction(middle))
                    {
                        continue;
                    }

                    var side = new Vector2(-direction.y, direction.x);

                    foreach (var offset in line.IsMajor ? new[] { -StripOffset, StripOffset } : new[] { 0f })
                    {
                        GapOrientedMesh.Flat(_builder, Yellow, middle + (side * offset), direction, Vector2.Distance(from, to), StripWidth, PaintHeight);
                    }

                    pieces++;
                }
            }

            return pieces;
        }

        /// <summary>Paints the crosswalks and stop bars at a junction: one crossing on every leg that is, or meets, a major street.</summary>
        internal int Crossings(StreetJunction junction)
        {
            var painted = 0;

            foreach (var leg in junction.Legs)
            {
                if (!junction.HasMajor || (!leg.IsMajor && junction.MajorLegs < 2))
                {
                    continue;
                }

                var distance = junction.HalfSize + CrosswalkGap + (BarLength / 2f);

                var centre = junction.Position + (leg.Direction * distance);

                var across = new Vector2(-leg.Direction.y, leg.Direction.x);

                var span = (leg.Width / 2f) - Sidewalk;

                for (var offset = -span + (BarWidth / 2f); offset <= span - (BarWidth / 2f); offset += BarSpacing)
                {
                    GapOrientedMesh.Flat(_builder, White, centre + (across * offset), leg.Direction, BarLength, BarWidth, PaintHeight);

                    painted++;
                }

                // The stop bar crosses the lanes that approach the junction, which are on the right of the approaching traffic.
                var stopCentre = junction.Position + (leg.Direction * (distance + (BarLength / 2f) + StopBarGap));

                var right = across;

                GapOrientedMesh.Flat(_builder, White, stopCentre + (right * (span / 2f)), across, span, StopBarWidth, PaintHeight);

                painted++;
            }

            return painted;
        }

        /// <summary>True when a point is inside a junction's clear zone.</summary>
        private bool NearJunction(Vector2 point)
        {
            return _junctions.Any(junction => (junction.Position - point).sqrMagnitude < Mathf.Pow(junction.HalfSize + JunctionClearance, 2f));
        }
    }
}
