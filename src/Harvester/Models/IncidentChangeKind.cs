namespace HoustonIncidents.Harvester.Models;

/// <summary>
/// The kind of change detected between two polls.
/// </summary>
internal enum IncidentChangeKind
{
    /// <summary>The incident appeared in the latest snapshot.</summary>
    New,

    /// <summary>The incident was in the previous snapshot but is gone now.</summary>
    Closed,
}
