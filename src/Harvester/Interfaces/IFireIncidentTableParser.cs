namespace HoustonIncidents.Harvester.Interfaces;

/// <summary>
/// Extracts incidents from the fire-only Active Incidents page HTML.
/// </summary>
internal interface IFireIncidentTableParser
{
    /// <summary>
    /// Parses the GridView2 table of the fire-only page.
    /// </summary>
    /// <param name="html">The full page HTML.</param>
    /// <returns>The incidents, or a failure if the table or a required column is missing.</returns>
    Result<IReadOnlyList<FireIncident>> Parse(string html);
}
