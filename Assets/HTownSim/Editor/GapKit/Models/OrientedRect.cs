#nullable enable
using UnityEngine;

namespace HTownSim.EditorTools.GapKit.Models
{
    /// <summary>The smallest rectangle that holds a lot's outline: its centre, size, and the direction of its first axis.</summary>
    internal readonly struct OrientedRect
    {
        /// <summary>Creates a rectangle.</summary>
        internal OrientedRect(Vector2 centre, Vector2 size, float axisRadians)
        {
            Centre = centre;

            Size = size;

            AxisRadians = axisRadians;
        }

        /// <summary>The centre (x, z).</summary>
        internal Vector2 Centre { get; }

        /// <summary>Size along the first axis (x) and across it (y).</summary>
        internal Vector2 Size { get; }

        /// <summary>The angle of the first axis in the (x, z) plane, in radians.</summary>
        internal float AxisRadians { get; }

        /// <summary>The unit vector along the first axis.</summary>
        internal Vector2 AxisU => new Vector2(Mathf.Cos(AxisRadians), Mathf.Sin(AxisRadians));

        /// <summary>The unit vector across the first axis.</summary>
        internal Vector2 AxisV => new Vector2(-Mathf.Sin(AxisRadians), Mathf.Cos(AxisRadians));
    }
}
