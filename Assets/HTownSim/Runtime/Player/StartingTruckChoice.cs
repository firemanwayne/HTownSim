#nullable enable
using UnityEngine;

namespace HTownSim.Runtime
{
    /// <summary>Remembers which apparatus the player picked on the start screen and describes the stations and apparatus.</summary>
    internal static class StartingTruckChoice
    {
        /// <summary>The PlayerPrefs key holding the last picked apparatus.</summary>
        private const string PrefKey = "HTownSim.StartingTruck";

        private static readonly StartingTruck[] Station7Trucks = { StartingTruck.Engine, StartingTruck.Ladder, StartingTruck.Ambulance };

        private static readonly StartingTruck[] Station8Trucks = { StartingTruck.Engine08, StartingTruck.Engine01, StartingTruck.Medic08, StartingTruck.DistrictChief08 };

        private static readonly StartingTruck[] Station16Trucks = { StartingTruck.Engine16, StartingTruck.Ladder16, StartingTruck.Ambulance16, StartingTruck.Supervisor16 };

        /// <summary>The stations, in the order the start screen lists them.</summary>
        internal static readonly StartingStation[] Stations = { StartingStation.Station7, StartingStation.Station8, StartingStation.Station16 };

        /// <summary>The agencies, in the order the start screen lists them.</summary>
        internal static readonly StartingAgency[] Agencies = { StartingAgency.Hfd, StartingAgency.Hpd, StartingAgency.Citizen };

        /// <summary>True once the player has dealt with the start screen this session, so it is not shown again after the reload.</summary>
        internal static bool Decided => _sessionChoice.HasValue;

        private static StartingTruck? _sessionChoice;

        /// <summary>The selection for this run, falling back to the saved preference when opening the city directly.</summary>
        internal static StartingTruck? BootstrapChoice => _sessionChoice ?? Saved;

        /// <summary>The last apparatus the player picked, or null when none was ever picked (the scene's own truck is used).</summary>
        internal static StartingTruck? Saved
        {
            get
            {
                if (!PlayerPrefs.HasKey(PrefKey))
                {
                    return null;
                }

                var value = PlayerPrefs.GetInt(PrefKey);

                return System.Enum.IsDefined(typeof(StartingTruck), value) ? (StartingTruck)value : null;
            }
        }

        /// <summary>Stores the pick for the next scene load and for later sessions.</summary>
        internal static void Select(StartingTruck truck)
        {
            _sessionChoice = truck;

            PlayerPrefs.SetInt(PrefKey, (int)truck);

            PlayerPrefs.Save();
        }

        /// <summary>Whose vehicle an apparatus is.</summary>
        internal static StartingAgency AgencyOf(StartingTruck truck)
        {
            return truck switch
            {
                StartingTruck.PatrolHpd => StartingAgency.Hpd,
                StartingTruck.CitizenCar => StartingAgency.Citizen,
                _ => StartingAgency.Hfd,
            };
        }

        /// <summary>The vehicles of an agency that has no stations (HPD and civilian), in the order the start screen lists them.</summary>
        internal static StartingTruck[] VehiclesOf(StartingAgency agency)
        {
            return agency == StartingAgency.Hpd ? new[] { StartingTruck.PatrolHpd } : new[] { StartingTruck.CitizenCar };
        }

        /// <summary>The agency's short name, the title of its card on the start screen.</summary>
        internal static string AgencyShortName(StartingAgency agency)
        {
            return agency switch
            {
                StartingAgency.Hpd => "HPD",
                StartingAgency.Citizen => "Citizen",
                _ => "HFD",
            };
        }

        /// <summary>The agency's name, as shown on the start screen.</summary>
        internal static string AgencyName(StartingAgency agency)
        {
            return agency switch
            {
                StartingAgency.Hpd => "Houston Police Department",
                StartingAgency.Citizen => "Citizen",
                _ => "Houston Fire Department",
            };
        }

        /// <summary>The agency's one-line description on the start screen.</summary>
        internal static string AgencyDescription(StartingAgency agency)
        {
            return agency switch
            {
                StartingAgency.Hpd => "Patrol from the Midtown substation: lightbar and siren, police calls.",
                StartingAgency.Citizen => "A citizen car. No siren, no lightbar, no calls: just drive the city.",
                _ => "Engines, ladders, medic units and chiefs from Stations 7, 8 and 16.",
            };
        }

        /// <summary>The station an apparatus lives at.</summary>
        internal static StartingStation StationOf(StartingTruck truck)
        {
            if (truck is StartingTruck.Engine16 or StartingTruck.Ladder16 or StartingTruck.Ambulance16 or StartingTruck.Supervisor16)
            {
                return StartingStation.Station16;
            }

            return truck is StartingTruck.Engine08 or StartingTruck.Engine01 or StartingTruck.Medic08 or StartingTruck.DistrictChief08
                ? StartingStation.Station8
                : StartingStation.Station7;
        }

        /// <summary>The apparatus at a station, in the order the start screen lists them.</summary>
        internal static StartingTruck[] TrucksAt(StartingStation station)
        {
            return station switch
            {
                StartingStation.Station8 => Station8Trucks,
                StartingStation.Station16 => Station16Trucks,
                _ => Station7Trucks,
            };
        }

        /// <summary>The station's name, as shown on the start screen.</summary>
        internal static string StationName(StartingStation station)
        {
            return station switch
            {
                StartingStation.Station8 => "Station 8",
                StartingStation.Station16 => "Station 16",
                _ => "Station 7",
            };
        }

        /// <summary>The station's address and what it houses.</summary>
        internal static string StationDescription(StartingStation station)
        {
            return station switch
            {
                StartingStation.Station8 => "1919 Louisiana St, north-east of Midtown. Engine 08, Engine 01, Medic 08 and District Chief 08.",
                StartingStation.Station16 => "1700 Richmond Ave, in Montrose. Engine 16, Ladder 16, Ambulance 16 and Supervisor 16.",
                _ => "1402 Elgin St, in Midtown. Engine 07, Ladder 07 and Ambulance 07.",
            };
        }

        /// <summary>The unit's radio name, as shown on the start screen and the HUD.</summary>
        internal static string DisplayName(StartingTruck truck)
        {
            return truck switch
            {
                StartingTruck.Ladder => "Ladder 07",
                StartingTruck.Ambulance => "Ambulance 07",
                StartingTruck.Engine08 => "Engine 08",
                StartingTruck.Engine01 => "Engine 01",
                StartingTruck.Medic08 => "Medic 08",
                StartingTruck.Engine16 => "Engine 16",
                StartingTruck.Ladder16 => "Ladder 16",
                StartingTruck.Ambulance16 => "Ambulance 16",
                StartingTruck.Supervisor16 => "Supervisor 16",
                StartingTruck.DistrictChief08 => "District Chief 08",
                StartingTruck.PatrolHpd => "HPD Patrol 46837",
                StartingTruck.CitizenCar => "Citizen Car",
                _ => "Engine 07",
            };
        }

        /// <summary>The unit's HFD designation, shown on the start screen: E007, L016, DC08, AS016 ...</summary>
        internal static string Designation(StartingTruck truck)
        {
            return truck switch
            {
                StartingTruck.Ladder => "L007",
                StartingTruck.Ambulance => "A007",
                StartingTruck.Engine08 => "E008",
                StartingTruck.Engine01 => "E001",
                StartingTruck.Medic08 => "M008",
                StartingTruck.Engine16 => "E016",
                StartingTruck.Ladder16 => "L016",
                StartingTruck.Ambulance16 => "A016",
                StartingTruck.Supervisor16 => "AS016",
                StartingTruck.DistrictChief08 => "DC08",
                StartingTruck.PatrolHpd => "46837",
                StartingTruck.CitizenCar => "CIT",
                _ => "E007",
            };
        }

        /// <summary>A one-line description of what the apparatus does in the game.</summary>
        internal static string Description(StartingTruck truck)
        {
            return truck switch
            {
                StartingTruck.Ladder => "Aerial ladder. Wide turning circle; set the outriggers to raise the ladder and flow water from the tip.",
                StartingTruck.Ambulance => "Medic unit. Tight turns and EMS dispatch tones, but it carries no water.",
                StartingTruck.Engine08 => "Station 8's pumper: the same rig as Engine 07, with a hose line and the full water supply.",
                StartingTruck.Engine01 => "A second pumper at Station 8: the same rig as Engine 07, with a hose line and the full water supply.",
                StartingTruck.Medic08 => "Station 8's medic unit: the same rig as Ambulance 07, with EMS tones and no water.",
                StartingTruck.Engine16 => "Station 16's pumper: the same rig as Engine 07, with a hose line and the full water supply.",
                StartingTruck.Ladder16 => "Station 16's aerial: the same rig as Ladder 07. Wide turning circle; set the outriggers to raise the ladder.",
                StartingTruck.Ambulance16 => "Station 16's medic unit: the same rig as Ambulance 07, with EMS tones and no water.",
                StartingTruck.Supervisor16 => "The EMS supervisor's Chevrolet Suburban: quick and tight, EMS dispatch tones, no water.",
                StartingTruck.DistrictChief08 => "The district chief's command pickup: quick, electric horn, blinkers and a full light package, no water.",
                StartingTruck.PatrolHpd => "Houston Police patrol SUV: quick and tight, lightbar and siren, blinkers, brake and reverse lamps, answers police calls.",
                StartingTruck.CitizenCar => "A citizen car: headlights, blinkers, brake and reverse lamps. No siren, no lightbar, no calls.",
                _ => "Pumper. The quickest to get to a fire, with a hose line and the full water supply.",
            };
        }

        /// <summary>Clears per-session state when Enter Play Mode skips the domain reload.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession()
        {
            _sessionChoice = null;
        }
    }
}
