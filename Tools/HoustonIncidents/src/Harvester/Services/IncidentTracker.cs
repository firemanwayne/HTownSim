namespace HoustonIncidents.Harvester.Services;

/// <summary>
/// In-memory tracker that diffs consecutive snapshots of the incident table.
/// </summary>
internal sealed class IncidentTracker : IIncidentTracker
{
    /// <summary>The incidents seen in the previous snapshot.</summary>
    private HashSet<Incident> _tracked = [];

    /// <inheritdoc />
    public IReadOnlyList<IncidentChange> Apply(IReadOnlyList<Incident> snapshot)
    {
        var current = new HashSet<Incident>(snapshot);

        var changes = new List<IncidentChange>();

        foreach (var incident in _tracked)
        {
            if (!current.Contains(incident))
            {
                changes.Add(new IncidentChange(IncidentChangeKind.Closed, incident));
            }
        }

        foreach (var incident in snapshot.Distinct())
        {
            if (!_tracked.Contains(incident))
            {
                changes.Add(new IncidentChange(IncidentChangeKind.New, incident));
            }
        }

        _tracked = current;

        return changes;
    }
}
