#nullable enable
using System.Collections.Generic;
using HTownSim.Runtime.Incidents.Models;
using HTownSim.Runtime.Incidents.Projections;
using UnityEngine;

namespace HTownSim.Runtime.Incidents.Sources
{
    /// <summary>
    /// Invents a call when nothing real is waiting, replacing the old random spawns. It only builds the standard payload (the
    /// coordinates are the site's position converted to latitude and longitude); the manager then treats it like any other call.
    /// </summary>
    internal sealed class FallbackIncidentSource
    {
        /// <summary>The source name stamped on the calls.</summary>
        internal const string SourceName = "fallback";

        private const int MinSeverity = 2;

        private const int MaxSeverityExclusive = 5;

        private const int LadderSeverity = 3;

        private const int MultiUnitMedicalSeverity = 4;

        private int _counter;

        /// <summary>Makes one call at an eligible site, always asking for the player's own apparatus; null when no site is far enough from the player.</summary>
        internal IncidentPayload? Create(FallbackRequest request)
        {
            var site = ChooseSite(request);

            if (site < 0)
            {
                return null;
            }

            _counter++;

            var geo = GeoProjection.ToGeo(request.Sites[site]);

            var severity = Random.Range(MinSeverity, MaxSeverityExclusive);

            var medical = request.PlayerUnit == IncidentUnit.Ambulance;

            var address = site < request.Streets.Count ? request.Streets[site] : "";

            var parts = new IncidentPayloadParts(
                "FB-" + _counter.ToString("D4"),
                medical ? "EMS" : "HFD",
                medical ? "MEDICAL" : "STRUCTURE_FIRE",
                geo,
                severity,
                UnitsFor(request.PlayerUnit, severity),
                address,
                SourceName);

            return new IncidentPayload(parts);
        }

        /// <summary>The site to use: the preferred one, else a random one that is not excluded and is far enough from the player.</summary>
        private static int ChooseSite(FallbackRequest request)
        {
            if (request.PreferredSite >= 0 && request.PreferredSite < request.Sites.Count)
            {
                return request.PreferredSite;
            }

            var candidates = new List<int>();

            for (var i = 0; i < request.Sites.Count; i++)
            {
                if (i != request.ExcludedSite && Vector3.Distance(request.PlayerPosition, request.Sites[i]) >= request.MinimumDistance)
                {
                    candidates.Add(i);
                }
            }

            return candidates.Count == 0 ? -1 : candidates[Random.Range(0, candidates.Count)];
        }

        /// <summary>A realistic assignment for the severity that always includes the player's unit.</summary>
        private static string[] UnitsFor(IncidentUnit player, int severity)
        {
            var units = new List<string>();

            if (player == IncidentUnit.Ambulance)
            {
                units.Add("AMBULANCE");

                if (severity >= MultiUnitMedicalSeverity)
                {
                    units.Add("ENGINE");
                }

                return units.ToArray();
            }

            units.Add("ENGINE");

            if (severity >= LadderSeverity || player == IncidentUnit.Ladder)
            {
                units.Add("LADDER");
            }

            return units.ToArray();
        }
    }
}
