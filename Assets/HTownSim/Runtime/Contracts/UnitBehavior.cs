#nullable enable
using System;

namespace HTownSim.Runtime
{
    /// <summary>
    /// How a kind of vehicle's lights flash, which siren tones it has, and how it drives. Every unit of a kind shares one set (all
    /// engines flash, sound and drive alike; ladders and ambulances have their own), and the player's truck and the computer-driven
    /// units read the same values, so they always agree. A unit's behaviour comes from its radio name
    /// (<see cref="ApparatusProfileValues.Behavior"/>); to retune a kind, change its row below. What a unit is fitted with (seats, whether
    /// it transports patients, lenses, wheels) is in its <see cref="ApparatusProfileValues"/>.
    /// </summary>
    internal sealed class UnitBehavior
    {
        private static readonly string[] AllTones = { "Wail", "Yelp", "Phaser" };

        /// <summary>The kind of vehicle (Engine, Ladder, Ambulance, Chief, Supervisor or Patrol), for logs.</summary>
        internal string Kind { get; private set; } = "";

        /// <summary>Flash cycles per second of the warning lights. Raise it for faster flashing; lower it for slower.</summary>
        internal float FlashHertz { get; private set; } = 1.6f;

        /// <summary>How many times a light group pulses in its half of the cycle, evenly spaced (2 is the double pulse of LED lens bars; 1 a single flash).</summary>
        internal int FlashPulses { get; private set; } = 2;

        /// <summary>The fraction (0 to 1) of its half of the cycle that each pulse stays lit (never more than 1 divided by the pulse count). Raise it for longer pulses; lower it for sharper ones.</summary>
        internal float FlashPulseDuty { get; private set; } = 0.32f;

        /// <summary>The slower flash rate (cycles per second) of the front light bar and rear lamps of an engine.</summary>
        internal float FrontRearHertz { get; private set; } = 1.15f;

        /// <summary>The siren tones, by recording name in Resources/SirenAudio: the first two alternate, a third is the rarer pier tone.</summary>
        internal string[] SirenTones { get; private set; } = AllTones;

        /// <summary>Volume (0 to 1) of the player's own siren.</summary>
        internal float SirenVolume { get; private set; } = 0.55f;

        /// <summary>Volume (0 to 1) of a computer-driven unit's siren at its source (it is heard from where the unit is, so it is louder than the player's own).</summary>
        internal float AiSirenVolume { get; private set; } = 0.8f;

        /// <summary>Volume (0 to 1) of the horn: the air horn of an engine or ladder, the electric horn of the others.</summary>
        internal float HornVolume { get; private set; } = 0.7f;

        /// <summary>The player's top forward speed, metres per second (22 is about 49 mph).</summary>
        internal float TopSpeed { get; private set; } = 22f;

        /// <summary>The player's top reverse speed, metres per second.</summary>
        internal float ReverseSpeed { get; private set; } = 6f;

        /// <summary>How fast the player's unit picks up speed, in metres per second every second.</summary>
        internal float Acceleration { get; private set; } = 8.5f;

        /// <summary>The top speed (metres per second) of a computer-driven unit responding with lights and siren.</summary>
        internal float ResponseSpeed { get; private set; } = 17f;

        /// <summary>The speed (metres per second) of a computer-driven unit driving back to its station.</summary>
        internal float ReturnSpeed { get; private set; } = 11f;

        /// <summary>How fast a computer-driven unit picks up speed, in metres per second every second.</summary>
        internal float AiAcceleration { get; private set; } = 4.5f;

        /// <summary>How hard a computer-driven unit slows for corners and the end of its route, in metres per second every second.</summary>
        internal float Braking { get; private set; } = 9f;

        /// <summary>How fast (degrees per second) a computer-driven unit turns to face the way it is going.</summary>
        internal float TurnDegreesPerSecond { get; private set; } = 160f;

        /// <summary>
        /// Fire engines: the twin-LED bar (a single flash per group at 1.8 Hz, front and rear lamps slower). Also the behaviour of any
        /// unit that is none of the kinds below (a citizen car), so nothing changes for it.
        /// </summary>
        internal static readonly UnitBehavior Engine = new UnitBehavior
        {
            Kind = "Engine",
            FlashHertz = 1.8f,
            FlashPulses = 1,
            FlashPulseDuty = 0.9f,
        };

        /// <summary>Ladder trucks: lens-mesh lights (a double pulse per group at 1.6 Hz); heavier than an engine, so a lower top speed, slower build-up and wider turns.</summary>
        internal static readonly UnitBehavior Ladder = new UnitBehavior
        {
            Kind = "Ladder",
            TopSpeed = 20f,
            ReverseSpeed = 5f,
            Acceleration = 7f,
            ResponseSpeed = 15f,
            ReturnSpeed = 10f,
            AiAcceleration = 3.5f,
            Braking = 8f,
            TurnDegreesPerSecond = 130f,
        };

        /// <summary>Ambulances and medic units: lens-mesh lights, a lighter and quicker truck than an engine.</summary>
        internal static readonly UnitBehavior Ambulance = new UnitBehavior
        {
            Kind = "Ambulance",
            TopSpeed = 24f,
            ReverseSpeed = 6.5f,
            Acceleration = 9.5f,
            ResponseSpeed = 19f,
            ReturnSpeed = 12f,
            AiAcceleration = 5.5f,
            Braking = 10f,
            TurnDegreesPerSecond = 180f,
        };

        /// <summary>District chief pickups, battalion units and EMS supervisor SUVs: lens-mesh lights, quick and nimble.</summary>
        internal static readonly UnitBehavior Chief = new UnitBehavior
        {
            Kind = "Chief",
            TopSpeed = 26f,
            ReverseSpeed = 7f,
            Acceleration = 10.5f,
            ResponseSpeed = 20f,
            ReturnSpeed = 13f,
            AiAcceleration = 6f,
            Braking = 10f,
            TurnDegreesPerSecond = 200f,
        };

        /// <summary>Police patrol cars: the quickest and most agile.</summary>
        internal static readonly UnitBehavior Patrol = new UnitBehavior
        {
            Kind = "Patrol",
            TopSpeed = 28f,
            ReverseSpeed = 8f,
            Acceleration = 11.5f,
            ResponseSpeed = 22f,
            ReturnSpeed = 14f,
            AiAcceleration = 6.5f,
            Braking = 11f,
            TurnDegreesPerSecond = 220f,
        };

        /// <summary>The behaviour of the kind of vehicle a radio name (Engine 07, Ladder 16, Medic 08, District Chief 08, HPD Unit 1) belongs to.</summary>
        internal static UnitBehavior For(string unitName)
        {
            if (unitName.StartsWith("Ladder", StringComparison.OrdinalIgnoreCase))
            {
                return Ladder;
            }

            if (unitName.StartsWith("Ambulance", StringComparison.OrdinalIgnoreCase) || unitName.StartsWith("Medic", StringComparison.OrdinalIgnoreCase))
            {
                return Ambulance;
            }

            if (unitName.StartsWith("District Chief", StringComparison.OrdinalIgnoreCase)
                || unitName.StartsWith("Battalion", StringComparison.OrdinalIgnoreCase)
                || unitName.StartsWith("Supervisor", StringComparison.OrdinalIgnoreCase))
            {
                return Chief;
            }

            if (unitName.StartsWith("HPD", StringComparison.OrdinalIgnoreCase) || unitName.StartsWith("Patrol", StringComparison.OrdinalIgnoreCase))
            {
                return Patrol;
            }

            return Engine;
        }
    }

    /// <summary>The flash pattern shared by every light group (see <see cref="UnitBehavior"/>).</summary>
    internal static class FlashPattern
    {
        /// <summary>
        /// True while a group's pulse is lit: <paramref name="half"/> (0 to 1) is how far through its half of the cycle the group is;
        /// the pulses are evenly spaced in that half and each stays lit for <paramref name="duty"/> of it.
        /// </summary>
        internal static bool IsLit(float half, int pulses, float duty)
        {
            var spacing = 1f / Math.Max(1, pulses);

            return half % spacing < duty;
        }
    }
}
