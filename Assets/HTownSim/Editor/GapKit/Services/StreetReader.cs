#nullable enable
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using HTownSim.EditorTools.GapKit.Models;
using UnityEngine;

namespace HTownSim.EditorTools.GapKit.Services
{
    /// <summary>Reads the City street centre lines written by <c>AgentScripts/ExtractStreets.js</c>.</summary>
    internal static class StreetReader
    {
        /// <summary>The street pieces in a file: <c>R</c>, class, name, then x,z pairs separated by semicolons.</summary>
        internal static List<StreetLine> Read(string path)
        {
            var lines = new List<StreetLine>();

            foreach (var text in File.ReadAllLines(path))
            {
                var fields = text.Split('\t');

                if (fields.Length < 4 || fields[0] != "R")
                {
                    continue;
                }

                var points = new List<Vector2>();

                foreach (var pair in fields[3].Split(';'))
                {
                    var numbers = pair.Split(',');

                    if (numbers.Length == 2)
                    {
                        points.Add(new Vector2(float.Parse(numbers[0], CultureInfo.InvariantCulture), float.Parse(numbers[1], CultureInfo.InvariantCulture)));
                    }
                }

                if (points.Count >= 2)
                {
                    lines.Add(new StreetLine { Class = fields[1], Name = fields[2], Points = points });
                }
            }

            return lines;
        }
    }
}
