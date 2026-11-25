#nullable enable
using UnityEngine;

namespace HTownSim.EditorTools.GapKit.Models
{
    /// <summary>A footprint on the ground: centre, size along its first axis and across it, and the yaw in degrees that turns a cube's X axis onto the first axis.</summary>
    internal readonly struct OrientedBox
    {
        /// <summary>Creates a box.</summary>
        internal OrientedBox(Vector2 centre, Vector2 size, float yaw)
        {
            Centre = centre;

            Size = size;

            Yaw = yaw;
        }

        /// <summary>The centre (x, z).</summary>
        internal Vector2 Centre { get; }

        /// <summary>Size along the first axis (x) and across it (y).</summary>
        internal Vector2 Size { get; }

        /// <summary>The yaw in degrees.</summary>
        internal float Yaw { get; }

        /// <summary>The unit vector along the first axis.</summary>
        internal Vector2 AxisX => new Vector2(Mathf.Cos(Yaw * Mathf.Deg2Rad), -Mathf.Sin(Yaw * Mathf.Deg2Rad));

        /// <summary>The unit vector across it.</summary>
        internal Vector2 AxisZ => new Vector2(Mathf.Sin(Yaw * Mathf.Deg2Rad), Mathf.Cos(Yaw * Mathf.Deg2Rad));

        /// <summary>The world position of a point given in the box's own frame (x along the first axis, z across it).</summary>
        internal Vector2 ToWorld(Vector2 local)
        {
            return Centre + (AxisX * local.x) + (AxisZ * local.y);
        }

        /// <summary>True when the two boxes overlap, each grown by <paramref name="margin"/> metres on every side (separating axis test).</summary>
        internal bool Overlaps(OrientedBox other, float margin)
        {
            var axes = new[] { AxisX, AxisZ, other.AxisX, other.AxisZ };

            var between = other.Centre - Centre;

            foreach (var axis in axes)
            {
                var distance = Mathf.Abs(Vector2.Dot(between, axis));

                var reach = Radius(this, axis) + Radius(other, axis) + (2f * margin);

                if (distance > reach)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>The box's half extent along an axis.</summary>
        private static float Radius(OrientedBox box, Vector2 axis)
        {
            return (Mathf.Abs(Vector2.Dot(box.AxisX, axis)) * box.Size.x / 2f) + (Mathf.Abs(Vector2.Dot(box.AxisZ, axis)) * box.Size.y / 2f);
        }
    }
}
