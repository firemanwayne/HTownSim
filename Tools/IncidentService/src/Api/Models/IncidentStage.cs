namespace IncidentService.Api.Models;

/// <summary>
/// Where an incident is in its life. An incident only moves forward through these.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum IncidentStage
{
    /// <summary>The incident exists; no unit is assigned yet.</summary>
    Created = 0,

    /// <summary>At least one unit is assigned. From here the City closing the call cannot clear it; only the lifecycle can.</summary>
    Assigned = 1,

    /// <summary>The call finished (the City closed it or the units reported done); the scene is held for a wrap-up period.</summary>
    Handled = 2,

    /// <summary>Done: the scene can be removed and the record is history.</summary>
    Cleared = 3,
}
