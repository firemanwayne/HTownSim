#nullable enable
using System.Collections.Generic;
using System.Linq;
using HTownSim.EditorTools.GapKit.Models;
using UnityEngine;

namespace HTownSim.EditorTools.GapKit.Services
{
    /// <summary>
    /// Finds the junctions of a street network: the points where two street pieces cross, or where one ends on another. Candidates within
    /// a few metres of each other are one junction; its legs are the directions the streets leave in, with near-parallel legs merged. A point
    /// where only two collinear pieces join (a street cut into pieces) has two legs and is not a junction.
    /// </summary>
    internal static class StreetJunctionFinder
    {
        /// <summary>A street end this close to another street is a T junction.</summary>
        private const float EndSnap = 3.5f;

        /// <summary>Candidate points this close together are one junction.</summary>
        private const float ClusterRadius = 9f;

        /// <summary>A street must run at least this far from the junction to count as a leg.</summary>
        private const float MinimumLeg = 8f;

        /// <summary>Legs within this angle (cosine) of each other are one leg.</summary>
        private const float SameLegCosine = 0.9f;

        /// <summary>The search grid's cell size.</summary>
        private const float CellSize = 60f;

        /// <summary>The junctions of the network.</summary>
        internal static List<StreetJunction> Find(IReadOnlyList<StreetLine> lines)
        {
            var segments = new List<(int Line, Vector2 A, Vector2 B)>();

            var grid = new Dictionary<long, List<int>>();

            for (var l = 0; l < lines.Count; l++)
            {
                var points = lines[l].Points;

                for (var i = 0; i + 1 < points.Count; i++)
                {
                    var index = segments.Count;

                    segments.Add((l, points[i], points[i + 1]));

                    foreach (var key in Cells(points[i], points[i + 1]))
                    {
                        if (!grid.TryGetValue(key, out var list))
                        {
                            list = new List<int>();

                            grid[key] = list;
                        }

                        list.Add(index);
                    }
                }
            }

            var candidates = new List<Vector2>();

            for (var s = 0; s < segments.Count; s++)
            {
                var segment = segments[s];

                var seen = new HashSet<int>();

                foreach (var key in Cells(segment.A, segment.B))
                {
                    if (!grid.TryGetValue(key, out var neighbours))
                    {
                        continue;
                    }

                    foreach (var other in neighbours)
                    {
                        if (other <= s || !seen.Add(other) || segments[other].Line == segment.Line)
                        {
                            continue;
                        }

                        if (TryIntersect(segment.A, segment.B, segments[other].A, segments[other].B, out var point))
                        {
                            candidates.Add(point);
                        }
                    }
                }
            }

            // Street ends that stop on another street.
            for (var l = 0; l < lines.Count; l++)
            {
                foreach (var end in new[] { lines[l].Points[0], lines[l].Points[lines[l].Points.Count - 1] })
                {
                    if (Touches(end, l, segments, grid))
                    {
                        candidates.Add(end);
                    }
                }
            }

            var junctions = new List<StreetJunction>();

            foreach (var centre in Cluster(candidates))
            {
                var legs = LegsAt(centre, lines);

                if (legs.Count >= 3)
                {
                    junctions.Add(new StreetJunction { Position = centre, Legs = legs });
                }
            }

            return junctions;
        }

        /// <summary>Groups candidate points that lie close together and returns the middle of each group.</summary>
        private static List<Vector2> Cluster(List<Vector2> points)
        {
            var sums = new List<(Vector2 Sum, int Count, Vector2 First)>();

            foreach (var point in points)
            {
                var found = -1;

                for (var i = 0; i < sums.Count; i++)
                {
                    if ((sums[i].First - point).sqrMagnitude <= ClusterRadius * ClusterRadius)
                    {
                        found = i;

                        break;
                    }
                }

                if (found < 0)
                {
                    sums.Add((point, 1, point));
                }
                else
                {
                    sums[found] = (sums[found].Sum + point, sums[found].Count + 1, sums[found].First);
                }
            }

            return sums.Select(entry => entry.Sum / entry.Count).ToList();
        }

        /// <summary>The streets that leave a junction, one leg per direction.</summary>
        private static List<StreetLeg> LegsAt(Vector2 centre, IReadOnlyList<StreetLine> lines)
        {
            var legs = new List<StreetLeg>();

            foreach (var line in lines)
            {
                var points = line.Points;

                var best = float.MaxValue;

                var bestIndex = -1;

                for (var i = 0; i + 1 < points.Count; i++)
                {
                    var distance = DistanceToSegment(centre, points[i], points[i + 1]);

                    if (distance < best)
                    {
                        best = distance;

                        bestIndex = i;
                    }
                }

                if (bestIndex < 0 || best > ClusterRadius)
                {
                    continue;
                }

                var direction = (points[bestIndex + 1] - points[bestIndex]).normalized;

                var ahead = RemainingLength(points, bestIndex, centre, true);

                var behind = RemainingLength(points, bestIndex, centre, false);

                if (ahead >= MinimumLeg)
                {
                    AddLeg(legs, direction, line.IsMajor);
                }

                if (behind >= MinimumLeg)
                {
                    AddLeg(legs, -direction, line.IsMajor);
                }
            }

            return legs;
        }

        /// <summary>Adds a leg, or merges it into an existing leg that points the same way.</summary>
        private static void AddLeg(List<StreetLeg> legs, Vector2 direction, bool major)
        {
            foreach (var leg in legs)
            {
                if (Vector2.Dot(leg.Direction, direction) >= SameLegCosine)
                {
                    leg.IsMajor |= major;

                    return;
                }
            }

            legs.Add(new StreetLeg { Direction = direction, IsMajor = major });
        }

        /// <summary>How far a street runs from a point on segment <paramref name="index"/> to its end (forward) or its start (backward).</summary>
        private static float RemainingLength(List<Vector2> points, int index, Vector2 from, bool forward)
        {
            var edge = points[index + 1] - points[index];

            var t = Mathf.Clamp01(Vector2.Dot(from - points[index], edge) / Mathf.Max(edge.sqrMagnitude, 1e-6f));

            var here = points[index] + (edge * t);

            var length = forward ? Vector2.Distance(here, points[index + 1]) : Vector2.Distance(here, points[index]);

            if (forward)
            {
                for (var i = index + 1; i + 1 < points.Count; i++)
                {
                    length += Vector2.Distance(points[i], points[i + 1]);
                }
            }
            else
            {
                for (var i = index; i > 0; i--)
                {
                    length += Vector2.Distance(points[i], points[i - 1]);
                }
            }

            return length;
        }

        /// <summary>True when a street end lies on a segment of a different street.</summary>
        private static bool Touches(Vector2 end, int line, List<(int Line, Vector2 A, Vector2 B)> segments, Dictionary<long, List<int>> grid)
        {
            if (!grid.TryGetValue(Key(Cell(end.x), Cell(end.y)), out var list))
            {
                return false;
            }

            foreach (var index in list)
            {
                var segment = segments[index];

                if (segment.Line != line && DistanceToSegment(end, segment.A, segment.B) <= EndSnap)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out Vector2 point)
        {
            point = default;

            var r = b - a;

            var s = d - c;

            var denominator = (r.x * s.y) - (r.y * s.x);

            if (Mathf.Abs(denominator) < 1e-6f)
            {
                return false;
            }

            var t = (((c.x - a.x) * s.y) - ((c.y - a.y) * s.x)) / denominator;

            var u = (((c.x - a.x) * r.y) - ((c.y - a.y) * r.x)) / denominator;

            if (t < 0f || t > 1f || u < 0f || u > 1f)
            {
                return false;
            }

            point = a + (r * t);

            return true;
        }

        private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            var edge = b - a;

            var t = Mathf.Clamp01(Vector2.Dot(point - a, edge) / Mathf.Max(edge.sqrMagnitude, 1e-6f));

            return Vector2.Distance(point, a + (edge * t));
        }

        private static IEnumerable<long> Cells(Vector2 a, Vector2 b)
        {
            var steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / (CellSize / 2f)));

            var seen = new HashSet<long>();

            for (var i = 0; i <= steps; i++)
            {
                var p = Vector2.Lerp(a, b, (float)i / steps);

                for (var dx = -1; dx <= 1; dx++)
                {
                    for (var dz = -1; dz <= 1; dz++)
                    {
                        var key = Key(Cell(p.x) + dx, Cell(p.y) + dz);

                        if (seen.Add(key))
                        {
                            yield return key;
                        }
                    }
                }
            }
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
