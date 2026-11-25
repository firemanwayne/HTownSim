#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace HTownSim.Runtime
{
    /// <summary>
    /// The emergency vehicles that are driving with lights and siren right now, other than the player's: the computer-driven units
    /// register here while they respond, and the traffic reads the list to clear cars out of their way.
    /// </summary>
    internal static class EmergencyTraffic
    {
        private static readonly List<Transform> Active = new List<Transform>();

        /// <summary>The vehicles responding now.</summary>
        internal static IReadOnlyList<Transform> Vehicles => Active;

        /// <summary>Adds a vehicle (once).</summary>
        internal static void Register(Transform vehicle)
        {
            if (!Active.Contains(vehicle))
            {
                Active.Add(vehicle);
            }
        }

        /// <summary>Removes a vehicle.</summary>
        internal static void Unregister(Transform vehicle)
        {
            Active.Remove(vehicle);
        }

        /// <summary>Forgets every vehicle (a scene was unloaded).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Active.Clear();
        }
    }
}
