namespace HoustonIncidents.Harvester.Interfaces;

/// <summary>
/// Downloads the Active Incidents page.
/// </summary>
internal interface IIncidentPageClient
{
    /// <summary>
    /// Fetches the page HTML.
    /// </summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The HTML, or a failure describing an HTTP error or timeout.</returns>
    Task<Result<string>> GetPageAsync(CancellationToken cancellationToken);
}
