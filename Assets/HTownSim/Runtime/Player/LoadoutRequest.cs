#nullable enable

namespace HTownSim.Runtime
{
    /// <summary>Asks <see cref="ApparatusLoadout"/> to turn the driven object into one particular apparatus.</summary>
    internal readonly struct LoadoutRequest
    {
        /// <summary>Creates a request.</summary>
        internal LoadoutRequest(FireResponse apparatus, StartingTruck target)
        {
            Apparatus = apparatus;

            Target = target;
        }

        /// <summary>The driven object's FireResponse, which owns the beacon lights.</summary>
        internal FireResponse Apparatus { get; }

        /// <summary>The apparatus to drive.</summary>
        internal StartingTruck Target { get; }
    }
}
