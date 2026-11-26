#nullable enable
using System;
using System.Collections.Generic;
using HTownSim.Runtime.Incidents.Models;
using HTownSim.Runtime.Incidents.Projections;
using UnityEngine;

namespace HTownSim.Runtime.Incidents.Parsers
{
    /// <summary>Turns incident JSON into validated <see cref="Incident"/> objects, or says why it cannot.</summary>
    internal static class IncidentParser
    {
        /// <summary>The longest incident id accepted, in characters; a longer id is refused. Raise it if the feed uses longer ids; lower it to be stricter.</summary>
        private const int MaxIdLength = 64;

        /// <summary>The lowest severity a parsed call may have; a lower value is clamped up to it.</summary>
        private const int MinSeverity = 1;

        /// <summary>The highest severity a parsed call may have; a higher value is clamped down to it.</summary>
        private const int MaxSeverity = 5;

        /// <summary>Marker that tells a batch (<c>{"incidents":[...]}</c>) from a single incident.</summary>
        private const string BatchKey = "\"incidents\"";

        /// <summary>Validates every incident in a JSON text: a single object, a batch object, or JSON Lines (one object per line).</summary>
        internal static List<IncidentParseResult> ParseJson(string json, string source)
        {
            var results = new List<IncidentParseResult>();

            if (string.IsNullOrWhiteSpace(json))
            {
                results.Add(IncidentParseResult.Refuse("", "empty payload"));

                return results;
            }

            var trimmed = json.Trim();

            var lines = trimmed.Split('\n');

            // JSON Lines: several lines that each hold a whole object.
            if (lines.Length > 1 && IsJsonLines(lines))
            {
                foreach (var line in lines)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        results.Add(ParseOne(line.Trim(), source));
                    }
                }

                return results;
            }

            if (trimmed.Contains(BatchKey))
            {
                try
                {
                    var batch = JsonUtility.FromJson<IncidentBatch>(trimmed);

                    if (batch.Incidents.Length > 0)
                    {
                        foreach (var payload in batch.Incidents)
                        {
                            results.Add(Parse(payload, source));
                        }

                        return results;
                    }
                }
                catch (ArgumentException exception)
                {
                    results.Add(IncidentParseResult.Refuse("", "invalid JSON: " + exception.Message));

                    return results;
                }
            }

            results.Add(ParseOne(trimmed, source));

            return results;
        }

        /// <summary>Validates one payload.</summary>
        internal static IncidentParseResult Parse(IncidentPayload payload, string source)
        {
            var id = payload.IncidentId.Trim();

            if (id.Length == 0)
            {
                return IncidentParseResult.Refuse(id, "incidentId is missing");
            }

            if (id.Length > MaxIdLength)
            {
                return IncidentParseResult.Refuse(id, "incidentId is longer than " + MaxIdLength + " characters");
            }

            if (!TryParseAgency(payload.Agency, out var agency))
            {
                return IncidentParseResult.Refuse(id, "agency must be HFD, EMS or HPD (got '" + payload.Agency + "')");
            }

            if (payload.CallType.Trim().Length == 0)
            {
                return IncidentParseResult.Refuse(id, "callType is missing");
            }

            var geo = payload.Coordinates;

            if (!IsValidCoordinate(geo))
            {
                return IncidentParseResult.Refuse(id, "coordinates are missing or out of range (lat " + geo.Latitude + ", lon " + geo.Longitude + ")");
            }

            if (payload.Severity < MinSeverity || payload.Severity > MaxSeverity)
            {
                return IncidentParseResult.Refuse(id, "severity must be " + MinSeverity + " to " + MaxSeverity + " (got " + payload.Severity + ")");
            }

            var callText = payload.CallType.Trim().Replace('_', ' ').ToUpperInvariant();

            var identity = new IncidentIdentity(id, agency, ParseCallType(payload.CallType), callText);

            var sourceName = payload.Source.Length > 0 ? payload.Source : source;

            var details = new IncidentDetails(payload.Severity, ParseUnits(payload.RequiredUnits), payload.Address.Trim(), sourceName);

            return IncidentParseResult.Accept(new Incident(new IncidentParts(identity, geo, GeoProjection.ToWorld(geo), details)));
        }

        /// <summary>Parses one JSON object into a payload and validates it.</summary>
        private static IncidentParseResult ParseOne(string json, string source)
        {
            try
            {
                return Parse(JsonUtility.FromJson<IncidentPayload>(json), source);
            }
            catch (ArgumentException exception)
            {
                return IncidentParseResult.Refuse("", "invalid JSON: " + exception.Message);
            }
        }

        /// <summary>True when every non-blank line looks like its own JSON object.</summary>
        private static bool IsJsonLines(string[] lines)
        {
            foreach (var line in lines)
            {
                var text = line.Trim();

                if (text.Length > 0 && (text[0] != '{' || text[text.Length - 1] != '}'))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>True for a finite latitude and longitude in range that are not the (0, 0) of a missing object.</summary>
        private static bool IsValidCoordinate(GeoPoint point)
        {
            var lat = point.Latitude;

            var lon = point.Longitude;

            if (double.IsNaN(lat) || double.IsNaN(lon) || double.IsInfinity(lat) || double.IsInfinity(lon))
            {
                return false;
            }

            if (lat < -90d || lat > 90d || lon < -180d || lon > 180d)
            {
                return false;
            }

            return lat != 0d || lon != 0d;
        }

        /// <summary>Reads HFD, EMS or HPD, with a few common spellings.</summary>
        private static bool TryParseAgency(string text, out IncidentAgency agency)
        {
            switch (Normalize(text))
            {
                case "HFD":
                case "FIRE":
                case "FIREDEPARTMENT":
                    agency = IncidentAgency.Hfd;

                    return true;
                case "EMS":
                case "MEDICAL":
                    agency = IncidentAgency.Ems;

                    return true;
                case "HPD":
                case "POLICE":
                    agency = IncidentAgency.Hpd;

                    return true;
                default:
                    agency = IncidentAgency.Hfd;

                    return false;
            }
        }

        /// <summary>Maps the call type text to a call type; anything unknown is <see cref="IncidentCallType.Other"/>.</summary>
        private static IncidentCallType ParseCallType(string text)
        {
            switch (Normalize(text))
            {
                case "STRUCTUREFIRE":
                case "BUILDINGFIRE":
                case "HOUSEFIRE":
                    return IncidentCallType.StructureFire;
                case "VEHICLEFIRE":
                case "CARFIRE":
                    return IncidentCallType.VehicleFire;
                case "BRUSHFIRE":
                case "GRASSFIRE":
                    return IncidentCallType.BrushFire;
                case "FIREALARM":
                case "ALARM":
                    return IncidentCallType.FireAlarm;
                case "MEDICAL":
                case "EMS":
                case "CARDIAC":
                    return IncidentCallType.Medical;
                case "TRAFFICACCIDENT":
                case "MVA":
                case "COLLISION":
                    return IncidentCallType.TrafficAccident;
                case "HAZMAT":
                    return IncidentCallType.HazMat;
                case "RESCUE":
                    return IncidentCallType.Rescue;
                default:
                    return IncidentCallType.Other;
            }
        }

        /// <summary>Maps unit names to units; a name the game has no apparatus for becomes <see cref="IncidentUnit.Other"/>. Repeats are dropped.</summary>
        private static IReadOnlyList<IncidentUnit> ParseUnits(string[] names)
        {
            var units = new List<IncidentUnit>();

            foreach (var name in names)
            {
                var unit = ParseUnit(name);

                if (!units.Contains(unit))
                {
                    units.Add(unit);
                }
            }

            return units;
        }

        /// <summary>Maps one unit name.</summary>
        private static IncidentUnit ParseUnit(string name)
        {
            switch (Normalize(name))
            {
                case "ENGINE":
                case "PUMPER":
                case "E":
                    return IncidentUnit.Engine;
                case "LADDER":
                case "AERIAL":
                case "TRUCK":
                case "L":
                    return IncidentUnit.Ladder;
                case "AMBULANCE":
                case "MEDIC":
                case "EMS":
                case "M":
                    return IncidentUnit.Ambulance;
                case "RESCUE":
                    return IncidentUnit.Rescue;
                case "HAZMAT":
                    return IncidentUnit.HazMat;
                case "BATTALION":
                case "CHIEF":
                    return IncidentUnit.Battalion;
                case "PATROL":
                case "POLICE":
                    return IncidentUnit.Patrol;
                default:
                    return IncidentUnit.Other;
            }
        }

        /// <summary>Upper-cases the text and drops everything but letters and digits, so "Structure Fire", "STRUCTURE_FIRE" and "structure-fire" match.</summary>
        private static string Normalize(string text)
        {
            var letters = new System.Text.StringBuilder(text.Length);

            foreach (var character in text)
            {
                if (char.IsLetterOrDigit(character))
                {
                    letters.Append(char.ToUpperInvariant(character));
                }
            }

            return letters.ToString();
        }
    }
}
