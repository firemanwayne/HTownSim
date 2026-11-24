#nullable enable
using System;
using HTownSim.Runtime.Incidents.Models;
using UnityEngine;

namespace HTownSim.Runtime.Incidents.Projections
{
    /// <summary>
    /// Maps latitude and longitude to the HTownSim scene and back. These are the constants the GIS import uses
    /// (Blender/HTown/scripts/fetch_houston.js and AgentScripts/PrepareMidtownData.js): the Station 7 geocode is the origin,
    /// the street grid is rotated 35.06 degrees, and +Z is the top of the map. Keep them identical or calls land in the wrong place.
    /// </summary>
    internal static class GeoProjection
    {
        /// <summary>Latitude of the scene origin (Station 7).</summary>
        private const double OriginLatitude = 29.73948387;

        /// <summary>Longitude of the scene origin (Station 7).</summary>
        private const double OriginLongitude = -95.3734589;

        /// <summary>Metres per degree of latitude.</summary>
        private const double MetersPerDegreeLatitude = 110574d;

        /// <summary>Metres per degree of longitude at the origin latitude.</summary>
        private static readonly double MetersPerDegreeLongitude = 111320d * Math.Cos(OriginLatitude * Math.PI / 180d);

        /// <summary>Rotation of the street grid against north, in radians.</summary>
        private static readonly double Rotation = 35.06d * Math.PI / 180d;

        /// <summary>The scene position (x, 0, z) of a latitude and longitude.</summary>
        internal static Vector3 ToWorld(GeoPoint point)
        {
            var east = (point.Longitude - OriginLongitude) * MetersPerDegreeLongitude;

            var north = (point.Latitude - OriginLatitude) * MetersPerDegreeLatitude;

            var x = (east * Math.Cos(Rotation)) - (north * Math.Sin(Rotation));

            var z = (east * Math.Sin(Rotation)) + (north * Math.Cos(Rotation));

            return new Vector3((float)x, 0f, (float)z);
        }

        /// <summary>The latitude and longitude of a scene position.</summary>
        internal static GeoPoint ToGeo(Vector3 world)
        {
            var east = (world.x * Math.Cos(Rotation)) + (world.z * Math.Sin(Rotation));

            var north = (-world.x * Math.Sin(Rotation)) + (world.z * Math.Cos(Rotation));

            return new GeoPoint(OriginLatitude + (north / MetersPerDegreeLatitude), OriginLongitude + (east / MetersPerDegreeLongitude));
        }
    }
}
