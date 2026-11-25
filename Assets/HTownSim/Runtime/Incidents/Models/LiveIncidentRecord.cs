#nullable enable
using System;
using UnityEngine;

namespace HTownSim.Runtime.Incidents.Models
{
    /// <summary>
    /// One City of Houston active incident as the HoustonIncidents harvester writes it (J:\HoustonIncidents, LiveIncident). Field names
    /// are the JSON keys. Latitude and longitude are 0 when the harvester could not place the address.
    /// </summary>
    [Serializable]
    internal sealed class LiveIncidentRecord
    {
        [SerializeField] private string incidentId = "";

        [SerializeField] private string agency = "";

        [SerializeField] private string incidentType = "";

        [SerializeField] private string address = "";

        [SerializeField] private string crossStreet = "";

        [SerializeField] private string keyMap = "";

        [SerializeField] private string callTimeOpened = "";

        [SerializeField] private string alarmLevel = "";

        [SerializeField] private string[] units = new string[0];

        [SerializeField] private double lat;

        [SerializeField] private double lon;

        /// <summary>The stable id (COH-...).</summary>
        internal string IncidentId => incidentId ?? "";

        /// <summary>The page agency code: FD (fire and EMS) or PD.</summary>
        internal string Agency => agency ?? "";

        /// <summary>The incident type as the City shows it, e.g. EMS EVENT, AUTOMATIC ALARM.</summary>
        internal string IncidentType => incidentType ?? "";

        /// <summary>The address.</summary>
        internal string Address => address ?? "";

        /// <summary>The cross street, e.g. BLK CLAREWOOD DR, or empty.</summary>
        internal string CrossStreet => crossStreet ?? "";

        /// <summary>The Key Map grid reference, or empty.</summary>
        internal string KeyMap => keyMap ?? "";

        /// <summary>The call-opened time as shown by the City.</summary>
        internal string CallTimeOpened => callTimeOpened ?? "";

        /// <summary>The alarm level as shown (0 for a routine response), or empty.</summary>
        internal string AlarmLevel => alarmLevel ?? "";

        /// <summary>The assigned HFD unit codes, e.g. E007, L016, A008.</summary>
        internal string[] Units => units ?? new string[0];

        /// <summary>True when the harvester placed the incident on the map.</summary>
        internal bool HasPosition => lat != 0d || lon != 0d;

        /// <summary>The position.</summary>
        internal GeoPoint Geo => new GeoPoint(lat, lon);

        /// <summary>Replaces the unit list (units are added after an incident first appears).</summary>
        internal void SetUnits(string[] value)
        {
            units = value ?? new string[0];
        }
    }
}
