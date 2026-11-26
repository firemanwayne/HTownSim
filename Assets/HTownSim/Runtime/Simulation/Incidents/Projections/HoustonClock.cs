#nullable enable
using System;

namespace HTownSim.Runtime.Incidents.Projections
{
    /// <summary>
    /// The current time in Houston (Central time). The City's incident page shows the time a call was opened in Houston time, so an
    /// incident's age is worked out against this and not against the computer's own clock, which may be set to another zone.
    /// </summary>
    internal static class HoustonClock
    {
        /// <summary>The zone ids to look for: Windows names it "Central Standard Time", other systems "America/Chicago".</summary>
        private static readonly string[] ZoneIds = { "Central Standard Time", "America/Chicago" };

        private static readonly TimeZoneInfo Zone = FindZone();

        /// <summary>The time now in Houston.</summary>
        internal static DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Zone);

        private static TimeZoneInfo FindZone()
        {
            foreach (var id in ZoneIds)
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById(id);
                }
                catch (TimeZoneNotFoundException)
                {
                    // Try the next name.
                }
                catch (InvalidTimeZoneException)
                {
                    // Try the next name.
                }
            }

            // No Central zone on this system: the computer's own zone is the best guess.
            return TimeZoneInfo.Local;
        }
    }
}
