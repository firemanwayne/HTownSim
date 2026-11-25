namespace HoustonIncidents.Harvester.Formatters;

/// <summary>
/// Renders incident changes as single console lines.
/// </summary>
internal static class IncidentChangeFormatter
{
    /// <summary>Label printed for a new incident.</summary>
    private const string NewLabel = "NEW";

    /// <summary>Label printed for a closed incident.</summary>
    private const string ClosedLabel = "CLOSED";

    /// <summary>
    /// Formats a change as <c>[HH:mm:ss] LABEL     Agency | Address | Type | Opened</c>.
    /// </summary>
    /// <param name="change">The change to format.</param>
    /// <param name="timestamp">The time the change was detected.</param>
    /// <returns>The formatted line.</returns>
    public static string Format(IncidentChange change, DateTimeOffset timestamp)
    {
        var label = change.Kind == IncidentChangeKind.New ? NewLabel : ClosedLabel;

        var incident = change.Incident;

        return $"[{timestamp:HH:mm:ss}] {label,-9} {incident.Agency} | {incident.Address} | {incident.IncidentType} | {incident.CallTimeOpened}";
    }
}
