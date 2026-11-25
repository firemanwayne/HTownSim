#nullable enable
using System;
using System.Collections.Generic;
using HTownSim.Runtime.Incidents.Models;

namespace HTownSim.Runtime.Incidents.Managers
{
    /// <summary>
    /// Decides whether a live incident goes to the player (pure C#). First: the City lists one of the player's unit codes on it.
    /// Otherwise first-due: the incident is inside the map, the nearest of the game's stations is the player's, and the incident
    /// type is one the player's kind of rig runs. Also maps unit names (Engine 07) to HFD unit codes (E007).
    /// </summary>
    internal static class LiveDispatchRule
    {
        /// <summary>Reason shown when the player's unit is listed on the incident.</summary>
        internal const string UnitReason = "YOUR UNIT";

        /// <summary>Reason shown for a first-due dispatch.</summary>
        internal const string FirstDueReason = "FIRST DUE";

        /// <summary>Words in an incident type that a fire rig runs on first-due.</summary>
        private static readonly string[] FireWords = { "FIRE", "ALARM", "SMOKE", "GAS", "HAZ", "RESCUE", "ELEVATOR", "WATER", "WIRES", "EXPLOSION", "COLLAPSE" };

        /// <summary>Words in an incident type that an EMS unit runs on first-due.</summary>
        private static readonly string[] EmsWords = { "EMS", "MEDICAL", "CARDIAC", "OVERDOSE", "INJUR" };

        /// <summary>The reason to send the player (UnitReason or FirstDueReason), or empty when the player is not sent.</summary>
        /// <param name="record">The incident.</param>
        /// <param name="context">The player.</param>
        /// <param name="nearestStationIndex">The station nearest the incident, or -1 when the incident is outside the map.</param>
        internal static string Reason(LiveIncidentRecord record, LiveDispatchContext context, int nearestStationIndex)
        {
            foreach (var unit in record.Units)
            {
                foreach (var code in context.UnitCodes)
                {
                    if (string.Equals(unit, code, StringComparison.OrdinalIgnoreCase))
                    {
                        return UnitReason;
                    }
                }
            }

            if (nearestStationIndex < 0 || nearestStationIndex != context.StationIndex)
            {
                return "";
            }

            return TypeFits(record, context.Kind) ? FirstDueReason : "";
        }

        /// <summary>True when the player's kind of rig runs this type of incident.</summary>
        internal static bool TypeFits(LiveIncidentRecord record, LiveUnitKind kind)
        {
            var type = record.IncidentType.ToUpperInvariant();

            return kind switch
            {
                LiveUnitKind.Ems => ContainsAny(type, EmsWords),
                LiveUnitKind.Chief => type.Contains("FIRE") || (int.TryParse(record.AlarmLevel, out var level) && level >= 1),
                _ => record.Agency == "FD" && ContainsAny(type, FireWords),
            };
        }

        /// <summary>
        /// The HFD unit codes for a unit name: "Engine 07" is E007, "Ladder 16" L016, "Ambulance 16" A016, "Medic 08" M008,
        /// "Supervisor 16" AS016 (ambulance supervisor; also MS016), "District Chief 08" DC08 (also DC008, D008, B008).
        /// </summary>
        internal static IReadOnlyList<string> UnitCodes(string unitName)
        {
            var name = unitName.Trim();

            var space = name.LastIndexOf(' ');

            if (space < 0 || !int.TryParse(name.Substring(space + 1), out var number))
            {
                return Array.Empty<string>();
            }

            var kind = name.Substring(0, space).ToUpperInvariant();

            var digits = number.ToString("000");

            return kind switch
            {
                "ENGINE" => new[] { "E" + digits },
                "LADDER" => new[] { "L" + digits },
                "AMBULANCE" => new[] { "A" + digits },
                "MEDIC" => new[] { "M" + digits },
                "SUPERVISOR" => new[] { "AS" + digits, "MS" + digits },
                "DISTRICT CHIEF" => new[] { "DC" + number.ToString("00"), "DC" + digits, "D" + digits, "B" + digits },
                _ => Array.Empty<string>(),
            };
        }

        /// <summary>The kind of rig for a unit name.</summary>
        internal static LiveUnitKind KindOf(string unitName)
        {
            var upper = unitName.ToUpperInvariant();

            if (upper.StartsWith("DISTRICT CHIEF"))
            {
                return LiveUnitKind.Chief;
            }

            return upper.StartsWith("AMBULANCE") || upper.StartsWith("MEDIC") || upper.StartsWith("SUPERVISOR") ? LiveUnitKind.Ems : LiveUnitKind.Fire;
        }

        private static bool ContainsAny(string text, string[] words)
        {
            foreach (var word in words)
            {
                if (text.Contains(word))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
