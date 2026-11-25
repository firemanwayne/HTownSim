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
    /// incident (E007, L016, A008 ...): each listed unit goes to the game unit of that name when the game has it and it is free;
    /// otherwise to the closest free game unit of the same kind (an engine for an engine, a ladder for a ladder, an ambulance or medic
    /// unit for an ambulance or a medic). An incident that lists no usable unit goes to the closest free unit that suits its type.
    /// Only ambulances and medic units count as transport units, so an EMS event is never matched to anything else.
    /// </summary>
    internal static class FleetDispatcher
    {
        /// <summary>The most units one incident gets.</summary>
        internal const int MaximumUnits = 8;

        /// <summary>Incident words that need a ladder as well as an engine.</summary>
        private static readonly string[] LadderWords = { "STRUCTURE", "HIGH RISE", "HIGHRISE", "ELEVATOR", "RESCUE", "COLLAPSE" };

        /// <summary>What a unit is, for matching (the letter in its code).</summary>
        internal enum UnitCategory
        {
            /// <summary>Not a unit the game has (or no unit).</summary>
            None,

            /// <summary>An engine, and the fire units that stand in for one (squads, rescues, hazmat).</summary>
            Engine,

            /// <summary>A ladder.</summary>
            Ladder,

            /// <summary>An ambulance or a medic unit: the units that can take a patient to a hospital.</summary>
            Transport,

            /// <summary>An EMS supervisor.</summary>
            Supervisor,

            /// <summary>A district chief.</summary>
            Chief,
        }

        /// <summary>The units to send to an incident, closest first for each kind; empty when no free unit suits it.</summary>
        /// <param name="record">The incident.</param>
        /// <param name="target">Where it is (ground x, z).</param>
        /// <param name="candidates">The units the game has.</param>
        internal static IReadOnlyList<FleetCandidate> Match(LiveIncidentRecord record, Vector2 target, IReadOnlyList<FleetCandidate> candidates)
        {
            var chosen = new List<FleetCandidate>();

            foreach (var code in record.Units)
            {
                if (chosen.Count >= MaximumUnits)
                {
                    break;
                }

                var category = CategoryOfCode(code);

                if (category == UnitCategory.None)
                {
                    continue;
                }

                var unit = candidates.FirstOrDefault(candidate => !candidate.Busy && !chosen.Contains(candidate) && LiveDispatchRule.UnitCodes(candidate.Name).Contains(code, StringComparer.OrdinalIgnoreCase))
                    ?? Closest(candidates.Where(candidate => !candidate.Busy && !chosen.Contains(candidate) && CategoryOfName(candidate.Name) == category), target);

                if (unit != null)
                {
                    chosen.Add(unit);
                }
            }

            if (chosen.Count == 0)
            {
                var fallback = Choose(record, target, candidates);

                if (fallback != null)
                {
                    chosen.Add(fallback);
                }
            }

            return chosen;
        }

        /// <summary>The closest free unit that suits the incident's type, or null.</summary>
        internal static FleetCandidate? Choose(LiveIncidentRecord record, Vector2 target, IEnumerable<FleetCandidate> candidates)
        {
            return Closest(candidates.Where(candidate => !candidate.Busy && Suits(record, candidate)), target);
        }

        /// <summary>True when this unit is the kind that runs this type of incident on its own.</summary>
        internal static bool Suits(LiveIncidentRecord record, FleetCandidate candidate)
        {
            if (!LiveDispatchRule.TypeFits(record, candidate.Kind))
            {
                return false;
            }

            return candidate.Kind switch
            {
                // Only a unit that can take a patient to a hospital runs an EMS event on its own.
                LiveUnitKind.Ems => LiveDispatchRule.CanTransport(candidate.Name),
                LiveUnitKind.Fire => !candidate.Name.StartsWith("Ladder", StringComparison.OrdinalIgnoreCase) || NeedsLadder(record),
                LiveUnitKind.Police => true,
                _ => false,
            };
        }

        /// <summary>The kind of unit an HFD unit code names: E007 is an engine, L016 a ladder, A008 an ambulance, M008 a medic unit.</summary>
        internal static UnitCategory CategoryOfCode(string code)
        {
            var upper = code.Trim().ToUpperInvariant();

            if (upper.Length < 2)
            {
                return UnitCategory.None;
            }

            if (upper.StartsWith("AS", StringComparison.Ordinal) || upper.StartsWith("MS", StringComparison.Ordinal))
            {
                return UnitCategory.Supervisor;
            }

            if (upper.StartsWith("DC", StringComparison.Ordinal) || (upper[0] == 'B' && char.IsDigit(upper[1])) || (upper[0] == 'D' && char.IsDigit(upper[1])))
            {
                return UnitCategory.Chief;
            }

            return upper[0] switch
            {
                'E' => UnitCategory.Engine,
                'L' => UnitCategory.Ladder,
                'A' => UnitCategory.Transport,
                'M' => UnitCategory.Transport,

                // Squads, rescues, hazmat and tankers are fire units the game stands an engine in for.
                'S' or 'R' or 'H' or 'T' => upper.Length > 2 && char.IsLetter(upper[1]) || char.IsDigit(upper[1]) ? UnitCategory.Engine : UnitCategory.None,
                _ => UnitCategory.None,
            };
        }

        /// <summary>The kind of unit a game unit name is (Engine 16, Ladder 07, Ambulance 16, Medic 08 ...).</summary>
        internal static UnitCategory CategoryOfName(string name)
        {
            var upper = name.Trim().ToUpperInvariant();

            if (upper.StartsWith("ENGINE", StringComparison.Ordinal))
            {
                return UnitCategory.Engine;
            }

            if (upper.StartsWith("LADDER", StringComparison.Ordinal))
            {
                return UnitCategory.Ladder;
            }

            if (LiveDispatchRule.CanTransport(upper))
            {
                return UnitCategory.Transport;
            }

            if (upper.StartsWith("SUPERVISOR", StringComparison.Ordinal))
            {
                return UnitCategory.Supervisor;
            }

            return upper.StartsWith("DISTRICT CHIEF", StringComparison.Ordinal) ? UnitCategory.Chief : UnitCategory.None;
        }

        private static FleetCandidate? Closest(IEnumerable<FleetCandidate> candidates, Vector2 target)
        {
            FleetCandidate? best = null;

            var bestDistance = float.MaxValue;

            foreach (var candidate in candidates)
            {
                var distance = Vector2.Distance(candidate.Position, target);

                if (distance < bestDistance)
                {
                    bestDistance = distance;

                    best = candidate;
                }
            }

            return best;
        }

        private static bool NeedsLadder(LiveIncidentRecord record)
        {
            var type = record.IncidentType.ToUpperInvariant();

            return LadderWords.Any(word => type.Contains(word));
        }
    }
}
