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

        /// <summary>Engine 16, the pumper at Station 16.</summary>
        Engine16 = 6,

        /// <summary>Ladder 16, the aerial at Station 16.</summary>
        Ladder16 = 7,

        /// <summary>Ambulance 16, the medic unit at Station 16.</summary>
        Ambulance16 = 8,

        /// <summary>Supervisor 16, the Suburban the EMS supervisor drives from Station 16.</summary>
        Supervisor16 = 9,

        /// <summary>District Chief 08, the command pickup at Station 8.</summary>
        DistrictChief08 = 10,

        /// <summary>HPD patrol unit 46837, the Ford SUV at the HPD Midtown substation.</summary>
        PatrolHpd = 11,

        /// <summary>A citizen car.</summary>
        CitizenCar = 12,
    }
}
