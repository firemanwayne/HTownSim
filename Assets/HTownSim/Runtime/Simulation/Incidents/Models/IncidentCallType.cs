#nullable enable

namespace HTownSim.Runtime.Incidents.Models
{
    /// <summary>What kind of call an incident is; the game plays the fire types as a burning building and the rest as a scene to stage at.</summary>
    internal enum IncidentCallType
    {
        /// <summary>Anything the game has no specific model for.</summary>
        Other,

        /// <summary>A structure fire.</summary>
        StructureFire,

        /// <summary>A vehicle fire.</summary>
        VehicleFire,

        /// <summary>A brush or grass fire.</summary>
        BrushFire,

        /// <summary>A fire alarm with no confirmed fire.</summary>
        FireAlarm,

        /// <summary>A medical emergency.</summary>
        Medical,

        /// <summary>A traffic collision.</summary>
        TrafficAccident,

        /// <summary>A hazardous materials release.</summary>
        HazMat,

        /// <summary>A technical rescue.</summary>
        Rescue,
    }
}
