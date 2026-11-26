#nullable enable
using UnityEngine;

namespace HTownSim.Runtime
{
    /// <summary>Where the roof beacons go on an apparatus: the right-hand position (the left mirrors in x), in the driven object's space.</summary>
    internal readonly struct BeaconMove
    {
        /// <summary>Creates a move.</summary>
        internal BeaconMove(FireResponse apparatus, Vector3 position)
        {
            Apparatus = apparatus;

            Position = position;
        }

        /// <summary>The driven object's FireResponse, which owns the beacon lights.</summary>
        internal FireResponse Apparatus { get; }

        /// <summary>The right-hand beacon position.</summary>
        internal Vector3 Position { get; }
    }
}
