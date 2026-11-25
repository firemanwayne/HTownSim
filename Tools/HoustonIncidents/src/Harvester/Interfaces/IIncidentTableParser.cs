namespace HoustonIncidents.Harvester.Interfaces;

/// <summary>
/// Extracts incidents from the Active Incidents page HTML.
/// </summary>
internal interface IIncidentTableParser
{
    /// <summary>
    /// Parses the GridView2 table in the supplied page HTML.
    /// </summary>
    /// <param name="html">The full page HTML.</param>
    /// <returns>The incidents, or a failure if the table or a required column is missing.</returns>
    Result<IReadOnlyList<Incident>> Parse(string html);
}
