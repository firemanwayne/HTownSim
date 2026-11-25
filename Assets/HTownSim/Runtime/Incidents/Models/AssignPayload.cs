#nullable enable
using System;
using UnityEngine;

namespace HTownSim.Runtime.Incidents.Models
{
    /// <summary>Tells the incident service which units the game assigned to an incident (and, for a game-made call, what it is). Field names are the JSON keys.</summary>
    [Serializable]
    internal sealed class AssignPayload
    {
        [SerializeField] private string[] units = new string[0];

        [SerializeField] private string incidentType = "";

        [SerializeField] private string address = "";

        [SerializeField] private double lat;

        [SerializeField] private double lon;

        /// <summary>Creates a payload.</summary>
        internal AssignPayload(string[] assigned, string type, string place, GeoPoint position)
        {
            units = assigned;

            incidentType = type;

            address = place;

            lat = position.Latitude;

            lon = position.Longitude;
        }
    }
}
