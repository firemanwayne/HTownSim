#nullable enable
using System;
using UnityEngine;

namespace HTownSim.Runtime.Incidents.Models
{
    /// <summary>A WGS84 position in decimal degrees: the <c>coordinates</c> object of an incident payload.</summary>
    [Serializable]
    internal sealed class GeoPoint
    {
        /// <summary>Latitude in degrees, north positive. The field name is the JSON key.</summary>
        [SerializeField] private double lat;

        /// <summary>Longitude in degrees, east positive. The field name is the JSON key.</summary>
        [SerializeField] private double lon;

        /// <summary>Creates an empty point (0, 0), which payload validation treats as missing.</summary>
        internal GeoPoint()
        {
        }

        /// <summary>Creates a point.</summary>
        internal GeoPoint(double latitude, double longitude)
        {
            lat = latitude;

            lon = longitude;
        }

        /// <summary>Latitude in degrees.</summary>
        internal double Latitude => lat;

        /// <summary>Longitude in degrees.</summary>
        internal double Longitude => lon;
    }
}
