#nullable enable

namespace HTownSim.Runtime.Incidents.Models
{
    /// <summary>The kind of rig the player drives, for first-due dispatch.</summary>
    internal enum LiveUnitKind
    {
        /// <summary>Engine or ladder: fires, alarms, rescues, hazards.</summary>
        Fire,

        /// <summary>Ambulance, medic or EMS supervisor: EMS events.</summary>
        Ems,

        /// <summary>District chief: fires and multi-alarm incidents.</summary>
        Chief,

        /// <summary>HPD patrol unit: police (PD) incidents.</summary>
        Police,

        /// <summary>Citizen car: no emergency calls.</summary>
        Citizen,
    }
}
