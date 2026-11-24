namespace HoustonIncidents.Harvester.Services;

/// <summary>
/// Parses the GridView2 table of the Active Incidents page using HtmlAgilityPack.
/// </summary>
internal sealed class IncidentTableParser : IIncidentTableParser
{
    /// <summary>The HTML id of the incidents table.</summary>
    private const string TableId = "GridView2";

    /// <summary>Header text of the Agency column.</summary>
    private const string AgencyHeader = "Agency";

    /// <summary>Header text of the Address column.</summary>
    private const string AddressHeader = "Address";

    /// <summary>Header text of the Incident Type column.</summary>
    private const string IncidentTypeHeader = "Incident Type";

    /// <summary>Header text of the Call Time(Opened) column.</summary>
    private const string CallTimeHeader = "Call Time(Opened)";

    /// <inheritdoc />
    public Result<IReadOnlyList<Incident>> Parse(string html)
    {
        var document = new HtmlDocument();

        document.LoadHtml(html);

        var table = document.GetElementbyId(TableId);

        if (table is null)
        {
            return Result<IReadOnlyList<Incident>>.Fail($"Table '{TableId}' not found in page.");
        }

        var rows = table.Descendants("tr").ToList();

        var headerRow = rows.FirstOrDefault(row => row.Elements("th").Any());

        if (headerRow is null)
        {
            return Result<IReadOnlyList<Incident>>.Fail($"Table '{TableId}' has no header row.");
        }

        var headers = headerRow.Elements("th").Select(cell => Clean(cell.InnerText)).ToList();

        var agencyIndex = headers.IndexOf(AgencyHeader);

        var addressIndex = headers.IndexOf(AddressHeader);

        var typeIndex = headers.IndexOf(IncidentTypeHeader);

        var timeIndex = headers.IndexOf(CallTimeHeader);

        var missing = new[]
        {
            (AgencyHeader, agencyIndex),
            (AddressHeader, addressIndex),
            (IncidentTypeHeader, typeIndex),
            (CallTimeHeader, timeIndex),
        }.Where(column => column.Item2 < 0).Select(column => column.Item1).ToList();

        if (missing.Count > 0)
        {
            return Result<IReadOnlyList<Incident>>.Fail($"Table '{TableId}' is missing column(s): {string.Join(", ", missing)}.");
        }

        var maxIndex = new[] { agencyIndex, addressIndex, typeIndex, timeIndex }.Max();

        var incidents = new List<Incident>();

        foreach (var row in rows)
        {
            var cells = row.Elements("td").Select(cell => Clean(cell.InnerText)).ToList();

            if (cells.Count <= maxIndex)
            {
                continue;
            }

            incidents.Add(new Incident(cells[agencyIndex], cells[addressIndex], cells[typeIndex], cells[timeIndex]));
        }

        return Result<IReadOnlyList<Incident>>.Ok(incidents);
    }

    /// <summary>
    /// Decodes HTML entities and collapses runs of whitespace (including non-breaking spaces) to single spaces.
    /// </summary>
    /// <param name="text">The raw cell text.</param>
    /// <returns>The cleaned text.</returns>
    private static string Clean(string text)
    {
        var decoded = HtmlEntity.DeEntitize(text) ?? string.Empty;

        return string.Join(' ', decoded.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
