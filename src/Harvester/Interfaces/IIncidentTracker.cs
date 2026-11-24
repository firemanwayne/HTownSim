namespace HoustonIncidents.Harvester.Interfaces;

/// <summary>
/// Tracks the set of open incidents between polls and reports what changed.
/// </summary>
internal interface IIncidentTracker
{
    /// <summary>
    /// Compares the latest snapshot with the previous one and updates the tracked set.
    /// </summary>
    /// <param name="snapshot">All incidents currently shown on the page.</param>
    /// <returns>CLOSED changes first, then NEW changes in page order.</returns>
    IReadOnlyList<IncidentChange> Apply(IReadOnlyList<Incident> snapshot);
}
