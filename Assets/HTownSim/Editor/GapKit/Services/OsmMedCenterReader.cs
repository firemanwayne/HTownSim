#nullable enable
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using HTownSim.EditorTools.GapKit.Models;
using UnityEngine;

namespace HTownSim.EditorTools.GapKit.Services
{
    /// <summary>Reads Assets/HTownSim/Data/Osm_MedCenter.txt: the buildings ("B" lines) and the street centre lines ("W" lines).</summary>
    internal static class OsmMedCenterReader
    {
        /// <summary>Street classes that are not roads a building fronts (paths and steps).</summary>
        private static readonly HashSet<string> NotRoads = new HashSet<string> { "path", "footway", "pedestrian", "steps", "cycleway", "track", "bridleway" };

        /// <summary>Reads every building in the file.</summary>
        internal static List<MedBuilding> ReadBuildings(string path)
        {
            var buildings = new List<MedBuilding>();

            foreach (var line in File.ReadLines(path))
            {
                var f = line.Split('\t');

                // The OSM file's lines end with a name; the corridor footprints file has none.
                if (f[0] != "B" || f.Length < 7)
                {
                    continue;
                }

                buildings.Add(new MedBuilding
                {
                    Centre = new Vector2(Number(f[1]), Number(f[2])),
                    Width = Number(f[3]),
                    Depth = Number(f[4]),
                    Yaw = Number(f[5]),
                    Floors = Mathf.Max(1, (int)Number(f[6])),
                    Name = f.Length > 7 ? f[7] : "civic",
                });
            }

            return buildings;
        }

        /// <summary>Reads the road centre lines as segments (start, end), so a building can face the nearest road.</summary>
        internal static List<(Vector2 A, Vector2 B)> ReadRoadSegments(string path)
        {
            var segments = new List<(Vector2, Vector2)>();

            foreach (var line in File.ReadLines(path))
            {
                var f = line.Split('\t');

                if (f[0] != "W" || f.Length < 3 || NotRoads.Contains(f[1]))
                {
                    continue;
                }

                var points = new List<Vector2>();

                foreach (var pair in f[2].Split(';'))
                {
                    var xz = pair.Split(',');

                    if (xz.Length == 2)
                    {
                        points.Add(new Vector2(Number(xz[0]), Number(xz[1])));
                    }
                }

                for (var i = 0; i + 1 < points.Count; i++)
                {
                    segments.Add((points[i], points[i + 1]));
                }
            }

            return segments;
        }

        /// <summary>Reads the street centre lines of the given classes as whole polylines (class, points), in file order.</summary>
        internal static List<(string Class, List<Vector2> Points)> ReadRoadLines(string path, ICollection<string> classes)
        {
            var lines = new List<(string, List<Vector2>)>();

            foreach (var line in File.ReadLines(path))
            {
                var f = line.Split('	');

                if (f[0] != "W" || f.Length < 3 || !classes.Contains(f[1]))
                {
                    continue;
                }

                var points = new List<Vector2>();

                foreach (var pair in f[2].Split(';'))
                {
                    var xz = pair.Split(',');

                    if (xz.Length == 2)
                    {
                        points.Add(new Vector2(Number(xz[0]), Number(xz[1])));
                    }
                }

                if (points.Count >= 2)
                {
                    lines.Add((f[1], points));
                }
            }

            return lines;
        }

        /// <summary>Reads the corridor road centre lines ("R" lines of SouthRoads.txt) as segments.</summary>
        internal static List<(Vector2 A, Vector2 B)> ReadCorridorRoads(string path)
        {
            var segments = new List<(Vector2, Vector2)>();

            foreach (var line in File.ReadLines(path))
            {
                var f = line.Split('	');

                if (f[0] != "R" || f.Length < 3)
                {
                    continue;
                }

                var points = new List<Vector2>();

                foreach (var pair in f[2].Split(';'))
                {
                    var xz = pair.Split(',');

                    if (xz.Length == 2)
                    {
                        points.Add(new Vector2(Number(xz[0]), Number(xz[1])));
                    }
                }

                for (var i = 0; i + 1 < points.Count; i++)
                {
                    segments.Add((points[i], points[i + 1]));
                }
            }

            return segments;
        }

        private static float Number(string text)
        {
            return float.Parse(text, CultureInfo.InvariantCulture);
        }
    }
}
