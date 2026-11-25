namespace HoustonIncidents.Harvester.Services;

/// <summary>
/// Parses the GridView2 table of the fire-only Active Incidents page (<c>agency=F</c>) using HtmlAgilityPack.
/// </summary>
internal sealed class FireIncidentTableParser : IFireIncidentTableParser
{
    /// <summary>The HTML id of the incidents table.</summary>
    private const string TableId = "GridView2";

    /// <summary>Header text of the columns this parser reads.</summary>
    private static readonly string[] Headers = ["Address", "Cross Street", "Key Map", "Call Time(Opened)", "Incident Type", "Alarm Level", "Units"];

    /// <inheritdoc />
    public Result<IReadOnlyList<FireIncident>> Parse(string html)
    {
        var document = new HtmlDocument();

        document.LoadHtml(html);

        var table = document.GetElementbyId(TableId);

        if (table is null)
        {
            return Result<IReadOnlyList<FireIncident>>.Fail($"Table '{TableId}' not found in page.");
        }

        var rows = table
            .Descendants("tr")
            .ToList();

        var headerRow = rows.FirstOrDefault(row => row.Elements("th").Any());

        if (headerRow is null)
        {
            return Result<IReadOnlyList<FireIncident>>.Fail($"Table '{TableId}' has no header row.");
        }

        var found = headerRow
            .Elements("th")
            .Select(cell => Clean(cell.InnerText))
            .ToList();

        var indexes = Headers
            .Select(header => found.IndexOf(header))
            .ToArray();

        var missing = Headers
            .Where((header, i) => indexes[i] < 0)
            .ToList();

        if (missing.Count > 0)
        {
            return Result<IReadOnlyList<FireIncident>>.Fail($"Table '{TableId}' is missing column(s): {string.Join(", ", missing)}.");
        }

        var maxIndex = indexes.Max();

        var incidents = new List<FireIncident>();

        foreach (var row in rows)
        {
            var cells = row
                .Elements("td")
                .Select(cell => Clean(cell.InnerText))
                .ToList();

            if (cells.Count <= maxIndex)
            {
                continue;
            }

            incidents.Add(new FireIncident(cells[indexes[0]], cells[indexes[1]], cells[indexes[2]], cells[indexes[3]], cells[indexes[4]], cells[indexes[5]], cells[indexes[6]]));
        }

        return Result<IReadOnlyList<FireIncident>>.Ok(incidents);
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
