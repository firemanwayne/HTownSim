namespace IncidentService.Api.Models;

/// <summary>
/// One incident as the harvester sends it and the game reads it (the HTownSim live-feed shape).
/// </summary>
internal sealed class FeedIncident
{
    /// <summary>Gets or sets the stable id.</summary>
    [JsonPropertyName("incidentId")]
    public string IncidentId { get; set; } = string.Empty;

    /// <summary>Gets or sets the page agency code.</summary>
    [JsonPropertyName("agency")]
    public string Agency { get; set; } = string.Empty;

    /// <summary>Gets or sets the incident type as shown.</summary>
    [JsonPropertyName("incidentType")]
    public string IncidentType { get; set; } = string.Empty;

    /// <summary>Gets or sets the address.</summary>
    [JsonPropertyName("address")]
    public string Address { get; set; } = string.Empty;

    /// <summary>Gets or sets the cross street.</summary>
    [JsonPropertyName("crossStreet")]
    public string CrossStreet { get; set; } = string.Empty;

    /// <summary>Gets or sets the Key Map grid reference.</summary>
    [JsonPropertyName("keyMap")]
    public string KeyMap { get; set; } = string.Empty;

    /// <summary>Gets or sets the call-opened time as shown.</summary>
    [JsonPropertyName("callTimeOpened")]
    public string CallTimeOpened { get; set; } = string.Empty;

    /// <summary>Gets or sets the alarm level.</summary>
    [JsonPropertyName("alarmLevel")]
    public string AlarmLevel { get; set; } = string.Empty;

    /// <summary>Gets or sets the assigned unit codes.</summary>
    [JsonPropertyName("units")]
    public List<string> Units { get; set; } = [];

    /// <summary>Gets or sets the latitude, or 0 when unplaced.</summary>
    [JsonPropertyName("lat")]
    public double Latitude { get; set; }

    /// <summary>Gets or sets the longitude, or 0 when unplaced.</summary>
    [JsonPropertyName("lon")]
    public double Longitude { get; set; }
}
