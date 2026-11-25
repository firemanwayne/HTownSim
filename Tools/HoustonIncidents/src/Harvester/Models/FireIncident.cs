namespace HoustonIncidents.Harvester.Models;

/// <summary>
/// One row of the fire-only Active Incidents page (<c>agency=F</c>), which adds the units assigned and the alarm level.
/// </summary>
/// <param name="Address">Incident address.</param>
/// <param name="CrossStreet">Nearest cross street, e.g. <c>BLK CLAREWOOD DR</c>.</param>
/// <param name="KeyMap">Key Map grid reference, e.g. <c>493Q</c>.</param>
/// <param name="CallTimeOpened">Call-opened time exactly as shown on the page.</param>
/// <param name="IncidentType">Incident type, e.g. EMS EVENT.</param>
/// <param name="AlarmLevel">Alarm level as shown (0 for a single-unit or first-alarm response).</param>
/// <param name="Units">The assigned units exactly as shown, comma-separated (e.g. <c>A067,M074</c>).</param>
internal sealed record FireIncident(string Address, string CrossStreet, string KeyMap, string CallTimeOpened, string IncidentType, string AlarmLevel, string Units);
