namespace HoustonIncidents.Harvester.Interfaces;

/// <summary>
/// Places an incident address on the map.
/// </summary>
internal interface IIncidentGeocoder
{
    /// <summary>
    /// Finds the position of an address, or of the corner of the address street and the cross street.
    /// </summary>
    /// <param name="request">The address and cross street.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The position, or null when it could not be placed.</returns>
    Task<GeoPoint?> LocateAsync(GeocodeRequest request, CancellationToken cancellationToken);
}
