#nullable enable

namespace HTownSim.Runtime
{
    /// <summary>The first level of the start screen: whose vehicle the player drives. The numbers are not saved, but keep them stable.</summary>
    internal enum StartingAgency
    {
        /// <summary>Houston Fire Department: a station, then one of its apparatus.</summary>
        Hfd = 0,

        /// <summary>Houston Police Department: a patrol unit from the Midtown substation.</summary>
        Hpd = 1,

        /// <summary>A citizen car: no lights or sirens, just a driver.</summary>
        Citizen = 2,
    }
}
