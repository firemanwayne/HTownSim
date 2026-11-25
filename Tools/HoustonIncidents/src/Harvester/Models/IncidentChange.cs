namespace HoustonIncidents.Harvester.Models;

/// <summary>
/// A single detected change to an incident.
/// </summary>
/// <param name="Kind">Whether the incident is new or closed.</param>
/// <param name="Incident">The incident that changed.</param>
internal sealed record IncidentChange(IncidentChangeKind Kind, Incident Incident);
