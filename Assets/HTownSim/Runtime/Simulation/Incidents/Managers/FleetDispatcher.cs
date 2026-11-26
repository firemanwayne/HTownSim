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
    /// incident (E007, L016, A008 ...): each listed unit goes to the game unit of that name when the game has it and it is free. A listed
    /// unit the game has but that is busy gets nobody. A listed engine, ladder or medic unit the game does not have can be covered by the
    /// closest free game unit of the same type (when substitution is on); other listed units the game does not have (Ambulance 07, say) get
    /// nobody. An incident that lists no unit gets no unit until the City lists one.
    /// </summary>
    internal static class FleetDispatcher
    {
        /// <summary>The most units one fire or EMS incident gets, however many the City lists. Raise it to send big responses in full; lower it to cap them.</summary>
        internal const int MaximumUnits = 8;

        /// <summary>The most patrol units (officers) one police incident gets, as many as the City lists up to this number. Raise it for bigger police responses; lower it for fewer officers per call.</summary>
        internal const int MaximumOfficers = 4;

        /// <summary>The listed-code letters that can be covered by another unit of the same type, and the unit-name start they map to. Add a pair to cover another type; remove one to stop covering it. Ambulance codes (A) are left out on purpose: a listed ambulance the game lacks gets nobody.</summary>
        private static readonly KeyValuePair<string, string>[] CoverPrefixes =
        {
            new KeyValuePair<string, string>("E", "Engine"),
            new KeyValuePair<string, string>("L", "Ladder"),
            new KeyValuePair<string, string>("M", "Medic"),
        };

        /// <summary>The game units named by the incident's listed unit codes that are free, in the order listed, plus same-type cover for listed engines, ladders and medic units the game lacks.</summary>
        /// <param name="record">The incident.</param>
        /// <param name="candidates">The units the game has.</param>
        /// <param name="request">Where the incident is and whether same-type cover is allowed.</param>
        internal static IReadOnlyList<FleetCandidate> Match(LiveIncidentRecord record, IReadOnlyList<FleetCandidate> candidates, MatchRequest request)
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

                    continue;
                }

                if (request.Substitute && !candidates.Any(candidate => LiveDispatchRule.UnitCodes(candidate.Name).Contains(code, StringComparer.OrdinalIgnoreCase)))
                {
                    var cover = CoverFor(code, request.Target, candidates, chosen);

                    if (cover != null)
                    {
                        chosen.Add(cover);
                    }
                }
            }

            return chosen;
        }

        /// <summary>The closest free game unit of the same type as a listed code the game does not have, or null when the type is not covered or none is free.</summary>
        private static FleetCandidate? CoverFor(string code, Vector2 target, IReadOnlyList<FleetCandidate> candidates, List<FleetCandidate> chosen)
        {
            var prefix = CoverPrefixes.FirstOrDefault(pair => code.StartsWith(pair.Key, StringComparison.OrdinalIgnoreCase) && code.Length > pair.Key.Length && char.IsDigit(code[pair.Key.Length])).Value;

            if (prefix == null)
            {
                return null;
            }

            return candidates
                .Where(candidate => !candidate.Busy && !chosen.Contains(candidate) && candidate.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(candidate => Vector2.Distance(candidate.Position, target))
                .FirstOrDefault();
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
