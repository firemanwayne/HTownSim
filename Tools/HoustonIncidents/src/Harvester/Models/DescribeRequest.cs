namespace HoustonIncidents.Harvester.Models;

/// <summary>
/// What the worker needs to build the game record of a new incident.
/// </summary>
/// <param name="Id">The incident id.</param>
/// <param name="Incident">The incident from the all-agencies page.</param>
/// <param name="Fire">Its row on the fire-only page, or null.</param>
internal sealed record DescribeRequest(string Id, Incident Incident, FireIncident? Fire);
