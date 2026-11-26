using UnityEngine;

namespace HTownSim.Runtime.Contracts.Models
{
    /// <summary>Collision data passed from a vehicle to the world simulation.</summary>
    internal struct TrafficImpact
    {
        /// <summary>The struck collider.</summary>
        internal Collider Collider { get; set; }

        /// <summary>The world-space contact point.</summary>
        internal Vector3 Point { get; set; }

        /// <summary>The vehicle velocity at impact.</summary>
        internal Vector3 Velocity { get; set; }
    }
}
