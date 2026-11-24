#nullable enable

namespace HTownSim.Runtime
{
    /// <summary>
    /// The apparatus the player can start the game in. The numbers are saved in PlayerPrefs, so existing values must not change.
    /// </summary>
    internal enum StartingTruck
    {
        /// <summary>Engine 07, the pumper at Station 7.</summary>
        Engine = 0,

        /// <summary>Ladder 07, the aerial at Station 7.</summary>
        Ladder = 1,

        /// <summary>Ambulance 07, the medic unit at Station 7.</summary>
        Ambulance = 2,

        /// <summary>Engine 08, the pumper at Station 8.</summary>
        Engine08 = 3,

        /// <summary>Engine 01, a second pumper at Station 8.</summary>
        Engine01 = 4,

        /// <summary>Medic 08, the medic unit at Station 8.</summary>
        Medic08 = 5,
    }
}
