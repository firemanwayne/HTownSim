#nullable enable
using System.Collections.Generic;
using HTownSim.EditorTools.GapKit.Models;
using UnityEngine;

namespace HTownSim.EditorTools.GapKit.Services
{
    /// <summary>Answers how far a point is from the nearest street centre line, up to a search limit.</summary>
    internal sealed class StreetDistanceIndex
    {
        private const float CellSize = 40f;

        private readonly Dictionary<long, List<(Vector2 A, Vector2 B)>> _cells = new Dictionary<long, List<(Vector2 A, Vector2 B)>>();

        /// <summary>Indexes the street pieces.</summary>
        internal StreetDistanceIndex(IEnumerable<StreetLine> lines)
        {
            foreach (var line in lines)
            {
                for (var i = 0; i + 1 < line.Points.Count; i++)
                {
                    var a = line.Points[i];

                    var b = line.Points[i + 1];

                    var steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / (CellSize / 2f)));

                    var seen = new HashSet<long>();

                    for (var s = 0; s <= steps; s++)
                    {
                        var p = Vector2.Lerp(a, b, (float)s / steps);

                        var key = Key(Cell(p.x), Cell(p.y));

                        if (seen.Add(key))
                        {
                            if (!_cells.TryGetValue(key, out var list))
                            {
                                list = new List<(Vector2 A, Vector2 B)>();

                                _cells[key] = list;
                            }

                            list.Add((a, b));
                        }
                    }
                }
            }
        }

        /// <summary>The distance to the nearest street piece, or a large number when none is within a cell or so.</summary>
        internal float Nearest(Vector2 point)
        {
            var best = float.MaxValue;

            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dz = -1; dz <= 1; dz++)
                {
                    if (!_cells.TryGetValue(Key(Cell(point.x) + dx, Cell(point.y) + dz), out var list))
                    {
                        continue;
                    }

                    foreach (var (a, b) in list)
                    {
                        var edge = b - a;

                        var t = Mathf.Clamp01(Vector2.Dot(point - a, edge) / Mathf.Max(edge.sqrMagnitude, 1e-6f));

                        best = Mathf.Min(best, Vector2.Distance(point, a + (edge * t)));
                    }
                }
            }

            return best;
        }

        private static int Cell(float value)
        {
            return Mathf.FloorToInt(value / CellSize);
        }

        private static long Key(int cx, int cz)
        {
            return ((long)cx << 32) ^ (uint)cz;
        }
    }
}
