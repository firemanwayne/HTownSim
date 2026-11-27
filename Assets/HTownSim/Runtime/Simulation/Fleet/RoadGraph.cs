#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace HTownSim.Runtime.Fleet
{
    /// <summary>
    /// The drivable street network built from the City's street centre lines (the files <c>CityStreets.txt</c> and <c>MedCenterStreets.txt</c>: one street piece per line,
    /// <c>R</c>, class, optionally a name, then x,z pairs separated by semicolons, in scene metres). Street pieces are cut where they cross another street
    /// or end on one, so a route can turn at every junction. Routes are found with A*, from and to any point on the map (a point is taken
    /// to the nearest street of the main network).
    /// </summary>
    internal sealed class RoadGraph
    {
        /// <summary>A street end this close (metres) to another street is joined to it as a T junction. Raise it to connect streets with small gaps; lower it to join only streets that really touch. Wrong values can leave routes cut off or join streets that should not meet.</summary>
        private const float EndSnap = 3.5f;

        /// <summary>Points closer than this (metres, as a grid step) are the same junction. Raise it to merge nearby points; lower it to keep them apart.</summary>
        private const float NodeStep = 0.5f;

        /// <summary>The size (metres) of the cells of the search grids. It only affects speed: raise it for fewer, bigger cells; lower it for many small ones.</summary>
        private const float CellSize = 60f;

        /// <summary>A part of the street network smaller than this many junctions is not used for routing (a stub cut off from the rest). Raise it to ignore more small fragments; lower it to allow small isolated roads.</summary>
        private const int MainComponentNodes = 300;

        /// <summary>The furthest (metres) a start or end point may be from the street network and still be routed from. Raise it so far-off points still get routes (a long drive across country); lower it to refuse routes from points not near a street.</summary>
        private const float MaximumSnap = 400f;

        private readonly List<Vector2> _nodes = new List<Vector2>();

        private readonly List<List<Edge>> _edges = new List<List<Edge>>();

        private readonly Dictionary<long, int> _nodeByKey = new Dictionary<long, int>();

        private readonly Dictionary<long, List<int>> _nodeGrid = new Dictionary<long, List<int>>();

        /// <summary>The component each node belongs to, and which components are big enough to route in.</summary>
        private int[] _component = new int[0];

        private bool[] _usable = new bool[0];

        /// <summary>How many nodes the network has.</summary>
        internal int NodeCount => _nodes.Count;

        /// <summary>Builds the network from the street file's text.</summary>
        internal static RoadGraph Parse(string text)
        {
            var lines = new List<List<Vector2>>();

            foreach (var row in text.Split('\n'))
            {
                var fields = row.TrimEnd('\r').Split('\t');

                if (fields.Length < 3 || fields[0] != "R")
                {
                    continue;
                }

                var points = new List<Vector2>();

                // The points are the last field (the City's file has a name before them, the south roads file does not).
                foreach (var pair in fields[fields.Length - 1].Split(';'))
                {
                    var numbers = pair.Split(',');

                    if (numbers.Length == 2)
                    {
                        points.Add(new Vector2(float.Parse(numbers[0], CultureInfo.InvariantCulture), float.Parse(numbers[1], CultureInfo.InvariantCulture)));
                    }
                }

                if (points.Count >= 2)
                {
                    lines.Add(points);
                }
            }

            var graph = new RoadGraph();

            graph.Build(lines);

            return graph;
        }

        /// <summary>
        /// The route from one point to another: the start, the street junctions between them, and the end; null when no street is near
        /// either point or they are not connected.
        /// </summary>
        internal List<Vector2>? Route(Vector2 from, Vector2 to)
        {
            var start = NearestNode(from);

            var end = NearestNode(to);

            if (start < 0 || end < 0)
            {
                return null;
            }

            var path = Search(start, end);

            if (path == null)
            {
                return null;
            }

            var route = new List<Vector2> { from };

            foreach (var node in path)
            {
                route.Add(_nodes[node]);
            }

            route.Add(to);

            return route;
        }

        /// <summary>True when a point is a junction of the street network: a node where three or more streets meet.</summary>
        internal bool IsJunction(Vector2 point)
        {
            var node = NearestNode(point);

            return node >= 0 && (_nodes[node] - point).sqrMagnitude < 4f && _edges[node].Count >= 3;
        }

        /// <summary>The usable node nearest a point, or -1.</summary>
        internal int NearestNode(Vector2 point)
        {
            var best = -1;

            var bestDistance = MaximumSnap * MaximumSnap;

            var rings = Mathf.CeilToInt(MaximumSnap / CellSize);

            var cx = Cell(point.x);

            var cz = Cell(point.y);

            for (var dx = -rings; dx <= rings; dx++)
            {
                for (var dz = -rings; dz <= rings; dz++)
                {
                    if (!_nodeGrid.TryGetValue(Key(cx + dx, cz + dz), out var list))
                    {
                        continue;
                    }

                    foreach (var node in list)
                    {
                        if (!_usable[_component[node]])
                        {
                            continue;
                        }

                        var distance = (_nodes[node] - point).sqrMagnitude;

                        if (distance < bestDistance)
                        {
                            bestDistance = distance;

                            best = node;
                        }
                    }
                }
            }

            return best;
        }

        private void Build(List<List<Vector2>> lines)
        {
            var segments = new List<Segment>();

            var grid = new Dictionary<long, List<int>>();

            for (var l = 0; l < lines.Count; l++)
            {
                var points = lines[l];

                for (var i = 0; i + 1 < points.Count; i++)
                {
                    if ((points[i + 1] - points[i]).sqrMagnitude < 0.25f)
                    {
                        continue;
                    }

                    var index = segments.Count;

                    segments.Add(new Segment(l, points[i], points[i + 1]));

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

            // Crossings: where two streets cross, both segments get the same new junction point.
            for (var s = 0; s < segments.Count; s++)
            {
                var seen = new HashSet<int>();

                foreach (var key in Cells(segments[s].A, segments[s].B))
                {
                    if (!grid.TryGetValue(key, out var neighbours))
                    {
                        continue;
                    }

                    foreach (var other in neighbours)
                    {
                        if (other <= s || !seen.Add(other) || segments[other].Line == segments[s].Line)
                        {
                            continue;
                        }

                        if (TryIntersect(segments[s], segments[other], out var point))
                        {
                            segments[s].Splits.Add(point);

                            segments[other].Splits.Add(point);
                        }
                    }
                }
            }

            // Street ends that stop on another street: the end is joined to the street at the nearest point.
            var joins = new List<(Vector2 End, Vector2 OnStreet)>();

            for (var l = 0; l < lines.Count; l++)
            {
                foreach (var end in new[] { lines[l][0], lines[l][lines[l].Count - 1] })
                {
                    FindJoin(end, l, segments, grid, joins);
                }
            }

            foreach (var segment in segments)
            {
                AddSegment(segment);
            }

            foreach (var (end, onStreet) in joins)
            {
                Connect(NodeAt(end), NodeAt(onStreet));
            }

            Classify();
        }

        /// <summary>Joins a street end to the nearest other street within the snap distance, splitting that street at the joining point.</summary>
        private static void FindJoin(Vector2 end, int line, List<Segment> segments, Dictionary<long, List<int>> grid, List<(Vector2 End, Vector2 OnStreet)> joins)
        {
            if (!grid.TryGetValue(Key(Cell(end.x), Cell(end.y)), out var list))
            {
                return;
            }

            var best = -1;

            var bestDistance = EndSnap;

            var bestPoint = Vector2.zero;

            foreach (var index in list)
            {
                var segment = segments[index];

                if (segment.Line == line)
                {
                    continue;
                }

                var edge = segment.B - segment.A;

                var t = Mathf.Clamp01(Vector2.Dot(end - segment.A, edge) / Mathf.Max(edge.sqrMagnitude, 1e-6f));

                var point = segment.A + (edge * t);

                var distance = Vector2.Distance(end, point);

                if (distance < bestDistance)
                {
                    bestDistance = distance;

                    best = index;

                    bestPoint = point;
                }
            }

            if (best >= 0)
            {
                segments[best].Splits.Add(bestPoint);

                joins.Add((end, bestPoint));
            }
        }

        /// <summary>Adds a segment, cut at its crossings, as a chain of edges.</summary>
        private void AddSegment(Segment segment)
        {
            var direction = segment.B - segment.A;

            var points = new List<Vector2> { segment.A };

            segment.Splits.Sort((a, b) => Vector2.Dot(a - segment.A, direction).CompareTo(Vector2.Dot(b - segment.A, direction)));

            points.AddRange(segment.Splits);

            points.Add(segment.B);

            for (var i = 0; i + 1 < points.Count; i++)
            {
                Connect(NodeAt(points[i]), NodeAt(points[i + 1]));
            }
        }

        private void Connect(int a, int b)
        {
            if (a == b)
            {
                return;
            }

            var length = Vector2.Distance(_nodes[a], _nodes[b]);

            _edges[a].Add(new Edge(b, length));

            _edges[b].Add(new Edge(a, length));
        }

        /// <summary>The node at a point, made when there is none within a grid step.</summary>
        private int NodeAt(Vector2 point)
        {
            var key = Key(Mathf.RoundToInt(point.x / NodeStep), Mathf.RoundToInt(point.y / NodeStep));

            if (_nodeByKey.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var index = _nodes.Count;

            _nodes.Add(point);

            _edges.Add(new List<Edge>());

            _nodeByKey[key] = index;

            var cell = Key(Cell(point.x), Cell(point.y));

            if (!_nodeGrid.TryGetValue(cell, out var list))
            {
                list = new List<int>();

                _nodeGrid[cell] = list;
            }

            list.Add(index);

            return index;
        }

        /// <summary>Finds the connected parts of the network and marks the big ones as the ones to route in.</summary>
        private void Classify()
        {
            _component = new int[_nodes.Count];

            Array.Fill(_component, -1);

            var sizes = new List<int>();

            var stack = new Stack<int>();

            for (var start = 0; start < _nodes.Count; start++)
            {
                if (_component[start] >= 0)
                {
                    continue;
                }

                var id = sizes.Count;

                var size = 0;

                stack.Push(start);

                _component[start] = id;

                while (stack.Count > 0)
                {
                    var node = stack.Pop();

                    size++;

                    foreach (var edge in _edges[node])
                    {
                        if (_component[edge.To] < 0)
                        {
                            _component[edge.To] = id;

                            stack.Push(edge.To);
                        }
                    }
                }

                sizes.Add(size);
            }

            _usable = new bool[sizes.Count];

            for (var i = 0; i < sizes.Count; i++)
            {
                _usable[i] = sizes[i] >= MainComponentNodes;
            }
        }

        /// <summary>A* between two nodes; the node chain, or null when they are not connected.</summary>
        private List<int>? Search(int start, int end)
        {
            if (_component[start] != _component[end])
            {
                return null;
            }

            var cost = new Dictionary<int, float> { [start] = 0f };

            var parent = new Dictionary<int, int>();

            var open = new SortedSet<(float Estimate, int Node)>();

            open.Add((Vector2.Distance(_nodes[start], _nodes[end]), start));

            var closed = new HashSet<int>();

            while (open.Count > 0)
            {
                var (_, node) = open.Min;

                open.Remove(open.Min);

                if (node == end)
                {
                    var path = new List<int> { node };

                    while (parent.TryGetValue(node, out var previous))
                    {
                        path.Add(previous);

                        node = previous;
                    }

                    path.Reverse();

                    return path;
                }

                if (!closed.Add(node))
                {
                    continue;
                }

                foreach (var edge in _edges[node])
                {
                    if (closed.Contains(edge.To))
                    {
                        continue;
                    }

                    var next = cost[node] + edge.Length;

                    if (!cost.TryGetValue(edge.To, out var known) || next < known)
                    {
                        cost[edge.To] = next;

                        parent[edge.To] = node;

                        open.Add((next + Vector2.Distance(_nodes[edge.To], _nodes[end]), edge.To));
                    }
                }
            }

            return null;
        }

        private static bool TryIntersect(Segment first, Segment second, out Vector2 point)
        {
            point = default;

            var r = first.B - first.A;

            var s = second.B - second.A;

            var denominator = (r.x * s.y) - (r.y * s.x);

            if (Mathf.Abs(denominator) < 1e-6f)
            {
                return false;
            }

            var t = (((second.A.x - first.A.x) * s.y) - ((second.A.y - first.A.y) * s.x)) / denominator;

            var u = (((second.A.x - first.A.x) * r.y) - ((second.A.y - first.A.y) * r.x)) / denominator;

            if (t <= 0f || t >= 1f || u <= 0f || u >= 1f)
            {
                return false;
            }

            point = first.A + (r * t);

            return true;
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

        /// <summary>A street segment and the junction points it is cut at.</summary>
        private sealed class Segment
        {
            internal Segment(int line, Vector2 a, Vector2 b)
            {
                Line = line;

                A = a;

                B = b;
            }

            internal int Line { get; }

            internal Vector2 A { get; }

            internal Vector2 B { get; }

            internal List<Vector2> Splits { get; } = new List<Vector2>();
        }

        private readonly struct Edge
        {
            internal Edge(int to, float length)
            {
                To = to;

                Length = length;
            }

            internal int To { get; }

            internal float Length { get; }
        }
    }
}
