#nullable enable
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using HTownSim.EditorTools.GapKit.Models;

namespace HTownSim.EditorTools.GapKit.Services
{
    /// <summary>Reads the lots ("P" lines) from a gap data file.</summary>
    internal static class GapParcelReader
    {
        /// <summary>Reads every lot in the file, in file order.</summary>
        internal static List<GapParcel> Read(string path)
        {
            var parcels = new List<GapParcel>();

            var index = 0;

            foreach (var line in File.ReadLines(path))
            {
                var fields = line.Split('\t');

                if (fields[0] != "P" || fields.Length < 15)
                {
                    continue;
                }

                parcels.Add(new GapParcel
                {
                    Index = index++,
                    Centre = new[] { Number(fields[1]), Number(fields[2]) },
                    Bounds = new[] { Number(fields[3]), Number(fields[4]), Number(fields[5]), Number(fields[6]) },
                    Area = (int)Number(fields[7]),
                    Floors = (int)Number(fields[8]),
                    LandUse = fields[9],
                    Year = (int)Number(fields[10]),
                    Polygon = Pairs(fields[14]),
                    MeasuredFloors = fields.Length > 15 ? (int)Number(fields[15]) : 0,
                    Footprints = fields.Length > 16 ? Numbers(fields[16], ',') : new float[0],
                });
            }

            return parcels;
        }

        private static float Number(string text)
        {
            return float.Parse(text, CultureInfo.InvariantCulture);
        }

        /// <summary>"x,z;x,z;..." as a flat list.</summary>
        private static float[] Pairs(string text)
        {
            var values = new List<float>();

            foreach (var pair in text.Split(';'))
            {
                foreach (var part in pair.Split(','))
                {
                    if (part.Length > 0)
                    {
                        values.Add(Number(part));
                    }
                }
            }

            return values.ToArray();
        }

        private static float[] Numbers(string text, char separator)
        {
            var values = new List<float>();

            foreach (var part in text.Split(separator))
            {
                if (part.Length > 0)
                {
                    values.Add(Number(part));
                }
            }

            return values.ToArray();
        }
    }
}
