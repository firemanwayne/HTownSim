#nullable enable

namespace HTownSim.Runtime.Incidents.Models
{
    /// <summary>A kind of apparatus an incident asks for.</summary>
    internal enum IncidentUnit
    {
        /// <summary>A unit the game has no apparatus for.</summary>
        Other,

        /// <summary>A pumper.</summary>
        Engine,

        /// <summary>An aerial ladder.</summary>
        Ladder,

        /// <summary>An ambulance or medic unit.</summary>
        Ambulance,

        /// <summary>A rescue company.</summary>
        Rescue,

        /// <summary>A hazardous materials unit.</summary>
        HazMat,

        /// <summary>A battalion chief.</summary>
        Battalion,

        /// <summary>A police patrol unit.</summary>
        Patrol,
    }
}
