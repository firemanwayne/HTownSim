#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using HTownSim.Runtime.Incidents.Models;
using UnityEngine;

namespace HTownSim.Runtime.Incidents.Managers
{
    /// <summary>
    /// Matches an incident to the units the game has (pure logic over the units it is given). The City lists the units it sent to each
    /// incident (E007, L016, A008 ...): each listed unit goes to the game unit of that name when the game has it and it is free. A unit
    /// the game does not have (Ambulance 07, say) gets nobody: no other unit stands in for it. An incident that lists no unit gets no unit
    /// until the City lists one.
    /// </summary>
    internal static class FleetDispatcher
    {
        /// <summary>The most units one fire or EMS incident gets, however many the City lists. Raise it to send big responses in full; lower it to cap them.</summary>
        internal const int MaximumUnits = 8;

        /// <summary>The most patrol units (officers) one police incident gets, as many as the City lists up to this number. Raise it for bigger police responses; lower it for fewer officers per call.</summary>
        internal const int MaximumOfficers = 4;

        /// <summary>The game units named by the incident's listed unit codes that are free, in the order listed.</summary>
        /// <param name="record">The incident.</param>
        /// <param name="candidates">The units the game has.</param>
        internal static IReadOnlyList<FleetCandidate> Match(LiveIncidentRecord record, IReadOnlyList<FleetCandidate> candidates)
        {
            var chosen = new List<FleetCandidate>();

            foreach (var code in record.Units)
            {
                if (chosen.Count >= MaximumUnits)
                {
                    break;
                }

                var unit = candidates.FirstOrDefault(candidate => !candidate.Busy && !chosen.Contains(candidate) && LiveDispatchRule.UnitCodes(candidate.Name).Contains(code, StringComparer.OrdinalIgnoreCase));

                if (unit != null)
                {
                    chosen.Add(unit);
                }
            }

            return chosen;
        }

        /// <summary>
        /// The patrol units to send to a police incident: as many as the City lists on it, up to <see cref="MaximumOfficers"/>, the closest free
        /// ones first. The City's police unit numbers mean nothing in the game, so only how many are listed counts.
        /// </summary>
        /// <param name="record">The incident.</param>
        /// <param name="target">Where it is (ground x, z).</param>
        /// <param name="candidates">The units the game has.</param>
        internal static IReadOnlyList<FleetCandidate> MatchPolice(LiveIncidentRecord record, Vector2 target, IReadOnlyList<FleetCandidate> candidates)
        {
            var wanted = Math.Min(MaximumOfficers, record.Units.Length);

            return candidates
                .Where(candidate => candidate.Kind == LiveUnitKind.Police && !candidate.Busy)
                .OrderBy(candidate => Vector2.Distance(candidate.Position, target))
                .Take(wanted)
                .ToList();
        }
    }
}
