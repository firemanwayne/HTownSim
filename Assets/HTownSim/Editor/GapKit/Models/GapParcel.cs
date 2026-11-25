#nullable enable

namespace HTownSim.EditorTools.GapKit.Models
{
    /// <summary>One City tax lot from a gap data file (the "P" lines of Assets/HTownSim/Data/Gap_*.txt), in Midtown's frame.</summary>
    internal sealed class GapParcel
    {
        /// <summary>Position of the lot's line in the file, which makes its id.</summary>
        internal int Index { get; set; }

        /// <summary>The lot's centre (x, z).</summary>
        internal float[] Centre { get; set; } = new float[0];

        /// <summary>The lot's bounds (minX, minZ, maxX, maxZ).</summary>
        internal float[] Bounds { get; set; } = new float[0];

        /// <summary>The lot's area in square metres.</summary>
        internal int Area { get; set; }

        /// <summary>Floors from the City's records.</summary>
        internal int Floors { get; set; }

        /// <summary>Floors measured from the gross floor area over the footprint, or 0 when unavailable.</summary>
        internal int MeasuredFloors { get; set; }

        /// <summary>The land-use class: A residential, B multi-family, C and O other residential, X parks, and so on.</summary>
        internal string LandUse { get; set; } = string.Empty;

        /// <summary>The year the lot's building was built, or 0.</summary>
        internal int Year { get; set; }

        /// <summary>The lot's outline as flat x, z pairs.</summary>
        internal float[] Polygon { get; set; } = new float[0];

        /// <summary>The real building footprint bounds (minX, minZ, maxX, maxZ per building), or empty.</summary>
        internal float[] Footprints { get; set; } = new float[0];

        /// <summary>The number of floors to build: the measured count, else the City's, at least one.</summary>
        internal int BuildFloors => MeasuredFloors > 0 ? MeasuredFloors : System.Math.Max(1, Floors);
    }
}
