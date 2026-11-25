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

    /// <summary>
    /// Fetches the HTML of another page (the fire-only view).
    /// </summary>
    /// <param name="url">The page URL.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The HTML, or a failure describing an HTTP error or timeout.</returns>
    Task<Result<string>> GetPageAsync(string url, CancellationToken cancellationToken);
}
