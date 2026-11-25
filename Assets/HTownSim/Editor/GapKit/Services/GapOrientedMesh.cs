#nullable enable
using UnityEngine;

namespace HTownSim.EditorTools.GapKit.Services
{
    /// <summary>Adds rectangles and boxes turned to any compass direction to a mesh builder (the builder's own box only lines up with the axes).</summary>
    internal static class GapOrientedMesh
    {
        /// <summary>A flat rectangle at a height: <paramref name="along"/> long in the direction given, <paramref name="across"/> wide, facing up.</summary>
        internal static void Flat(GapMeshBuilder builder, string material, Vector2 centre, Vector2 direction, float along, float across, float height)
        {
            var forward = new Vector3(direction.x, 0f, direction.y) * (along / 2f);

            var side = new Vector3(-direction.y, 0f, direction.x) * (across / 2f);

            var middle = new Vector3(centre.x, height, centre.y);

            builder.Quad(
                material,
                new[] { middle - forward - side, middle - forward + side, middle + forward + side, middle + forward - side },
                new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up },
                Vector3.up);
        }

        /// <summary>
        /// A box with its bottom at <paramref name="centre"/>'s height: <paramref name="size"/> is (across, up, along), where along follows
        /// <paramref name="direction"/> on the ground.
        /// </summary>
        internal static void Box(GapMeshBuilder builder, string material, Vector3 centre, Vector2 direction, Vector3 size)
        {
            var forward = new Vector3(direction.x, 0f, direction.y);

            var side = new Vector3(-direction.y, 0f, direction.x);

            var half = new Vector3(size.x / 2f, 0f, size.z / 2f);

            Vector3 Corner(float s, float f, float y)
            {
                return centre + (side * (s * half.x)) + (forward * (f * half.z)) + (Vector3.up * y);
            }

            var flat = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };

            builder.Quad(material, new[] { Corner(-1, -1, size.y), Corner(1, -1, size.y), Corner(1, 1, size.y), Corner(-1, 1, size.y) }, flat, Vector3.up);

            builder.Quad(material, new[] { Corner(-1, -1, 0), Corner(1, -1, 0), Corner(1, -1, size.y), Corner(-1, -1, size.y) }, flat, -forward);

            builder.Quad(material, new[] { Corner(-1, 1, 0), Corner(1, 1, 0), Corner(1, 1, size.y), Corner(-1, 1, size.y) }, flat, forward);

            builder.Quad(material, new[] { Corner(-1, -1, 0), Corner(-1, 1, 0), Corner(-1, 1, size.y), Corner(-1, -1, size.y) }, flat, -side);

            builder.Quad(material, new[] { Corner(1, -1, 0), Corner(1, 1, 0), Corner(1, 1, size.y), Corner(1, -1, size.y) }, flat, side);
        }
    }
}
