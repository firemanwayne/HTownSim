namespace HoustonIncidents.Harvester.Models;

/// <summary>
/// One active incident row from the page's GridView2 table. Record equality over all four
/// fields is the incident's identity, because the page publishes no incident ID.
/// </summary>
/// <param name="Agency">Responding agency, e.g. FD or PD.</param>
/// <param name="Address">Incident address.</param>
/// <param name="IncidentType">Incident type, e.g. EMS EVENT.</param>
/// <param name="CallTimeOpened">Call-opened time exactly as shown on the page.</param>
internal sealed record Incident(string Agency, string Address, string IncidentType, string CallTimeOpened);
