#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace HTownSim.Runtime.Incidents.Models
{
    /// <summary>A validated call: the standard payload with its coordinates already mapped into the Unity scene.</summary>
    internal sealed class Incident
    {
        /// <summary>Creates an incident from its parts.</summary>
        internal Incident(IncidentParts parts)
        {
            Id = parts.Id;

            Agency = parts.Agency;

            CallType = parts.CallType;

            CallText = parts.CallText;

            Geo = parts.Geo;

            WorldPosition = parts.WorldPosition;

            Severity = parts.Severity;

            RequiredUnits = parts.RequiredUnits;

            Address = parts.Address;

            Source = parts.Source;
        }

        /// <summary>The unique incident id.</summary>
        internal string Id { get; }

        /// <summary>The owning agency.</summary>
        internal IncidentAgency Agency { get; }

        /// <summary>The call type.</summary>
        internal IncidentCallType CallType { get; }

        /// <summary>The call type as sent, upper case with spaces, for call types the game has no name for.</summary>
        internal string CallText { get; }

        /// <summary>Where the call is, in latitude and longitude.</summary>
        internal GeoPoint Geo { get; }

        /// <summary>Where the call is in the Unity scene (x, z in metres; y is set when the incident is placed).</summary>
        internal Vector3 WorldPosition { get; }

        /// <summary>1 (minor) to 5 (major).</summary>
        internal int Severity { get; }

        /// <summary>The apparatus the call asks for; empty means any unit may take it.</summary>
        internal IReadOnlyList<IncidentUnit> RequiredUnits { get; }

        /// <summary>Street address or location text; empty when the source gave none.</summary>
        internal string Address { get; }

        /// <summary>What produced the call.</summary>
        internal string Source { get; }

        /// <summary>True for calls the game plays as a burning building.</summary>
        internal bool IsFire => CallType is IncidentCallType.StructureFire or IncidentCallType.VehicleFire or IncidentCallType.BrushFire;

        /// <summary>The call's name for the HUD, upper case.</summary>
        internal string CallLabel
        {
            get
            {
                return CallType switch
                {
                    IncidentCallType.StructureFire => "STRUCTURE FIRE",
                    IncidentCallType.VehicleFire => "VEHICLE FIRE",
                    IncidentCallType.BrushFire => "BRUSH FIRE",
                    IncidentCallType.FireAlarm => "FIRE ALARM",
                    IncidentCallType.Medical => "MEDICAL",
                    IncidentCallType.TrafficAccident => "TRAFFIC ACCIDENT",
                    IncidentCallType.HazMat => "HAZMAT",
                    IncidentCallType.Rescue => "RESCUE",
                    _ => string.IsNullOrEmpty(CallText) ? "INCIDENT" : CallText,
                };
            }
        }

        /// <summary>True when the call asks for the unit, or names no units at all.</summary>
        internal bool Requires(IncidentUnit unit)
        {
            if (RequiredUnits.Count == 0)
            {
                return true;
            }

            for (var i = 0; i < RequiredUnits.Count; i++)
            {
                if (RequiredUnits[i] == unit)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The required units as upper-case words for the HUD, e.g. "ENGINE, LADDER".</summary>
        internal string RequiredUnitsText()
        {
            if (RequiredUnits.Count == 0)
            {
                return "ANY";
            }

            var words = new string[RequiredUnits.Count];

            for (var i = 0; i < words.Length; i++)
            {
                words[i] = RequiredUnits[i].ToString().ToUpperInvariant();
            }

            return string.Join(", ", words);
        }
    }
}
