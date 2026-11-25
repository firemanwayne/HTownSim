using System.Text.Json.Serialization;

namespace HoustonIncidents.Harvester.Models;

/// <summary>
/// One incident as the game's live feed reads it (HTownSim <c>LiveIncidentFeed</c>). Field names are the JSON keys.
/// </summary>
internal sealed class LiveIncident
{
    /// <summary>Gets or sets the stable id (see <see cref="Formatters.IncidentIdFormatter"/>).</summary>
    [JsonPropertyName("incidentId")]
    public string IncidentId { get; set; } = string.Empty;

    /// <summary>Gets or sets the page agency code (FD or PD).</summary>
    [JsonPropertyName("agency")]
    public string Agency { get; set; } = string.Empty;

    /// <summary>Gets or sets the incident type as shown, e.g. EMS EVENT.</summary>
    [JsonPropertyName("incidentType")]
    public string IncidentType { get; set; } = string.Empty;

    /// <summary>Gets or sets the address.</summary>
    [JsonPropertyName("address")]
    public string Address { get; set; } = string.Empty;

    /// <summary>Gets or sets the cross street (fire incidents only).</summary>
    [JsonPropertyName("crossStreet")]
    public string CrossStreet { get; set; } = string.Empty;

    /// <summary>Gets or sets the Key Map grid reference (fire incidents only).</summary>
    [JsonPropertyName("keyMap")]
    public string KeyMap { get; set; } = string.Empty;

    /// <summary>Gets or sets the call-opened time as shown.</summary>
    [JsonPropertyName("callTimeOpened")]
    public string CallTimeOpened { get; set; } = string.Empty;

    /// <summary>Gets or sets the alarm level (fire incidents only).</summary>
    [JsonPropertyName("alarmLevel")]
    public string AlarmLevel { get; set; } = string.Empty;

    /// <summary>Gets or sets the assigned unit codes, e.g. E007, L016, A008.</summary>
    [JsonPropertyName("units")]
    public IReadOnlyList<string> Units { get; set; } = [];

    /// <summary>Gets or sets the latitude, or 0 when the address could not be placed.</summary>
    [JsonPropertyName("lat")]
    public double Latitude { get; set; }

    /// <summary>Gets or sets the longitude, or 0 when the address could not be placed.</summary>
    [JsonPropertyName("lon")]
    public double Longitude { get; set; }
}
