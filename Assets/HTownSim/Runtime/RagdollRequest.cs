#nullable enable
using UnityEngine;

namespace HTownSim.Runtime
{
    /// <summary>What <see cref="RagdollBuilder"/> needs to turn a pedestrian model into a ragdoll.</summary>
    internal sealed class RagdollRequest
    {
        /// <summary>The humanoid animator whose bones become the ragdoll.</summary>
        internal Animator Animator = null!;

        /// <summary>Total body mass in kilograms, shared out across the bones.</summary>
        internal float Mass = 75f;

        /// <summary>The player's character controller, which the ragdoll must not collide with.</summary>
        internal Collider? Ignore;

        /// <summary>The launch velocity every bone starts with.</summary>
        internal Vector3 Velocity;
    }
}
