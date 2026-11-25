#nullable enable
using UnityEngine;

namespace HTownSim.EditorTools.GapKit.Models
{
    /// <summary>One OpenStreetMap building in the Medical Center window: an oriented box around its outline, with the floors OSM gives it.</summary>
    internal sealed class MedBuilding
    {
        /// <summary>The box's centre (x, z) in Midtown's frame.</summary>
        internal Vector2 Centre { get; set; }

        /// <summary>Size along the box's first axis, metres.</summary>
        internal float Width { get; set; }

        /// <summary>Size across it, metres.</summary>
        internal float Depth { get; set; }

        /// <summary>The yaw in degrees that turns a cube's X axis onto the first axis.</summary>
        internal float Yaw { get; set; }

        /// <summary>The floors OSM records (1 when it does not know).</summary>
        internal int Floors { get; set; }

        /// <summary>The building's name, or the OSM building type ("yes", "parking", "industrial" ...).</summary>
        internal string Name { get; set; } = string.Empty;

        /// <summary>The box's area in square metres.</summary>
        internal float Area => Width * Depth;
    }
}
