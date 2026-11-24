#nullable enable
using UnityEngine;

namespace HTownSim.Runtime
{
    /// <summary>Remembers which apparatus the player picked on the start screen and describes each choice.</summary>
    internal static class StartingTruckChoice
    {
        /// <summary>The PlayerPrefs key holding the last picked apparatus.</summary>
        private const string PrefKey = "HTownSim.StartingTruck";

        /// <summary>True once the player has dealt with the start screen this session, so it is not shown again after the reload.</summary>
        internal static bool Decided { get; set; }

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
        internal static void Save(StartingTruck truck)
        {
            PlayerPrefs.SetInt(PrefKey, (int)truck);

            PlayerPrefs.Save();
        }

        /// <summary>The unit's radio name, as shown on the start screen and the HUD.</summary>
        internal static string DisplayName(StartingTruck truck)
        {
            return truck switch
            {
                StartingTruck.Ladder => "Ladder 07",
                StartingTruck.Ambulance => "Ambulance 07",
                _ => "Engine 07",
            };
        }

        /// <summary>A one-line description of what the apparatus does in the game.</summary>
        internal static string Description(StartingTruck truck)
        {
            return truck switch
            {
                StartingTruck.Ladder => "Aerial ladder. Wide turning circle; set the outriggers to raise the ladder and flow water from the tip.",
                StartingTruck.Ambulance => "Medic unit. Tight turns and EMS dispatch tones, but it carries no water.",
                _ => "Pumper. The quickest to get to a fire, with a hose line and the full water supply.",
            };
        }

        /// <summary>Clears per-session state when Enter Play Mode skips the domain reload.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession()
        {
            Decided = false;
        }
    }
}
