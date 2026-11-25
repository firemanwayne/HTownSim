#nullable enable
using UnityEngine;

namespace HTownSim.EditorTools.GapKit.Models
{
    /// <summary>One street arriving at a junction: the way it leaves the junction and whether it is a major street.</summary>
    internal sealed class StreetLeg
    {
        /// <summary>Unit direction (x, z) pointing away from the junction along the street.</summary>
        internal Vector2 Direction { get; set; }

        /// <summary>True when the street is a major street.</summary>
        internal bool IsMajor { get; set; }

        /// <summary>The street's paved width, metres.</summary>
        internal float Width => IsMajor ? MajorWidth : LocalWidth;

        /// <summary>Paved width of a major street and of a local street, metres.</summary>
        internal const float MajorWidth = 16f;

        internal const float LocalWidth = 10f;
    }
}
