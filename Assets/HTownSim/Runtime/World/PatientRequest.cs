#nullable enable
using UnityEngine;

namespace HTownSim.Runtime
{
    /// <summary>Where and how <see cref="DownedCitizen"/> should lay out a patient.</summary>
    internal readonly struct PatientRequest
    {
        /// <summary>Creates a request.</summary>
        internal PatientRequest(Vector3 position, Vector3 facing, int variant, Collider? ignore)
        {
            Position = position;

            Facing = facing;

            Variant = variant;

            Ignore = ignore;
        }

        /// <summary>Roughly where the patient lies, in the scene; the ground below it is found by a probe.</summary>
        internal Vector3 Position { get; }

        /// <summary>The direction the patient was standing to face; the body falls from there.</summary>
        internal Vector3 Facing { get; }

        /// <summary>Picks which pedestrian model is used (modulo the number of models).</summary>
        internal int Variant { get; }

        /// <summary>The player's own collider, which the patient's body must not collide with.</summary>
        internal Collider? Ignore { get; }
    }
}
