#nullable enable
using UnityEngine;

namespace HTownSim.EditorTools.GapKit.Models
{
    /// <summary>What to build on one lot: where it stands, which way it faces, and its size and style.</summary>
    internal sealed class GapPlan
    {
        /// <summary>The lot this is for.</summary>
        internal GapParcel Parcel { get; set; } = new GapParcel();

        /// <summary>The kind of building.</summary>
        internal GapStyle Style { get; set; }

        /// <summary>True for a mid-rise hospital, clinic or university building: stone or stucco walls with tall windows, not apartment brick.</summary>
        internal bool Institutional { get; set; }

        /// <summary>True for a tower clad in glass; false for stone with windows.</summary>
        internal bool GlassTower { get; set; }

        /// <summary>The building's centre on the ground (x, z).</summary>
        internal Vector2 Centre { get; set; }

        /// <summary>The yaw in degrees that turns the building's local +Z (its street front) to face the street.</summary>
        internal float Yaw { get; set; }

        /// <summary>Size across the front, along local X, in metres.</summary>
        internal float Width { get; set; }

        /// <summary>Size from front to back, along local Z, in metres.</summary>
        internal float Depth { get; set; }

        /// <summary>Wall height in metres.</summary>
        internal float Height { get; set; }

        /// <summary>Storeys.</summary>
        internal int Floors { get; set; }

        /// <summary>A stable number from the lot, which picks colours and details.</summary>
        internal int Seed { get; set; }
    }
}
