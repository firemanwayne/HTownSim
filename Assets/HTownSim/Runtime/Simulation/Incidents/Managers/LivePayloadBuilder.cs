#nullable enable
using HTownSim.Runtime.Incidents.Models;

namespace HTownSim.Runtime.Incidents.Managers
{
    /// <summary>
    /// Turns a City incident into the game's standard incident payload (pure C#), so a live dispatch plays exactly like any other
    /// call: agency and call type from the City's incident type, severity from the alarm level, and the player's rig as the
    /// required unit so the banner shows them assigned.
    /// </summary>
    internal static class LivePayloadBuilder
    {
        /// <summary>The source name live calls carry.</summary>
        internal const string SourceName = "coh-live";

        /// <summary>Builds the payload for a dispatch.</summary>
        /// <param name="record">The City incident.</param>
        /// <param name="requiredUnit">The player's rig in payload terms (ENGINE, LADDER, AMBULANCE, BATTALION).</param>
        internal static IncidentPayload Build(LiveIncidentRecord record, string requiredUnit)
        {
            var type = record.IncidentType.ToUpperInvariant();

            var ems = type.Contains("EMS") || type.Contains("MEDICAL");

            var agency = record.Agency == "PD" ? "HPD" : ems ? "EMS" : "HFD";

            var address = record.CrossStreet.Length > 0 ? record.Address + " / " + record.CrossStreet.Replace("BLK ", "") : record.Address;

            return new IncidentPayload(new IncidentPayloadParts(
                record.IncidentId,
                agency,
                CallType(type, ems),
                record.Geo,
                Severity(record.AlarmLevel, ems),
                new[] { requiredUnit },
                address,
                SourceName));
        }

        /// <summary>The game call type for a City incident type; the City text itself when there is no match.</summary>
        internal static string CallType(string type, bool ems)
        {
            if (ems)
            {
                return "MEDICAL";
            }

            if (type.Contains("ALARM"))
            {
                return "FIRE_ALARM";
            }

            if (type.Contains("FIRE"))
            {
                if (type.Contains("VEHICLE") || type.Contains("AUTO") || type.Contains("CAR"))
                {
                    return "VEHICLE_FIRE";
                }

                return type.Contains("GRASS") || type.Contains("BRUSH") || type.Contains("TRASH") || type.Contains("OUTSIDE") ? "BRUSH_FIRE" : "STRUCTURE_FIRE";
            }

            if (type.Contains("CRASH") || type.Contains("ACCIDENT") || type.Contains("MVA"))
            {
                return "TRAFFIC_ACCIDENT";
            }

            if (type.Contains("HAZ") || type.Contains("GAS") || type.Contains("SPILL"))
            {
                return "HAZMAT";
            }

            return type.Contains("RESCUE") || type.Contains("ELEVATOR") ? "RESCUE" : type;
        }

        /// <summary>Severity 1 to 5 from the alarm level (0 or blank is a routine call).</summary>
        internal static int Severity(string alarmLevel, bool ems)
        {
            if (!int.TryParse(alarmLevel, out var level) || level <= 0)
            {
                return ems ? 2 : 2;
            }

            return level >= 3 ? 5 : level + 2;
        }
    }
}
