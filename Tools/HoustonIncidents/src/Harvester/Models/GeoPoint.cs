namespace HoustonIncidents.Harvester.Models;

/// <summary>
/// A WGS84 position.
/// </summary>
/// <param name="Latitude">Latitude in decimal degrees.</param>
/// <param name="Longitude">Longitude in decimal degrees.</param>
internal sealed record GeoPoint(double Latitude, double Longitude);
